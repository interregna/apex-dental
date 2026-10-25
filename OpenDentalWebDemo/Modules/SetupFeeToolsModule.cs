/*
 * Fee schedule tools — the FormFeeSchedTools operations, over the real business
 * layer, restricted to practice-level fees (ClinicNum=0, ProvNum=0):
 *
 *   - Copy all fees from one schedule to another (butCopy_Click /
 *     FeeScheds.CopyFeeSchedule).  We mirror the desktop copy: fees are pulled
 *     from the source with Fees.GetListExact and written to the target with
 *     Fees.Insert/Update, with the same FeeSchedEdit security-log entries.  The
 *     desktop always deletes the exactly-matching "To" combo before inserting;
 *     here that is the optional "clear target first" — when off we do an
 *     insert-or-update so target codes absent from the source are left alone.
 *
 *   - Increase/adjust every fee by a percent or flat amount (butIncrease_Click /
 *     Fees.IncreaseNew).  The rounding is copied verbatim from Fees.IncreaseNew:
 *     zero/negative-one fees are skipped, otherwise round>0 => Math.Round(v,round)
 *     and round==0 => Math.Round(v,MidpointRounding.AwayFromZero) (nearest dollar).
 *     Each changed fee is written with Fees.Update and the same ProcFeeEdit +
 *     LogFeeEdit audit pair the desktop writes.
 *
 * Schedule dropdowns reuse the existing GET /api/feescheds (SetupFeeSchedsModule).
 * Retires the copy / increase halves of FormFeeSchedTools.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupFeeToolsModule {
	record CopyDto(long FromFeeSchedNum,long ToFeeSchedNum,bool ClearFirst);
	record AdjustDto(long FeeSchedNum,string Mode,double Value,int RoundTo);

	//Mirrors Fees.IncreaseNew: skip 0 / -1 fees, apply percent or flat amount, then round.
	//round is decimal places (0,1,2); 0 rounds to nearest dollar AwayFromZero, matching the desktop.
	static double AdjustAmount(double amount,string mode,double value,int round) {
		if(amount==0 || amount==-1) {
			return amount;
		}
		double newVal=(mode=="amount") ? amount+value : amount*(1+value/100.0);
		if(newVal<0) {
			newVal=0;
		}
		if(round>0) {
			newVal=Math.Round(newVal,round);
		}
		else {
			newVal=Math.Round(newVal,MidpointRounding.AwayFromZero);
		}
		return newVal;
	}

	public static void Map(WebApplication app) {
		//---------- Copy all fees (FormFeeSchedTools butCopy_Click) ----------

		app.MapPost("/api/feetools/copy",(CopyDto dto) => {
			FeeScheds.RefreshCache();
			FeeSched feeSchedFrom=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==dto.FromFeeSchedNum);
			FeeSched feeSchedTo=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==dto.ToFeeSchedNum);
			if(feeSchedFrom==null || feeSchedTo==null) {
				return Results.NotFound();
			}
			if(feeSchedFrom.FeeSchedNum==feeSchedTo.FeeSchedNum) {
				//Same guard the desktop uses — a schedule cannot be copied into itself.
				return Results.Text("A fee schedule cannot be copied into itself. Choose a different target.",statusCode:400);
			}
			//Practice-level rows only (ClinicNum=0, ProvNum=0), same rows the web fee editor edits.
			List<Fee> listFeesFrom=Fees.GetListExact(feeSchedFrom.FeeSchedNum,clinicNum:0,provNum:0);
			if(dto.ClearFirst) {
				Fees.DeleteFees(feeSchedTo.FeeSchedNum,clinicNum:0,provNum:0);//same as CopyFeeSchedule's pre-delete of the matching "To" combo
			}
			//Existing target fees after the optional clear, for insert-or-update.
			List<Fee> listFeesTo=Fees.GetListExact(feeSchedTo.FeeSchedNum,clinicNum:0,provNum:0);
			Dictionary<long,Fee> dictFeesToByCodeNum=listFeesTo.GroupBy(x => x.CodeNum).ToDictionary(x => x.Key,x => x.First());
			int copied=0;
			foreach(Fee feeFrom in listFeesFrom) {
				if(dictFeesToByCodeNum.TryGetValue(feeFrom.CodeNum,out Fee feeTo)) {
					if(Math.Abs(feeTo.Amount-feeFrom.Amount)>=0.005) {
						Fee feeOld=feeTo.Copy();
						feeTo.Amount=feeFrom.Amount;
						Fees.Update(feeTo,feeOld);
					}
				}
				else {
					feeTo=feeFrom.Copy();
					feeTo.FeeNum=0;
					feeTo.FeeSched=feeSchedTo.FeeSchedNum;
					feeTo.ClinicNum=0;
					feeTo.ProvNum=0;
					Fees.Insert(feeTo);//stamps SecUserNumEntry, handles fee sched groups
					dictFeesToByCodeNum[feeTo.CodeNum]=feeTo;
				}
				copied++;
			}
			//Single summary audit entry, same permission the desktop copy logs under.
			SecurityLogs.MakeLogEntry(EnumPermType.FeeSchedEdit,0,"Fee Schedule \""+feeSchedFrom.Description
				+"\" copied to Fee Schedule \""+feeSchedTo.Description+"\" ("+copied+" fee(s)"
				+(dto.ClearFirst?", target cleared first":"")+") via the web Fee Tools.");
			return Results.Json(new { copied });
		});

		//---------- Increase / adjust fees (FormFeeSchedTools butIncrease_Click) ----------

		app.MapGet("/api/feetools/preview",(long feeSchedNum,string mode,double value,int? roundTo) => {
			FeeScheds.RefreshCache();
			FeeSched feeSched=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==feeSchedNum);
			if(feeSched==null) {
				return Results.NotFound();
			}
			string modeUse=(mode=="amount")?"amount":"percent";
			int round=Math.Clamp(roundTo??0,0,2);
			const int limit=15;
			List<Fee> listFees=Fees.GetListExact(feeSchedNum,clinicNum:0,provNum:0)
				.OrderBy(x => ProcedureCodes.GetStringProcCode(x.CodeNum,doThrowIfMissing:false)).ToList();
			int adjustable=listFees.Count(x => x.Amount!=0 && x.Amount!=-1);
			return Results.Json(new {
				feeSchedNum=feeSched.FeeSchedNum,
				description=feeSched.Description,
				feeCount=listFees.Count,
				adjustable,
				limit,
				rows=listFees.Take(limit).Select(x => new {
					procCode=ProcedureCodes.GetStringProcCode(x.CodeNum,doThrowIfMissing:false),
					before=x.Amount,
					after=AdjustAmount(x.Amount,modeUse,value,round),
				}),
			});
		});

		app.MapPost("/api/feetools/adjust",(AdjustDto dto) => {
			FeeScheds.RefreshCache();
			FeeSched feeSched=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==dto.FeeSchedNum);
			if(feeSched==null) {
				return Results.NotFound();
			}
			string mode=(dto.Mode=="amount")?"amount":"percent";
			if(mode=="percent" && (dto.Value<-99 || dto.Value>99)) {
				//Same range the desktop enforces on the percent field.
				return Results.Text("Percent must be between -99 and 99.",statusCode:400);
			}
			int round=Math.Clamp(dto.RoundTo,0,2);
			List<Fee> listFees=Fees.GetListExact(dto.FeeSchedNum,clinicNum:0,provNum:0);
			int adjusted=0;
			foreach(Fee fee in listFees) {
				if(fee.Amount==0 || fee.Amount==-1) {//skipped exactly as Fees.IncreaseNew does
					continue;
				}
				double newVal=AdjustAmount(fee.Amount,mode,dto.Value,round);
				if(Math.Abs(newVal-fee.Amount)<0.005) {
					continue;//no change, write nothing
				}
				string procCode;
				try {
					procCode=ProcedureCodes.GetStringProcCode(fee.CodeNum);
				}
				catch(Exception) {//CodeNum not in the procedurecode table — skip like the desktop loop
					continue;
				}
				DateTime datePrevious=fee.SecDateTEdit;
				Fee feeOld=fee.Copy();
				fee.Amount=newVal;
				Fees.Update(fee,feeOld);
				string logText="Procedure: "+procCode+", Fee: "+newVal.ToString("c")
					+", Fee Schedule: "+feeSched.Description+". "
					+(mode=="amount"
						? "Fee adjusted by "+dto.Value.ToString("c")
						: "Fee increased by "+(dto.Value/100.0).ToString("p"))
					+" using the web Fee Tools.";
				SecurityLogs.MakeLogEntry(EnumPermType.ProcFeeEdit,0,logText,fee.CodeNum,DateTime.MinValue);
				SecurityLogs.MakeLogEntry(EnumPermType.LogFeeEdit,0,"Fee Updated",fee.FeeNum,datePrevious);
				adjusted++;
			}
			return Results.Json(new { adjusted });
		});
	}
}
