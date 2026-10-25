// Legacy compatibility facade: local policy/accounting with external tax computation.
// Modified 2026-10-24; see NOTICE. No vendor SDK is linked into this process.
using System;
using System.Collections.Generic;
using System.Linq;
using CodeBase;
namespace OpenDentBusiness {
public class AvaTax {
public static bool IsEnabled() {
			return PrefC.IsODHQ && Programs.IsEnabled(ProgramName.AvaTax);
		}
public static bool IsTaxable(long patNum) {
			if(!IsEnabled()) {
				return false;//Save a few db calls
			}
			Patient patient=Patients.GetPat(patNum);
			if(patient==null) {
				return false;
			}
			PatField patFieldTaxExempt=null;
			if(GetTaxExemptPatField()!=null) {
				patFieldTaxExempt=PatFields.Refresh(patNum).FirstOrDefault(x => x.FieldName==GetTaxExemptPatField().FieldName);
			}
			if (GetListTaxableStates().Count==0) {
				return false;
			}
			if (!HasTaxableState(patient)) { //if they aren't taxable, skip them
				return false;
			}
			if (patFieldTaxExempt!=null && PIn.Bool(patFieldTaxExempt.FieldValue)) {
				return false;
			}
			return true;
		}
public static bool DoSendProcToAvalara(Procedure procedure,bool isSilent=false) {
			ProcedureCode procedureCode=ProcedureCodes.GetProcCode(procedure.CodeNum);
			if(!IsEnabled()) {
				return false;
			}
			if(procedure.ProcFee==0){//repeat charges for prepay use ProcFee=0
				return false;
			}
			Patient patient=Patients.GetPat(procedure.PatNum);
			//don't call IsTaxable() here, because that would get pat twice, so duplicating some if its functionality:
			if(patient==null) {
				return false;
			}
			if(GetListTaxableStates().Count==0){
				return false;//no taxable states
			}
			if(!HasTaxableState(patient)){//if this patient is not in a taxable state
				return false;
			}
			if(GetTaxExemptPatField()==null){//no tax exempt pat field entered in setup
				return false;
			}
			PatField patFieldTaxExempt=PatFields.Refresh(patient.PatNum).FirstOrDefault(x => x.FieldName==GetTaxExemptPatField().FieldName);
			if(patFieldTaxExempt!=null && PIn.Bool(patFieldTaxExempt.FieldValue)){//patient field exists and is true
				return false;//so not taxable
			}
			//end of the duplicated functionality from IsTaxable().
			string procTaxCode=GetTaxOverrideIfNeeded(patient,procedureCode);//could be an avalara code, an override, or a percent
			if(string.IsNullOrWhiteSpace(procTaxCode)){//if there is no avalara code or percent for this proc/state
				return false;
			}
			if(!Patients.HasValidUSZipCode(patient)) {
				if(isSilent) {

				}
				else {
					//Remove the message box for now to avoid it from popping up on the server, stopping anyone using middletier to continue
					//forward, because they can't click OK in the message box.
					//MessageBox.Show("A valid zip code is required to process sales tax on procedures in this patient's state. "
					//+"Please update the patient information with a valid zip code before continuing.");
				}
			}
			return true;
		}
public static string GetTaxOverrideIfNeeded(Patient patient,ProcedureCode procedureCode) {
			string overrides=ProgramProperties.GetPropVal(ProgramName.AvaTax,"Tax Code Overrides");
			if(string.IsNullOrWhiteSpace(overrides)) {
				return procedureCode.TaxCode;
			}
			string patStateLower=patient.State.ToLower();
			Address addressTax=Addresses.GetOneByPatNum(patient.PatNum);
			if (addressTax!=null) {
				patStateLower=addressTax.State.ToLower();
			}
			bool stateHasPercentage=false;
			bool hasFormatError=false;
			string[] stringArrayOverrides=overrides.Split(',');
			foreach(string entry in stringArrayOverrides) {
				string[] stringArrayParts=entry.Split('-');
				if(stringArrayParts.Count()!=3) {

					hasFormatError=true;
					continue;
				}
				string stateCode=stringArrayParts[0].Trim();
				string procCodeCur=stringArrayParts[1].Trim();
				string taxCodeCur=stringArrayParts[2].Trim();
				if(stateCode.ToLower()==patStateLower){
					if(taxCodeCur!="" && taxCodeCur.EndsWith("%")){
						stateHasPercentage=true;
					}
				}
				if(stateCode.ToLower()==patStateLower//Allowing state code to be case insensitive, since our database data is really inconsistent.
					&& procCodeCur==procedureCode.ProcCode)//We must use case sensitive proc codes, because OD allows (ex D0120 to exist with d0120).
				{
					return taxCodeCur;
				}
			}
			if(stateHasPercentage){//but matching code was not found.
				return "0%";//we don't want any other codes to be sent to Avalara
			}
			if(hasFormatError) {
				throw new ODException("Unable to parse tax code overrides due to formatting error.");
			}
			return procedureCode.TaxCode;
		}
public static long GetSalesTaxAdjType() {
			return PIn.Long(ProgramProperties.GetPropVal(Programs.GetProgramNum(ProgramName.AvaTax),"Sales Tax Adjustment Type"));
		}
public static long GetSalesTaxReturnAdjType() {
			return PIn.Long(ProgramProperties.GetPropVal(Programs.GetProgramNum(ProgramName.AvaTax),"Sales Tax Return Adjustment Type"));
		}
public static string GetCompanyCode() {
			return PIn.String(ProgramProperties.GetPropVal(ProgramName.AvaTax,"Company Code"));
		}
public static List<string> GetListTaxableStates() {
			return ProgramProperties.GetPropVal(ProgramName.AvaTax,"Taxable States").Split(',').ToList();
		}
public static bool IsProduction() {
			return PIn.String(ProgramProperties.GetPropVal(ProgramName.AvaTax,"Test (T) or Production (P)"))=="P";
		}
public static List<ProcedureCode> GetListProcedureCodesPrePay() {
			List<ProcedureCode> listProcedureCodes=new List<ProcedureCode>();
			foreach(string procCode in ProgramProperties.GetPropVal(ProgramName.AvaTax,"Prepay Proc Codes").Split(',')) {
				listProcedureCodes.Add(ProcedureCodes.GetProcCode(procCode));
			}
			return listProcedureCodes;
		}
public static List<ProcedureCode> GetListProcedureCodesDiscount() {
			List<ProcedureCode> listProcedureCodes=new List<ProcedureCode>();
			foreach(string procCode in ProgramProperties.GetPropVal(ProgramName.AvaTax,"Discount Proc Codes").Split(',')) {
				listProcedureCodes.Add(ProcedureCodes.GetProcCode(procCode));
			}
			return listProcedureCodes;
		}
public static PatFieldDef GetTaxExemptPatField() {
			return PatFieldDefs.GetFirstOrDefault(x => x.PatFieldDefNum==PIn.Long(
				ProgramProperties.GetPropVal(Programs.GetProgramNum(ProgramName.AvaTax),"Tax Exempt Pat Field Def")
			));
		}
public static DateTime GetTaxLockDate() {
			return PIn.Date(ProgramProperties.GetPropVal(Programs.GetProgramNum(ProgramName.AvaTax),"Tax Lock Date"));
		}
public static void CreateDiscountAdjustment(Procedure procedure,double discountPercentage,long adjType) {
			Adjustment adjustment=new Adjustment {
				DateEntry=DateTime.Today,
				AdjDate=DateTime.Today,
				ProcDate=procedure.ProcDate,
				ProvNum=procedure.ProvNum,
				ProcNum=procedure.ProcNum,
				ClinicNum=procedure.ClinicNum,
				PatNum=procedure.PatNum,
				AdjType=adjType,
				AdjAmt=-(procedure.ProcFee*discountPercentage)//Flip the sign to make it a negative adjustment.
			};
			Adjustments.Insert(adjustment);
			TsiTransLogs.CheckAndInsertLogsIfAdjTypeExcluded(adjustment);
		}
private static bool HasTaxableState(Patient patient) {
			Address addressTax=Addresses.GetOneByPatNum(patient.PatNum);
			//Use the physical tax address instead of the pat address, if they have one
			bool hasTaxedState=GetListTaxableStates().Any(x => x==patient.State);
			if(addressTax!=null) {
				hasTaxedState=GetListTaxableStates().Any(x => x==addressTax.State);
			}
			return hasTaxedState;
		}
public static bool IsApiAvailable(bool isProduction,string username,string password) =>
    throw new NotSupportedException("Configure provider accounts in the optional service; credentials are not accepted here.");
public static bool PingAvaTax() => PracticeServiceClient.Call<bool>("tax/health",new {});
public static decimal GetEstimate(long codeNum,long patNum,double procFee,bool hasExceptions=false) {
    Patient patient=Patients.GetPat(patNum);
    return PracticeServiceClient.Call<decimal>("tax/estimate",new {
        code=ProcedureCodes.GetProcCode(codeNum).ProcCode, amount=procFee,
        postalCode=patient.Zip, region=patient.State
    });
}
public static bool DidUpdateAdjustment(Procedure procedure,Adjustment adjustment) {
    decimal tax=GetEstimate(procedure.CodeNum,procedure.PatNum,
        procedure.ProcFeeTotal+Adjustments.GetTotForProc(procedure.ProcNum,canIncludeTax:false),true);
    if(tax==0 && adjustment.AdjNum==0) { return false; }
    adjustment.AdjAmt=(double)tax;
    return true;
}
public static bool DoCreateReturnAdjustment(Procedure procedure,Adjustment adjustmentLocked,Adjustment adjustmentReturn) {
    decimal tax=GetEstimate(procedure.CodeNum,procedure.PatNum,
        procedure.ProcFeeTotal+Adjustments.GetTotForProc(procedure.ProcNum,canIncludeTax:false),true);
    adjustmentReturn.AdjAmt=-(Adjustments.GetTotTaxForProc(procedure)-(double)tax);
    return adjustmentReturn.AdjAmt!=0;
}
public static void CreatePrepaymentTransaction(Dictionary<ProcedureCode,List<TransQtyAmt>> items,Patient patient,List<Procedure> completed) =>
    throw new NotSupportedException("Legacy tax prepayment requires migration to the optional service contract.");
public class TransQtyAmt { public int Qty; public double Rate; }

}
}
