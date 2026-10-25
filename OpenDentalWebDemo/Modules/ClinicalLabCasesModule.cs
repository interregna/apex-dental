/*
 * Clinical lab-cases module: web slice of FormLabCases / FormLabCaseEdit /
 * FormLabCaseSelect.  Tracks a patient's dental-lab cases — which lab, the due
 * date on the lab slip, when the case went out (sent), came back (received) and
 * was quality-checked, plus the free-text instructions and the appointment it
 * is attached to.  All writes go through the real business layer —
 * LabCases.Insert / LabCases.Update / LabCases.Delete and Laboratories.Refresh —
 * the same calls the desktop lab windows make.  Note: the desktop FormLabCase*
 * windows do NOT stamp SecurityLogs, so neither do we (there is no lab-case
 * permission type); the business-layer DateTStamp column still records the edit.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalLabCasesModule {
	//procNum is accepted but not persisted: a LabCase attaches to an appointment
	//(AptNum), not to a procedure — the LabCase table has no ProcNum column.
	record NewLabCaseDto(long LaboratoryNum,string DateDue,string Instructions,long AptNum);
	record EditLabCaseDto(string DateSent,string DateRecd,string DateChecked,string DateDue,string Instructions);

	//Replicates FormLabCases' status column, which is derived from the date fields
	//(there is no stored status): quality-checked, else received, else sent, else not sent.
	static string LabCaseStatus(LabCase labCase) {
		if(labCase.DateTimeChecked.Year>1880) {
			return "Quality Checked";
		}
		if(labCase.DateTimeRecd.Year>1880) {
			return "Received";
		}
		if(labCase.DateTimeSent.Year>1880) {
			return "Sent";
		}
		return "Not Sent";
	}

	static string DateStr(DateTime dt) {
		return dt.Year>1880?dt.ToString("yyyy-MM-dd"):"";
	}

	public static void Map(WebApplication app) {
		//Lab dropdown source: FormLabCaseEdit's lab list.  Laboratories.Refresh hits
		//the db live (no startup cache), so this returns empty cleanly when no labs
		//have been set up yet.  Hidden labs are excluded, matching the desktop.
		app.MapGet("/api/laboratories",() => {
			List<Laboratory> listLabs=Laboratories.Refresh().Where(x => !x.IsHidden).ToList();
			return Results.Json(listLabs.Select(x => new {
				laboratoryNum=x.LaboratoryNum,
				description=x.Description??"",
				phone=x.Phone??"",
			}));
		});

		//A patient's lab cases — the FormLabCases grid narrowed to one patient,
		//including cases not attached to any appointment.  GetLabCasesForApi with
		//aptNum/plannedAptNum=-1 means "no appointment filter".
		app.MapGet("/api/patients/{patNum:long}/labcases",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<LabCase> listLabCases=LabCases.GetLabCasesForApi(int.MaxValue,0,patNum,0,-1,-1,0);
			return Results.Json(listLabCases
				.OrderByDescending(x => x.DateTimeDue)
				.ThenByDescending(x => x.LabCaseNum)
				.Select(x => {
					Laboratory lab=Laboratories.GetOne(x.LaboratoryNum);
					string procDescript="";
					if(x.AptNum>0) {
						Appointment apt=Appointments.GetOneApt(x.AptNum);
						procDescript=apt?.ProcDescript??"";
					}
					return new {
						labCaseNum=x.LabCaseNum,
						laboratoryNum=x.LaboratoryNum,
						labName=lab?.Description??"",
						dateDue=DateStr(x.DateTimeDue),
						dateSent=DateStr(x.DateTimeSent),
						dateRecd=DateStr(x.DateTimeRecd),
						dateChecked=DateStr(x.DateTimeChecked),
						status=LabCaseStatus(x),
						instructions=x.Instructions??"",
						procDescript,
					};
				}));
		});

		//Create: FormLabCaseSelect's "new unattached lab case" flow — stamps the
		//creation time and the patient's primary provider, then LabCases.Insert.
		app.MapPost("/api/patients/{patNum:long}/labcases",(long patNum,NewLabCaseDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(dto.LaboratoryNum<=0 || Laboratories.GetOne(dto.LaboratoryNum)==null) {
				return Results.Text("Please select a lab first.",statusCode:400);
			}
			LabCase labCase=new LabCase();
			labCase.PatNum=patNum;
			labCase.LaboratoryNum=dto.LaboratoryNum;
			labCase.ProvNum=pat.PriProv;
			labCase.AptNum=dto.AptNum>0?dto.AptNum:0;
			labCase.DateTimeCreated=DateTime.Now;
			if(DateTime.TryParse(dto.DateDue,out DateTime dateDue)) {
				labCase.DateTimeDue=dateDue;
			}
			labCase.Instructions=dto.Instructions??"";
			long labCaseNum=LabCases.Insert(labCase);//real business insert; sets DateTStamp
			return Results.Json(new { labCaseNum });
		});

		//Edit: FormLabCaseEdit's save path — the user-editable fields are the date
		//milestones (sent/received/quality-checked/due) and the instructions.  An
		//empty date clears the milestone (DateTime.MinValue), same as the desktop.
		//The displayed status is recomputed from these dates.
		app.MapPut("/api/labcases/{labCaseNum:long}",(long labCaseNum,EditLabCaseDto dto) => {
			LabCase labCase=LabCases.GetOne(labCaseNum);
			if(labCase==null) {
				return Results.NotFound();
			}
			labCase.DateTimeSent=DateTime.TryParse(dto.DateSent,out DateTime dSent)?dSent:DateTime.MinValue;
			labCase.DateTimeRecd=DateTime.TryParse(dto.DateRecd,out DateTime dRecd)?dRecd:DateTime.MinValue;
			labCase.DateTimeChecked=DateTime.TryParse(dto.DateChecked,out DateTime dChk)?dChk:DateTime.MinValue;
			labCase.DateTimeDue=DateTime.TryParse(dto.DateDue,out DateTime dDue)?dDue:DateTime.MinValue;
			if(dto.Instructions!=null) {
				labCase.Instructions=dto.Instructions;
			}
			LabCases.Update(labCase);//real business update; refreshes DateTStamp
			return Results.Json(new { labCaseNum=labCase.LabCaseNum,status=LabCaseStatus(labCase) });
		});

		//Delete: LabCases.Delete validates dependencies (an attached lab slip blocks
		//deletion) and throws when not allowed, same as FormLabCaseEdit's delete button.
		app.MapDelete("/api/labcases/{labCaseNum:long}",(long labCaseNum) => {
			LabCase labCase=LabCases.GetOne(labCaseNum);
			if(labCase==null) {
				return Results.NotFound();
			}
			try {
				LabCases.Delete(labCaseNum);
			}
			catch(Exception ex) {
				return Results.Text("Cannot delete: "+ex.Message,statusCode:400);
			}
			return Results.Json(new { deleted=labCaseNum });
		});
	}
}
