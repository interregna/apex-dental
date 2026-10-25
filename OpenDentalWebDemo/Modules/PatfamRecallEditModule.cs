/*
 * Per-patient recall editor (patfam-recall-edit): FormRecallsPat / FormRecallEdit.
 * Lists a patient's recalls via Recalls.GetList, edits status/due date/interval/
 * note/disable fields through Recalls.Update(recall,recallOld), adds new recalls
 * via Recalls.Insert, and re-derives due dates with Recalls.Synch — the same call
 * FormRecallsPat makes when it opens.  Status choices are the practice's
 * DefCat.RecallUnschedStatus definitions; types come from the RecallTypes cache.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class PatfamRecallEditModule {
	record RecallEditDto(long RecallTypeNum,string DateDue,int IntervalYears,int IntervalMonths,int IntervalWeeks,int IntervalDays,
		long RecallStatus,string Note,bool IsDisabled,string DisableUntilDate,double DisableUntilBalance,string Priority);

	public static void Map(WebApplication app) {
		//Lookup lists for the editor: recall types (full cache, hidden included so existing
		//rows always resolve) and the RecallUnschedStatus definitions used for recall.RecallStatus.
		app.MapGet("/api/recall-types",() => {
			List<RecallType> listRecallTypes=RecallTypes.GetDeepCopy();
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.RecallUnschedStatus,true);
			return Results.Json(new {
				types=listRecallTypes.Select(x => new {
					recallTypeNum=x.RecallTypeNum,
					description=x.Description,
					defaultInterval=x.DefaultInterval.ToString(),
					isSpecial=RecallTypes.IsSpecialRecallType(x.RecallTypeNum),
				}),
				statuses=listDefs.Select(x => new {
					defNum=x.DefNum,
					name=x.ItemName,
				}),
			});
		});

		//The patient's recalls (FormRecallsPat grid).  Read-only; the desktop form synchs
		//on open — the UI does that explicitly through the synch endpoint below.
		app.MapGet("/api/patients/{patNum:long}/recalls",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.RecallUnschedStatus,false);
			List<Recall> listRecalls=Recalls.GetList(patNum);
			return Results.Json(listRecalls.OrderBy(x => x.RecallTypeNum).Select(x => new {
				recallNum=x.RecallNum,
				recallTypeNum=x.RecallTypeNum,
				recallType=RecallTypes.GetDescription(x.RecallTypeNum),
				dateDue=FmtDate(x.DateDue),
				dateDueCalc=FmtDate(x.DateDueCalc),
				datePrevious=FmtDate(x.DatePrevious),
				dateScheduled=FmtDate(x.DateScheduled),
				interval=x.RecallInterval.ToString(),
				intervalYears=x.RecallInterval.Years,
				intervalMonths=x.RecallInterval.Months,
				intervalWeeks=x.RecallInterval.Weeks,
				intervalDays=x.RecallInterval.Days,
				recallStatus=x.RecallStatus,
				statusName=x.RecallStatus==0?"":Defs.GetName(DefCat.RecallUnschedStatus,x.RecallStatus,listDefs),
				note=x.Note??"",
				isDisabled=x.IsDisabled,
				disableUntilDate=FmtDate(x.DisableUntilDate),
				disableUntilBalance=x.DisableUntilBalance,
				priority=x.Priority.ToString(),
			}));
		});

		//Add a recall (FormRecallEdit in IsNew mode).  One recall per type per patient.
		app.MapPost("/api/patients/{patNum:long}/recalls",(long patNum,RecallEditDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(dto.RecallTypeNum==0) {
				return Results.Text("Recall type is required",statusCode:400);
			}
			if(Recalls.GetList(patNum).Any(x => x.RecallTypeNum==dto.RecallTypeNum)) {
				return Results.Text("Patient already has a recall of that type",statusCode:400);
			}
			Recall recall=new Recall();
			recall.PatNum=patNum;
			recall.RecallTypeNum=dto.RecallTypeNum;
			ApplyDto(recall,dto);
			if(recall.RecallInterval.ToInt()==0) {
				//Same default the desktop dialogs use: the type's standard interval.
				recall.RecallInterval=RecallTypes.GetInterval(dto.RecallTypeNum);
			}
			long recallNum=Recalls.Insert(recall);
			SecurityLogs.MakeLogEntry(EnumPermType.RecallEdit,patNum,"Recall added from the web recall editor.");
			return Results.Json(new { recallNum });
		});

		//Edit a recall (FormRecallEdit OK click): status, due date, interval override,
		//note, disable flags, priority.  Old-copy overload so only changed fields write.
		app.MapPut("/api/recalls/{recallNum:long}",(long recallNum,RecallEditDto dto) => {
			Recall recall=Recalls.GetRecall(recallNum);
			if(recall==null) {
				return Results.NotFound();
			}
			Recall recallOld=recall.Copy();
			if(dto.RecallTypeNum>0) {
				recall.RecallTypeNum=dto.RecallTypeNum;
			}
			ApplyDto(recall,dto);
			bool updated=Recalls.Update(recall,recallOld);
			if(updated) {
				SecurityLogs.MakeLogEntry(EnumPermType.RecallEdit,recall.PatNum,"Recall edited from the web recall editor.");
			}
			return Results.Json(new { updated });
		});

		//Re-derive due dates from trigger procedures — what FormRecallsPat does in
		//FillGrid every time it opens.  Creates/updates rows as needed.
		app.MapPost("/api/patients/{patNum:long}/recalls/synch",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Recalls.Synch(patNum);
			return Results.Json(new { synched=true });
		});
	}

	static string FmtDate(DateTime date) {
		return date.Year>1880?date.ToString("yyyy-MM-dd"):"";
	}

	static void ApplyDto(Recall recall,RecallEditDto dto) {
		recall.DateDue=DateTime.TryParse(dto.DateDue,out DateTime dateDue)?dateDue:DateTime.MinValue;
		recall.RecallInterval=new Interval(dto.IntervalDays,dto.IntervalWeeks,dto.IntervalMonths,dto.IntervalYears);
		recall.RecallStatus=dto.RecallStatus;
		recall.Note=dto.Note??"";
		recall.IsDisabled=dto.IsDisabled;
		recall.DisableUntilDate=DateTime.TryParse(dto.DisableUntilDate,out DateTime dateDisableUntil)?dateDisableUntil:DateTime.MinValue;
		recall.DisableUntilBalance=dto.DisableUntilBalance;
		recall.Priority=Enum.TryParse(dto.Priority,out RecallPriority priority)?priority:RecallPriority.Normal;
		if(recall.IsDisabled) {
			//Table contract: "When setting disabled, this field [DateDue] will also be cleared."
			recall.DateDue=DateTime.MinValue;
		}
	}
}
