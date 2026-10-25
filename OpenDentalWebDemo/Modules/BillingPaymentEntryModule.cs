/*
 * Patient payment entry (FormPayment) — the real, splittable payment the desktop
 * dialog writes, as opposed to the bare amount+note stub on the Account module.
 * A payment is a single Payment row (PatNum, PayDate, PayType from
 * DefCat.PaymentTypes, PayAmt, PayNote) that is broken into one or more PaySplit
 * rows.  Each split allocates part of the money to a procedure/provider (or, with
 * ProcNum 0, sits "unallocated" on the account — the same thing FormPayment's
 * AddOneSplit does when the office does not itemise).  Money always balances: the
 * splits must sum to PayAmt, and if the caller leaves a remainder we append one
 * unallocated split for it, exactly like the form fills the difference.
 *
 * Writes go through the same business calls the dialog makes:
 *   create — Payments.Insert(pay,listPaySplits) (which chains PaySplits.InsertMany),
 *            PaymentCreate security log via Payments.GetSecuritylogEntryText;
 *   edit   — Payments.Update(payNew,payOld) + PaySplits.Sync(listNew,payNum),
 *            PaymentEdit security log carrying the previous SecDateTEdit;
 *   delete — the form's deposit guard (DepositNum!=0 -> the dialog's exact message,
 *            returned as 409), PaymentEdit "Delete for" log, Payments.Delete.
 * Every mutation ends with Signalods.SetInvalid(InvalidType.BillingList), the signal
 * FormPayment raises (the enum has no dedicated Payment member).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingPaymentEntryModule {
	record SplitDto(long ProcNum,long ProvNum,double SplitAmt,long PayPlanNum);
	record PaymentDto(string Date,long PayType,double PayAmt,string Note,List<SplitDto> Splits);

	static object SplitToPayload(PaySplit split) {
		return new {
			splitNum=split.SplitNum,
			procNum=split.ProcNum,
			provNum=split.ProvNum,
			provAbbr=Providers.GetAbbr(split.ProvNum),
			amount=split.SplitAmt,
			unearnedType=split.UnearnedType,
			payPlanNum=split.PayPlanNum,
		};
	}

	static object PaymentToPayload(Payment payment,List<PaySplit> listSplits,List<Def> listPayTypeDefs) {
		return new {
			payNum=payment.PayNum,
			payDate=payment.PayDate.Year>1880?payment.PayDate.ToString("yyyy-MM-dd"):"",
			payType=payment.PayType,
			typeName=payment.PayType==0?"Income Transfer":Defs.GetName(DefCat.PaymentTypes,payment.PayType,listPayTypeDefs),
			amount=payment.PayAmt,
			note=payment.PayNote??"",
			depositNum=payment.DepositNum,
			splits=listSplits.Select(SplitToPayload),
		};
	}

	//Turns the caller's split DTOs into PaySplit rows for the payment, mirroring how
	//FormPayment allocates: each row carries the money against a procedure/provider (or
	//ProcNum 0 = unallocated on the account).  Any remainder between the splits and
	//PayAmt becomes one appended unallocated split, the same balancing AddOneSplit does.
	static List<PaySplit> BuildSplits(Patient pat,DateTime payDate,double payAmt,List<SplitDto> dtos) {
		List<PaySplit> listSplits=new List<PaySplit>();
		foreach(SplitDto dto in dtos??new List<SplitDto>()) {
			if(dto.SplitAmt==0) {
				continue;
			}
			PaySplit split=new PaySplit();
			split.PatNum=pat.PatNum;
			split.SplitAmt=dto.SplitAmt;
			split.ProvNum=dto.ProvNum==0?pat.PriProv:dto.ProvNum;
			split.ProcNum=dto.ProcNum;
			split.PayPlanNum=dto.PayPlanNum;
			split.DatePay=payDate;
			split.ProcDate=payDate;
			split.ClinicNum=pat.ClinicNum;
			listSplits.Add(split);
		}
		double remainder=Math.Round(payAmt-listSplits.Sum(x => x.SplitAmt),2);
		if(remainder!=0 || listSplits.Count==0) {
			//No itemisation (or leftover money): one unallocated split for the balance,
			//attributed to the patient's primary provider — FormPayment's AddOneSplit.
			PaySplit split=new PaySplit();
			split.PatNum=pat.PatNum;
			split.SplitAmt=remainder;
			split.ProvNum=pat.PriProv;
			split.DatePay=payDate;
			split.ProcDate=payDate;
			split.ClinicNum=pat.ClinicNum;
			listSplits.Add(split);
		}
		return listSplits;
	}

	public static void Map(WebApplication app) {
		//The practice's payment types (DefCat.PaymentTypes), the list that fills
		//FormPayment's payment-type box.  isShort:true drops hidden defs.
		app.MapGet("/api/payment-types",() => {
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.PaymentTypes,true);
			return Results.Json(listDefs.Select(x => new {
				defNum=x.DefNum,
				name=x.ItemName,
			}));
		});

		//A patient's payments with their splits — Payments.Refresh(patNum) plus
		//PaySplits.GetForPayments, newest first, type names resolved once.
		app.MapGet("/api/patients/{patNum:long}/payments",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Payment> listPayments=Payments.Refresh(patNum);
			List<Def> listPayTypeDefs=Defs.GetDefsForCategory(DefCat.PaymentTypes,true);
			List<PaySplit> listAllSplits=PaySplits.GetForPayments(listPayments.Select(x => x.PayNum).ToList());
			return Results.Json(new {
				patNum=pat.PatNum,
				patient=pat.GetNameLF(),
				total=listPayments.Sum(x => x.PayAmt),
				payments=listPayments
					.OrderByDescending(x => x.PayDate)
					.ThenByDescending(x => x.PayNum)
					.Select(p => PaymentToPayload(p,listAllSplits.Where(s => s.PayNum==p.PayNum).ToList(),listPayTypeDefs)),
			});
		});

		//Create — FormPayment's OK path for a new payment: PayType from the picked def
		//(0 = income transfer, same as the dialog's "None"), splits built to balance the
		//amount, Payments.Insert(pay,splits) (stamps SecUserNumEntry and inserts the
		//splits), PaymentCreate security log, BillingList signal.
		app.MapPost("/api/patients/{patNum:long}/payments",(long patNum,PaymentDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.Date,out DateTime payDate)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			if(dto.PayType!=0 && Defs.GetDef(DefCat.PaymentTypes,dto.PayType)==null) {
				return Results.Text("Unknown payment type",statusCode:400);
			}
			//Any itemised split must point at a procedure that belongs to this patient.
			foreach(SplitDto s in dto.Splits??new List<SplitDto>()) {
				if(s.ProcNum!=0) {
					Procedure procedure=Procedures.GetOneProc(s.ProcNum,false);
					if(procedure==null || procedure.ProcNum==0 || procedure.PatNum!=pat.PatNum) {
						return Results.Text("Split procedure not found for this patient",statusCode:400);
					}
				}
			}
			Payment payment=new Payment();
			payment.PatNum=pat.PatNum;
			payment.PayDate=payDate.Date;
			payment.DateEntry=DateTime.Today;
			payment.PayType=dto.PayType;
			payment.PayAmt=dto.PayAmt;
			payment.PayNote=dto.Note??"";
			payment.ClinicNum=pat.ClinicNum;
			payment.IsSplit=(dto.Splits?.Count(x => x.SplitAmt!=0)??0)>1;
			List<PaySplit> listSplits=BuildSplits(pat,payDate.Date,dto.PayAmt,dto.Splits);
			long payNum=Payments.Insert(payment,listSplits);
			if(payNum==0) {
				return Results.Text("Payment insert failed",statusCode:500);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.PaymentCreate,payment.PatNum,
				Payments.GetSecuritylogEntryText(payment,payment,true));
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(new { payNum });
		});

		//Edit — same field set as create; Payments.Update(new,old), then PaySplits.Sync
		//rewrites the splits against the payment, and PaymentEdit security log carries the
		//prior SecDateTEdit like the form does.
		app.MapPut("/api/payments/{payNum:long}",(long payNum,PaymentDto dto) => {
			Payment payment=Payments.GetPayment(payNum);
			if(payment==null || payment.PayNum==0) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.Date,out DateTime payDate)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			if(dto.PayType!=0 && Defs.GetDef(DefCat.PaymentTypes,dto.PayType)==null) {
				return Results.Text("Unknown payment type",statusCode:400);
			}
			Patient pat=Patients.GetPat(payment.PatNum);
			foreach(SplitDto s in dto.Splits??new List<SplitDto>()) {
				if(s.ProcNum!=0) {
					Procedure procedure=Procedures.GetOneProc(s.ProcNum,false);
					if(procedure==null || procedure.ProcNum==0 || procedure.PatNum!=payment.PatNum) {
						return Results.Text("Split procedure not found for this patient",statusCode:400);
					}
				}
			}
			Payment paymentOld=payment.Clone();
			payment.PayDate=payDate.Date;
			payment.PayType=dto.PayType;
			payment.PayAmt=dto.PayAmt;
			payment.PayNote=dto.Note??"";
			payment.IsSplit=(dto.Splits?.Count(x => x.SplitAmt!=0)??0)>1;
			Payments.Update(payment,paymentOld);
			List<PaySplit> listSplits=BuildSplits(pat,payDate.Date,dto.PayAmt,dto.Splits);
			PaySplits.Sync(listSplits,payment.PayNum);
			SecurityLogs.MakeLogEntry(EnumPermType.PaymentEdit,payment.PatNum,
				Payments.GetSecuritylogEntryText(payment,paymentOld,false),0,paymentOld.SecDateTEdit);
			Signalods.SetInvalid(InvalidType.BillingList);
			List<PaySplit> listAfter=PaySplits.GetForPayment(payment.PayNum);
			return Results.Json(PaymentToPayload(payment,listAfter,Defs.GetDefsForCategory(DefCat.PaymentTypes,true)));
		});

		//Delete — FormPayment's Delete button: blocked while attached to a deposit (the
		//dialog's exact message, plus the business layer re-checks and throws), then the
		//"Delete for" PaymentEdit log and Payments.Delete (which removes the splits too).
		app.MapDelete("/api/payments/{payNum:long}",(long payNum) => {
			Payment payment=Payments.GetPayment(payNum);
			if(payment==null || payment.PayNum==0) {
				return Results.NotFound();
			}
			if(payment.DepositNum!=0 && payment.PayAmt!=0) {
				return Results.Text("This payment is attached to a deposit.  Not allowed to delete.",statusCode:409);
			}
			Patient pat=Patients.GetPat(payment.PatNum);
			List<Def> listPayTypeDefs=Defs.GetDefsForCategory(DefCat.PaymentTypes,true);
			try {
				Payments.Delete(payment);
			}
			catch(Exception ex) {
				//Business layer guards the deposit attachment again; surface its message.
				return Results.Text(ex.Message,statusCode:409);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.PaymentEdit,payment.PatNum,
				"Delete for: "+(pat?.GetNameLF()??"")+", "+payment.PayAmt.ToString("c")
					+", with payment type '"+Payments.GetPaymentTypeDesc(payment,listPayTypeDefs)+"'",
				0,payment.SecDateTEdit);
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Ok();
		});
	}
}
