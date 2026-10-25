/*
 * Claim creation from unbilled procedures (the desktop "New Claim" flow in
 * ContrAccount / ClaimL.CreateClaimFromWrapper / FormClaimEdit creation path).
 * All data flows through the real business layer:
 * AccountModules.GetCreateClaimData assembles the same working set the desktop
 * uses (patplans, inssubs, insplans, claimprocs, procs, patnote), and
 * AccountModules.CreateClaim is the exact business method ClaimL calls — it
 * validates the selection, finds/creates the ClaimProc estimates
 * (Procedures.GetClaimProcEstimate / ClaimProcs.CreateEst), inserts the claim
 * (Claims.Insert), attaches the claimprocs as NotReceived, and finishes with
 * Claims.CalculateAndUpdate so fees/estimates land the way FormClaimEdit shows.
 * The read side mirrors the account grid's eligibility rules:
 * Procedures.NoBillIns and Procedures.IsAlreadyAttachedToClaim per ordinal.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingClaimBuilderModule {
	record NewClaimDto(List<long> ProcNums,int Ordinal);

	public static void Map(WebApplication app) {
		//Completed procedures not yet attached to a claim for the patient's insurance,
		//with per-ordinal availability flags (same NoBillIns / IsAlreadyAttachedToClaim
		//rules the desktop applies before letting a proc onto a claim).
		app.MapGet("/api/patients/{patNum:long}/unbilled-procs",(long patNum) => {
			Patient patient=Patients.GetPat(patNum);
			if(patient==null) {
				return Results.NotFound();
			}
			Family family=Patients.GetFamily(patNum);
			AccountModules.CreateClaimData data=AccountModules.GetCreateClaimData(patient,family);
			InsTarget targetPri=GetInsTarget(data,1);
			InsTarget targetSec=GetInsTarget(data,2);
			List<Procedure> listProcsComplete=data.ListProcs.FindAll(x => x.ProcStatus==ProcStat.C);
			return Results.Json(new {
				patNum=patient.PatNum,
				patient=patient.GetNameLF(),
				insurance=new[] { targetPri,targetSec }.Where(x => x!=null).Select(x => new {
					ordinal=x.Ordinal,
					label=x.Label,
					carrier=x.CarrierName,
					planNum=x.InsPlan_.PlanNum,
					insSubNum=x.InsSub_.InsSubNum,
					subscriberId=x.InsSub_.SubscriberID??"",
				}),
				procs=listProcsComplete
					.Select(x => new {
						proc=x,
						canBillPri=CanBill(x,data,targetPri),
						canBillSec=CanBill(x,data,targetSec),
					})
					.Where(x => x.canBillPri || x.canBillSec)
					.OrderBy(x => x.proc.ProcDate)
					.Select(x => {
						ProcedureCode procedureCode=ProcedureCodes.GetProcCode(x.proc.CodeNum);
						return new {
							procNum=x.proc.ProcNum,
							procDate=x.proc.ProcDate.Year>1880?x.proc.ProcDate.ToString("yyyy-MM-dd"):"",
							procCode=procedureCode.ProcCode,
							descript=procedureCode.Descript,
							toothNum=x.proc.ToothNum??"",
							surf=x.proc.Surf??"",
							provider=Providers.GetAbbr(x.proc.ProvNum),
							fee=x.proc.ProcFee,
							canBillPri=x.canBillPri,
							canBillSec=x.canBillSec,
						};
					}),
			});
		});

		//Create a claim from selected completed procedures, the way the desktop does:
		//status "W" (waiting to send), PlanNum/InsSubNum resolved from the patplan
		//ordinal, claimprocs attached and computed by AccountModules.CreateClaim
		//(which ends with Claims.CalculateAndUpdate), then the same security log
		//entry ClaimL.CreateClaimFromWrapper writes.
		app.MapPost("/api/patients/{patNum:long}/claims",(long patNum,NewClaimDto dto) => {
			Patient patient=Patients.GetPat(patNum);
			if(patient==null) {
				return Results.NotFound();
			}
			if(dto.ProcNums==null || dto.ProcNums.Count==0) {
				return Results.Text("Please select the procedures to attach to the claim first.",statusCode:400);
			}
			if(dto.Ordinal!=1 && dto.Ordinal!=2) {
				return Results.Text("Ordinal must be 1 (primary) or 2 (secondary).",statusCode:400);
			}
			//These caches are not part of the startup Cache.Refresh set but are read
			//inside AccountModules.CreateClaim (ortho span check, sec ins filing code).
			CovCats.RefreshCache();
			CovSpans.RefreshCache();
			InsFilingCodes.RefreshCache();
			Family family=Patients.GetFamily(patNum);
			AccountModules.CreateClaimData data=AccountModules.GetCreateClaimData(patient,family);
			if(data.ListPatPlans.Count==0) {
				return Results.Text("Patient does not have insurance.",statusCode:400);
			}
			InsTarget target=GetInsTarget(data,dto.Ordinal);
			if(target==null) {
				return Results.Text(dto.Ordinal==2?"Patient does not have secondary insurance.":"Patient does not have primary insurance.",statusCode:400);
			}
			List<Procedure> listProcsSelected=data.ListProcs.FindAll(x => dto.ProcNums.Contains(x.ProcNum));
			List<long> listProcNumsMissing=dto.ProcNums.FindAll(x => !listProcsSelected.Select(y => y.ProcNum).Contains(x));
			if(listProcNumsMissing.Count>0) {
				return Results.Text("Procedure(s) not found for this patient: "+string.Join(",",listProcNumsMissing),statusCode:400);
			}
			if(listProcsSelected.Any(x => x.ProcStatus!=ProcStat.C)) {
				return Results.Text("Only completed procedures can be attached to a claim.",statusCode:400);
			}
			//Same NoBillIns split ClaimL.CreateClaim performs before calling the business method.
			List<Procedure> listProcsBillIns=listProcsSelected.FindAll(x => !Procedures.NoBillIns(x,data.ListClaimProcs,target.InsPlan_.PlanNum));
			List<Procedure> listProcsNoBillIns=listProcsSelected.FindAll(x => !listProcsBillIns.Select(y => y.ProcNum).Contains(x.ProcNum));
			if(listProcsBillIns.Count==0) {
				return Results.Text("No claim will be created due to all procedures being marked as NoBillIns.",statusCode:400);
			}
			//Same shell ClaimL.CreateClaimFromWrapper builds: waiting to send, dated today.
			Claim claim=new Claim();
			claim.DateSent=DateTime.Today;
			claim.DateSentOrig=DateTime.MinValue;
			claim.ClaimStatus="W";
			try {
				claim=AccountModules.CreateClaim(claim,target.ClaimType,data.ListPatPlans,data.ListInsPlans,data.ListClaimProcs,
					data.ListProcs,data.ListInsSubs,patient,data.PatNote,listProcsBillIns,"",target.InsPlan_,target.InsSub_,Relat.Self);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			if(claim==null || claim.ClaimNum==0) {
				return Results.Text("The claim could not be created.",statusCode:400);
			}
			//Same log entry the desktop writes after a successful create.
			SecurityLogs.MakeLogEntry(EnumPermType.ClaimEdit,patient.PatNum,"New claim created for "+patient.LName+","+patient.FName,
				claim.ClaimNum,claim.SecDateTEdit);
			List<ClaimProc> listClaimProcsAttached=ClaimProcs.RefreshForClaim(claim.ClaimNum);
			return Results.Json(new {
				claimNum=claim.ClaimNum,
				claimType=claim.ClaimType,
				claimStatus=claim.ClaimStatus,
				carrier=target.CarrierName,
				dateService=claim.DateService.Year>1880?claim.DateService.ToString("yyyy-MM-dd"):"",
				claimFee=claim.ClaimFee,
				insPayEst=claim.InsPayEst,
				procCount=listClaimProcsAttached.Count,
				excludedNoBillIns=listProcsNoBillIns.Select(x => ProcedureCodes.GetProcCode(x.CodeNum).ProcCode),
			});
		});
	}

	///<summary>Resolved insurance for a requested ordinal (1 or 2), the way ClaimL.CreateClaim resolves it via PatPlans.GetOrdinal.</summary>
	private class InsTarget {
		public int Ordinal;
		public string ClaimType;//P, S, or Med
		public string Label;
		public InsPlan InsPlan_;
		public InsSub InsSub_;
		public string CarrierName;
	}

	///<summary>Returns null if the patient has no plan at that ordinal. Ordinal 1 falls back to
	///a medical plan when the patient has medical insurance and no dental, exactly like
	///ClaimL.CreateClaimFromWrapper's claimType="Med" branch.</summary>
	private static InsTarget GetInsTarget(AccountModules.CreateClaimData data,int ordinal) {
		int ordinalPri=PatPlans.GetOrdinal(PriSecMed.Primary,data.ListPatPlans,data.ListInsPlans,data.ListInsSubs);
		int ordinalSec=PatPlans.GetOrdinal(PriSecMed.Secondary,data.ListPatPlans,data.ListInsPlans,data.ListInsSubs);
		int ordinalMed=PatPlans.GetOrdinal(PriSecMed.Medical,data.ListPatPlans,data.ListInsPlans,data.ListInsSubs);
		int ordinalPlan;
		string claimType;
		string label;
		if(ordinal==1) {
			if(ordinalMed>0 && ordinalPri==0 && ordinalSec==0) {//medical insurance and no dental
				ordinalPlan=ordinalMed;
				claimType="Med";
				label="Medical";
			}
			else if(ordinalPri>0) {
				ordinalPlan=ordinalPri;
				claimType="P";
				label="Primary";
			}
			else {
				return null;
			}
		}
		else if(ordinal==2 && ordinalSec>0) {
			ordinalPlan=ordinalSec;
			claimType="S";
			label="Secondary";
		}
		else {
			return null;
		}
		InsSub insSub=InsSubs.GetSub(PatPlans.GetInsSubNum(data.ListPatPlans,ordinalPlan),data.ListInsSubs);
		InsPlan insPlan=InsPlans.GetPlan(insSub.PlanNum,data.ListInsPlans);
		if(insPlan==null) {
			return null;
		}
		Carrier carrier=Carriers.GetCarrierDB(insPlan.CarrierNum);
		InsTarget insTarget=new InsTarget();
		insTarget.Ordinal=ordinal;
		insTarget.ClaimType=claimType;
		insTarget.Label=label;
		insTarget.InsPlan_=insPlan;
		insTarget.InsSub_=insSub;
		insTarget.CarrierName=carrier?.CarrierName??"";
		return insTarget;
	}

	///<summary>True if the procedure can still be billed to this insurance: not marked
	///NoBillIns for the plan and not already attached to a claim for the inssub.</summary>
	private static bool CanBill(Procedure procedure,AccountModules.CreateClaimData data,InsTarget insTarget) {
		if(insTarget==null) {
			return false;
		}
		if(Procedures.NoBillIns(procedure,data.ListClaimProcs,insTarget.InsPlan_.PlanNum)) {
			return false;
		}
		if(Procedures.IsAlreadyAttachedToClaim(procedure,data.ListClaimProcs,insTarget.InsSub_.InsSubNum)) {
			return false;
		}
		return true;
	}
}
