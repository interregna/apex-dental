/*
 * Sheet / web-form library + fill (FormSheetPicker + FormSheetFillEdit for the web).
 * This is the manual-entry flow: pick a SheetDef, create a Sheet for the active
 * patient (auto-filling patient fields), let the user type the input/checkbox
 * fields, save, and render the result to the existing GET /api/sheets/{n}/pdf.
 *
 * The def list mirrors FrmSheetPicker: the practice's custom SheetDefs for the
 * types a user manually fills (PatientForm, MedicalHistory, Consent, ExamSheet),
 * plus Open Dental's built-in internal defs (PatientRegistration, HIPAA,
 * Financial Agreement, COVID19, Medical History, Consent) when a type has no
 * custom def yet - exactly the fallback FrmSheetPicker uses. Internal defs live
 * in XML resources (SheetsInternal.GetSheetDef), so they are always available even
 * against an empty sheetdef table; they are referenced here by a negative synthetic
 * id that encodes the SheetInternalType.
 *
 * Business calls: SheetDefs.GetCustomForType / GetSheetDef,
 * SheetsInternal.GetSheetDef, SheetUtil.CreateSheet, SheetParameter.SetParameter,
 * SheetFiller.FillFields, SheetUtil.CalculateHeights, Sheets.SaveNewSheet,
 * Sheets.GetSheet / GetForPatient, SheetFields.Update - the same calls the desktop
 * picker and fill-edit dialog make.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class MiscSheetsModule {
	record CreateSheetDto(long SheetDefNum);
	record FieldUpdateDto(Dictionary<string,string> Fields);

	//Sheet types a user manually fills out and that only require a PatNum parameter,
	//matching the manual-entry side of FrmSheetPicker.
	static readonly SheetTypeEnum[] _fillableTypes=new[]{
		SheetTypeEnum.PatientForm,
		SheetTypeEnum.MedicalHistory,
		SheetTypeEnum.Consent,
		SheetTypeEnum.ExamSheet,
	};

	//Built-in internal defs to offer per type when the office has no custom def of
	//that type - the same fallbacks FrmSheetPicker adds from SheetsInternal.
	static readonly Dictionary<SheetTypeEnum,SheetInternalType[]> _internalFallbacks=new(){
		{ SheetTypeEnum.PatientForm,new[]{ SheetInternalType.PatientRegistration,SheetInternalType.FinancialAgreement,SheetInternalType.HIPAA,SheetInternalType.COVID19 } },
		{ SheetTypeEnum.MedicalHistory,new[]{ SheetInternalType.MedicalHist } },
		{ SheetTypeEnum.Consent,new[]{ SheetInternalType.Consent } },
		{ SheetTypeEnum.ExamSheet,new[]{ SheetInternalType.ExamSheet } },
	};

	//Field types the user actually fills - the set FormSheetFillEdit lets you tab
	//through and edit. Everything else (StaticText, Image, PatImage, SigBox, Line,
	//Rectangle, Drawing, Grid, Special, Parameter, MobileHeader) stays on the sheet
	//but is not offered for editing.
	static bool IsEditable(SheetFieldType fieldType) {
		return fieldType==SheetFieldType.InputField
			|| fieldType==SheetFieldType.OutputText
			|| fieldType==SheetFieldType.CheckBox
			|| fieldType==SheetFieldType.ComboBox;
	}

	//Internal defs have SheetDefNum 0 (they are XML, not DB rows), so we encode the
	//SheetInternalType into a negative id to round-trip it through the picker.
	static long InternalId(SheetInternalType internalType) {
		return -(1000+(long)internalType);
	}

	static bool TryDecodeInternal(long sheetDefNum,out SheetInternalType internalType) {
		internalType=default;
		if(sheetDefNum>=0) {
			return false;
		}
		int raw=(int)(-sheetDefNum-1000);
		if(!Enum.IsDefined(typeof(SheetInternalType),raw)) {
			return false;
		}
		internalType=(SheetInternalType)raw;
		return true;
	}

	//Resolves the picker id (positive=custom DB def, negative=internal XML def) to a
	//SheetDef with its fields loaded, ready for SheetUtil.CreateSheet.
	static SheetDef ResolveSheetDef(long sheetDefNum) {
		if(TryDecodeInternal(sheetDefNum,out SheetInternalType internalType)) {
			return SheetsInternal.GetSheetDef(internalType);
		}
		if(sheetDefNum>0) {
			return SheetDefs.GetSheetDef(sheetDefNum,false);//loads SheetFieldDefs + parameters
		}
		return null;
	}

	//Value a checkbox stores on a sheet is "X" when checked, "" when not (see
	//FormSheetFillEdit's checkbox handling), so we normalize whatever the browser sends.
	static string NormalizeCheckBox(string value) {
		if(string.IsNullOrEmpty(value) || value=="0" || value=="false" || value=="off") {
			return "";
		}
		return "X";
	}

	static object ToFieldPayload(SheetField field) {
		return new {
			fieldName=field.FieldName??"",
			fieldType=field.FieldType.ToString(),
			fieldValue=field.FieldValue??"",
			editable=true,
		};
	}

	public static void Map(WebApplication app) {
		//----- Available SheetDefs for manual entry (FrmSheetPicker's list) -----
		app.MapGet("/api/sheetdefs",() => {
			List<object> list=new List<object>();
			for(int t=0;t<_fillableTypes.Length;t++) {
				SheetTypeEnum sheetType=_fillableTypes[t];
				List<SheetDef> listCustom=SheetDefs.GetCustomForType(sheetType).OrderBy(x => x.Description).ToList();
				for(int i=0;i<listCustom.Count;i++) {
					list.Add(new {
						sheetDefNum=listCustom[i].SheetDefNum,
						description=listCustom[i].Description,
						sheetType=sheetType.ToString(),
						isInternal=false,
					});
				}
				//Only fall back to the built-in internal defs when the office has no
				//custom def of this type, exactly like FrmSheetPicker.
				if(listCustom.Count==0 && _internalFallbacks.TryGetValue(sheetType,out SheetInternalType[] arrayInternal)) {
					for(int i=0;i<arrayInternal.Length;i++) {
						SheetDef sheetDefInternal=SheetsInternal.GetSheetDef(arrayInternal[i]);
						list.Add(new {
							sheetDefNum=InternalId(arrayInternal[i]),
							description=sheetDefInternal.Description,
							sheetType=sheetType.ToString(),
							isInternal=true,
						});
					}
				}
			}
			return Results.Json(list);
		});

		//----- Create a Sheet from a chosen def for a patient, auto-filling fields -----
		app.MapPost("/api/patients/{patNum:long}/sheets",(long patNum,CreateSheetDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			SheetDef sheetDef=ResolveSheetDef(dto.SheetDefNum);
			if(sheetDef==null) {
				return Results.Text("Unknown sheetDefNum "+dto.SheetDefNum,statusCode:404);
			}
			//Same sequence the desktop uses before showing FormSheetFillEdit.
			Sheet sheet=SheetUtil.CreateSheet(sheetDef,patNum);
			SheetParameter.SetParameter(sheet,"PatNum",patNum);
			SheetFiller.FillFields(sheet);
			SheetUtil.CalculateHeights(sheet);
			long sheetNum=Sheets.SaveNewSheet(sheet);
			List<object> listFields=sheet.SheetFields
				.Where(x => IsEditable(x.FieldType) && !string.IsNullOrEmpty(x.FieldName))
				.Select(ToFieldPayload)
				.ToList();
			return Results.Json(new { sheetNum,fields=listFields });
		});

		//----- Update the editable field values on an existing sheet and re-save -----
		app.MapPut("/api/sheets/{sheetNum:long}/fields",(long sheetNum,FieldUpdateDto dto) => {
			Sheet sheet=Sheets.GetSheet(sheetNum);
			if(sheet==null) {
				return Results.NotFound();
			}
			Dictionary<string,string> dictValues=dto.Fields??new Dictionary<string,string>();
			for(int i=0;i<sheet.SheetFields.Count;i++) {
				SheetField field=sheet.SheetFields[i];
				if(!IsEditable(field.FieldType) || string.IsNullOrEmpty(field.FieldName)) {
					continue;
				}
				if(!dictValues.TryGetValue(field.FieldName,out string newValue)) {
					continue;
				}
				field.FieldValue=field.FieldType==SheetFieldType.CheckBox?NormalizeCheckBox(newValue):(newValue??"");
				//Sheet already exists (not new), so update the field rows in place rather
				//than SaveNewSheet, which only accepts brand-new sheets.
				SheetFields.Update(field);
			}
			List<object> listFields=sheet.SheetFields
				.Where(x => IsEditable(x.FieldType) && !string.IsNullOrEmpty(x.FieldName))
				.Select(ToFieldPayload)
				.ToList();
			return Results.Json(new { sheetNum,fields=listFields });
		});

		//----- Sheets already on file for a patient (render via GET /api/sheets/{n}/pdf) -----
		app.MapGet("/api/patients/{patNum:long}/sheets",(long patNum) => {
			List<Sheet> listSheets=Sheets.GetForPatient(patNum).OrderByDescending(x => x.DateTimeSheet).ToList();
			return Results.Json(listSheets.Select(x => new {
				sheetNum=x.SheetNum,
				description=x.Description??"",
				sheetType=x.SheetType.ToString(),
				dateTimeSheet=x.DateTimeSheet.Year>1880?x.DateTimeSheet.ToString("yyyy-MM-dd HH:mm"):"",
			}));
		});
	}
}
