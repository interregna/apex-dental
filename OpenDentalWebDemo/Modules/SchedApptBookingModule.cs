/*
 * Appointment booking (create) — web replacement for FormApptEdit's *create*
 * branch / the ContrAppt "new appointment for this patient" flow. The detail +
 * edit surface (open an existing appt, edit its fields, attach/detach procs)
 * lives in SchedApptEditModule; this module only ADDS the create path plus a
 * picker of the patient's bookable (treatment-planned, not-yet-scheduled)
 * procedures, and shares that module's DTO/business-call shapes so the two
 * interoperate.
 *
 * Booking rides the same business calls the desktop create path uses:
 * Appointments.CalculatePattern (Pattern/length from the chosen procs — the
 * form's CalculatePatternFromProcs), Appointments.Insert (the audited insert,
 * which also stamps the confirmed/provider defaults and fires SetInvalidAppt),
 * then per-proc Procedures.Update to set each procedure.AptNum to the new appt
 * (exactly what FormApptEdit does when it attaches TP procs), then
 * Appointments.SetProcDescript + Appointments.Update to persist the description.
 * Finished with a SecurityLogs(AppointmentCreate) entry — matching the desktop
 * create log — and Signalods.SetInvalid(InvalidType.Appointment).
 *
 * When a date/time is supplied the appt is Scheduled into the given operatory;
 * with no date/time it is parked on the unscheduled list (AptStatus.UnschedList,
 * Op forced to 0, the same rule Appointments.SetAptStatus enforces). An explicit
 * aptStatus in the body overrides that inference.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedApptBookingModule {
	//Body for POST .../appointments. dateTime empty/null => unscheduled/planned parking.
	//pattern/minutes are optional length overrides; procNums drive the computed length
	//when present. aptStatus optional — otherwise inferred from whether a time was given.
	record BookDto(string DateTime,long OpNum,long ProvNum,long ProvHyg,int? Minutes,string Pattern,string Note,
		List<long> ProcNums,string AptStatus);

	//Same compact proc projection the appt editor grid uses, so a booked proc looks
	//identical whether it is picked here or attached in SchedApptEditModule.
	static object ProcRow(Procedure proc) {
		ProcedureCode code=ProcedureCodes.GetProcCode(proc.CodeNum);
		return new {
			procNum=proc.ProcNum,
			codeNum=proc.CodeNum,
			procCode=code?.ProcCode??"",
			descript=code?.Descript??"",
			toothNum=proc.ToothNum??"",
			surf=proc.Surf??"",
			fee=proc.ProcFee,
			minutes=(ProcCodeNotes.GetTimePattern(proc.ProvNum,proc.CodeNum)??"").Length*5,
			status=proc.ProcStatus.ToString(),
			provNum=proc.ProvNum,
			provAbbr=Providers.GetAbbr(proc.ProvNum),
		};
	}

	public static void Map(WebApplication app) {
		//The patient's bookable procedures: treatment-planned (TP / TP-inactive) procs
		//that are not already attached to any appointment (AptNum==0) — the exact pool
		//FormApptEdit offers when you build a new appointment from the treatment plan.
		app.MapGet("/api/patients/{patNum:long}/bookable-procs",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Procedure> listProcs=Procedures.Refresh(patNum)
				.FindAll(x => x.AptNum==0 && x.PlannedAptNum==0
					&& (x.ProcStatus==ProcStat.TP || x.ProcStatus==ProcStat.TPi));
			return Results.Json(listProcs
				.OrderBy(x => x.Priority)
				.ThenByDescending(x => x.ProcDate)
				.ThenBy(x => x.ToothNum)
				.Select(ProcRow));
		});

		//Create a new appointment for the patient (FormApptEdit create branch). Distinct
		//from the schedule day view's POST /api/appointments — this is per-patient, computes
		//Pattern from selected TP procedures, attaches them, and parks the appt on the
		//unscheduled list when no date/time is given.
		app.MapPost("/api/patients/{patNum:long}/appointments",(long patNum,BookDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			//A supplied date/time schedules the appt; a blank one parks it on the unscheduled
			//list. An explicit aptStatus in the body wins over that inference.
			bool hasTime=DateTime.TryParse(dto.DateTime,out DateTime dateTimeAppt) && dateTimeAppt.Year>=1880;
			ApptStatus aptStatus=hasTime?ApptStatus.Scheduled:ApptStatus.UnschedList;
			if(!string.IsNullOrEmpty(dto.AptStatus)) {
				if(!Enum.TryParse(dto.AptStatus,out aptStatus) || aptStatus==ApptStatus.None) {
					return Results.Text("Invalid appointment status: "+dto.AptStatus,statusCode:400);
				}
			}
			if(aptStatus==ApptStatus.Scheduled && !hasTime) {
				return Results.Text("A scheduled appointment needs a date/time.",statusCode:400);
			}
			//Gather + validate the procedures to book onto this appt.
			List<Procedure> listProcs=new List<Procedure>();
			if(dto.ProcNums!=null) {
				foreach(long procNum in dto.ProcNums.Distinct()) {
					Procedure procedure=Procedures.GetOneProc(procNum,false);
					if(procedure==null || procedure.ProcNum==0) {
						return Results.Text("Procedure "+procNum+" not found.",statusCode:400);
					}
					if(procedure.PatNum!=patNum) {
						return Results.Text("Procedure "+procNum+" belongs to a different patient.",statusCode:400);
					}
					if(procedure.AptNum!=0) {
						return Results.Text("Procedure "+procNum+" is already on an appointment.",statusCode:400);
					}
					listProcs.Add(procedure);
				}
			}
			Appointment appointment=new Appointment();
			appointment.PatNum=patNum;
			appointment.ClinicNum=pat.ClinicNum;
			appointment.AptStatus=aptStatus;
			//UnschedList appts carry no operatory or time (Appointments.SetAptStatus zeroes Op
			//for exactly this reason — a parked appt must not pin an operatory open).
			appointment.AptDateTime=(aptStatus==ApptStatus.Scheduled)?dateTimeAppt:DateTime.MinValue;
			appointment.Op=(aptStatus==ApptStatus.UnschedList)?0:dto.OpNum;
			//Provider defaults mirror the desktop: chosen provider, else the patient's primary,
			//else the first provider (the same fallback Appointments.Insert applies to ProvNum==0).
			appointment.ProvNum=dto.ProvNum!=0?dto.ProvNum:(pat.PriProv!=0?pat.PriProv:Providers.GetFirst(true).ProvNum);
			appointment.ProvHyg=dto.ProvHyg;
			appointment.Note=dto.Note??"";
			//Length/Pattern: an explicit pattern wins; otherwise the selected procs compute it
			//via Appointments.CalculatePattern (form's CalculatePatternFromProcs); otherwise a
			//minutes value builds a 5-minute-increment provider-time pattern; otherwise
			//CalculatePattern with an empty list yields the practice's default appt length.
			if(!string.IsNullOrEmpty(dto.Pattern)) {
				appointment.Pattern=dto.Pattern;
			}
			else if(listProcs.Count>0) {
				appointment.Pattern=Appointments.CalculatePattern(pat,appointment.ProvNum,appointment.ProvHyg,listProcs);
			}
			else if(dto.Minutes.HasValue && dto.Minutes.Value>0) {
				appointment.Pattern=new string('X',Math.Max(1,dto.Minutes.Value/5));
			}
			else {
				appointment.Pattern=Appointments.CalculatePattern(pat,appointment.ProvNum,appointment.ProvHyg,listProcs);
			}
			if(string.IsNullOrEmpty(appointment.Pattern)) {
				appointment.Pattern="//XX//";//safety net; CalculatePattern normally supplies a default
			}
			try {
				Appointments.Insert(appointment);//audited insert; stamps confirmed/prov defaults + SetInvalidAppt
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			//Attach each selected proc to the freshly-created appt exactly as FormApptEdit does
			//(procedure.AptNum=appt.AptNum; Procedures.Update), then recompute Pattern/ProcDescript
			//from the attached procs and resave the appt — the create-side mirror of
			//SchedApptEditModule.ResaveAfterProcChange.
			if(listProcs.Count>0) {
				foreach(Procedure procedure in listProcs) {
					Procedure procedureOld=procedure.Copy();
					procedure.AptNum=appointment.AptNum;
					Procedures.Update(procedure,procedureOld);
				}
				Appointment appointmentOld=appointment.Copy();
				string pattern=Appointments.CalculatePattern(pat,appointment.ProvNum,appointment.ProvHyg,listProcs);
				if(!string.IsNullOrEmpty(pattern)) {
					appointment.Pattern=pattern;
				}
				Appointments.SetProcDescript(appointment,listProcs);
				Appointments.Update(appointment,appointmentOld);
			}
			Signalods.SetInvalid(InvalidType.Appointment);
			SecurityLogs.MakeLogEntry(EnumPermType.AppointmentCreate,patNum,
				(appointment.AptDateTime.Year>1880?appointment.AptDateTime.ToString():"Unscheduled")+", "+appointment.ProcDescript,
				appointment.AptNum,DateTime.MinValue);
			return Results.Json(new {
				aptNum=appointment.AptNum,
				patNum=appointment.PatNum,
				dateTime=appointment.AptDateTime.Year>1880?appointment.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
				opNum=appointment.Op,
				provNum=appointment.ProvNum,
				provHyg=appointment.ProvHyg,
				aptStatus=appointment.AptStatus.ToString(),
				pattern=appointment.Pattern??"",
				minutes=(appointment.Pattern??"").Length*5,
				procDescript=appointment.ProcDescript??"",
			});
		});
	}
}
