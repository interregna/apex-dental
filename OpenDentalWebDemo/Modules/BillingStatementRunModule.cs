/*
 * Batch statements / billing run (FormBilling + FormBillingOptions "Create statements").
 * The desktop dialog builds a list of guarantor accounts that carry a balance (optionally
 * aged past N days and above a minimum) and then generates one Statement per account.
 *
 * Eligible-account list: the REAL business query, Patients.GetAgingList — the exact call
 * FormBillingOptions.CreateHelper makes (via BillingL) to populate its grid. It reads the
 * pre-computed aging columns on the patient table (guar.Bal_0_30/31_60/61_90/BalOver90/
 * BalTotal/InsEst/PayPlanDue), grouped per guarantor, applying the same min-balance and
 * age-of-account filters. We refresh those columns first with Ledgers.ComputeAging(0,today),
 * the same FuncComputeAging step the dialog runs before billing, so the query sees current
 * balances rather than stale aging. lastStatement is left at Today (the form's default) so
 * accounts are not excluded merely for having a prior statement.
 *
 * Generation per account mirrors the single-account path already in BillingStatementsModule:
 * Statements.GenerateStatement(guarantor patient, open date range, mode, isSinglePatient:false)
 * assembles the Statement (guarantor PatNum, BalTotal/InsEst) exactly as FormStatementOptions
 * does, then Statements.Insert persists it. Each resulting statement renders through the
 * existing GET /api/statements/{n}/pdf sheet path (reused, not re-registered).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingStatementRunModule {
	record RunReqDto(double? MinBalance,int? DaysOld,string Mode);

	//Cap so a preview or an accidental run can't fan out over the whole database.
	const int MaxAccounts=200;

	//comboAge in FormBillingOptions maps to the "" / "30" / "60" / "90" buckets GetAgingList
	//understands (age 0/"" = include every aged bucket).
	static string AgeString(int daysOld) {
		if(daysOld>=90) { return "90"; }
		if(daysOld>=60) { return "60"; }
		if(daysOld>=30) { return "30"; }
		return "";
	}

	//StatementMode.InPerson is the printable/previewable mode (renders headlessly through the
	//sheet PDF path); the dialog also offers Mail/Email/Electronic runs.
	static StatementMode ParseMode(string mode) {
		switch((mode??"").Trim().ToLowerInvariant()) {
			case "mail": return StatementMode.Mail;
			case "email": return StatementMode.Email;
			case "electronic": return StatementMode.Electronic;
			default: return StatementMode.InPerson;
		}
	}

	//The real FormBillingOptions.CreateHelper account query: refresh aging, then GetAgingList
	//with excludeNeg=true (hide credits, the dialog's default with ShowNegative unchecked),
	//excludeLessThan=minBalance, no billing-type/clinic/super filters. Guarantor-level rows.
	static List<PatAging> GetEligible(double minBalance,int daysOld) {
		//Same pre-billing step as FormBillingOptions.FuncComputeAging (all guarantors, as of today).
		Ledgers.ComputeAging(0,DateTime.Today);
		List<PatAging> listPatAgings=Patients.GetAgingList(
			AgeString(daysOld),        //age bucket ("" = all)
			DateTime.Today,            //lastStatement (form default = today; don't exclude prior-billed)
			new List<long>(),          //billingNums empty = all billing types
			false,                     //excludeAddr
			true,                      //excludeNeg (hide negative balances)
			minBalance,                //excludeLessThan
			false,                     //excludeInactive
			false,                     //ignoreInPerson
			new List<long>(),          //clinicNums empty = all
			false,                     //isSuperStatements
			false,                     //isSinglePatient (whole-family, guarantor rows)
			new List<long>(),          //listPendingInsPatNums
			new List<long>(),          //listUnsentPatNums
			new SerializableDictionary<long,List<PatAgingTransaction>>());
		return listPatAgings;
	}

	static object ToAccountPayload(PatAging pa) {
		return new {
			patNum=pa.PatNum,
			guarantor=pa.PatName,
			balTotal=pa.BalTotal,
			amountDue=pa.AmountDue,
			insEst=pa.InsEst,
			payPlanDue=pa.PayPlanDue,
			agingBuckets=new {
				d0_30=pa.Bal_0_30,
				d31_60=pa.Bal_31_60,
				d61_90=pa.Bal_61_90,
				d90plus=pa.BalOver90,
			},
			dateLastStatement=pa.DateLastStatement.Year>1880?pa.DateLastStatement.ToString("yyyy-MM-dd"):"",
		};
	}

	public static void Map(WebApplication app) {
		//Preview: the guarantor accounts a billing run would pick up, with aged buckets — the
		//grid FormBilling shows before you commit. Real Patients.GetAgingList behind it.
		app.MapGet("/api/billing/aging-accounts",(double? minBalance,int? daysOld) => {
			double minBal=minBalance??0;
			int days=daysOld??0;
			List<PatAging> listPatAgings=GetEligible(minBal,days);
			int total=listPatAgings.Count;
			List<PatAging> listCapped=listPatAgings.Take(MaxAccounts).ToList();
			return Results.Json(new {
				minBalance=minBal,
				daysOld=days,
				count=listCapped.Count,
				totalEligible=total,
				capped=total>listCapped.Count,
				grandTotal=listCapped.Sum(x => x.BalTotal),
				accounts=listCapped.Select(ToAccountPayload),
			});
		});

		//Commit the run: generate + persist one Statement per eligible account, exactly the
		//single-account GenerateStatement/Insert flow, looped over the aging list. Returns the
		//created statement numbers so each row can be viewed via /api/statements/{n}/pdf.
		app.MapPost("/api/billing/statement-run",(RunReqDto dto) => {
			double minBal=dto?.MinBalance??0;
			int days=dto?.DaysOld??0;
			StatementMode mode=ParseMode(dto?.Mode);
			List<PatAging> listPatAgings=GetEligible(minBal,days).Take(MaxAccounts).ToList();
			List<long> statementNums=new List<long>();
			List<object> patients=new List<object>();
			foreach(PatAging pa in listPatAgings) {
				Patient pat=Patients.GetPat(pa.PatNum);
				if(pat==null) {
					continue;//guarantor vanished between query and generation
				}
				//Open date range (MinValue,MinValue) = practice default range, whole family.
				Statement statement=Statements.GenerateStatement(pat,DateTime.MinValue,DateTime.MinValue,mode,false);
				statement.StatementNum=Statements.Insert(statement);
				statementNums.Add(statement.StatementNum);
				patients.Add(new {
					patNum=pat.PatNum,
					guarantor=pat.GetNameLF(),
					statementNum=statement.StatementNum,
					balanceTotal=statement.BalTotal,
					insEst=statement.InsEst,
				});
			}
			//Same billing-list invalidation the dialog raises after creating statements.
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(new {
				generated=statementNums.Count,
				mode=mode.ToString(),
				minBalance=minBal,
				daysOld=days,
				statementNums,
				patients,
			});
		});

		//Recent runs: statements grouped by the day they were sent — the natural grouping for
		//billing runs, newest first. Reads Statements.GetStatementsForApi (patNum 0 = all).
		app.MapGet("/api/billing/statement-runs",() => {
			List<Statement> listStatements=Statements.GetStatementsForApi(2000,0,0);
			var runs=listStatements
				.Where(s => s.DateSent.Year>1880)
				.GroupBy(s => s.DateSent.Date)
				.OrderByDescending(g => g.Key)
				.Take(60)
				.Select(g => new {
					date=g.Key.ToString("yyyy-MM-dd"),
					count=g.Count(),
					totalBalance=g.Sum(x => x.BalTotal),
					totalInsEst=g.Sum(x => x.InsEst),
					statementNums=g.OrderByDescending(x => x.StatementNum).Select(x => x.StatementNum),
				});
			return Results.Json(new { runs });
		});
	}
}
