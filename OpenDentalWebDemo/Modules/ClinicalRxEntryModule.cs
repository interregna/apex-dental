/*
 * Clinical Rx entry module: prescriptions for one patient — the web replacement
 * for FormRxSelect (pick from RxDef templates), FormRxEdit (edit fields, controlled
 * flag, provider), and the list slice of FormRxManage.  Templates come from
 * RxDefs.Refresh, and all patient Rx reads/writes go through the real business
 * layer: RxPats.GetAllForPat/GetRx/Insert/Update/Delete (audit-logged via CRUD).
 * Template-to-Rx defaults mirror FormRxSelect.RxSelected: copy Drug/Sig/Disp/
 * Refills/IsControlled/PatientInstruction, never Notes.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalRxEntryModule {
	record RxPatDto(long RxDefNum,string RxDate,string Drug,string Sig,string Disp,string Refills,string Notes,string PatientInstruction,bool IsControlled,long ProvNum,string SendStatus);

	public static void Map(WebApplication app) {
		//---------- Rx templates (RxDef pick list, FormRxSelect grid) ----------
		app.MapGet("/api/rxdefs",() => {
			RxDef[] arrayRxDefs=RxDefs.Refresh();
			return Results.Json(arrayRxDefs.OrderBy(x => x.Drug).Select(x => new {
				rxDefNum=x.RxDefNum,
				drug=x.Drug,
				sig=x.Sig,
				disp=x.Disp,
				refills=x.Refills,
				notes=x.Notes,
				isControlled=x.IsControlled,
				patientInstruction=x.PatientInstruction,
			}));
		});

		//---------- patient prescriptions ----------
		app.MapGet("/api/patients/{patNum:long}/rx",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<RxPat> listRxPats=RxPats.GetAllForPat(patNum);
			return Results.Json(listRxPats.OrderByDescending(x => x.RxDate).ThenByDescending(x => x.RxNum).Select(x => ToJson(x)));
		});

		app.MapPost("/api/patients/{patNum:long}/rx",(long patNum,RxPatDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			//Same starting point as FormRxSelect.RxSelected/butBlank_Click for a new Rx.
			RxPat rxPat=new RxPat();
			rxPat.PatNum=patNum;
			rxPat.ClinicNum=pat.ClinicNum;
			rxPat.RxDate=DateTime.Today;
			rxPat.SendStatus=RxSendStatus.Unsent;
			rxPat.Drug="";
			rxPat.Sig="";
			rxPat.Disp="";
			rxPat.Refills="";
			rxPat.Notes="";
			rxPat.PatientInstruction="";
			rxPat.DosageCode="";
			rxPat.ErxGuid="";
			rxPat.ErxPharmacyInfo="";
			rxPat.ProvNum=pat.PriProv;
			if(dto.RxDefNum!=0) {
				RxDef rxDef=RxDefs.GetOne(dto.RxDefNum);
				if(rxDef==null) {
					return Results.Text("Rx template not found",statusCode:400);
				}
				//Copy the template down, exactly like FormRxSelect (Notes intentionally not copied).
				rxPat.Drug=rxDef.Drug??"";
				rxPat.Sig=rxDef.Sig??"";
				rxPat.Disp=rxDef.Disp??"";
				rxPat.Refills=rxDef.Refills??"";
				rxPat.IsControlled=rxDef.IsControlled;
				rxPat.PatientInstruction=rxDef.PatientInstruction??"";
			}
			ApplyDto(rxPat,dto);
			if(string.IsNullOrWhiteSpace(rxPat.Drug)) {
				return Results.Text("Drug is required",statusCode:400);
			}
			long rxNum=RxPats.Insert(rxPat);
			return Results.Json(ToJson(RxPats.GetRx(rxNum)??rxPat));
		});

		app.MapPut("/api/rx/{rxNum:long}",(long rxNum,RxPatDto dto) => {
			RxPat rxPat=RxPats.GetRx(rxNum);
			if(rxPat==null) {
				return Results.NotFound();
			}
			ApplyDto(rxPat,dto);
			if(string.IsNullOrWhiteSpace(rxPat.Drug)) {
				return Results.Text("Drug is required",statusCode:400);
			}
			RxPats.Update(rxPat);
			return Results.Json(ToJson(rxPat));
		});

		app.MapDelete("/api/rx/{rxNum:long}",(long rxNum) => {
			RxPat rxPat=RxPats.GetRx(rxNum);
			if(rxPat==null) {
				return Results.NotFound();
			}
			RxPats.Delete(rxNum);
			return Results.Json(new { deleted=rxNum });
		});

		//---------- Printable Rx (FormRxEdit "Print" -> the internal Rx sheet) ----------
		//The Rx sheet is a field-based sheet (no grid) filled from the RxPat via the "RxNum"
		//parameter (SheetFiller.FillFieldsForRx). Same headless PDF path as the consent sheet.
		app.MapGet("/api/rx/{rxNum:long}/pdf",(long rxNum) => {
			RxPat rxPat=RxPats.GetRx(rxNum);
			if(rxPat==null) {
				return Results.NotFound();
			}
			OpenDentBusiness.Patient pat=Patients.GetPat(rxPat.PatNum);
			SheetDef sheetDef=SheetDefs.GetInternalOrCustom(SheetInternalType.Rx);
			Sheet sheet=SheetUtil.CreateSheet(sheetDef,rxPat.PatNum);
			SheetParameter.SetParameter(sheet,"RxNum",rxNum);
			SheetFiller.FillFields(sheet);
			SheetUtil.CalculateHeights(sheet);
			string pdfPath=Path.Combine(Path.GetTempPath(),$"odrx_{rxNum}.pdf");
			SheetPrinting.CreatePdf(sheet,pdfPath,null,null,null,pat);
			return Results.File(File.ReadAllBytes(pdfPath),"application/pdf");
		});
	}

	///<summary>Copies the editable FormRxEdit fields from the dto onto the RxPat.  Zero/blank dto values leave defaults alone where that matches desktop behavior.</summary>
	static void ApplyDto(RxPat rxPat,RxPatDto dto) {
		if(DateTime.TryParse(dto.RxDate,out DateTime rxDate)) {
			rxPat.RxDate=rxDate;
		}
		if(dto.Drug!=null) {
			rxPat.Drug=dto.Drug.Trim();
		}
		if(dto.Sig!=null) {
			rxPat.Sig=dto.Sig;
		}
		if(dto.Disp!=null) {
			rxPat.Disp=dto.Disp;
		}
		if(dto.Refills!=null) {
			rxPat.Refills=dto.Refills;
		}
		if(dto.Notes!=null) {
			rxPat.Notes=dto.Notes;
		}
		if(dto.PatientInstruction!=null) {
			rxPat.PatientInstruction=dto.PatientInstruction;
		}
		rxPat.IsControlled=dto.IsControlled;
		if(dto.ProvNum!=0) {
			rxPat.ProvNum=dto.ProvNum;
		}
		if(Enum.TryParse(dto.SendStatus,out RxSendStatus sendStatus)) {
			rxPat.SendStatus=sendStatus;
		}
	}

	static object ToJson(RxPat rxPat) {
		return new {
			rxNum=rxPat.RxNum,
			patNum=rxPat.PatNum,
			rxDate=rxPat.RxDate.Year>1880?rxPat.RxDate.ToString("yyyy-MM-dd"):"",
			drug=rxPat.Drug,
			sig=rxPat.Sig,
			disp=rxPat.Disp,
			refills=rxPat.Refills,
			notes=rxPat.Notes,
			patientInstruction=rxPat.PatientInstruction,
			isControlled=rxPat.IsControlled,
			provNum=rxPat.ProvNum,
			provAbbr=rxPat.ProvNum==0?"":Providers.GetAbbr(rxPat.ProvNum),
			sendStatus=rxPat.SendStatus.ToString(),
		};
	}
}
