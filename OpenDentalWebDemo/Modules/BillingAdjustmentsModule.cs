/*
 * Adjustment entry (FormAdjust + the adjustments slice of the account module).
 * Adjustments are additions/discounts/write-offs on a patient account, typed by the
 * practice's DefCat.AdjTypes definitions: def ItemValue "+" = positive (addition),
 * "-" = negative (discount/write-off), "dp" = discount plan (also negative).
 * The API accepts the amount as a positive magnitude and applies the sign per type,
 * exactly like FormAdjust does on OK.  Writes go through the same business calls the
 * desktop dialog makes: Adjustments.Insert / Update / Delete, each paired with the
 * SecurityLogs.MakeLogEntry the form stamps, plus the form's delete guards
 * (attached paysplits via PaySplits.GetForAdjustments, payment-plan links via
 * PayPlanLinks.GetForFKeyAndLinkType) and Signalods.SetInvalid(BillingList).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingAdjustmentsModule {
	record AdjustmentDto(string Date,long AdjType,double Amount,long ProvNum,string Note,long ProcNum);

	//Sign convention from the def's ItemValue, same test FormAdjust runs: "+" stores the
	//amount as entered, "-" and "dp" store it negated.
	static double SignedAmount(long adjType,double amount) {
		string itemValue=Defs.GetValue(DefCat.AdjTypes,adjType);
		if(itemValue=="-" || itemValue=="dp") {
			return -Math.Abs(amount);
		}
		return Math.Abs(amount);
	}

	static object ToPayload(Adjustment adj) {
		return new {
			adjNum=adj.AdjNum,
			adjDate=adj.AdjDate.ToString("yyyy-MM-dd"),
			procDate=adj.ProcDate.Year>1880?adj.ProcDate.ToString("yyyy-MM-dd"):"",
			adjType=adj.AdjType,
			typeName=Defs.GetName(DefCat.AdjTypes,adj.AdjType),
			sign=Defs.GetValue(DefCat.AdjTypes,adj.AdjType),
			amount=adj.AdjAmt,
			provNum=adj.ProvNum,
			provAbbr=Providers.GetAbbr(adj.ProvNum),
			note=adj.AdjNote??"",
			procNum=adj.ProcNum,
		};
	}

	public static void Map(WebApplication app) {
		//The practice's adjustment types (DefCat.AdjTypes), split by sign the way the
		//dialog shows its Additions and Subtractions list boxes.  isShort:true excludes
		//hidden defs, same as FillListBoxAdjTypes.
		app.MapGet("/api/adjustment-types",() => {
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.AdjTypes,true);
			return Results.Json(listDefs.Select(x => new {
				defNum=x.DefNum,
				name=x.ItemName,
				sign=x.ItemValue,//"+" addition, "-" subtraction, "dp" discount plan
			}));
		});

		//A patient's adjustments, the rows the Account module grid shows for the adjust
		//table: Adjustments.Refresh(patNum), newest first, with type/provider resolved.
		app.MapGet("/api/patients/{patNum:long}/adjustments",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Adjustment[] adjustmentArray=Adjustments.Refresh(patNum);
			return Results.Json(new {
				patNum=pat.PatNum,
				patient=pat.GetNameLF(),
				total=adjustmentArray.Sum(x => x.AdjAmt),
				adjustments=adjustmentArray
					.OrderByDescending(x => x.AdjDate)
					.ThenByDescending(x => x.AdjNum)
					.Select(ToPayload),
			});
		});

		//Create — FormAdjust's OK path for a new adjustment: sign from the def, ProcDate
		//from the attached procedure when one is linked (the form locks the field then),
		//Adjustments.Insert (stamps SecUserNumEntry), AdjustmentCreate security log,
		//BillingList signal.
		app.MapPost("/api/patients/{patNum:long}/adjustments",(long patNum,AdjustmentDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.Date,out DateTime adjDate)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			Def def=Defs.GetDef(DefCat.AdjTypes,dto.AdjType);
			if(def==null) {
				return Results.Text("Unknown adjustment type",statusCode:400);
			}
			Adjustment adjustment=new Adjustment();
			adjustment.PatNum=pat.PatNum;
			adjustment.AdjDate=adjDate.Date;
			adjustment.ProcDate=adjDate.Date;
			adjustment.AdjType=dto.AdjType;
			adjustment.AdjAmt=SignedAmount(dto.AdjType,dto.Amount);
			adjustment.ProvNum=dto.ProvNum;
			adjustment.ClinicNum=pat.ClinicNum;
			adjustment.AdjNote=dto.Note??"";
			if(dto.ProcNum!=0) {
				Procedure procedure=Procedures.GetOneProc(dto.ProcNum,false);
				if(procedure==null || procedure.ProcNum==0 || procedure.PatNum!=pat.PatNum) {
					return Results.Text("Procedure not found for this patient",statusCode:400);
				}
				adjustment.ProcNum=procedure.ProcNum;
				adjustment.ProcDate=procedure.ProcDate;//form sets and locks ProcDate from the attached proc
			}
			Adjustments.Insert(adjustment);
			SecurityLogs.MakeLogEntry(EnumPermType.AdjustmentCreate,adjustment.PatNum,
				pat.GetNameLF()+", "+adjustment.AdjAmt.ToString("c"));
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(new { adjNum=adjustment.AdjNum });
		});

		//Edit — same field set as create; Adjustments.Update, AdjustmentEdit security log
		//carrying the previous SecDateTEdit like the form does.
		app.MapPut("/api/adjustments/{adjNum:long}",(long adjNum,AdjustmentDto dto) => {
			Adjustment adjustment=Adjustments.GetOne(adjNum);
			if(adjustment==null || adjustment.AdjNum==0) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.Date,out DateTime adjDate)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			Def def=Defs.GetDef(DefCat.AdjTypes,dto.AdjType);
			if(def==null) {
				return Results.Text("Unknown adjustment type",statusCode:400);
			}
			Patient pat=Patients.GetPat(adjustment.PatNum);
			DateTime datePreviousChange=adjustment.SecDateTEdit;
			adjustment.AdjDate=adjDate.Date;
			adjustment.AdjType=dto.AdjType;
			adjustment.AdjAmt=SignedAmount(dto.AdjType,dto.Amount);
			adjustment.ProvNum=dto.ProvNum;
			adjustment.AdjNote=dto.Note??"";
			if(dto.ProcNum!=adjustment.ProcNum) {
				if(dto.ProcNum==0) {
					adjustment.ProcNum=0;
					adjustment.ProcDate=adjDate.Date;
				}
				else {
					Procedure procedure=Procedures.GetOneProc(dto.ProcNum,false);
					if(procedure==null || procedure.ProcNum==0 || procedure.PatNum!=adjustment.PatNum) {
						return Results.Text("Procedure not found for this patient",statusCode:400);
					}
					adjustment.ProcNum=procedure.ProcNum;
					adjustment.ProcDate=procedure.ProcDate;
				}
			}
			else if(adjustment.ProcNum==0) {
				adjustment.ProcDate=adjDate.Date;
			}
			Adjustments.Update(adjustment);
			SecurityLogs.MakeLogEntry(EnumPermType.AdjustmentEdit,adjustment.PatNum,
				(pat?.GetNameLF()??"")+", "+adjustment.AdjAmt.ToString("c"),0,datePreviousChange);
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(ToPayload(adjustment));
		});

		//Delete — the dialog's Delete button: blocked while a paysplit or payment-plan
		//link is attached, then security log (stamped before the row goes away, like the
		//form) and Adjustments.Delete.
		app.MapDelete("/api/adjustments/{adjNum:long}",(long adjNum) => {
			Adjustment adjustment=Adjustments.GetOne(adjNum);
			if(adjustment==null || adjustment.AdjNum==0) {
				return Results.NotFound();
			}
			if(PaySplits.GetForAdjustments(new List<long>() { adjustment.AdjNum }).Count>0) {
				return Results.Text("Cannot delete adjustment while a payment split is attached.",statusCode:409);
			}
			if(PayPlanLinks.GetForFKeyAndLinkType(adjustment.AdjNum,PayPlanLinkType.Adjustment).Count>0) {
				return Results.Text("Cannot delete adjustment that is attached to a payment plan.",statusCode:409);
			}
			Patient pat=Patients.GetPat(adjustment.PatNum);
			SecurityLogs.MakeLogEntry(EnumPermType.AdjustmentEdit,adjustment.PatNum,
				"Delete for patient: "+(pat?.GetNameLF()??"")+", "+adjustment.AdjAmt.ToString("c"),0,adjustment.SecDateTEdit);
			Adjustments.Delete(adjustment);
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Ok();
		});
	}
}
