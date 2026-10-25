/*
 * Insurance benefit verification tracking: web replacement for FormInsVerificationList
 * and the insurance-verify workflow.  Lists insplans/patplans due for verification and
 * records a verification (who/when).  All data flows through the real business layer:
 *   InsVerifies.GetAll / InsVerifies.GetOne / InsVerifies.Update /
 *   InsVerifyHists.InsertFromInsVerify (the same "mark verified" write FormInsVerificationList.OnVerify
 *   makes: stamps DateLastVerified, writes an insverifyhist row, then blanks the reusable insverify row) —
 *   enriched with InsPlans.RefreshOne / Carriers.GetCarrierDB / PatPlans.GetByPatPlanNum /
 *   InsSubs.GetOne / Patients.GetPat / Userods.GetName.
 *
 * NOTE ON DATA: insverify rows are created by the nightly process / on plan creation
 *   (InsVerifies.InsertForPlanNum / InsertForPatPlanNum).  If none exist for the demo plans,
 *   GET returns an empty list cleanly.  To seed one for a plan:  InsVerifies.InsertForPlanNum(planNum)
 *   (VerifyType=InsuranceBenefit, FKey=insplan.PlanNum) or InsVerifies.InsertForPatPlanNum(patPlanNum)
 *   (VerifyType=PatientEnrollment, FKey=patplan.PatPlanNum).  We never fabricate rows here.
 *
 * insverify is not a cached type and has no InvalidType, so (like the WinForms form) no Signalod is
 * set; the verification is captured via SecurityLogs.MakeLogEntry(InsuranceVerification).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class InsVerificationModule {
	record VerifyDto(string Note,string NextDueDate);
	record EditDto(string Note,long? UserNum);

	public static void Map(WebApplication app) {
		//Verification worklist (FormInsVerificationList grid equivalent).  type=Benefit|PatEnroll,
		//optional daysDue window (defaults to the matching eligibility pref).  Enriched with carrier/patient.
		app.MapGet("/api/insverify",(string type,int? daysDue) => {
			VerifyTypes verifyType=ParseType(type);
			int windowDays=daysDue??(verifyType==VerifyTypes.PatientEnrollment
				?PrefC.GetInt(PrefName.InsVerifyPatientEnrollmentDays)
				:PrefC.GetInt(PrefName.InsVerifyBenefitEligibilityDays));
			DateTime cutoff=DateTime.Today.AddDays(-Math.Max(0,windowDays));
			List<InsVerify> listInsVerifies=InsVerifies.GetAll().FindAll(x => x.VerifyType==verifyType);
			return Results.Json(listInsVerifies
				.OrderBy(x => x.DateLastVerified)
				.Select(x => ToRow(x,cutoff)));
		});

		//Mark verified (FormInsVerificationList.OnVerify): stamp DateLastVerified=today, record who
		//via UserNum=Security.CurUser, write the insverifyhist history row, then the live insverify row
		//is reused (UserNum/Note blanked) — exactly what InsVerifyHists.InsertFromInsVerify does.
		app.MapPost("/api/insverify/{insVerifyNum:long}/verify",(long insVerifyNum,VerifyDto dto) => {
			InsVerify insVerify=InsVerifies.GetOne(insVerifyNum);
			if(insVerify==null) {
				return Results.NotFound();
			}
			string note=dto?.Note??"";
			if(!string.IsNullOrWhiteSpace(dto?.NextDueDate)) {
				note=(note.Length>0?note+" ":"")+"(next due: "+dto.NextDueDate.Trim()+")";
			}
			//Set who/note BEFORE the hist insert so the insverifyhist row captures them; the live row is
			//then blanked for reuse by InsertFromInsVerify (matches the desktop verify behavior).
			insVerify.UserNum=Security.CurUser.UserNum;
			if(note.Length>0) {
				insVerify.Note=note;
			}
			insVerify.DateLastVerified=DateTime.Today;
			InsVerifyHists.InsertFromInsVerify(insVerify);//inserts history + Updates the insverify record
			SecurityLogs.MakeLogEntry(EnumPermType.InsuranceVerification,GetPatNum(insVerify),
				"Insurance "+(insVerify.VerifyType==VerifyTypes.PatientEnrollment?"enrollment":"benefits")
				+" marked verified from the web verification list.");
			return Results.Json(new { insVerifyNum,dateLastVerified=DateTime.Today.ToString("yyyy-MM-dd") });
		});

		//Edit note / assignment (FormInsVerificationList assign + set-status writes): sets Note/UserNum
		//and stamps DateLastAssigned=today, then InsVerifies.Update.
		app.MapPut("/api/insverify/{insVerifyNum:long}",(long insVerifyNum,EditDto dto) => {
			InsVerify insVerify=InsVerifies.GetOne(insVerifyNum);
			if(insVerify==null) {
				return Results.NotFound();
			}
			if(dto?.Note!=null) {
				insVerify.Note=dto.Note;
			}
			if(dto?.UserNum!=null) {
				insVerify.UserNum=dto.UserNum.Value;
			}
			insVerify.DateLastAssigned=DateTime.Today;
			InsVerifies.Update(insVerify);
			SecurityLogs.MakeLogEntry(EnumPermType.InsuranceVerification,GetPatNum(insVerify),
				"Insurance verification note/assignment edited from the web verification list.");
			return Results.Json(new { insVerifyNum });
		});
	}

	///<summary>Maps the type query param onto a VerifyTypes.  Anything enrollment/patient-ish is
	///PatientEnrollment; everything else (default) is InsuranceBenefit.</summary>
	private static VerifyTypes ParseType(string type) {
		string t=(type??"").Trim().ToLower();
		if(t.Contains("enroll") || t.Contains("pat")) {
			return VerifyTypes.PatientEnrollment;
		}
		return VerifyTypes.InsuranceBenefit;
	}

	///<summary>Enriches one insverify row with carrier/patient/user info for the worklist grid.</summary>
	private static object ToRow(InsVerify insVerify,DateTime cutoff) {
		long planNum=0;
		long patNum=0;
		string carrier="";
		string patient="";
		if(insVerify.VerifyType==VerifyTypes.InsuranceBenefit) {
			//FKey links to insplan.PlanNum.
			planNum=insVerify.FKey;
			InsPlan insPlan=InsPlans.RefreshOne(insVerify.FKey);
			if(insPlan!=null) {
				carrier=Carriers.GetCarrierDB(insPlan.CarrierNum)?.CarrierName??"";
			}
		}
		else {//PatientEnrollment: FKey links to patplan.PatPlanNum.
			PatPlan patPlan=PatPlans.GetByPatPlanNum(insVerify.FKey);
			if(patPlan!=null) {
				patNum=patPlan.PatNum;
				patient=Patients.GetPat(patPlan.PatNum)?.GetNameLF()??"";
				InsSub insSub=InsSubs.GetOne(patPlan.InsSubNum);
				if(insSub!=null) {
					InsPlan insPlan=InsPlans.RefreshOne(insSub.PlanNum);
					if(insPlan!=null) {
						planNum=insPlan.PlanNum;
						carrier=Carriers.GetCarrierDB(insPlan.CarrierNum)?.CarrierName??"";
					}
				}
			}
		}
		bool neverVerified=insVerify.DateLastVerified.Year<1880;
		bool isDue=neverVerified || insVerify.DateLastVerified.Date<cutoff;
		return new {
			insVerifyNum=insVerify.InsVerifyNum,
			verifyType=insVerify.VerifyType.ToString(),
			planNum,
			patNum,
			carrier,
			patient,
			dateLastVerified=neverVerified?"":insVerify.DateLastVerified.ToString("yyyy-MM-dd"),
			dateLastAssigned=insVerify.DateLastAssigned.Year<1880?"":insVerify.DateLastAssigned.ToString("yyyy-MM-dd"),
			userNum=insVerify.UserNum,
			userName=insVerify.UserNum==0?"":Userods.GetName(insVerify.UserNum),
			note=insVerify.Note??"",
			isDue,
		};
	}

	///<summary>Best-effort patient number for the security log (0 for plan-level benefit rows).</summary>
	private static long GetPatNum(InsVerify insVerify) {
		if(insVerify.VerifyType==VerifyTypes.PatientEnrollment) {
			return PatPlans.GetByPatPlanNum(insVerify.FKey)?.PatNum??0;
		}
		return 0;
	}
}
