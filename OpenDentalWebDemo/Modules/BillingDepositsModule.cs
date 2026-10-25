/*
 * Deposit slips (FormDeposits / FormDepositEdit / FormDepositSlips) — the web
 * replacement for grouping not-yet-deposited money into a Deposit.
 *
 * A Deposit row (DateDeposit, BankAccountInfo, Memo, Amount) is the slip; the
 * money it contains is not a child table but a back-reference: patient Payments
 * and insurance ClaimPayments each carry a DepositNum, and DepositNum=0 is the
 * pickable pool (Payments.GetForDeposit / ClaimPayments.GetForDeposit with a
 * start date).  Creating a deposit inserts the Deposit and then stamps its
 * DepositNum onto the chosen payments/claimpayments — exactly what FormDepositEdit
 * does in SaveToDB: Deposits.Insert, then Payments.Update(pay,false) (the false
 * writes the DepositNum) and ClaimPayments.Update(cp,isDepNew:true) (true skips the
 * "can't change amount on a deposited check" guard, since we're only attaching).
 * Deleting runs Deposits.Delete, which resets DepositNum back to 0 on every item
 * and removes the slip (throwing if it is chained to an accounting transaction).
 *
 * Like the form, only deposit-eligible payment types are offered (Defs whose
 * ItemValue=="" — the "show on deposit slip" flag), and each attach is guarded by
 * Payments/ClaimPayments.GetCountAttachedToDeposit so a payment already claimed by
 * another slip is rejected.  Every mutation writes a DepositSlips security log and
 * raises Signalods.SetInvalid(InvalidType.BillingList), the signal the form raises.
 * Practice-wide (no patient context): clinicNum 0 = all clinics.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingDepositsModule {
	record CreateDto(string Date,string BankAcctInfo,string Note,List<long> PayNums,List<long> ClaimPaymentNums);
	record EditDto(string Date,string BankAcctInfo,string Note);

	//The payment types flagged to show on deposit slips (Def.ItemValue==""), the same
	//filter FormDepositEdit pre-selects in its two type lists.
	static List<long> DepositPayTypeNums() {
		return Defs.GetDefsForCategory(DefCat.PaymentTypes,true)
			.Where(d => d.ItemValue=="").Select(d => d.DefNum).ToList();
	}
	static List<long> DepositInsPayTypeNums() {
		return Defs.GetDefsForCategory(DefCat.InsurancePaymentType,true)
			.Where(d => d.ItemValue=="").Select(d => d.DefNum).ToList();
	}

	//PatNum -> "Last, First" for the payments in a slip/pool, one GetMultPats query.
	static Dictionary<long,string> PatNameLookup(List<long> listPatNums) {
		Dictionary<long,string> dict=new Dictionary<long,string>();
		if(listPatNums.Count==0) {
			return dict;
		}
		foreach(Patient pat in Patients.GetMultPats(listPatNums)) {
			dict[pat.PatNum]=pat.GetNameLF();
		}
		return dict;
	}

	static object DepositSummary(Deposit deposit,int itemCount) {
		return new {
			depositNum=deposit.DepositNum,
			date=deposit.DateDeposit.Year>1880?deposit.DateDeposit.ToString("yyyy-MM-dd"):"",
			amount=deposit.Amount,
			bankAcctInfo=deposit.BankAccountInfo??"",
			note=deposit.Memo??"",
			itemCount,
		};
	}

	public static void Map(WebApplication app) {
		//Deposit slips in a date range (Deposits.Refresh is all slips, date-ordered).
		//itemCount is the attached payment + claimpayment count, read the same way the
		//edit form fills its two read-only grids for an existing deposit.
		app.MapGet("/api/deposits",(string from,string to) => {
			DateTime dateFrom=DateTime.TryParse(from,out DateTime f)?f.Date:DateTime.MinValue;
			DateTime dateTo=DateTime.TryParse(to,out DateTime t)?t.Date:DateTime.MaxValue;
			List<Deposit> listDeposits=Deposits.Refresh()
				.Where(x => x.DateDeposit.Date>=dateFrom && x.DateDeposit.Date<=dateTo)
				.OrderByDescending(x => x.DateDeposit).ThenByDescending(x => x.DepositNum).ToList();
			return Results.Json(listDeposits.Select(d => {
				int count=Payments.GetForDeposit(d.DepositNum).Count
					+ClaimPayments.GetForDeposit(d.DepositNum).Length;
				return DepositSummary(d,count);
			}));
		});

		//One deposit with the checks attached to it — Payments.GetForDeposit(depositNum)
		//and ClaimPayments.GetForDeposit(depositNum), the exact pair FillGrids reads when
		//IsNew is false.  Patient names resolved once via GetMultPats like the form.
		app.MapGet("/api/deposits/{depositNum:long}",(long depositNum) => {
			Deposit deposit=Deposits.GetOne(depositNum);
			if(deposit==null || deposit.DepositNum==0) {
				return Results.NotFound();
			}
			List<Payment> listPayments=Payments.GetForDeposit(depositNum);
			List<ClaimPayment> listClaimPayments=ClaimPayments.GetForDeposit(depositNum).ToList();
			Dictionary<long,string> dictPatNames=PatNameLookup(listPayments.Select(x => x.PatNum).ToList());
			List<object> listItems=new List<object>();
			foreach(Payment pay in listPayments) {
				listItems.Add(new {
					kind="pat",
					num=pay.PayNum,
					patientOrCarrier=dictPatNames.TryGetValue(pay.PatNum,out string nameLf)?nameLf:"",
					date=pay.PayDate.Year>1880?pay.PayDate.ToString("yyyy-MM-dd"):"",
					amount=pay.PayAmt,
					payType=Defs.GetName(DefCat.PaymentTypes,pay.PayType),
					checkNum=pay.CheckNum??"",
				});
			}
			foreach(ClaimPayment cp in listClaimPayments) {
				listItems.Add(new {
					kind="ins",
					num=cp.ClaimPaymentNum,
					patientOrCarrier=cp.CarrierName??"",
					date=cp.CheckDate.Year>1880?cp.CheckDate.ToString("yyyy-MM-dd"):"",
					amount=cp.CheckAmt,
					payType=Defs.GetName(DefCat.InsurancePaymentType,cp.PayType),
					checkNum=cp.CheckNum??"",
				});
			}
			return Results.Json(new {
				depositNum=deposit.DepositNum,
				date=deposit.DateDeposit.Year>1880?deposit.DateDeposit.ToString("yyyy-MM-dd"):"",
				amount=deposit.Amount,
				bankAcctInfo=deposit.BankAccountInfo??"",
				note=deposit.Memo??"",
				items=listItems,
			});
		});

		//The pool available to deposit: unattached patient payments (DepositNum=0) and
		//unattached insurance checks (DepositNum=0) on or after the given date, restricted
		//to deposit-eligible types — Payments.GetForDeposit / ClaimPayments.GetForDeposit,
		//the two queries FillGrids runs for a new slip (clinicNum 0 = all clinics).
		app.MapGet("/api/deposits/unattached",(string date) => {
			DateTime dateStart=DateTime.TryParse(date,out DateTime d)?d.Date:DateTime.MinValue;
			List<long> listPayTypeNums=DepositPayTypeNums();
			List<long> listInsPayTypeNums=DepositInsPayTypeNums();
			List<Payment> listPayments=listPayTypeNums.Count==0
				?new List<Payment>()
				:Payments.GetForDeposit(dateStart,0,listPayTypeNums);
			List<ClaimPayment> listClaimPayments=listInsPayTypeNums.Count==0
				?new List<ClaimPayment>()
				:ClaimPayments.GetForDeposit(dateStart,0,listInsPayTypeNums);
			Dictionary<long,string> dictPatNames=PatNameLookup(listPayments.Select(x => x.PatNum).ToList());
			List<object> listPool=new List<object>();
			foreach(Payment pay in listPayments) {
				listPool.Add(new {
					kind="pat",
					num=pay.PayNum,
					patientOrCarrier=dictPatNames.TryGetValue(pay.PatNum,out string nameLf)?nameLf:"",
					date=pay.PayDate.Year>1880?pay.PayDate.ToString("yyyy-MM-dd"):"",
					amount=pay.PayAmt,
					payType=Defs.GetName(DefCat.PaymentTypes,pay.PayType),
					checkNum=pay.CheckNum??"",
				});
			}
			foreach(ClaimPayment cp in listClaimPayments) {
				listPool.Add(new {
					kind="ins",
					num=cp.ClaimPaymentNum,
					patientOrCarrier=cp.CarrierName??"",
					date=cp.CheckDate.Year>1880?cp.CheckDate.ToString("yyyy-MM-dd"):"",
					amount=cp.CheckAmt,
					payType=Defs.GetName(DefCat.InsurancePaymentType,cp.PayType),
					checkNum=cp.CheckNum??"",
				});
			}
			return Results.Json(listPool);
		});

		//Create — FormDepositEdit's SaveToDB new path: guard that none of the picked
		//payments/claimpayments are already on another slip, total the selection into
		//Amount, Deposits.Insert, then stamp DepositNum onto each item (Payments.Update
		//with excludeDepositNum:false writes it; ClaimPayments.Update with isDepNew:true
		//attaches without tripping the amount-change guard).  DepositSlips log + signal.
		app.MapPost("/api/deposits",(CreateDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime dateDeposit)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			List<long> payNums=(dto.PayNums??new List<long>()).Distinct().ToList();
			List<long> claimPaymentNums=(dto.ClaimPaymentNums??new List<long>()).Distinct().ToList();
			if(payNums.Count==0 && claimPaymentNums.Count==0) {
				return Results.Text("Please select at least one payment for this deposit first.",statusCode:400);
			}
			//Already-attached guards (ignoreDepositNum 0 = this is a brand new slip).
			if(Payments.GetCountAttachedToDeposit(payNums,0)>0) {
				return Results.Text("One or more patient payments are already attached to another deposit.",statusCode:409);
			}
			if(ClaimPayments.GetCountAttachedToDeposit(claimPaymentNums,0)>0) {
				return Results.Text("One or more insurance payments are already attached to another deposit.",statusCode:409);
			}
			//Load the chosen items and total them (Amount is never user-entered — it is the
			//sum of the selected checks, exactly like ComputeAmt).
			List<Payment> listPayments=new List<Payment>();
			foreach(long payNum in payNums) {
				Payment pay=Payments.GetPayment(payNum);
				if(pay==null || pay.PayNum==0) {
					return Results.Text("Payment "+payNum+" not found.",statusCode:400);
				}
				listPayments.Add(pay);
			}
			List<ClaimPayment> listClaimPayments=new List<ClaimPayment>();
			foreach(long claimPaymentNum in claimPaymentNums) {
				ClaimPayment cp=ClaimPayments.GetOne(claimPaymentNum);
				if(cp==null || cp.ClaimPaymentNum==0) {
					return Results.Text("Insurance payment "+claimPaymentNum+" not found.",statusCode:400);
				}
				listClaimPayments.Add(cp);
			}
			double amount=Math.Round(listPayments.Sum(x => x.PayAmt)+listClaimPayments.Sum(x => x.CheckAmt),2);
			Deposit deposit=new Deposit();
			deposit.DateDeposit=dateDeposit.Date;
			deposit.BankAccountInfo=dto.BankAcctInfo??"";
			deposit.Memo=dto.Note??"";
			deposit.Amount=amount;
			long depositNum=Deposits.Insert(deposit);
			if(depositNum==0) {
				return Results.Text("Deposit insert failed",statusCode:500);
			}
			foreach(Payment pay in listPayments) {
				pay.DepositNum=depositNum;
				Payments.Update(pay,false);//false => also runs the UPDATE that sets DepositNum
			}
			foreach(ClaimPayment cp in listClaimPayments) {
				cp.DepositNum=depositNum;
				try {
					ClaimPayments.Update(cp,true);//isDepNew:true => attaching, skip amount-change guard
				}
				catch(Exception ex) {
					return Results.Text(ex.Message,statusCode:409);
				}
			}
			SecurityLogs.MakeLogEntry(EnumPermType.DepositSlips,0,
				deposit.DateDeposit.ToShortDateString()+" New "+amount.ToString("c"));
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(new { depositNum,amount });
		});

		//Edit — the form only lets you change date / bank-account-info / memo on an
		//existing slip (the attached checks are frozen), so this mirrors that: no item
		//re-selection.  Deposits.Update(new,old) with a DepositSlips log + signal.
		app.MapPut("/api/deposits/{depositNum:long}",(long depositNum,EditDto dto) => {
			Deposit deposit=Deposits.GetOne(depositNum);
			if(deposit==null || deposit.DepositNum==0) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.Date,out DateTime dateDeposit)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			Deposit depositOld=deposit.Copy();
			deposit.DateDeposit=dateDeposit.Date;
			deposit.BankAccountInfo=dto.BankAcctInfo??"";
			deposit.Memo=dto.Note??"";
			Deposits.Update(deposit,depositOld);
			SecurityLogs.MakeLogEntry(EnumPermType.DepositSlips,0,
				deposit.DateDeposit.ToShortDateString()+" "+deposit.Amount.ToString("c"));
			Signalods.SetInvalid(InvalidType.BillingList);
			int count=Payments.GetForDeposit(deposit.DepositNum).Count
				+ClaimPayments.GetForDeposit(deposit.DepositNum).Length;
			return Results.Json(DepositSummary(deposit,count));
		});

		//Delete — FormDepositEdit's Delete button: Deposits.Delete resets DepositNum back
		//to 0 on every attached payment/claimpayment and removes the slip, throwing if it
		//is chained to an accounting transaction (surfaced as 409).  Log + signal.
		app.MapDelete("/api/deposits/{depositNum:long}",(long depositNum) => {
			Deposit deposit=Deposits.GetOne(depositNum);
			if(deposit==null || deposit.DepositNum==0) {
				return Results.NotFound();
			}
			try {
				Deposits.Delete(deposit);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.DepositSlips,0,
				"Deleted deposit "+deposit.DateDeposit.ToShortDateString()+" "+deposit.Amount.ToString("c"));
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Ok();
		});
	}
}
