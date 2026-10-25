/*
 * Generic report runner over the real OpenDentBusiness report classes
 * (workplan: forms queue, Reports category). One registry entry per report;
 * each runs with sensible whole-practice defaults (all providers, all
 * clinics, all billing types) over a caller-supplied date range. This single
 * surface replaces the per-report WinForms dialogs for read-only use.
 */
using System.Data;
using OpenDentBusiness;

namespace OpenDentalWebDemo;

public record ReportDef(string Id,string Name,Func<DateTime,DateTime,DataTable> Run);

public static class ReportRegistry {

	static List<long> AllProvNums() => Providers.GetDeepCopy(true).Select(x => x.ProvNum).ToList();
	static List<Provider> AllProvs() => Providers.GetDeepCopy(true);
	static List<long> AllClinicNums() => Clinics.GetDeepCopy(true).Select(x => x.ClinicNum).ToList();
	static List<Clinic> AllClinics() => Clinics.GetDeepCopy(true);
	static List<long> AllBillingTypes() => Defs.GetDefsForCategory(DefCat.BillingTypes,true).Select(x => x.DefNum).ToList();
	static List<long> AllPatStatuses() => Enum.GetValues<PatientStatus>().Select(x => (long)x).ToList();
	static List<long> AllUserNums() => Userods.GetDeepCopy(true).Select(x => x.UserNum).ToList();
	static List<long> AllUnearnedTypes() => Defs.GetDefsForCategory(DefCat.PaySplitUnearnedType,true).Select(x => x.DefNum).ToList();
	static List<string> AllAdjTypeStrings() => Defs.GetDefsForCategory(DefCat.AdjTypes,true).Select(x => x.DefNum.ToString()).ToList();

	///<summary>Finance charge AdjType: practice pref if set, else the first visible AdjTypes def.</summary>
	static long FinChargeAdjType() {
		long defNum=PrefC.GetLong(PrefName.FinanceChargeAdjustmentType);
		if(defNum==0) {
			defNum=Defs.GetDefsForCategory(DefCat.AdjTypes,true).Select(x => x.DefNum).FirstOrDefault();
		}
		return defNum;
	}

	///<summary>Whole-practice aging defaults. Clinic list only matters when clinics are enabled; empty prov/billtype lists mean all.</summary>
	static RpAgingParamObject AgingParams(bool isForInsAging) {
		return new RpAgingParamObject {
			AsOfDate=DateTime.Today,
			ListClinicNums=AllClinicNums(),
			IsForInsAging=isForInsAging,
		};
	}

	///<summary>RpOutstandingIns returns typed rows rather than a DataTable; flatten to one.</summary>
	static DataTable OutstandingInsTable(DateTime from,DateTime to) {
		List<RpOutstandingIns.OutstandingInsClaim> listClaims=RpOutstandingIns.GetOutInsClaims(new List<long>(),from,to,
			RpOutstandingIns.PreauthOptions.ExcludingPreauths,new List<long>(),"",new List<long>(),RpOutstandingIns.DateFilterBy.DateSent);
		DataTable table=new DataTable("OutstandingIns");
		table.Columns.Add("Carrier");
		table.Columns.Add("Phone");
		table.Columns.Add("Type");
		table.Columns.Add("Patient");
		table.Columns.Add("PatNum");
		table.Columns.Add("DateService");
		table.Columns.Add("DateSent");
		table.Columns.Add("ClaimFee");
		foreach(RpOutstandingIns.OutstandingInsClaim claim in listClaims??new List<RpOutstandingIns.OutstandingInsClaim>()) {
			table.Rows.Add(claim.CarrierName,claim.CarrierPhone,claim.ClaimType,
				claim.PatLName+", "+claim.PatFName,claim.PatNum.ToString(),
				claim.DateService.ToShortDateString(),claim.DateSent.ToShortDateString(),
				claim.ClaimFee.ToString("F2"));
		}
		return table;
	}

