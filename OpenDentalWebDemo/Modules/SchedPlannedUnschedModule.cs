/*
 * Scheduling worklists: planned-appointment tracker + unscheduled list
 * (FormTrackNext / FormUnsched / FormUnschedListPatient / FormApptsOther).
 * The tracker grid rides Appointments.RefreshPlannedTracker (planned appts not
 * yet converted), the unscheduled grid rides Appointments.RefreshUnsched
 * (unscheduled + optionally broken appts), and the per-patient slice rides
 * Appointments.GetUnschedApptsForPat. One-click scheduling converts a planned
 * appt into a real scheduled appt via Appointments.SchedulePlannedApt — the
 * same multi-entity flow (copy appt, move procs, appt fields, lab cases) the
 * desktop pinboard drop performs.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedPlannedUnschedModule {
	record SchedulePlannedDto(string DateTime,long OpNum);

	public static void Map(WebApplication app) {
		//Planned tracker (FormTrackNext grid): planned appointments for active patients
		//that no scheduled appointment references yet. Filters mirror the desktop form:
		//provider, proc-code range, date range, sort order. clinicNum -1 = all clinics,
		//siteNum 0 = no site filter. scheduledAptNum is filled via GetScheduledPlannedApt
		//so the UI can show when a planned appt got converted between refreshes.
		app.MapGet("/api/planned",(string? from,string? to,long provNum=0,string orderBy="date",string codeStart="",string codeEnd="") => {
			DateTime dateStart=DateTime.TryParse(from,out DateTime dateStartParsed)?dateStartParsed:DateTime.MinValue;
			DateTime dateEnd=DateTime.TryParse(to,out DateTime dateEndParsed)?dateEndParsed:DateTime.MinValue;
			if(!string.IsNullOrEmpty(codeStart) && string.IsNullOrEmpty(codeEnd)) {
				codeEnd=codeStart;//single-code filter, same as entering one code in both boxes on desktop
			}
			List<Appointment> listAppointments=Appointments.RefreshPlannedTracker(orderBy,provNum,0,-1,codeStart,codeEnd,dateStart,dateEnd);
			Patient[] arrayPatients=Patients.GetMultPats(listAppointments.Select(x => x.PatNum).Distinct().ToList());
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.RecallUnschedStatus,true);
			return Results.Json(listAppointments.Select(x => {
				Patient pat=arrayPatients.FirstOrDefault(y => y.PatNum==x.PatNum);
				Appointment appointmentSched=Appointments.GetScheduledPlannedApt(x.AptNum);
				return new {
					aptNum=x.AptNum,
					patNum=x.PatNum,
					patient=pat?.GetNameLF()??"",
					phone=pat?.WirelessPhone??"",
					dateTime=x.AptDateTime.Year>1880?x.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
					provAbbr=Providers.GetAbbr(x.ProvNum),
					minutes=(x.Pattern??"").Length*5,
					unschedStatus=Defs.GetName(DefCat.RecallUnschedStatus,x.UnschedStatus,listDefs),
					procDescript=x.ProcDescript,
					note=x.Note,
					scheduledAptNum=appointmentSched?.AptNum??0,
					scheduledFor=appointmentSched!=null && appointmentSched.AptDateTime.Year>1880
						?appointmentSched.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
				};
			}));
		});

		//Unscheduled list (FormUnsched grid): appointments parked on the unscheduled
		//list, optionally including broken ones. Same filter set as the tracker.
		app.MapGet("/api/unsched",(string? from,string? to,long provNum=0,bool includeBroken=true,string orderBy="date",string codeStart="",string codeEnd="") => {
			DateTime dateStart=DateTime.TryParse(from,out DateTime dateStartParsed)?dateStartParsed:DateTime.MinValue;
			DateTime dateEnd=DateTime.TryParse(to,out DateTime dateEndParsed)?dateEndParsed:DateTime.MinValue;
			if(!string.IsNullOrEmpty(codeStart) && string.IsNullOrEmpty(codeEnd)) {
				codeEnd=codeStart;
			}
			List<Appointment> listAppointments=Appointments.RefreshUnsched(orderBy,provNum,0,includeBroken,-1,codeStart,codeEnd,dateStart,dateEnd);
			Patient[] arrayPatients=Patients.GetMultPats(listAppointments.Select(x => x.PatNum).Distinct().ToList());
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.RecallUnschedStatus,true);
			return Results.Json(listAppointments.Select(x => {
				Patient pat=arrayPatients.FirstOrDefault(y => y.PatNum==x.PatNum);
				return new {
					aptNum=x.AptNum,
					patNum=x.PatNum,
					patient=pat?.GetNameLF()??"",
					phone=pat?.WirelessPhone??"",
					dateTime=x.AptDateTime.Year>1880?x.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
					status=x.AptStatus.ToString(),
					unschedStatus=Defs.GetName(DefCat.RecallUnschedStatus,x.UnschedStatus,listDefs),
					provAbbr=Providers.GetAbbr(x.ProvNum),
					minutes=(x.Pattern??"").Length*5,
					procDescript=x.ProcDescript,
					note=x.Note,
				};
			}));
		});

		//Per-patient unscheduled slice (FormUnschedListPatient / FormApptsOther).
		app.MapGet("/api/patients/{patNum:long}/unsched",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Appointment> listAppointments=Appointments.GetUnschedApptsForPat(patNum);
			return Results.Json(listAppointments.Select(x => new {
				aptNum=x.AptNum,
				patNum=x.PatNum,
				dateTime=x.AptDateTime.Year>1880?x.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
				provAbbr=Providers.GetAbbr(x.ProvNum),
				minutes=(x.Pattern??"").Length*5,
				procDescript=x.ProcDescript,
				note=x.Note,
			}));
		});

		//Convert a planned appt into a real scheduled appt at the given date/time and
		//operatory — Appointments.SchedulePlannedApt copies the appt, links NextAptNum,
		//moves the planned procedures, appt fields and lab cases, same as dropping the
		//planned appt from the pinboard onto the schedule.
		app.MapPost("/api/planned/{aptNum:long}/schedule",(long aptNum,SchedulePlannedDto dto) => {
			Appointment appointmentPlanned=Appointments.GetOneApt(aptNum);
			if(appointmentPlanned==null) {
				return Results.NotFound();
			}
			if(appointmentPlanned.AptStatus!=ApptStatus.Planned) {
				return Results.Text("Appointment "+aptNum+" is not a planned appointment.",statusCode:400);
			}
			Appointment appointmentSched=Appointments.GetScheduledPlannedApt(aptNum);
			if(appointmentSched!=null) {
				return Results.Text("Already scheduled as appointment "+appointmentSched.AptNum
					+(appointmentSched.AptDateTime.Year>1880?" on "+appointmentSched.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"")+".",statusCode:400);
			}
			if(!DateTime.TryParse(dto.DateTime,out DateTime dateTimeAppt) || dateTimeAppt.Year<1880) {
				return Results.Text("Invalid date/time.",statusCode:400);
			}
			Operatory operatory=Operatories.GetOperatory(dto.OpNum);
			if(operatory==null) {
				return Results.Text("Operatory "+dto.OpNum+" not found.",statusCode:400);
			}
			Patient pat=Patients.GetPat(appointmentPlanned.PatNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<ApptField> listApptFields=ApptFields.GetForAppt(appointmentPlanned.AptNum);
			Appointment appointmentNew=Appointments.SchedulePlannedApt(appointmentPlanned,pat,listApptFields,dateTimeAppt,dto.OpNum);
			return Results.Json(new {
				aptNum=appointmentNew.AptNum,
				plannedAptNum=aptNum,
				patNum=pat.PatNum,
				dateTime=appointmentNew.AptDateTime.ToString("yyyy-MM-dd HH:mm"),
				opNum=appointmentNew.Op,
			});
		});
	}
}
