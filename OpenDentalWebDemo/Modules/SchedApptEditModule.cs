/*
 * Appointment detail & edit — web replacement for FormApptEdit's core surface.
 * Opens one appointment, shows/edits its scheduling fields (date/time, pattern
 * length, operatory, provider + hygienist, status, note) and the procedures
 * attached to it, and attaches/detaches procedures the same way the desktop
 * form does.
 *
 * Reads ride Appointments.GetOneApt + Procedures.GetProcsForApptEdit (the exact
 * proc list FormApptEdit builds). Core-field edits go through the same
 * diff+audit path the form uses — Appointments.Update(apt,aptOld) — followed by
 * an AppointmentEdit security log and Signalods.SetInvalid(InvalidType.Appointment).
 * Attaching a procedure mirrors FormApptEdit's per-proc save exactly
 * (procedure.AptNum=appt.AptNum; Procedures.Update(proc,procOld)); detaching
 * sets AptNum back to 0. After either, the appointment's Pattern is recomputed
 * with Appointments.CalculatePattern and its ProcDescript with
 * Appointments.SetProcDescript — the same two calls FormApptEdit fires in
 * CalculatePatternFromProcs / SetProcDescript — then Appointments.Update saves it.
 *
 * The bare PUT/DELETE /api/appointments/{n} and POST .../confirm live in other
 * modules; these detail + procedures subpaths do not touch those routes.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedApptEditModule {
	record ApptEditDto(string DateTime,string Pattern,int? Minutes,long OpNum,long ProvNum,long ProvHyg,string AptStatus,string Note);
	record AttachDto(List<long> ProcNums);

	//Project one attached procedure into the compact shape the editor grid uses.
	static object ProcRow(Procedure proc) {
		ProcedureCode code=ProcedureCodes.GetProcCode(proc.CodeNum);
		return new {
			procNum=proc.ProcNum,
			procCode=code?.ProcCode??"",
			descript=code?.Descript??"",
			toothNum=proc.ToothNum??"",
			surf=proc.Surf??"",
			fee=proc.ProcFee,
			status=proc.ProcStatus.ToString(),
		};
	}

	//The procedures FormApptEdit shows as "attached" — those whose AptNum is this appt.
	static List<Procedure> AttachedProcs(Appointment appointment) {
		return Procedures.GetProcsForApptEdit(appointment).FindAll(x => x.AptNum==appointment.AptNum);
	}

	//Recompute Pattern + ProcDescript from the currently-attached procs and save the
	//appointment — the exact pair of calls FormApptEdit makes after a proc change
	//(CalculatePatternFromProcs -> Appointments.CalculatePattern, then SetProcDescript),
	//followed by the same Update(diff+audit) path.
	static void ResaveAfterProcChange(Appointment appointment,Patient patient) {
		List<Procedure> listAttached=AttachedProcs(appointment);
		Appointment appointmentOld=appointment.Copy();
		string pattern=Appointments.CalculatePattern(patient,appointment.ProvNum,appointment.ProvHyg,listAttached);
		if(!string.IsNullOrEmpty(pattern)) {//null when the appt time is locked; leave the pattern untouched then
			appointment.Pattern=pattern;
		}
		Appointments.SetProcDescript(appointment,listAttached);
		Appointments.Update(appointment,appointmentOld);//fires the InvalidType.Appointment signal
		Signalods.SetInvalid(InvalidType.Appointment);
		SecurityLogs.MakeLogEntry(EnumPermType.AppointmentEdit,appointment.PatNum,
			"Appointment procedures changed from the web appointment editor.",appointment.AptNum,appointmentOld.DateTStamp);
	}

	public static void Map(WebApplication app) {
		//Every appointment for a patient (FormApptsOther / the patient's appt history) so the
		//UI can pick one to edit. Appointments.GetForPat is the same per-patient read the
		//desktop uses; newest first.
		app.MapGet("/api/patients/{patNum:long}/appointments",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Appointment[] arrayAppointments=Appointments.GetForPat(patNum);
			return Results.Json(arrayAppointments
				.OrderByDescending(x => x.AptDateTime)
				.Select(x => new {
					aptNum=x.AptNum,
					patNum=x.PatNum,
					dateTime=x.AptDateTime.Year>1880?x.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
					aptStatus=x.AptStatus.ToString(),
					provAbbr=Providers.GetAbbr(x.ProvNum),
					minutes=(x.Pattern??"").Length*5,
					procDescript=x.ProcDescript,
					note=x.Note,
				}));
		});

		//Full detail for one appointment: the scheduling fields FormApptEdit edits, the
		//confirmation status name, the patient name, and every attached procedure.
		app.MapGet("/api/appointments/{aptNum:long}/detail",(long aptNum) => {
			Appointment appointment=Appointments.GetOneApt(aptNum);
			if(appointment==null) {
				return Results.NotFound();
			}
			Patient pat=Patients.GetPat(appointment.PatNum);
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			Operatory operatory=Operatories.GetOperatory(appointment.Op);
			return Results.Json(new {
				aptNum=appointment.AptNum,
				patNum=appointment.PatNum,
				patient=pat?.GetNameLF()??"",
				dateTime=appointment.AptDateTime.Year>1880?appointment.AptDateTime.ToString("yyyy-MM-dd HH:mm"):"",
				pattern=appointment.Pattern??"",
				minutes=(appointment.Pattern??"").Length*5,
				opNum=appointment.Op,
				opName=operatory==null?"":(string.IsNullOrEmpty(operatory.Abbrev)?operatory.OpName:operatory.Abbrev),
				provNum=appointment.ProvNum,
				provAbbr=Providers.GetAbbr(appointment.ProvNum),
				provHyg=appointment.ProvHyg,
				provHygAbbr=appointment.ProvHyg==0?"":Providers.GetAbbr(appointment.ProvHyg),
				confirmedDefNum=appointment.Confirmed,
				confirmed=appointment.Confirmed==0?"":Defs.GetName(DefCat.ApptConfirmed,appointment.Confirmed),
				aptStatus=appointment.AptStatus.ToString(),
				note=appointment.Note??"",
				procDescript=appointment.ProcDescript??"",
				procedures=AttachedProcs(appointment).Select(ProcRow),
			});
		});

		//Edit the core scheduling fields — the desktop save path: fetch, copy, mutate,
		//Appointments.Update(new,old) (the diff+audit method FormApptEdit calls), then an
		//AppointmentEdit security log + Appointment invalid signal. Distinct route from the
		//bare PUT /api/appointments/{n} owned elsewhere.
		app.MapPut("/api/appointments/{aptNum:long}/detail",(long aptNum,ApptEditDto dto) => {
			Appointment appointment=Appointments.GetOneApt(aptNum);
			if(appointment==null) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.DateTime,out DateTime dateTimeAppt) || dateTimeAppt.Year<1880) {
				return Results.Text("Invalid date/time.",statusCode:400);
			}
			if(!Enum.TryParse(dto.AptStatus,out ApptStatus aptStatus) || aptStatus==ApptStatus.None) {
				return Results.Text("Invalid appointment status: "+dto.AptStatus,statusCode:400);
			}
			Appointment appointmentOld=appointment.Copy();
			appointment.AptDateTime=dateTimeAppt;
			appointment.Op=dto.OpNum;
			appointment.ProvNum=dto.ProvNum;
			appointment.ProvHyg=dto.ProvHyg;
			appointment.AptStatus=aptStatus;
			appointment.Note=dto.Note??"";
			//Length: an explicit pattern wins; otherwise a minutes value rebuilds a 5-minute
			//provider-time pattern (minutes/5 chars); otherwise the existing pattern stands.
			if(!string.IsNullOrEmpty(dto.Pattern)) {
				appointment.Pattern=dto.Pattern;
			}
			else if(dto.Minutes.HasValue && dto.Minutes.Value>0) {
				appointment.Pattern=new string('X',Math.Max(1,dto.Minutes.Value/5));
			}
			try {
				Appointments.Update(appointment,appointmentOld);//diff+audit + Appointment invalid signal
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Signalods.SetInvalid(InvalidType.Appointment);
			SecurityLogs.MakeLogEntry(EnumPermType.AppointmentEdit,appointment.PatNum,
				"Appointment edited from the web appointment editor.",appointment.AptNum,appointmentOld.DateTStamp);
			return Results.Json(new {
				aptNum=appointment.AptNum,
				patNum=appointment.PatNum,
				dateTime=appointment.AptDateTime.ToString("yyyy-MM-dd HH:mm"),
				pattern=appointment.Pattern??"",
				minutes=(appointment.Pattern??"").Length*5,
				opNum=appointment.Op,
				provNum=appointment.ProvNum,
				provHyg=appointment.ProvHyg,
				aptStatus=appointment.AptStatus.ToString(),
				note=appointment.Note??"",
			});
		});

		//Attach one or more of the patient's procedures to this appointment, exactly as
		//FormApptEdit does per proc (procedure.AptNum=appt.AptNum; Procedures.Update), then
		//recompute Pattern/ProcDescript and resave the appointment.
		app.MapPost("/api/appointments/{aptNum:long}/procedures",(long aptNum,AttachDto dto) => {
			Appointment appointment=Appointments.GetOneApt(aptNum);
			if(appointment==null) {
				return Results.NotFound();
			}
			Patient pat=Patients.GetPat(appointment.PatNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(dto.ProcNums==null || dto.ProcNums.Count==0) {
				return Results.Text("No procedures specified.",statusCode:400);
			}
			foreach(long procNum in dto.ProcNums.Distinct()) {
				Procedure procedure=Procedures.GetOneProc(procNum,false);
				if(procedure==null || procedure.ProcNum==0) {
					return Results.Text("Procedure "+procNum+" not found.",statusCode:400);
				}
				if(procedure.PatNum!=appointment.PatNum) {
					return Results.Text("Procedure "+procNum+" belongs to a different patient.",statusCode:400);
				}
				if(procedure.AptNum==appointment.AptNum) {
					continue;//already attached
				}
				Procedure procedureOld=procedure.Copy();
				procedure.AptNum=appointment.AptNum;//same field FormApptEdit sets before saving
				Procedures.Update(procedure,procedureOld);
			}
			ResaveAfterProcChange(appointment,pat);
			return Results.Json(new {
				aptNum=appointment.AptNum,
				minutes=(appointment.Pattern??"").Length*5,
				pattern=appointment.Pattern??"",
				procDescript=appointment.ProcDescript??"",
				procedures=AttachedProcs(appointment).Select(ProcRow),
			});
		});

		//Detach one procedure (set AptNum back to 0), then recompute Pattern/ProcDescript
		//and resave — the delete-proc half of FormApptEdit's grid handling.
		app.MapDelete("/api/appointments/{aptNum:long}/procedures/{procNum:long}",(long aptNum,long procNum) => {
			Appointment appointment=Appointments.GetOneApt(aptNum);
			if(appointment==null) {
				return Results.NotFound();
			}
			Patient pat=Patients.GetPat(appointment.PatNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Procedure procedure=Procedures.GetOneProc(procNum,false);
			if(procedure==null || procedure.ProcNum==0) {
				return Results.NotFound();
			}
			if(procedure.AptNum!=appointment.AptNum) {
				return Results.Text("Procedure "+procNum+" is not attached to appointment "+aptNum+".",statusCode:400);
			}
			Procedure procedureOld=procedure.Copy();
			procedure.AptNum=0;
			Procedures.Update(procedure,procedureOld);
			ResaveAfterProcChange(appointment,pat);
			return Results.Json(new {
				aptNum=appointment.AptNum,
				minutes=(appointment.Pattern??"").Length*5,
				pattern=appointment.Pattern??"",
				procDescript=appointment.ProcDescript??"",
				procedures=AttachedProcs(appointment).Select(ProcRow),
			});
		});
	}
}