	public static readonly List<ReportDef> Reports=new() {
		new("procs-individual","Procedures — individual",
			(from,to) => RpProcSheet.GetIndividualTable(from,to,AllProvNums(),new List<long>(),"",false,true,false)),
		new("procs-grouped","Procedures — grouped by code",
			(from,to) => RpProcSheet.GetGroupedTable(from,to,AllProvNums(),new List<long>(),"",true)),
		new("newpatients","New patients",
			(from,to) => RpNewPatients.GetNewPatients(from,to,AllProvNums(),includeAddress:true,excludeNoProd:false,hasAllProvs:true)),
		new("birthdays","Patient birthdays",
			(from,to) => RpBirthday.GetBirthdayTable(from,to)),
		new("activepatients","Active patients",
			(from,to) => RpActivePatients.GetActivePatientTable(from,to,AllProvNums(),AllClinicNums(),AllBillingTypes(),AllPatStatuses(),true,true,true)),
		new("netproduction","Net production detail",
			(from,to) => RpProdInc.GetNetProductionDetailDataSet(from,to,AllProvs(),AllClinics(),true,false)),
		new("prescriptions","Prescriptions",
			(from,to) => RpPrescriptions.GetPrescriptionTable(false,"")),
		new("insco","Insurance carriers",
			(from,to) => RpInsCo.GetInsCoTable("")),
		new("capitation","Capitation utilization",
			(from,to) => RpCapitation.GetCapitationTable(from,to,"",false)),
		new("patuncollected","Patient portion uncollected",
			(from,to) => RpPatPortionUncollected.GetPatUncollected(from,to,AllClinicNums())),
		new("insoverpaid","Insurance overpaid",
			(from,to) => RpInsOverpaid.GetInsuranceOverpaid(from,to,AllClinicNums(),groupByProc:true)),
		new("discountplans","Discount plans",
			(from,to) => RpDiscountPlan.GetTable("")),
		new("aging","Aging of A/R",
			(from,to) => RpAging.GetAgingTable(AgingParams(false))),
		new("insaging","Insurance aging",
			(from,to) => RpInsAging.GetInsAgingTable(AgingParams(true))),
		new("financecharge","Finance charges",
			(from,to) => RpFinanceCharge.GetFinanceChargeTable(from,to,FinChargeAdjType(),AllProvNums(),AllBillingTypes())),
		new("ppowriteoff","PPO writeoffs",
			(from,to) => RpPPOwriteoff.GetWriteoffTable(from,to,isIndividual:true,carrierText:"",PPOWriteoffDateCalc.InsPayDate)),
		new("paysheet-ins","Payments — insurance",
			(from,to) => RpPaySheet.GetInsTable(from,to,AllProvNums(),new List<long>(),new List<long>(),new List<long>(),
				hasAllProvs:true,hasAllClinics:true,hasInsuranceTypes:false,isGroupedByPatient:false,hasAllClaimPayGroups:true,doShowProvSeparate:false)),
		new("paysheet-pat","Payments — patient",
			(from,to) => RpPaySheet.GetPatTable(from,to,AllProvNums(),new List<long>(),new List<long>(),
				hasAllProvs:true,hasAllClinics:true,hasPatientTypes:false,isGroupedByPatient:false,isUnearnedIncluded:true,
				doShowProvSeparate:false,doShowHiddenTPUnearned:false)),
		new("adjustments","Adjustments",
			(from,to) => RpAdjSheet.GetAdjTable(from,to,AllProvNums(),AllClinicNums(),AllAdjTypeStrings(),
				hasAllClinics:true,hasClinicsEnabled:PrefC.HasClinicsEnabled)),
		new("unearned","Unearned income — line items",
			(from,to) => RpUnearnedIncome.GetLineItemUnearnedData(new List<long>(),from,to,showProvider:true)),
		new("referrals","Referral analysis",
			(from,to) => RpReferralAnalysis.GetReferralTable(from,to,AllProvNums(),hasAddress:true,hasOnlyNewPats:false)),
		new("outstandingins","Outstanding insurance claims",
			(from,to) => OutstandingInsTable(from,to)),
		new("claimsnotsent","Claims not sent",
			(from,to) => RpClaimNotSent.GetClaimsNotSent(from,to,AllClinicNums(),hasClaimTypeExpanded:false,ClaimNotSentStatuses.All)),
		new("procsnotbilled","Procedures not billed to insurance",
			(from,to) => RpProcNotBilledIns.GetProcsNotBilled(AllClinicNums(),includeMedProcs:true,from,to,
				EnumShowProcsBeforeIns.All,hasMultiVisitProcs:false)),
		new("appointments","Appointments",
			(from,to) => RpAppointments.GetAppointmentTable(from,to,AllProvNums(),AllClinicNums(),PrefC.HasClinicsEnabled,
				isShowRecall:true,isShowNewPat:true,isShowASAP:true,isShowExistingPat:true,
				RpAppointments.SortAndFilterBy.AptDateTime,new List<ApptStatus>(),new List<long>(),"")),
		new("brokenappts","Broken appointments",
			(from,to) => RpBrokenAppointments.GetBrokenApptTable(from,to,AllProvNums(),AllClinicNums(),new List<long>(),
				BrokenApptProcedure.Both,hasAllClinics:true,isByProc:false,isByAptStatus:true,isByAdj:false,
				hasClinicsEnabled:PrefC.HasClinicsEnabled)),
		new("writeoffs","Writeoffs",
			(from,to) => RpWriteoffSheet.GetWriteoffTable(from,to,AllProvNums(),AllClinicNums(),
				hasAllClinics:true,hasClinicsEnabled:PrefC.HasClinicsEnabled,PPOWriteoffDateCalc.InsPayDate)),
		new("payplans","Payment plans",
			(from,to) => RpPayPlan.GetPayPlanTable(from,to,AllProvNums(),AllClinicNums(),hasAllProvs:true,
				DisplayPayPlanType.Both,hideCompletedPlans:false,showFamilyBalance:false,hasDateRange:true,isPayPlanV2:false).Tables[0]),
		new("procnotes","Procedure notes",
			(from,to) => RpProcNote.GetData(AllProvNums(),AllClinicNums(),from,to,includeNoNotes:true,includeUnsignedNotes:true,
				ToothNumberingNomenclature.Universal,RpProcNote.ProcNoteGroupBy.Procedure)),
		new("hiddenpaysplits","Hidden payment splits",
			(from,to) => RpHiddenPaySplits.GetReportData(AllProvNums(),AllUnearnedTypes(),AllClinicNums(),PrefC.HasClinicsEnabled,from,to)),
		new("presentedtx","Presented treatment production",
			(from,to) => RpPresentedTreatmentProduction.GetPresentedTreatmentProductionTable(from,to,AllClinicNums(),
				hasAllClinics:true,hasClinicsEnabled:PrefC.HasClinicsEnabled,isPresenter:false,isFirstPresenter:false,AllUserNums(),isDetailed:true)),
		new("txplanstats","Treatment plan presentation statistics",
			(from,to) => RpTreatPlanPresentationStatistics.GetTreatPlanPresentationStatistics(from,to,isFirstPresented:true,
				hasAllClinics:true,hasClinicsEnabled:PrefC.HasClinicsEnabled,isPresenter:false,isGross:true,
				hasAllUsers:true,AllUserNums(),AllClinicNums())),
		new("sealants","Dental sealant measure (CMS)",
			(from,to) => RpDentalSealantMeasure.GetDentalSealantMeasureTable(from.Year.ToString())),
	};

	public static object RunReport(string id,DateTime from,DateTime to) {
		ReportDef report=Reports.FirstOrDefault(x => x.Id==id)
			??throw new ArgumentException("Unknown report: "+id);
		DataTable table=report.Run(from,to);
		var columns=table.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToList();
		var rows=new List<List<string>>();
		foreach(DataRow row in table.Rows) {
			rows.Add(columns.Select(c => row[c]?.ToString()??"").ToList());
		}
		return new { name=report.Name,columns,rows };
	}
}
