/*
 * Appointment confirmation list (FormConfirmList). The grid rides
 * Appointments.GetConfirmList — the exact confirm-list query the desktop form
 * runs (Scheduled + ASAP appts in the date range, all clinics, recall and
 * non-recall) — joined with Patients.GetMultPats for the raw phone numbers and
 * Appointments.GetMultApts for the Confirmed DefNum behind each row. Setting a
 * status follows the desktop combo path exactly: GetOneApt, copy, set
 * Confirmed, Appointments.Update(new,old), then an ApptConfirmStatusEdit
 * security log entry when the status actually changed.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedConfirmListModule {
	record ConfirmDto(long DefNum);

	static string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";

	public static void Map(WebApplication app) {
		//Confirmation list (FormConfirmList grid): scheduled + ASAP appointments in the
		//date range with contact info and confirmation status. Defaults mirror the form:
		//today through +7 days, all providers, all clinics, recall and non-recall shown.
		//The ApptConfirmed defs ride along (in ItemOrder) so the UI can build the status
		//dropdown from the same list the desktop combo uses.
		app.MapGet("/api/confirmlist",(string? from,string? to) => {
			DateTime dateFrom=DateTime.TryParse(from,out DateTime dateFromParsed)?dateFromParsed.Date:DateTime.Today;
			DateTime dateTo=DateTime.TryParse(to,out DateTime dateToParsed)?dateToParsed.Date:DateTime.Today.AddDays(7);
			//provNum 0 = all providers, clinicNum -1 = all clinics, showRecall+showNonRecall = the form's "All",
			//defNumConfirmStatus 0 = no status filter, groupFamilies false = plain date order.
			DataTable table=Appointments.GetConfirmList(dateFrom,dateTo,0,-1,true,true,false,0,false);
			List<long> listAptNums=table.Rows.OfType<DataRow>().Select(x => PIn.Long(x["AptNum"].ToString())).ToList();
			List<Appointment> listAppointments=Appointments.GetMultApts(listAptNums);
			Patient[] arrayPatients=Patients.GetMultPats(table.Rows.OfType<DataRow>()
				.Select(x => PIn.Long(x["PatNum"].ToString())).Distinct().ToList());
			List<Def> listDefsConfirmed=Defs.GetDefsForCategory(DefCat.ApptConfirmed,true);
			return Results.Json(new {
				defs=listDefsConfirmed.Select(x => new {
					defNum=x.DefNum,
					name=x.ItemName,
					color=Hex(x.ItemColor),
				}),
				appointments=table.Rows.OfType<DataRow>().Select(x => {
					long aptNum=PIn.Long(x["AptNum"].ToString());
					long patNum=PIn.Long(x["PatNum"].ToString());
					Appointment appointment=listAppointments.FirstOrDefault(y => y.AptNum==aptNum);
					Patient pat=arrayPatients.FirstOrDefault(y => y.PatNum==patNum);
					Def defConfirmed=listDefsConfirmed.FirstOrDefault(y => y.DefNum==(appointment?.Confirmed??0));
					return new {
						aptNum,
						patNum,
						dateTime=PIn.DateT(x["AptDateTime"].ToString()).ToString("yyyy-MM-dd HH:mm"),
						patient=pat?.GetNameLF()??(x["LName"]+", "+x["FName"]),
						age=x["age"].ToString(),
						homePhone=pat?.HmPhone??"",
						wirelessPhone=pat?.WirelessPhone??"",
						email=x["email"].ToString(),//patient email, falling back to guarantor email like the desktop grid
						contactMethod=x["contactMethod"].ToString(),
						provAbbr=Providers.GetAbbr(appointment?.ProvNum??PIn.Long(x["PriProv"].ToString())),
						procDescript=x["ProcDescript"].ToString(),
						note=x["Note"].ToString(),
						medNotes=x["medNotes"].ToString(),
						confirmedDefNum=appointment?.Confirmed??0,
						confirmed=x["confirmed"].ToString(),
						confirmedColor=defConfirmed==null?"":Hex(defConfirmed.ItemColor),
					};
				}),
			});
		});

		//Set an appointment's confirmation status — the desktop combo path: fetch, copy,
		//set Confirmed to the chosen ApptConfirmed def, Appointments.Update (which handles
		//the arrived/seated/dismissed time triggers via the same field), and log the change
		//with EnumPermType.ApptConfirmStatusEdit exactly as FormConfirmList does.
		app.MapPost("/api/appointments/{aptNum:long}/confirm",(long aptNum,ConfirmDto dto) => {
			Appointment appointment=Appointments.GetOneApt(aptNum);
			if(appointment==null) {
				return Results.NotFound();
			}
			List<Def> listDefsConfirmed=Defs.GetDefsForCategory(DefCat.ApptConfirmed,true);
			Def defConfirmed=listDefsConfirmed.FirstOrDefault(x => x.DefNum==dto.DefNum);
			if(defConfirmed==null) {
				return Results.Text("DefNum "+dto.DefNum+" is not an appointment confirmation status.",statusCode:400);
			}
			Appointment appointmentOld=appointment.Copy();
			appointment.Confirmed=defConfirmed.DefNum;
			try {
				Appointments.Update(appointment,appointmentOld);
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			if(appointment.Confirmed!=appointmentOld.Confirmed) {
				//Log confirmation status changes, same wording and fKey as the desktop form.
				SecurityLogs.MakeLogEntry(EnumPermType.ApptConfirmStatusEdit,appointment.PatNum,
					"Appointment confirmation status changed from "+Defs.GetName(DefCat.ApptConfirmed,appointmentOld.Confirmed)
					+" to "+Defs.GetName(DefCat.ApptConfirmed,appointment.Confirmed)
					+" from the confirmation list.",appointment.AptNum,appointmentOld.DateTStamp);
			}
			return Results.Json(new {
				aptNum=appointment.AptNum,
				patNum=appointment.PatNum,
				confirmedDefNum=defConfirmed.DefNum,
				confirmed=defConfirmed.ItemName,
				confirmedColor=Hex(defConfirmed.ItemColor),
			});
		});
	}
}
