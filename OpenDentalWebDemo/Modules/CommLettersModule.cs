/*
 * Patient letters & slips: generate a patient-directed sheet (welcome/recall
 * letter, mailing label, referral slip/letter, appointment label) filled with
 * the patient's data and render it to PDF via the existing GET /api/sheets/{n}/pdf.
 *
 * This is the web replacement for the desktop "print letter / slip / label" flows
 * (e.g. FrmReferralSlip, the appointment label print, and the patient-letter print
 * in the Patient/Chart modules). Each of these types is an internal SheetDef
 * (an XML resource in SheetsInternal), so it is always available even against an
 * empty sheetdef table; SheetDefs.GetInternalOrCustom prefers the office's custom
 * def of that type when one exists, exactly like the desktop.
 *
 * Business calls: SheetDefs.GetInternalOrCustom, SheetUtil.CreateSheet,
 * SheetParameter.GetParamByName / SetParameter, SheetFiller.FillFields,
 * SheetUtil.CalculateHeights, Sheets.SaveNewSheet, Sheets.GetForPatient,
 * RefAttaches.GetPatientData, Appointments.GetFutureSchedApts /
 * GetAppointmentsForPat - the same calls the desktop makes to build these sheets.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class CommLettersModule {
	//Body is optional: only PatientLetter/ReferralLetter carry an editable body block;
	//for the label/slip types with no body field it is ignored gracefully.
	record CreateLetterDto(string Type,string Body);

	//The internal letter defs (PatientLetter/ReferralLetter) place the editable letter
	//body in a single growable StaticText field that ships with the placeholder text
	//"letter text" (see OpenDentBusiness/Resources/PatientLetter.xml + ReferralLetter.xml).
	//Labels/slips have no such field.
	const string _bodyPlaceholder="letter text";

	//Patient-directed internal sheet types this module offers, in display order.
	//These are the letter/label/slip types (as opposed to the form/consent types
	//MiscSheetsModule covers). All are internal XML defs, so always available.
	static readonly SheetInternalType[] _letterTypes=new[]{
		SheetInternalType.PatientLetter,   //welcome / recall / generic patient letter
		SheetInternalType.LabelPatientMail,//patient mailing label
		SheetInternalType.ReferralSlip,    //referral slip for a referral on file
		SheetInternalType.ReferralLetter,  //referral letter for a referral on file
		SheetInternalType.LabelAppointment,//appointment label
	};

	//SheetTypeEnums produced by the types above - used to recognize which sheets
	//already on file are letters/labels/slips (vs forms, consents, statements, etc).
	static readonly HashSet<SheetTypeEnum> _letterSheetTypes=new(){
		SheetTypeEnum.PatientLetter,
		SheetTypeEnum.LabelPatient,
		SheetTypeEnum.ReferralSlip,
		SheetTypeEnum.ReferralLetter,
		SheetTypeEnum.LabelAppointment,
	};

	//Only accept the enum keys this module actually offers.
	static bool TryParseLetterType(string key,out SheetInternalType internalType) {
		internalType=default;
		if(string.IsNullOrEmpty(key)) {
			return false;
		}
		if(!Enum.TryParse<SheetInternalType>(key,out internalType)) {
			return false;
		}
		return _letterTypes.Contains(internalType);
	}

	static bool HasParam(Sheet sheet,string paramName) {
		return SheetParameter.GetParamByName(sheet.Parameters,paramName)!=null;
	}

	static bool IsParamRequired(Sheet sheet,string paramName) {
		SheetParameter param=SheetParameter.GetParamByName(sheet.Parameters,paramName);
		return param!=null && param.IsRequired;
	}

	//Recognizes the editable letter-body field. It is a growable (DownGlobal) StaticText
	//field with no FieldName. We match the shipped placeholder "letter text" first, then
	//fall back to the growable StaticText block so a custom letter def still works. Same
	//shape on a SheetFieldDef (def list) and a SheetField (created sheet).
	static bool IsBodyField(SheetFieldType fieldType,string fieldName,string fieldValue,GrowthBehaviorEnum growth) {
		if(fieldType!=SheetFieldType.StaticText || !string.IsNullOrEmpty(fieldName)) {
			return false;
		}
		if(string.Equals((fieldValue??"").Trim(),_bodyPlaceholder,StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return growth==GrowthBehaviorEnum.DownGlobal;
	}

	//True when this def has an editable body block (PatientLetter/ReferralLetter) - drives
	//hasBody in /api/letter-types so the UI only shows the body textarea when relevant.
	static bool DefHasBody(SheetDef sheetDef) {
		return sheetDef.SheetFieldDefs!=null
			&& sheetDef.SheetFieldDefs.Any(x => IsBodyField(x.FieldType,x.FieldName,x.FieldValue,x.GrowthBehavior));
	}

	//The body SheetField on a created sheet, or null for label/slip types with no body.
	static SheetField FindBodyField(Sheet sheet) {
		return sheet.SheetFields
			.FirstOrDefault(x => IsBodyField(x.FieldType,x.FieldName,x.FieldValue,x.GrowthBehavior));
	}

	//Referral slips/letters need a ReferralNum - use the first referral attached to
	//the patient (the desktop presents the patient's referrals to pick from).
	static long ResolveReferralNum(long patNum) {
		return RefAttaches.GetPatientData(patNum)
			.Select(x => x.ReferralNum)
			.FirstOrDefault(x => x>0);
	}

	//Appointment labels need an AptNum - prefer the patient's next scheduled
	//appointment, otherwise fall back to their most recent one.
	static long ResolveAptNum(long patNum) {
		List<Appointment> listFuture=Appointments.GetFutureSchedApts(patNum);
		if(listFuture.Count>0) {
			return listFuture[0].AptNum;//already ordered by AptDateTime ascending
		}
		Appointment aptRecent=Appointments.GetAppointmentsForPat(patNum)
			.Where(x => x.AptDateTime.Year>1880)
			.OrderByDescending(x => x.AptDateTime)
			.FirstOrDefault();
		return aptRecent?.AptNum??0;
	}

	public static void Map(WebApplication app) {
		//----- Patient-letter/slip internal sheet types this module offers -----
		app.MapGet("/api/letter-types",() => {
			List<object> list=new List<object>();
			for(int i=0;i<_letterTypes.Length;i++) {
				//GetInternalOrCustom picks up the office's custom def of this type if any,
				//so the description matches what will actually be generated.
				SheetDef sheetDef=SheetDefs.GetInternalOrCustom(_letterTypes[i]);
				list.Add(new {
					key=_letterTypes[i].ToString(),
					description=sheetDef.Description??_letterTypes[i].ToString(),
					hasBody=DefHasBody(sheetDef),
				});
			}
			return Results.Json(list);
		});

		//----- Create + fill + persist the chosen letter/slip for the patient -----
		app.MapPost("/api/patients/{patNum:long}/letters",(long patNum,CreateLetterDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(!TryParseLetterType(dto?.Type,out SheetInternalType internalType)) {
				return Results.Text("Unknown letter type "+(dto?.Type??""),statusCode:400);
			}
			SheetDef sheetDef=SheetDefs.GetInternalOrCustom(internalType);
			//Same sequence the desktop uses to build one of these sheets.
			Sheet sheet=SheetUtil.CreateSheet(sheetDef,patNum);
			if(HasParam(sheet,"PatNum")) {
				SheetParameter.SetParameter(sheet,"PatNum",patNum);
			}
			if(HasParam(sheet,"ReferralNum")) {
				long referralNum=ResolveReferralNum(patNum);
				if(referralNum==0) {
					return Results.Text(sheetDef.Description+" needs a referral on file for this patient.",statusCode:409);
				}
				SheetParameter.SetParameter(sheet,"ReferralNum",referralNum);
			}
			if(HasParam(sheet,"AptNum")) {
				long aptNum=ResolveAptNum(patNum);
				if(aptNum==0 && IsParamRequired(sheet,"AptNum")) {
					return Results.Text(sheetDef.Description+" needs an appointment for this patient.",statusCode:409);
				}
				if(aptNum>0) {
					SheetParameter.SetParameter(sheet,"AptNum",aptNum);
				}
			}
			SheetFiller.FillFields(sheet);
			//Overwrite the letter body's placeholder ("letter text") with the user-supplied
			//body before CalculateHeights, so the growable (DownGlobal) block sizes to the new
			//text. Label/slip types have no body field, so a supplied body is ignored.
			if(dto?.Body!=null) {
				SheetField bodyField=FindBodyField(sheet);
				if(bodyField!=null) {
					bodyField.FieldValue=dto.Body;
				}
			}
			SheetUtil.CalculateHeights(sheet);
			long sheetNum=Sheets.SaveNewSheet(sheet);
			return Results.Json(new {
				sheetNum,
				description=sheet.Description??sheetDef.Description??"",
			});
		});

		//----- Letters already on file for the patient (render via GET /api/sheets/{n}/pdf) -----
		app.MapGet("/api/patients/{patNum:long}/letters",(long patNum) => {
			List<Sheet> listSheets=Sheets.GetForPatient(patNum)
				.Where(x => _letterSheetTypes.Contains(x.SheetType))
				.OrderByDescending(x => x.DateTimeSheet)
				.ToList();
			return Results.Json(listSheets.Select(x => new {
				sheetNum=x.SheetNum,
				description=x.Description??"",
				dateTimeSheet=x.DateTimeSheet.Year>1880?x.DateTimeSheet.ToString("yyyy-MM-dd HH:mm"):"",
			}));
		});

		//----- Delete a letter/sheet (the Delete button on FrmSheetFillEdit) -----
		//Distinct from GET /api/sheets/{n}/pdf (Program.cs) and PUT /api/sheets/{n}/fields
		//(MiscSheetsModule) - different verb, so no route collision.
		app.MapDelete("/api/sheets/{sheetNum:long}",(long sheetNum) => {
			Sheet sheet=Sheets.GetSheet(sheetNum);
			if(sheet==null || sheet.SheetNum==0) {
				return Results.NotFound();
			}
			//Sheets.Delete is a soft delete (flips IsDeleted=1; sheet+fields are retained).
			//The desktop has no hard dependency guard - the only prompt is the kiosk warning
			//(ShowInTerminal>0), which does not block - so we mirror FrmSheetFillEdit.DeleteSheet:
			//pass PatNum+ShowInTerminal so the kiosk notification/decrement runs, then stamp the
			//same SecurityLog.
			Sheets.Delete(sheet.SheetNum,sheet.PatNum,sheet.ShowInTerminal);
			SecurityLogs.MakeLogEntry(EnumPermType.SheetEdit,sheet.PatNum,(sheet.Description??"")
				+" deleted from "+(sheet.DateTimeSheet.Year>1880?sheet.DateTimeSheet.ToShortDateString():""));
			return Results.Ok();
		});
	}
}
