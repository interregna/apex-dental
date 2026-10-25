/*
 * Provider day sheet — read-only web slice of FormDaily / the Daily (Production & Income) report.
 * Every figure comes from the real report query: RpProdInc.GetDailyProdIncDataSet builds the exact
 * six SQL tables the desktop Daily/Provider P&I report runs (completed procedurelog production net of
 * claimproc writeoffs, adjustment amounts, claimproc InsPayDate writeoffs, earned paysplit patient
 * income, and claimpayment insurance income). We aggregate those raw rows per ProvNum — the same
 * grouping RpProdInc.GetProviderDataForClinics does — but keep ProvNum so the web UI can key/colour
 * by provider. Whole-practice defaults: all providers, no clinics, writeoffs dated by InsPayDate
 * (matching the ReportRegistry "writeoffs" default), earned income only (unearned excluded).
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ReportsDaySheetModule {
	//One provider's roll-up for the day/range. writeoffs arrive already negated from the report
	//query (-SUM(claimproc.WriteOff)); net = production + adjustments + writeoffs = the report's
	//"Tot Prod" column. Collections (patient + insurance payments) are reported separately.
	record ProvRow(long ProvNum,string ProvAbbr,double Production,double Adjustments,double Writeoffs,
		double PatientPayments,double InsPayments,double Net);

	record LineItem(string Date,string Patient,string Description,string Category,double Amount);

	public static void Map(WebApplication app) {
		//Per-provider day sheet: one row per provider with any activity, plus a synthetic totals row.
		//?date=YYYY-MM-DD for a single day (default today), or ?from=&to= for a range.
		app.MapGet("/api/daysheet",(DateTime? date,DateTime? from,DateTime? to) => {
			(DateTime dateFrom,DateTime dateTo)=ResolveRange(date,from,to);
			DataSet ds=RpProdInc.GetDailyProdIncDataSet(dateFrom,dateTo,AllProvs(),new List<Clinic>(),
				hasAllProvs:true,hasAllClinics:true,isUnearnedIncluded:false,PPOWriteoffDateCalc.InsPayDate);
			//Accumulate each report table's amount column keyed by the provider on that row.
			Dictionary<long,double[]> byProv=new Dictionary<long,double[]>();//[prod,adj,writeoff,patPay,insPay]
			SumInto(byProv,ds.Tables["tableProduction"],"Production",0);
			SumInto(byProv,ds.Tables["tableAdj"],"AdjAmt",1);
			SumInto(byProv,ds.Tables["tableInsWriteoff"],"WriteOff",2);
			SumInto(byProv,ds.Tables["tablePay"],"PayAmt",3);
			SumInto(byProv,ds.Tables["tableIns"],"InsPayAmt",4);
			List<ProvRow> rows=byProv.Select(kv => {
				double[] v=kv.Value;
				return new ProvRow(kv.Key,ProvLabel(kv.Key),v[0],v[1],v[2],v[3],v[4],v[0]+v[1]+v[2]);
			}).OrderBy(x => x.ProvAbbr,StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ProvNum).ToList();
			var totals=new {
				production=rows.Sum(x => x.Production),
				adjustments=rows.Sum(x => x.Adjustments),
				writeoffs=rows.Sum(x => x.Writeoffs),
				patientPayments=rows.Sum(x => x.PatientPayments),
				insPayments=rows.Sum(x => x.InsPayments),
				net=rows.Sum(x => x.Net),
				collections=rows.Sum(x => x.PatientPayments+x.InsPayments),
			};
			return Results.Json(new {
				from=dateFrom.ToString("yyyy-MM-dd"),
				to=dateTo.ToString("yyyy-MM-dd"),
				rows=rows.Select(x => new {
					provNum=x.ProvNum,provAbbr=x.ProvAbbr,
					production=x.Production,adjustments=x.Adjustments,writeoffs=x.Writeoffs,
					patientPayments=x.PatientPayments,insPayments=x.InsPayments,net=x.Net,
				}),
				totals,
			});
		});

		//Drill-in: the individual line items behind one provider's day, pulled from the same report
		//tables (production procs, adjustments, writeoffs, patient payments, insurance payments).
		app.MapGet("/api/daysheet/detail",(DateTime? date,DateTime? from,DateTime? to,long provNum) => {
			(DateTime dateFrom,DateTime dateTo)=ResolveRange(date,from,to);
			DataSet ds=RpProdInc.GetDailyProdIncDataSet(dateFrom,dateTo,AllProvs(),new List<Clinic>(),
				hasAllProvs:true,hasAllClinics:true,isUnearnedIncluded:false,PPOWriteoffDateCalc.InsPayDate);
			List<LineItem> items=new List<LineItem>();
			items.AddRange(Lines(ds.Tables["tableProduction"],provNum,"Production","Production"));
			items.AddRange(Lines(ds.Tables["tableAdj"],provNum,"AdjAmt","Adjustment"));
			items.AddRange(Lines(ds.Tables["tableInsWriteoff"],provNum,"WriteOff","Writeoff"));
			items.AddRange(Lines(ds.Tables["tablePay"],provNum,"PayAmt","Patient payment"));
			items.AddRange(Lines(ds.Tables["tableIns"],provNum,"InsPayAmt","Insurance payment"));
			return Results.Json(new {
				provNum,provAbbr=ProvLabel(provNum),
				from=dateFrom.ToString("yyyy-MM-dd"),to=dateTo.ToString("yyyy-MM-dd"),
				items=items.OrderBy(x => x.Date).ThenBy(x => x.Patient,StringComparer.OrdinalIgnoreCase)
					.Select(x => new { date=x.Date,patient=x.Patient,description=x.Description,category=x.Category,amount=x.Amount }),
			});
		});
	}

	//Sums one report table's numeric column into slot idx of each provider's accumulator.
	static void SumInto(Dictionary<long,double[]> byProv,DataTable table,string amountCol,int idx) {
		if(table==null) {
			return;
		}
		foreach(DataRow row in table.Rows) {
			long provNum=PIn.Long(row["ProvNum"].ToString());
			double amount=(double)PIn.Decimal(row[amountCol].ToString());
			if(!byProv.TryGetValue(provNum,out double[] v)) {
				v=new double[5];
				byProv[provNum]=v;
			}
			v[idx]+=amount;
		}
	}

	//Extracts one report table's rows for a single provider as detail line items.
	static IEnumerable<LineItem> Lines(DataTable table,long provNum,string amountCol,string category) {
		if(table==null) {
			yield break;
		}
		foreach(DataRow row in table.Rows) {
			if(PIn.Long(row["ProvNum"].ToString())!=provNum) {
				continue;
			}
			double amount=(double)PIn.Decimal(row[amountCol].ToString());
			if(amount==0) {
				continue;
			}
			yield return new LineItem(
				PIn.Date(row["Date"].ToString()).ToString("yyyy-MM-dd"),
				row.Table.Columns.Contains("namelf")?row["namelf"].ToString():"",
				row.Table.Columns.Contains("Description")?row["Description"].ToString():"",
				category,amount);
		}
	}

	//ProvNum 0 shows up on unallocated paysplits; the desktop report labels those "Unearned".
	static string ProvLabel(long provNum) {
		if(provNum==0) {
			return "Unallocated";
		}
		string abbr=Providers.GetAbbr(provNum,true);
		return string.IsNullOrEmpty(abbr)?("Prov "+provNum):abbr;
	}

	static List<Provider> AllProvs() => Providers.GetDeepCopy(true);

	//date wins when supplied; otherwise from/to define a range; falling back to today for either end.
	static (DateTime,DateTime) ResolveRange(DateTime? date,DateTime? from,DateTime? to) {
		if(date.HasValue) {
			return (date.Value.Date,date.Value.Date);
		}
		DateTime f=(from??to??DateTime.Today).Date;
		DateTime t=(to??from??DateTime.Today).Date;
		if(t<f) {
			(f,t)=(t,f);
		}
		return (f,t);
	}
}
