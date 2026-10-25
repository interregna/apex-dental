/*
 * Scheduling worklists: recall due list + ASAP list (FormRecallList / FormASAP).
 * Both desktop grids ride the same Recalls.GetRecallList business method — the
 * isAsap toggle flips between "patients due for recall" and "recalls flagged
 * ASAP" — and Appointments.RefreshASAP supplies the ASAP-flagged appointments
 * that want an earlier slot. Read-only worklists; booking goes through the
 * existing POST /api/appointments.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedRecallAsapModule {
	public static void Map(WebApplication app) {
		//Recall due list (FormRecallList grid). Optional filters mirror the desktop form:
		//date range, provider, sort order, family grouping. asap=true returns the
		//ASAP-flagged recalls instead (the recall half of FormASAP).
		app.MapGet("/api/recalls",(string? from,string? to,long provNum=0,bool groupByFamilies=false,string sortBy="DueDate",bool asap=false) => {
			DateTime dateFrom=DateTime.TryParse(from,out DateTime dateFromParsed)?dateFromParsed:DateTime.Today.AddYears(-3);
			DateTime dateTo=DateTime.TryParse(to,out DateTime dateToParsed)?dateToParsed:DateTime.Today;
			RecallListSort recallListSort=Enum.TryParse(sortBy,true,out RecallListSort sortParsed)?sortParsed:RecallListSort.DueDate;
			//clinicNum -1 = all clinics, siteNum 0 = no site filter, showReminders All + maxReminders 0 = no reminder-count filter.
			DataTable table=Recalls.GetRecallList(dateFrom,dateTo,groupByFamilies,provNum,-1,0,
				recallListSort,RecallListShowNumberReminders.All,0,isAsap:asap);
			return Results.Json(table.Rows.OfType<DataRow>().Select(x => new {
				recallNum=x["RecallNum"].ToString(),
				patNum=x["PatNum"].ToString(),
				patient=x["patientName"].ToString(),
				age=x["age"].ToString(),
				dueDate=x["dueDate"].ToString(),
				recallType=x["recallType"].ToString(),
				interval=x["recallInterval"].ToString(),
				status=x["status"].ToString(),
				note=x["Note"].ToString(),
				numberOfReminders=x["numberOfReminders"].ToString(),
				dateLastReminder=x["dateLastReminder"].ToString(),
				contactMethod=x["contactMethod"].ToString(),
				phone=x["WirelessPhone"].ToString(),
				email=x["Email"].ToString(),
				billingType=x["billingType"].ToString(),
				guarantor=x["Guarantor"].ToString(),
			}));
		});

		//ASAP list (FormASAP grid): appointments flagged ApptPriority.ASAP that want an
		//earlier slot. Empty statuses defaults inside RefreshASAP to Scheduled,
		//UnschedList, Broken and Planned — same as the desktop form.
		app.MapGet("/api/asap",(long provNum=0,string? statuses=null) => {
			List<ApptStatus> listApptStatuses=(statuses??"")
				.Split(',',StringSplitOptions.RemoveEmptyEntries)
				.Select(x => Enum.TryParse(x,true,out ApptStatus status)?status:ApptStatus.None)
				.Where(x => x!=ApptStatus.None)
				.ToList();
			//clinicNum -1 = all clinics, siteNum 0 = no site filter.
			List<Appointment> listAppointments=Appointments.RefreshASAP(provNum,0,-1,listApptStatuses);
			Patient[] arrayPatients=Patients.GetMultPats(listAppointments.Select(x => x.PatNum).Distinct().ToList());
			return Results.Json(listAppointments.Select(x => {
				Patient pat=arrayPatients.FirstOrDefault(y => y.PatNum==x.PatNum);
				return new {
					aptNum=x.AptNum,
					patNum=x.PatNum,
					patient=pat?.GetNameLF()??"",
					phone=pat?.WirelessPhone??"",
					dateTime=x.AptDateTime.Year>1880?x.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
					status=x.AptStatus.ToString(),
					provAbbr=Providers.GetAbbr(x.ProvNum),
					minutes=(x.Pattern??"").Length*5,
					procDescript=x.ProcDescript,
					note=x.Note,
				};
			}));
		});
	}
}
