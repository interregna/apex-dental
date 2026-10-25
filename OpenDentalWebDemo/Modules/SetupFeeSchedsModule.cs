/*
 * Fee schedules and per-procedure fee editing over the real business layer.
 * Schedules via FeeScheds.GetDeepCopy/Insert/Update (no delete — desktop only
 * allows hiding, never deleting, a fee schedule); fees via Fees.GetListExact
 * (practice-level rows: ClinicNum=0, ProvNum=0) joined against the ProcedureCodes
 * cache so codes without a fee row appear with an empty amount.  Saving a fee
 * mirrors FormFeeEdit exactly: blank amount deletes the fee row, otherwise
 * insert-or-update through Fees.Insert/Update (which stamp SecUserNumEntry and
 * handle fee sched groups), plus the same ProcFeeEdit/LogFeeEdit audit entries.
 * Retires FormFeeScheds, FrmFeeSchedEdit, FormFeeEdit.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupFeeSchedsModule {
	record FeeSchedDto(string Description,string FeeSchedType,bool IsHidden,bool IsGlobal);
	record FeeAmountDto(string Amount);

	public static void Map(WebApplication app) {
		//---------- Fee schedules (FormFeeScheds / FrmFeeSchedEdit) ----------

		app.MapGet("/api/feescheds",() => {
			FeeScheds.RefreshCache();
			return Results.Json(FeeScheds.GetDeepCopy(false)
				.OrderBy(x => x.FeeSchedType).ThenBy(x => x.ItemOrder).ThenBy(x => x.FeeSchedNum)
				.Select(x => new {
					feeSchedNum=x.FeeSchedNum,
					description=x.Description,
					feeSchedType=x.FeeSchedType.ToString(),
					itemOrder=x.ItemOrder,
					isHidden=x.IsHidden,
					isGlobal=x.IsGlobal,
				}));
		});

		app.MapPost("/api/feescheds",(FeeSchedDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description is required",statusCode:400);
			}
			if(!Enum.TryParse(dto.FeeSchedType,out FeeScheduleType feeScheduleType)) {
				feeScheduleType=FeeScheduleType.Normal;
			}
			FeeScheds.RefreshCache();
			if(FeeScheds.GetByExactName(dto.Description.Trim(),feeScheduleType)!=null) {
				return Results.Text("A fee schedule with that name and type already exists",statusCode:409);
			}
			FeeSched feeSched=new FeeSched();
			feeSched.Description=dto.Description.Trim();
			feeSched.FeeSchedType=feeScheduleType;
			feeSched.ItemOrder=FeeScheds.GetDeepCopy(false).Count;//append at the end, same as FormFeeScheds butAdd_Click
			feeSched.IsHidden=dto.IsHidden;
			feeSched.IsGlobal=dto.IsGlobal;
			long feeSchedNum=FeeScheds.Insert(feeSched);
			FeeScheds.RefreshCache();
			return Results.Json(new { feeSchedNum });
		});

		app.MapPut("/api/feescheds/{feeSchedNum:long}",(long feeSchedNum,FeeSchedDto dto) => {
			FeeScheds.RefreshCache();
			FeeSched feeSched=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==feeSchedNum);
			if(feeSched==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description is required",statusCode:400);
			}
			if(!Enum.TryParse(dto.FeeSchedType,out FeeScheduleType feeScheduleType)) {
				feeScheduleType=feeSched.FeeSchedType;
			}
			feeSched=feeSched.Copy();
			feeSched.Description=dto.Description.Trim();
			feeSched.FeeSchedType=feeScheduleType;
			feeSched.IsHidden=dto.IsHidden;
			feeSched.IsGlobal=dto.IsGlobal;
			FeeScheds.Update(feeSched);
			FeeScheds.RefreshCache();
			return Results.Ok();
		});

		//---------- Fees within a schedule (FormFeeEdit; fee grid of FormProcCodes) ----------

		app.MapGet("/api/feescheds/{feeSchedNum:long}/fees",(long feeSchedNum,string? search) => {
			FeeScheds.RefreshCache();
			FeeSched feeSched=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==feeSchedNum);
			if(feeSched==null) {
				return Results.NotFound();
			}
			//Practice-level fee rows only (ClinicNum=0, ProvNum=0), same rows the desktop fee grid edits by default.
			List<Fee> listFees=Fees.GetListExact(feeSchedNum,clinicNum:0,provNum:0);
			Dictionary<long,Fee> dictFeesByCodeNum=listFees.GroupBy(x => x.CodeNum).ToDictionary(x => x.Key,x => x.First());
			string searchTrim=(search??"").Trim();
			List<ProcedureCode> listProcedureCodes=ProcedureCodes.GetListDeep()//cache refreshed at startup (InvalidType.ProcCodes)
				.Where(x => searchTrim==""
					|| x.ProcCode.Contains(searchTrim,StringComparison.OrdinalIgnoreCase)
					|| (x.Descript??"").Contains(searchTrim,StringComparison.OrdinalIgnoreCase)
					|| (x.AbbrDesc??"").Contains(searchTrim,StringComparison.OrdinalIgnoreCase))
				.OrderBy(x => x.ProcCode)
				.ToList();
			const int limit=200;
			return Results.Json(new {
				feeSchedNum=feeSched.FeeSchedNum,
				description=feeSched.Description,
				feeSchedType=feeSched.FeeSchedType.ToString(),
				feeCount=listFees.Count,
				totalMatches=listProcedureCodes.Count,
				limit,
				fees=listProcedureCodes.Take(limit).Select(x => new {
					codeNum=x.CodeNum,
					procCode=x.ProcCode,
					abbrDesc=x.AbbrDesc,
					descript=x.Descript,
					amount=dictFeesByCodeNum.TryGetValue(x.CodeNum,out Fee fee)?(double?)fee.Amount:null,
				}),
			});
		});

		app.MapPut("/api/feescheds/{feeSchedNum:long}/fees/{codeNum:long}",(long feeSchedNum,long codeNum,FeeAmountDto dto) => {
			FeeScheds.RefreshCache();
			FeeSched feeSched=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==feeSchedNum);
			if(feeSched==null) {
				return Results.NotFound();
			}
			ProcedureCode procedureCode=ProcedureCodes.GetFirstOrDefault(x => x.CodeNum==codeNum);
			if(procedureCode==null) {
				return Results.NotFound();
			}
			string amountStr=(dto.Amount??"").Trim().TrimStart('$').Replace(",","");
			double amount=0;
			if(amountStr!="" && !double.TryParse(amountStr,out amount)) {
				return Results.Text("Invalid amount",statusCode:400);
			}
			if(amount<0) {
				return Results.Text("Amount cannot be negative",statusCode:400);
			}
			Fee fee=Fees.GetFeeFromDb(codeNum,feeSchedNum,clinicNum:0,provNum:0,doGetExactMatch:true);
			if(amountStr=="") {//blank = no fee, matching FormFeeEdit butSave_Click
				if(fee==null) {
					return Results.Json(new { codeNum,amount=(double?)null });//nothing to delete
				}
				DateTime datePrevious=fee.SecDateTEdit;
				Fees.Delete(fee);
				SecurityLogs.MakeLogEntry(EnumPermType.ProcFeeEdit,0,"Procedure: "+procedureCode.ProcCode
					+", Fee: deleted, Fee Schedule: "+feeSched.Description+". Manual edit in web fee editor.",codeNum,DateTime.MinValue);
				SecurityLogs.MakeLogEntry(EnumPermType.LogFeeEdit,0,"Fee Deleted",fee.FeeNum,datePrevious);
				return Results.Json(new { codeNum,amount=(double?)null });
			}
			if(fee==null) {
				fee=new Fee();
				fee.FeeSched=feeSchedNum;
				fee.CodeNum=codeNum;
				fee.ClinicNum=0;
				fee.ProvNum=0;
				fee.Amount=amount;
				Fees.Insert(fee);//stamps SecUserNumEntry, handles fee sched groups
				SecurityLogs.MakeLogEntry(EnumPermType.ProcFeeEdit,0,"Procedure: "+procedureCode.ProcCode
					+", Fee: "+amount.ToString("c")+", Fee Schedule: "+feeSched.Description
					+". Manual edit in web fee editor.",codeNum,DateTime.MinValue);
				SecurityLogs.MakeLogEntry(EnumPermType.LogFeeEdit,0,"Fee Created",fee.FeeNum,DateTime.MinValue);
			}
			else if(Math.Abs(fee.Amount-amount)>=0.005) {//unchanged amount saves nothing, same as FormFeeEdit
				DateTime datePrevious=fee.SecDateTEdit;
				Fee feeOld=fee.Copy();
				fee.Amount=amount;
				Fees.Update(fee,feeOld);
				SecurityLogs.MakeLogEntry(EnumPermType.ProcFeeEdit,0,"Procedure: "+procedureCode.ProcCode
					+", Fee: "+amount.ToString("c")+", Fee Schedule: "+feeSched.Description
					+". Manual edit in web fee editor.",codeNum,DateTime.MinValue);
				SecurityLogs.MakeLogEntry(EnumPermType.LogFeeEdit,0,"Fee Updated",fee.FeeNum,datePrevious);
			}
			return Results.Json(new { codeNum,amount=(double?)amount });
		});
	}
}
