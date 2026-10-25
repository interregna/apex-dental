/*
 * Insurance plan & subscriber editor: the write side of FormInsPlan / FormInsPlans /
 * FormInsPlanSelectFam / FormBenefitEdit. The full add-insurance workflow:
 * pick or create a plan (InsPlans.GetByCarrierName / Carriers.GetByNameAndPhone /
 * InsPlans.Insert), create the InsSub for the subscriber (InsSubs.Insert),
 * attach the patient at the next ordinal (PatPlans.Insert), and edit plan fields
 * plus the benefit list (InsPlans.Update / Benefits.UpdateList).
 * Every mutation goes through the real business layer, so InsEditLogs /
 * InsEditPatLogs audit entries and InsVerifies upserts happen exactly as on desktop.
 * Read endpoints /api/insplans/{planNum} and /api/insplans/{planNum}/benefits are
 * owned by PatfamInsuranceViewModule / InsCoveragePanelModule; this module adds
 * the raw editable shapes and the writes.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class InsPlanSubscriberEditorModule {
	record InsPlanDto(string CarrierName,string CarrierPhone,string GroupName,string GroupNum,string PlanType,
		string EmployerName,long? FeeSched,long? CopayFeeSched,int? MonthRenew,string PlanNote,bool? IsMedical,bool? IsHidden);
	record NewInsSubDto(long PlanNum,long SubscriberPatNum,string SubscriberId,string DateEffective,string DateTerm,
		bool? ReleaseInfo,bool? AssignBen,string SubscNote);
	record EditInsSubDto(string SubscriberId,string DateEffective,string DateTerm,bool? ReleaseInfo,bool? AssignBen,string SubscNote);
	record NewPatPlanDto(long InsSubNum,string Relationship,bool? IsPending);
	record BenefitDto(long BenefitNum,string BenefitType,long CovCatNum,int Percent,double MonetaryAmt,
		string TimePeriod,string QuantityQualifier,int Quantity,long CodeNum,string CoverageLevel);
	record BenefitListDto(List<BenefitDto> Benefits);

	public static void Map(WebApplication app) {
		//Plan search by carrier, mirroring FormInsPlans: type a carrier name, get every
		//matching carrier's plans so an existing plan can be reused instead of duplicated.
		app.MapGet("/api/insplans/search",(string carrier) => {
			if(string.IsNullOrWhiteSpace(carrier)) {
				return Results.Json(new { plans=new List<object>() });
			}
			List<Carrier> listCarriers=Carriers.GetWhere(x => x.CarrierName.ToUpper().Contains(carrier.Trim().ToUpper()),true)
				.OrderBy(x => x.CarrierName)
				.Take(25)
				.ToList();
			List<object> plans=new List<object>();
			foreach(Carrier carrierCur in listCarriers) {
				List<InsPlan> listInsPlans=InsPlans.GetByCarrierName(carrierCur.CarrierName);
				plans.AddRange(listInsPlans
					.Where(x => x.CarrierNum==carrierCur.CarrierNum && !x.IsHidden)
					.Select(x => (object)new {
						planNum=x.PlanNum,
						carrierNum=x.CarrierNum,
						carrier=carrierCur.CarrierName,
						carrierPhone=carrierCur.Phone,
						groupName=x.GroupName,
						groupNum=x.GroupNum,
						planType=x.PlanType,
						planTypeDesc=InsCoveragePanelModule.GetPlanTypeDesc(x),
						employer=x.EmployerNum==0?"":Employers.GetName(x.EmployerNum),
						feeSched=x.FeeSched==0?"":FeeScheds.GetDescription(x.FeeSched),
					}));
			}
			return Results.Json(new { plans=plans.Take(100) });
		});

		//Raw editable plan fields (the /api/insplans/{planNum} GET owned by
		//PatfamInsuranceViewModule returns display strings; the editor needs raw values).
		app.MapGet("/api/insplans/{planNum:long}/edit",(long planNum) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			return Results.Json(PlanToJson(insPlan));
		});

		//Create a plan, same chain FormInsPlan uses for a brand new plan:
		//carrier is get-or-create by exact name+phone (Carriers.GetByNameAndPhone inserts
		//and logs if missing), employer get-or-create by name, then InsPlans.Insert
		//(InsEditLogs + InsVerifies handled inside).
		app.MapPost("/api/insplans",(InsPlanDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.CarrierName)) {
				return Results.Text("Carrier name is required",statusCode:400);
			}
			string planType=dto.PlanType??"";
			if(planType!="" && planType!="p" && planType!="f" && planType!="c") {
				return Results.Text("PlanType must be one of '', 'p', 'f', 'c'",statusCode:400);
			}
			Carrier carrier=Carriers.GetByNameAndPhone(dto.CarrierName.Trim(),dto.CarrierPhone??"",updateCacheIfNew:true);
			InsPlan insPlan=new InsPlan();
			insPlan.CarrierNum=carrier.CarrierNum;
			insPlan.GroupName=dto.GroupName??"";
			insPlan.GroupNum=dto.GroupNum??"";
			insPlan.PlanType=planType;
			insPlan.EmployerNum=Employers.GetEmployerNum(dto.EmployerName?.Trim()??"");
			insPlan.FeeSched=dto.FeeSched??0;
			insPlan.CopayFeeSched=dto.CopayFeeSched??0;
			insPlan.MonthRenew=(byte)Math.Clamp(dto.MonthRenew??0,0,12);
			insPlan.PlanNote=dto.PlanNote??"";
			insPlan.IsMedical=dto.IsMedical??false;
			InsPlans.Insert(insPlan);
			return Results.Json(PlanToJson(insPlan));
		});

		//Edit plan fields: only fields supplied in the body change, then
		//InsPlans.Update(new,old) writes the InsEditLogs diff exactly like FormInsPlan OK.
		app.MapPut("/api/insplans/{planNum:long}",(long planNum,InsPlanDto dto) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			InsPlan insPlanOld=insPlan.Copy();
			if(!string.IsNullOrWhiteSpace(dto.CarrierName)) {
				Carrier carrier=Carriers.GetByNameAndPhone(dto.CarrierName.Trim(),dto.CarrierPhone??"",updateCacheIfNew:true);
				insPlan.CarrierNum=carrier.CarrierNum;
			}
			if(dto.GroupName!=null) {
				insPlan.GroupName=dto.GroupName;
			}
			if(dto.GroupNum!=null) {
				insPlan.GroupNum=dto.GroupNum;
			}
			if(dto.PlanType!=null) {
				if(dto.PlanType!="" && dto.PlanType!="p" && dto.PlanType!="f" && dto.PlanType!="c") {
					return Results.Text("PlanType must be one of '', 'p', 'f', 'c'",statusCode:400);
				}
				insPlan.PlanType=dto.PlanType;
			}
			if(dto.EmployerName!=null) {
				insPlan.EmployerNum=Employers.GetEmployerNum(dto.EmployerName.Trim());
			}
			if(dto.FeeSched!=null) {
				insPlan.FeeSched=dto.FeeSched.Value;
			}
			if(dto.CopayFeeSched!=null) {
				insPlan.CopayFeeSched=dto.CopayFeeSched.Value;
			}
			if(dto.MonthRenew!=null) {
				insPlan.MonthRenew=(byte)Math.Clamp(dto.MonthRenew.Value,0,12);
			}
			if(dto.PlanNote!=null) {
				insPlan.PlanNote=dto.PlanNote;
			}
			if(dto.IsMedical!=null) {
				insPlan.IsMedical=dto.IsMedical.Value;
			}
			if(dto.IsHidden!=null) {
				insPlan.IsHidden=dto.IsHidden.Value;
			}
			InsPlans.Update(insPlan,insPlanOld);
			return Results.Json(PlanToJson(insPlan));
		});

		//Subscriptions where this patient is the subscriber (FormInsPlanSelectFam's
		//per-subscriber rows) — lets the add flow reuse an existing InsSub.
		app.MapGet("/api/patients/{patNum:long}/inssubs",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<InsSub> listInsSubs=InsSubs.GetListForSubscriber(patNum);
			return Results.Json(listInsSubs.Select(x => SubToJson(x)));
		});

		//Create the subscription: FormInsPlan's "new plan for subscriber" path.
		//Defaults mirror the desktop dialog: ReleaseInfo checked, AssignBen from
		//PrefName.InsDefaultAssignBen. InsSubs.Insert writes the InsEditPatLogs entry.
		app.MapPost("/api/inssubs",(NewInsSubDto dto) => {
			InsPlan insPlan=InsPlans.RefreshOne(dto.PlanNum);
			if(insPlan==null) {
				return Results.Text("Plan not found",statusCode:404);
			}
			Patient patSubscriber=Patients.GetPat(dto.SubscriberPatNum);
			if(patSubscriber==null) {
				return Results.Text("Subscriber patient not found",statusCode:404);
			}
			if(InsSubs.GetListForSubscriber(dto.SubscriberPatNum).Any(x => x.PlanNum==dto.PlanNum)) {
				return Results.Text("Patient is already a subscriber on this plan.",statusCode:400);
			}
			InsSub insSub=new InsSub();
			insSub.PlanNum=dto.PlanNum;
			insSub.Subscriber=dto.SubscriberPatNum;
			insSub.SubscriberID=dto.SubscriberId??"";
			insSub.DateEffective=DateTime.TryParse(dto.DateEffective,out DateTime dateEffective)?dateEffective:DateTime.MinValue;
			insSub.DateTerm=DateTime.TryParse(dto.DateTerm,out DateTime dateTerm)?dateTerm:DateTime.MinValue;
			insSub.ReleaseInfo=dto.ReleaseInfo??true;
			insSub.AssignBen=dto.AssignBen??PrefC.GetBool(PrefName.InsDefaultAssignBen);
			insSub.SubscNote=dto.SubscNote??"";
			insSub.BenefitNotes="";
			InsSubs.Insert(insSub);
			return Results.Json(SubToJson(insSub));
		});

		//Edit subscription fields; InsSubs.Update is the same call FormInsPlan makes on OK.
		app.MapPut("/api/inssubs/{insSubNum:long}",(long insSubNum,EditInsSubDto dto) => {
			InsSub insSub=InsSubs.GetOne(insSubNum);
			if(insSub==null) {
				return Results.NotFound();
			}
			if(dto.SubscriberId!=null) {
				insSub.SubscriberID=dto.SubscriberId;
			}
			if(dto.DateEffective!=null) {
				insSub.DateEffective=DateTime.TryParse(dto.DateEffective,out DateTime dateEffective)?dateEffective:DateTime.MinValue;
			}
			if(dto.DateTerm!=null) {
				insSub.DateTerm=DateTime.TryParse(dto.DateTerm,out DateTime dateTerm)?dateTerm:DateTime.MinValue;
			}
			if(dto.ReleaseInfo!=null) {
				insSub.ReleaseInfo=dto.ReleaseInfo.Value;
			}
			if(dto.AssignBen!=null) {
				insSub.AssignBen=dto.AssignBen.Value;
			}
			if(dto.SubscNote!=null) {
				insSub.SubscNote=dto.SubscNote;
			}
			InsSubs.Update(insSub);
			return Results.Json(SubToJson(insSub));
		});

		//Attach the patient to a subscription at the next ordinal — FormInsPlan's
		//add-patplan path. PatPlans.Insert upserts InsVerifies and logs InsEditPatLogs.
		app.MapPost("/api/patients/{patNum:long}/patplans",(long patNum,NewPatPlanDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			InsSub insSub=InsSubs.GetOne(dto.InsSubNum);
			if(insSub==null) {
				return Results.Text("InsSub not found",statusCode:404);
			}
			List<PatPlan> listPatPlans=PatPlans.Refresh(patNum);
			if(listPatPlans.Any(x => x.InsSubNum==dto.InsSubNum)) {
				return Results.Text("Patient is already covered by this subscription.",statusCode:400);
			}
			PatPlan patPlan=new PatPlan();
			patPlan.PatNum=patNum;
			patPlan.InsSubNum=dto.InsSubNum;
			patPlan.Ordinal=(byte)(listPatPlans.Count+1);//next ordinal, same as FormInsPlan
			patPlan.Relationship=Enum.TryParse(dto.Relationship,out Relat relat)?relat:(insSub.Subscriber==patNum?Relat.Self:Relat.Dependent);
			patPlan.IsPending=dto.IsPending??false;
			patPlan.PatID="";
			PatPlans.Insert(patPlan);
			return Results.Json(new { patPlanNum=patPlan.PatPlanNum,ordinal=(int)patPlan.Ordinal,relationship=patPlan.Relationship.ToString() });
		});

		//Raw plan-level benefit list for the editor grid (the display-shaped GET
		///api/insplans/{planNum}/benefits is owned by InsCoveragePanelModule).
		app.MapGet("/api/insplans/{planNum:long}/benefits/edit",(long planNum) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			return Results.Json(new {
				planNum,
				benefits=Benefits.GetForPlanOrPatPlan(planNum,0).Select(x => BenefitToJson(x)),
			});
		});

		//Replace the plan-level benefit list: the exact FormInsPlan pattern —
		//Benefits.UpdateList(old,new) inserts rows with BenefitNum 0, diffs/updates
		//matching rows, and deletes rows missing from the new list (all audit-logged).
		app.MapPut("/api/insplans/{planNum:long}/benefits",(long planNum,BenefitListDto dto) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			if(dto.Benefits==null) {
				return Results.Text("Benefits list is required",statusCode:400);
			}
			List<Benefit> listBenefitsOld=Benefits.GetForPlanOrPatPlan(planNum,0);
			List<Benefit> listBenefitsNew=new List<Benefit>();
			foreach(BenefitDto benefitDto in dto.Benefits) {
				if(benefitDto.BenefitNum!=0 && !listBenefitsOld.Any(x => x.BenefitNum==benefitDto.BenefitNum)) {
					return Results.Text("BenefitNum "+benefitDto.BenefitNum+" does not belong to this plan",statusCode:400);
				}
				if(!Enum.TryParse(benefitDto.BenefitType,out InsBenefitType benefitType)) {
					return Results.Text("Invalid BenefitType: "+benefitDto.BenefitType,statusCode:400);
				}
				if(!Enum.TryParse(benefitDto.TimePeriod??"None",out BenefitTimePeriod timePeriod)) {
					return Results.Text("Invalid TimePeriod: "+benefitDto.TimePeriod,statusCode:400);
				}
				if(!Enum.TryParse(benefitDto.QuantityQualifier??"None",out BenefitQuantity quantityQualifier)) {
					return Results.Text("Invalid QuantityQualifier: "+benefitDto.QuantityQualifier,statusCode:400);
				}
				if(!Enum.TryParse(benefitDto.CoverageLevel??"None",out BenefitCoverageLevel coverageLevel)) {
					return Results.Text("Invalid CoverageLevel: "+benefitDto.CoverageLevel,statusCode:400);
				}
				Benefit benefit=new Benefit();
				benefit.BenefitNum=benefitDto.BenefitNum;
				benefit.PlanNum=planNum;
				benefit.PatPlanNum=0;
				benefit.CovCatNum=benefitDto.CovCatNum;
				benefit.BenefitType=benefitType;
				benefit.Percent=benefitDto.Percent;
				benefit.MonetaryAmt=benefitDto.MonetaryAmt;
				benefit.TimePeriod=timePeriod;
				benefit.QuantityQualifier=quantityQualifier;
				benefit.Quantity=(byte)Math.Clamp(benefitDto.Quantity,0,255);
				benefit.CodeNum=benefitDto.CodeNum;
				benefit.CoverageLevel=coverageLevel;
				listBenefitsNew.Add(benefit);
			}
			Benefits.UpdateList(listBenefitsOld,listBenefitsNew);
			return Results.Json(new {
				planNum,
				benefits=Benefits.GetForPlanOrPatPlan(planNum,0).Select(x => BenefitToJson(x)),
			});
		});

		//Pick lists for the editor: coverage categories and fee schedules.
		app.MapGet("/api/insurance/covcats",() =>
			Results.Json(CovCats.GetDeepCopy(true).Select(x => new {
				covCatNum=x.CovCatNum,
				description=x.Description,
				ebenCat=x.EbenefitCat.ToString(),
			})));

		app.MapGet("/api/insurance/feescheds",() =>
			Results.Json(FeeScheds.GetDeepCopy(true).Select(x => new {
				feeSchedNum=x.FeeSchedNum,
				description=x.Description,
				feeSchedType=x.FeeSchedType.ToString(),
			})));
	}

	static object PlanToJson(InsPlan insPlan) {
		Carrier carrier=Carriers.GetCarrier(insPlan.CarrierNum);
		return new {
			planNum=insPlan.PlanNum,
			carrierNum=insPlan.CarrierNum,
			carrierName=carrier?.CarrierName??"",
			carrierPhone=carrier?.Phone??"",
			groupName=insPlan.GroupName,
			groupNum=insPlan.GroupNum,
			planType=insPlan.PlanType,
			planTypeDesc=InsCoveragePanelModule.GetPlanTypeDesc(insPlan),
			employerName=insPlan.EmployerNum==0?"":Employers.GetName(insPlan.EmployerNum),
			feeSched=insPlan.FeeSched,
			copayFeeSched=insPlan.CopayFeeSched,
			monthRenew=(int)insPlan.MonthRenew,
			planNote=insPlan.PlanNote,
			isMedical=insPlan.IsMedical,
			isHidden=insPlan.IsHidden,
		};
	}

	static object SubToJson(InsSub insSub) {
		InsPlan insPlan=InsPlans.RefreshOne(insSub.PlanNum);
		return new {
			insSubNum=insSub.InsSubNum,
			planNum=insSub.PlanNum,
			subscriberPatNum=insSub.Subscriber,
			subscriber=Patients.GetPat(insSub.Subscriber)?.GetNameLF()??"",
			subscriberId=insSub.SubscriberID,
			carrier=insPlan==null?"":Carriers.GetName(insPlan.CarrierNum),
			groupName=insPlan?.GroupName??"",
			groupNum=insPlan?.GroupNum??"",
			dateEffective=insSub.DateEffective.Year>1880?insSub.DateEffective.ToString("yyyy-MM-dd"):"",
			dateTerm=insSub.DateTerm.Year>1880?insSub.DateTerm.ToString("yyyy-MM-dd"):"",
			releaseInfo=insSub.ReleaseInfo,
			assignBen=insSub.AssignBen,
			subscNote=insSub.SubscNote,
		};
	}

	static object BenefitToJson(Benefit benefit) {
		return new {
			benefitNum=benefit.BenefitNum,
			covCatNum=benefit.CovCatNum,
			covCat=benefit.CovCatNum==0?"":CovCats.GetDesc(benefit.CovCatNum),
			benefitType=benefit.BenefitType.ToString(),
			percent=benefit.Percent,
			monetaryAmt=benefit.MonetaryAmt,
			timePeriod=benefit.TimePeriod.ToString(),
			quantityQualifier=benefit.QuantityQualifier.ToString(),
			quantity=(int)benefit.Quantity,
			codeNum=benefit.CodeNum,
			coverageLevel=benefit.CoverageLevel.ToString(),
			descript=InsCoveragePanelModule.GetBenefitDesc(benefit),
			value=InsCoveragePanelModule.GetBenefitValueDesc(benefit),
		};
	}
}
