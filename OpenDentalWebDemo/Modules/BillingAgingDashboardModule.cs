/*
 * Accounts-receivable aging dashboard: whole-practice financial overview that
 * replaces the read side of FormRpAging (the "Aging of A/R" report).
 *
 * Data source is the real per-guarantor aging figures Open Dental stores on the
 * patient row (patient.BalTotal / Bal_0_30 / Bal_31_60 / Bal_61_90 / BalOver90 /
 * InsEst). These columns are populated for the family guarantor only and are
 * (re)computed by the aging process — Ledgers.ComputeAging / RunAging — which is
 * exactly what FormRpAging leans on. If the stored columns look stale (aging has
 * not been run today) or are all zero, we run Ledgers.ComputeAging(0,today) once,
 * mirroring the whole-practice run in Ledgers.RunAging, and report that in the
 * `recomputed` flag. The famaging-table run is guarded by the same
 * AgingBeginDateTime lock the business layer uses so we never truncate famaging
 * out from under another in-flight calculation.
 *
 * No patient context: these routes summarize the whole practice. Reads go through
 * DataCore.GetTable against the lowercase `patient` table (Linux MariaDB is
 * case-sensitive on table names; column names are not).
 */
using System;
using System.Collections.Generic;
using System.Data;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingAgingDashboardModule {
	//One aggregated summary row for the whole practice.
	record AgingSummary(int AccountCount,double TotalAR,double InsEst,
		double D0_30,double D31_60,double D61_90,double D90Plus);

	//The four aging buckets, whitelisted so the bucket filter can never be raw SQL.
	static readonly Dictionary<string,string> BucketCols=new() {
		["d0_30"]="Bal_0_30",
		["d31_60"]="Bal_31_60",
		["d61_90"]="Bal_61_90",
		["d90plus"]="BalOver90",
	};

	public static void Map(WebApplication app) {
		//Whole-practice A/R summary: total balance, insurance estimate, patient portion,
		//the four aging buckets, and the count of guarantors carrying a balance. If the
		//stored aging columns are stale (not run today) or empty, run aging once first.
		app.MapGet("/api/ar/aging-summary",(bool? recompute) => {
			bool ranToday=PrefC.GetDate(PrefName.DateLastAging).Date==DateTime.Today;
			AgingSummary sum=QuerySummary();
			bool recomputed=false;
			//Recompute when explicitly asked, or when nothing has been aged today and the
			//stored figures are all zero (a fresh/stale db). Gating on ranToday keeps an
			//empty practice from recomputing on every poll — the compute stamps DateLastAging.
			if((recompute==true) || (!ranToday && IsEmpty(sum))) {
				recomputed=RunWholePracticeAging();
				if(recomputed) {
					sum=QuerySummary();
				}
			}
			double patPortion=sum.TotalAR-sum.InsEst;
			return Results.Json(new {
				asOf=DateTime.Today.ToString("yyyy-MM-dd"),
				totalAR=sum.TotalAR,
				insEst=sum.InsEst,
				patPortion=patPortion,
				buckets=new {
					d0_30=sum.D0_30,
					d31_60=sum.D31_60,
					d61_90=sum.D61_90,
					d90plus=sum.D90Plus,
				},
				accountCount=sum.AccountCount,
				recomputed=recomputed,
			});
		});

		//The largest outstanding guarantor balances (the "top of the aging report"), optionally
		//filtered to families whose balance falls in one aging bucket. Each row is a family
		//guarantor with its family total, over-90 portion, and insurance estimate.
		app.MapGet("/api/ar/top-accounts",(int? limit,string? bucket) => {
			int lim=Math.Clamp(limit??25,1,200);
			string where="Guarantor=PatNum AND ABS(BalTotal)>=0.005";
			if(!string.IsNullOrEmpty(bucket)) {
				if(!BucketCols.TryGetValue(bucket,out string? col)) {
					return Results.Text("Unknown bucket. Use one of: "+string.Join(",",BucketCols.Keys),statusCode:400);
				}
				where+=" AND "+col+">=0.005";
			}
			//Order by family balance descending — the accounts a biller would chase first.
			DataTable table=DataCore.GetTable(
				"SELECT PatNum,LName,FName,MiddleI,BalTotal,BalOver90,InsEst "
				+"FROM patient WHERE "+where+" "
				+"ORDER BY BalTotal DESC LIMIT "+lim);
			List<object> list=new();
			foreach(DataRow row in table.Rows) {
				list.Add(new {
					patNum=PIn.Long(row["PatNum"].ToString()),
					name=BuildNameLF(row),
					balTotal=PIn.Double(row["BalTotal"].ToString()),
					over90=PIn.Double(row["BalOver90"].ToString()),
					insEst=PIn.Double(row["InsEst"].ToString()),
				});
			}
			return Results.Json(list);
		});
	}

	//Aggregate the stored aging columns across family guarantors carrying any balance or ins est.
	//BalTotal can be negative (credit balances), so accountCount uses ABS on either figure.
	static AgingSummary QuerySummary() {
		DataTable table=DataCore.GetTable(
			"SELECT COUNT(*) AccountCount,"
			+"COALESCE(SUM(BalTotal),0) TotalAR,"
			+"COALESCE(SUM(InsEst),0) InsEst,"
			+"COALESCE(SUM(Bal_0_30),0) D0_30,"
			+"COALESCE(SUM(Bal_31_60),0) D31_60,"
			+"COALESCE(SUM(Bal_61_90),0) D61_90,"
			+"COALESCE(SUM(BalOver90),0) D90Plus "
			+"FROM patient "
			+"WHERE Guarantor=PatNum AND (ABS(BalTotal)>=0.005 OR ABS(InsEst)>=0.005)");
		DataRow r=table.Rows[0];
		return new AgingSummary(
			PIn.Int(r["AccountCount"].ToString()),
			PIn.Double(r["TotalAR"].ToString()),
			PIn.Double(r["InsEst"].ToString()),
			PIn.Double(r["D0_30"].ToString()),
			PIn.Double(r["D31_60"].ToString()),
			PIn.Double(r["D61_90"].ToString()),
			PIn.Double(r["D90Plus"].ToString()));
	}

	static bool IsEmpty(AgingSummary s) {
		return Math.Abs(s.TotalAR)<0.005 && Math.Abs(s.InsEst)<0.005
			&& Math.Abs(s.D0_30)<0.005 && Math.Abs(s.D31_60)<0.005
			&& Math.Abs(s.D61_90)<0.005 && Math.Abs(s.D90Plus)<0.005;
	}

	//Whole-practice aging run, the same call Ledgers.RunAging makes for daily aging, wrapped in
	//the AgingBeginDateTime lock so we don't clobber a concurrent famaging calculation. Returns
	//false (skipped, not an error) if another aging run currently holds the lock.
	static bool RunWholePracticeAging() {
		Prefs.RefreshCache();
		if(!PrefC.IsAgingAllowedToStart()) {
			return false;//another aging calculation is in progress; leave the stored figures as-is
		}
		DateTime dtNow=MiscData.GetNowDateTime();
		Prefs.UpdateString(PrefName.AgingBeginDateTime,POut.DateT(dtNow,false));//take the lock
		Signalods.SetInvalid(InvalidType.Prefs);
		try {
			Ledgers.ComputeAging(0,DateTime.Today);//guarantor 0 = recompute all families
			Prefs.UpdateString(PrefName.DateLastAging,POut.Date(DateTime.Today,false));
		}
		finally {
			Prefs.UpdateString(PrefName.AgingBeginDateTime,"");//release the lock, success or not
			Signalods.SetInvalid(InvalidType.Prefs);
		}
		return true;
	}

	//"LName, FName MiddleI" from a report row (avoids an extra Patients.GetPat per row).
	static string BuildNameLF(DataRow row) {
		string lname=row["LName"].ToString()??"";
		string fname=row["FName"].ToString()??"";
		string mi=row["MiddleI"].ToString()??"";
		string name=lname+", "+fname;
		if(!string.IsNullOrWhiteSpace(mi)) {
			name+=" "+mi;
		}
		return name;
	}
}
