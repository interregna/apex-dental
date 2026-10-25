/*
 * Insurance claim receive module: EOB entry + insurance checks.
 * Replaces FormClaimPayTotal / FormClaimPayEdit / FormClaimPayBatch / FormClaimPayList.
 * All data flows through the real business layer:
 * ClaimProcs.RefreshForClaim / ClaimProcs.Update / ClaimProcs.Insert (by-total row) /
 * ClaimPayments.Insert / ClaimPayments.Update / ClaimPayments.GetForClaim /
 * ClaimProcs.UpdateForClaimPayment / Claims.GetClaimsByCheck / Claims.Update —
 * the same calls the WinForms client makes when entering an EOB, marking the
 * claim Received, then creating an insurance check and attaching claimprocs to it.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class InsClaimReceiveModule {
	record ReceiveProcDto(long ClaimProcNum,double DedApplied,double InsPayAmt,double WriteOff,string Remarks);
	record ReceiveTotalDto(double DedApplied,double InsPayAmt,double WriteOff,string Remarks);
	record ReceiveDto(List<ReceiveProcDto> Procs,bool MarkReceived,ReceiveTotalDto AsTotal);
	record NewClaimPaymentDto(string CheckDate,double CheckAmt,string CheckNum,string BankBranch,string Note,string CarrierName,bool IsPartial);
	record EditClaimPaymentDto(string CheckDate,double? CheckAmt,string CheckNum,string BankBranch,string Note,string CarrierName,bool? IsPartial);
	record AttachDto(List<long> ClaimProcNums);

	public static void Map(WebApplication app) {
		//Receive-info for one claim: header, claimprocs with entered amounts, and any checks already attached.
		app.MapGet("/api/claims/{claimNum:long}/receive",(long claimNum) => {
			Claim claim=Claims.GetClaim(claimNum);
			if(claim==null) {
				return Results.NotFound();
			}
			List<ClaimProc> listClaimProcs=ClaimProcs.RefreshForClaim(claimNum);
			DataTable tablePayments=ClaimPayments.GetForClaim(claimNum);//real business call, same grid FormClaimEdit shows
			return Results.Json(new {
				claimNum=claim.ClaimNum,
				patNum=claim.PatNum,
				patient=Patients.GetPat(claim.PatNum)?.GetNameLF()??"",
				carrier=GetCarrierName(claim.PlanNum),
				claimStatus=claim.ClaimStatus,
				claimType=claim.ClaimType,
				dateService=claim.DateService.Year>1880?claim.DateService.ToString("yyyy-MM-dd"):"",
				dateSent=claim.DateSent.Year>1880?claim.DateSent.ToString("yyyy-MM-dd"):"",
				dateReceived=claim.DateReceived.Year>1880?claim.DateReceived.ToString("yyyy-MM-dd"):"",
				claimFee=claim.ClaimFee,
				insPayEst=claim.InsPayEst,
				insPayAmt=claim.InsPayAmt,
				procs=listClaimProcs.Select(x => new {
					claimProcNum=x.ClaimProcNum,
					procNum=x.ProcNum,
					codeSent=x.CodeSent??"",
					descript=x.ProcNum==0?"Total payment":(string.IsNullOrEmpty(x.CodeSent)?"":ProcedureCodes.GetProcCode(x.CodeSent)?.Descript??""),
					provider=Providers.GetAbbr(x.ProvNum),
					feeBilled=x.FeeBilled,
					insPayEst=x.InsPayEst,
					dedApplied=x.DedApplied,
					insPayAmt=x.InsPayAmt,
					writeOff=x.WriteOff,
					status=x.Status.ToString(),
					remarks=x.Remarks??"",
					claimPaymentNum=x.ClaimPaymentNum,
				}),
				payments=tablePayments.Rows.Cast<DataRow>().Select(x => new {
					claimPaymentNum=PIn.Long(x["ClaimPaymentNum"].ToString()),
					checkDate=x["checkDate"].ToString(),
					checkNum=x["CheckNum"].ToString(),
					bankBranch=x["BankBranch"].ToString(),
					amount=PIn.Double(x["amount"].ToString()),
					payType=x["payType"].ToString(),
					note=x["Note"].ToString(),
				}),
			});
		});

		//Enter EOB amounts per claimproc (and/or a by-total row) and optionally mark the claim Received.
		//Mirrors FormClaimPayTotal OK + FormClaimEdit's receive: ClaimProcs.Update per row, then Claims.Update.
		app.MapPost("/api/claims/{claimNum:long}/receive",(long claimNum,ReceiveDto dto) => {
			Claim claim=Claims.GetClaim(claimNum);
			if(claim==null) {
				return Results.NotFound();
			}
			if(claim.ClaimType=="PreAuth") {
				return Results.Text("Preauths are not received here.",statusCode:400);
			}
			List<ClaimProc> listClaimProcs=ClaimProcs.RefreshForClaim(claimNum);
			List<long> listReceivedClaimProcNums=new List<long>();
			foreach(ReceiveProcDto procDto in dto.Procs??new List<ReceiveProcDto>()) {
				ClaimProc claimProc=listClaimProcs.FirstOrDefault(x => x.ClaimProcNum==procDto.ClaimProcNum);
				if(claimProc==null) {
					return Results.Text("ClaimProc "+procDto.ClaimProcNum+" is not on this claim.",statusCode:400);
				}
				if(claimProc.Status==ClaimProcStatus.Received && claimProc.ClaimPaymentNum!=0) {
					return Results.Text("ClaimProc "+procDto.ClaimProcNum+" is already attached to an insurance check.",statusCode:400);
				}
				ClaimProc claimProcOld=claimProc.Copy();
				claimProc.DedApplied=procDto.DedApplied;
				claimProc.InsPayAmt=procDto.InsPayAmt;
				claimProc.WriteOff=procDto.WriteOff;
				claimProc.Remarks=procDto.Remarks??claimProc.Remarks;
				if(dto.MarkReceived && claimProc.Status==ClaimProcStatus.NotReceived) {
					claimProc.Status=ClaimProcStatus.Received;
					claimProc.DateCP=DateTime.Today;
					claimProc.DateEntry=DateTime.Now;//date is now the payment date, same as desktop receive
				}
				ClaimProcs.Update(claimProc,claimProcOld);//real business update: security hash + DateSuppReceived upkeep
				if(claimProc.Status==ClaimProcStatus.Received && claimProc.ClaimPaymentNum==0) {
					listReceivedClaimProcNums.Add(claimProc.ClaimProcNum);
				}
			}
			//Optional by-total row (ProcNum=0), mirroring FormClaimPayTotal's total claimproc creation.
			if(dto.AsTotal!=null && (dto.AsTotal.InsPayAmt!=0 || dto.AsTotal.WriteOff!=0 || dto.AsTotal.DedApplied!=0)) {
				ClaimProc claimProcTotal=new ClaimProc();
				claimProcTotal.ClaimNum=claim.ClaimNum;
				claimProcTotal.PatNum=claim.PatNum;
				claimProcTotal.ProvNum=claim.ProvTreat;
				claimProcTotal.PlanNum=claim.PlanNum;
				claimProcTotal.InsSubNum=claim.InsSubNum;
				claimProcTotal.ClinicNum=claim.ClinicNum;
				claimProcTotal.Status=ClaimProcStatus.Received;
				claimProcTotal.DateCP=DateTime.Today;
				claimProcTotal.DateEntry=DateTime.Now;
				claimProcTotal.ProcDate=claim.DateService;
				claimProcTotal.DedApplied=dto.AsTotal.DedApplied;
				claimProcTotal.InsPayAmt=dto.AsTotal.InsPayAmt;
				claimProcTotal.WriteOff=dto.AsTotal.WriteOff;
				claimProcTotal.Remarks=dto.AsTotal.Remarks??"";
				ClaimProcs.Insert(claimProcTotal);
				listReceivedClaimProcNums.Add(claimProcTotal.ClaimProcNum);
			}
			if(dto.MarkReceived) {
				Claim claimOld=claim.Copy();
				claim.ClaimStatus="R";
				claim.DateReceived=DateTime.Today;
				//Same totals the first loop of Claims.CalculateAndUpdate computes from Received claimprocs.
				List<ClaimProc> listClaimProcsAfter=ClaimProcs.RefreshForClaim(claimNum).FindAll(x => x.Status==ClaimProcStatus.Received);
				claim.DedApplied=listClaimProcsAfter.Sum(x => x.DedApplied);
				claim.InsPayAmt=listClaimProcsAfter.Sum(x => x.InsPayAmt);
				claim.WriteOff=listClaimProcsAfter.Sum(x => x.WriteOff);
				Claims.Update(claim,claimOld);//real business update with security hash
			}
			return Results.Json(new { claimNum,received=listReceivedClaimProcNums });
		});

		//Insurance checks list (FormClaimPayList equivalent) for the last N days.
		app.MapGet("/api/claimpayments",(int? days) => {
			DateTime dateFrom=DateTime.Today.AddDays(-(days??60));
			DataTable table=ClaimPayments.GetForDateRange(dateFrom,DateTime.Today,0,0);
			List<ClaimPayment> listClaimPayments=OpenDentBusiness.Crud.ClaimPaymentCrud.TableToList(table);
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.InsurancePaymentType);
			return Results.Json(listClaimPayments.OrderByDescending(x => x.CheckDate).Select(x => {
				List<Claim> listClaimsAttached=Claims.GetClaimsByCheck(x.ClaimPaymentNum);
				double attachedTotal=0;
				if(listClaimsAttached.Count>0) {
					attachedTotal=ClaimProcs.RefreshForClaims(listClaimsAttached.Select(y => y.ClaimNum).ToList())
						.Where(y => y.ClaimPaymentNum==x.ClaimPaymentNum)
						.Sum(y => y.InsPayAmt);
				}
				return new {
					claimPaymentNum=x.ClaimPaymentNum,
					checkDate=x.CheckDate.Year>1880?x.CheckDate.ToString("yyyy-MM-dd"):"",
					checkAmt=x.CheckAmt,
					checkNum=x.CheckNum??"",
					bankBranch=x.BankBranch??"",
					carrierName=x.CarrierName??"",
					payType=Defs.GetName(DefCat.InsurancePaymentType,x.PayType,listDefs),
					isPartial=x.IsPartial,
					note=x.Note??"",
					claimCount=listClaimsAttached.Count,
					attachedTotal,
				};
			}));
		});

		//One check with its attached claims (FormClaimPayEdit's attach grid via Claims.GetClaimsByCheck).
		app.MapGet("/api/claimpayments/{claimPaymentNum:long}",(long claimPaymentNum) => {
			ClaimPayment claimPayment=ClaimPayments.GetOne(claimPaymentNum);
			if(claimPayment==null) {
				return Results.NotFound();
			}
			List<Claim> listClaims=Claims.GetClaimsByCheck(claimPaymentNum);
			List<ClaimProc> listClaimProcsAttached=listClaims.Count==0?new List<ClaimProc>()
				:ClaimProcs.RefreshForClaims(listClaims.Select(x => x.ClaimNum).ToList()).FindAll(x => x.ClaimPaymentNum==claimPaymentNum);
			return Results.Json(new {
				claimPaymentNum=claimPayment.ClaimPaymentNum,
				checkDate=claimPayment.CheckDate.Year>1880?claimPayment.CheckDate.ToString("yyyy-MM-dd"):"",
				checkAmt=claimPayment.CheckAmt,
				checkNum=claimPayment.CheckNum??"",
				bankBranch=claimPayment.BankBranch??"",
				carrierName=claimPayment.CarrierName??"",
				note=claimPayment.Note??"",
				isPartial=claimPayment.IsPartial,
				attachedTotal=listClaimProcsAttached.Sum(x => x.InsPayAmt),
				claims=listClaims.Select(x => new {
					claimNum=x.ClaimNum,
					patient=Patients.GetPat(x.PatNum)?.GetNameLF()??"",
					carrier=GetCarrierName(x.PlanNum),
					dateService=x.DateService.Year>1880?x.DateService.ToString("yyyy-MM-dd"):"",
					attachedAmt=listClaimProcsAttached.Where(y => y.ClaimNum==x.ClaimNum).Sum(y => y.InsPayAmt),
				}),
			});
		});

		//Create an insurance check (FormClaimPayEdit new).
		app.MapPost("/api/claimpayments",(NewClaimPaymentDto dto) => {
			ClaimPayment claimPayment=new ClaimPayment();
			claimPayment.CheckDate=DateTime.TryParse(dto.CheckDate,out DateTime checkDate)?checkDate:DateTime.Today;
			claimPayment.CheckAmt=dto.CheckAmt;
			claimPayment.CheckNum=dto.CheckNum??"";
			claimPayment.BankBranch=dto.BankBranch??"";
			claimPayment.Note=dto.Note??"";
			claimPayment.CarrierName=dto.CarrierName??"";
			claimPayment.IsPartial=dto.IsPartial;
			claimPayment.PayType=Defs.GetDefsForCategory(DefCat.InsurancePaymentType,true).FirstOrDefault()?.DefNum??0;
			long claimPaymentNum=ClaimPayments.Insert(claimPayment);//sets SecUserNumEntry from Security.CurUser
			return Results.Json(new { claimPaymentNum });
		});

		//Edit a check; ClaimPayments.Update throws if the amount changes on a deposited check.
		app.MapPut("/api/claimpayments/{claimPaymentNum:long}",(long claimPaymentNum,EditClaimPaymentDto dto) => {
			ClaimPayment claimPayment=ClaimPayments.GetOne(claimPaymentNum);
			if(claimPayment==null) {
				return Results.NotFound();
			}
			if(DateTime.TryParse(dto.CheckDate,out DateTime checkDate)) {
				claimPayment.CheckDate=checkDate;
			}
			claimPayment.CheckAmt=dto.CheckAmt??claimPayment.CheckAmt;
			claimPayment.CheckNum=dto.CheckNum??claimPayment.CheckNum;
			claimPayment.BankBranch=dto.BankBranch??claimPayment.BankBranch;
			claimPayment.Note=dto.Note??claimPayment.Note;
			claimPayment.CarrierName=dto.CarrierName??claimPayment.CarrierName;
			claimPayment.IsPartial=dto.IsPartial??claimPayment.IsPartial;
			try {
				ClaimPayments.Update(claimPayment);
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			return Results.Json(new { claimPaymentNum });
		});

		//Attach received claimprocs to a check (FormClaimPayBatch's core write).
		app.MapPost("/api/claimpayments/{claimPaymentNum:long}/attach",(long claimPaymentNum,AttachDto dto) => {
			ClaimPayment claimPayment=ClaimPayments.GetOne(claimPaymentNum);
			if(claimPayment==null) {
				return Results.NotFound();
			}
			if(dto.ClaimProcNums==null || dto.ClaimProcNums.Count==0) {
				return Results.Text("No claimprocs to attach.",statusCode:400);
			}
			foreach(long claimProcNum in dto.ClaimProcNums) {
				ClaimProc claimProc=ClaimProcs.GetOneClaimProc(claimProcNum);
				if(claimProc==null || claimProc.ClaimProcNum==0) {
					return Results.Text("ClaimProc "+claimProcNum+" not found.",statusCode:400);
				}
				if(claimProc.Status!=ClaimProcStatus.Received && claimProc.Status!=ClaimProcStatus.Supplemental) {
					return Results.Text("ClaimProc "+claimProcNum+" is not received; receive the claim first.",statusCode:400);
				}
				if(claimProc.ClaimPaymentNum!=0 && claimProc.ClaimPaymentNum!=claimPaymentNum) {
					return Results.Text("ClaimProc "+claimProcNum+" is attached to a different check.",statusCode:400);
				}
			}
			ClaimProcs.UpdateForClaimPayment(dto.ClaimProcNums,claimPayment);//real business call: sets DateCP + ClaimPaymentNum
			return Results.Json(new { claimPaymentNum,attached=dto.ClaimProcNums.Count });
		});
	}

	///<summary>Carrier name for an insplan, straight from the db (no cache dependency).</summary>
	private static string GetCarrierName(long planNum) {
		InsPlan insPlan=InsPlans.RefreshOne(planNum);
		if(insPlan==null) {
			return "";
		}
		Carrier carrier=Carriers.GetCarrierDB(insPlan.CarrierNum);
		return carrier?.CarrierName??"";
	}
}
