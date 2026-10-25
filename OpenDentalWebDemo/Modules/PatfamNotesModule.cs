/*
 * Patient notes: web replacement for the desktop patient-note editing that is spread
 * across FormMedical (Medical/MedicalComp/Service + patient.MedUrgNote), FormPatientEdit
 * (ICEName/ICEPhone), FormApptsOther (patient.ApptModNote) and the Account module's
 * family financial notes (patientnote.FamFinancial on the guarantor row + the
 * guarantor patient's FamFinUrgNote).
 * PatientNote is a lazy 1:1 companion row to patient: PatientNotes.Refresh(patNum,guar)
 * creates-on-demand for both the patient and the guarantor and always overlays the
 * guarantor's FamFinancial. Saves ride the exact desktop calls:
 *   PatientNotes.Update(note,guarantor)  — crud update, then FamFinancial written to the guarantor row
 *   Patients.Update(pat,patOld)          — diff-based update with audit/SecurityHash upkeep
 * FamFinUrgNote is stored on the guarantor patient (desktop edits _family.ListPats[0]),
 * so a non-guarantor save updates that row separately, also via Patients.Update.
 */
using System;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class PatfamNotesModule {
	record NotesDto(string FamFinancial,string FamFinUrgNote,string ApptModNote,
		string Medical,string MedUrgNote,string MedicalComp,
		string Service,string Treatment,string IceName,string IcePhone);

	static string Name(Patient p) => p==null?"":$"{p.LName}, {p.FName}".Trim(' ',',');

	public static void Map(WebApplication app) {
		//Everything the desktop shows for this patient's notes, labeled with where each
		//field lives. FamFinancial/FamFinUrgNote are family-level (guarantor-stored).
		app.MapGet("/api/patients/{patNum:long}/notes",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			PatientNote note=PatientNotes.Refresh(patNum,pat.Guarantor);//creates rows on demand; FamFinancial comes from guarantor
			Patient patGuar=(pat.PatNum==pat.Guarantor)?pat:Patients.GetPat(pat.Guarantor);
			return Results.Json(new {
				patNum=pat.PatNum,
				patientName=Name(pat),
				guarantorNum=pat.Guarantor,
				guarantorName=Name(patGuar),
				isGuarantor=pat.PatNum==pat.Guarantor,
				//Family financial (stored per-family on the guarantor)
				famFinUrgNote=patGuar?.FamFinUrgNote??"",   //patient.FamFinUrgNote (guarantor row)
				famFinancial=note.FamFinancial??"",         //patientnote.FamFinancial (guarantor row)
				//Appointment
				apptModNote=pat.ApptModNote??"",            //patient.ApptModNote
				//Medical
				medUrgNote=pat.MedUrgNote??"",              //patient.MedUrgNote
				medical=note.Medical??"",                   //patientnote.Medical (medical summary)
				medicalComp=note.MedicalComp??"",           //patientnote.MedicalComp (complete medical history)
				//Service / chart
				service=note.Service??"",                   //patientnote.Service
				treatment=note.Treatment??"",               //patientnote.Treatment (chart/odontogram note)
				//ICE
				iceName=note.ICEName??"",                   //patientnote.ICEName
				icePhone=note.ICEPhone??"",                 //patientnote.ICEPhone
			});
		});

		//Save, mirroring the desktop save paths: PatientNotes.Update for the patientnote
		//fields, Patients.Update(pat,patOld) for the patient-table note fields, and a
		//second Patients.Update against the guarantor when FamFinUrgNote changed and the
		//active patient is not the guarantor.
		app.MapPut("/api/patients/{patNum:long}/notes",(long patNum,NotesDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			try {
				//patientnote row (FamFinancial is written to the guarantor's row by Update)
				PatientNote note=PatientNotes.Refresh(patNum,pat.Guarantor);
				note.FamFinancial=dto.FamFinancial??"";
				note.Medical=dto.Medical??"";
				note.MedicalComp=dto.MedicalComp??"";
				note.Service=dto.Service??"";
				note.Treatment=dto.Treatment??"";
				note.ICEName=dto.IceName??"";
				note.ICEPhone=dto.IcePhone??"";
				PatientNotes.Update(note,pat.Guarantor);
				//patient-table fields on this patient
				Patient patOld=pat.Copy();
				pat.ApptModNote=dto.ApptModNote??"";
				pat.MedUrgNote=dto.MedUrgNote??"";
				if(pat.PatNum==pat.Guarantor) {
					pat.FamFinUrgNote=dto.FamFinUrgNote??"";
				}
				Patients.Update(pat,patOld);//diff-based; audit trail + SecurityHash upkeep
				//FamFinUrgNote lives on the guarantor patient (desktop edits ListPats[0])
				if(pat.PatNum!=pat.Guarantor) {
					Patient patGuar=Patients.GetPat(pat.Guarantor);
					if(patGuar!=null && (patGuar.FamFinUrgNote??"")!=(dto.FamFinUrgNote??"")) {
						Patient patGuarOld=patGuar.Copy();
						patGuar.FamFinUrgNote=dto.FamFinUrgNote??"";
						Patients.Update(patGuar,patGuarOld);
					}
				}
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			return Results.Ok();
		});
	}
}
