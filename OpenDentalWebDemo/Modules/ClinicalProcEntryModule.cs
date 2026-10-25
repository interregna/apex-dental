/*
 * Clinical procedure entry module: web slice of FormProcEdit / FormProcSelect /
 * FormProcCodes2 / FormProcEditAll.  Quick-add via procedure-code search plus
 * inline edit and delete of a patient's charted procedures.  All mutations go
 * through the real business layer — Procedures.Insert / Procedures.Update /
 * Procedures.Delete — the same audit-logged calls the desktop chart makes, so
 * results feed straight into the existing tooth chart and TreatPlan surfaces.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalProcEntryModule {
	record NewProcDto(string ProcCode,string ToothNum,string Surf,string Status,long ProvNum,double? Fee,string Note);
	record EditProcDto(string ToothNum,string Surf,string Status,long ProvNum,string ProcDate,double? Fee,string Note);

	public static void Map(WebApplication app) {
		//Code search for the quick-add bar: FormProcCodes2 / FormProcSelect equivalent.
		//Matches D-code, description, or abbreviation over the short (non-hidden) cached list.
		app.MapGet("/api/proccodes",(string? search) => {
			string terms=(search??"").Trim();
			List<ProcedureCode> listCodes;
			if(terms=="") {
				listCodes=ProcedureCodes.GetWhere(x => true,isShort:true);
			}
			else {
				listCodes=ProcedureCodes.GetWhere(x =>
					x.ProcCode.Contains(terms,StringComparison.OrdinalIgnoreCase)
					|| x.Descript.Contains(terms,StringComparison.OrdinalIgnoreCase)
					|| x.AbbrDesc.Contains(terms,StringComparison.OrdinalIgnoreCase),isShort:true);
			}
			return Results.Json(listCodes
				.OrderBy(x => x.ProcCode)
				.Take(30)
				.Select(x => new {
					codeNum=x.CodeNum,
					procCode=x.ProcCode,
					descript=x.Descript,
					abbrDesc=x.AbbrDesc,
					treatArea=x.TreatArea.ToString(),
				}));
		});

		//Grid of a patient's procedures, same rows the desktop Chart module lists.
		app.MapGet("/api/patients/{patNum:long}/procs",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Procedure> listProcs=Procedures.Refresh(patNum);
			return Results.Json(listProcs
				.Where(x => x.ProcStatus!=ProcStat.D)
				.OrderByDescending(x => x.ProcDate)
				.ThenBy(x => x.ToothNum)
				.Select(x => {
					ProcedureCode code=ProcedureCodes.GetProcCode(x.CodeNum);
					//Procedures.Refresh does not join procnote; surface the latest note the
					//same way FormProcEdit does (notes are stored as procnote history rows).
					ProcNote procNote=ProcNotes.GetProcNotesForProc(x.ProcNum).OrderByDescending(y => y.EntryDateTime).FirstOrDefault();
					return new {
						procNum=x.ProcNum,
						procDate=x.ProcDate.Year>1880?x.ProcDate.ToString("yyyy-MM-dd"):"",
						procCode=code.ProcCode,
						descript=code.Descript,
						toothNum=x.ToothNum??"",
						surf=x.Surf??"",
						status=x.ProcStatus.ToString(),
						provNum=x.ProvNum,
						provAbbr=Providers.GetAbbr(x.ProvNum),
						fee=x.ProcFee,
						note=procNote?.Note??x.Note??"",
					};
				}));
		});

		//Quick-add: FormProcSelect's add flow.  Fee defaults from the patient's
		//effective fee schedule (patient's, else provider's), same fallback the
		//desktop uses via FeeScheds.GetFeeSched.
		app.MapPost("/api/patients/{patNum:long}/procs",(long patNum,NewProcDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			long codeNum=ProcedureCodes.GetCodeNum(dto.ProcCode??"");
			if(codeNum==0) {
				return Results.Text("Unknown procedure code: "+dto.ProcCode,statusCode:400);
			}
			if(!Enum.TryParse(dto.Status,out ProcStat procStat) || procStat==ProcStat.D) {
				return Results.Text("Invalid status: "+dto.Status,statusCode:400);
			}
			Procedure proc=new Procedure();
			proc.PatNum=patNum;
			proc.CodeNum=codeNum;
			proc.ProvNum=dto.ProvNum!=0?dto.ProvNum:pat.PriProv;
			proc.ClinicNum=pat.ClinicNum;
			proc.ToothNum=dto.ToothNum??"";
			proc.Surf=dto.Surf??"";
			proc.ProcStatus=procStat;
			proc.ProcDate=DateTime.Today;
			proc.DateEntryC=DateTime.Today;
			if(dto.Fee.HasValue) {
				proc.ProcFee=dto.Fee.Value;
			}
			else {
				long feeSched=FeeScheds.GetFeeSched(0,pat.FeeSched,proc.ProvNum);
				proc.ProcFee=Fees.GetAmount0(proc.CodeNum,feeSched,proc.ClinicNum,proc.ProvNum);
			}
			proc.Note=dto.Note??"";
			long procNum=Procedures.Insert(proc);//real business insert: audit trail, tax calc
			return Results.Json(new { procNum,fee=proc.ProcFee });
		});

		//Inline edit of status/provider/date/fee/tooth/surface/note: FormProcEdit's save path.
		app.MapPut("/api/procs/{procNum:long}",(long procNum,EditProcDto dto) => {
			Procedure proc=Procedures.GetOneProc(procNum,true);
			if(proc.ProcNum==0) {
				return Results.NotFound();
			}
			if(!Enum.TryParse(dto.Status,out ProcStat procStat) || procStat==ProcStat.D) {
				return Results.Text("Invalid status: "+dto.Status,statusCode:400);
			}
			Procedure procOld=proc.Copy();
			proc.ProcStatus=procStat;
			if(dto.ProvNum!=0) {
				proc.ProvNum=dto.ProvNum;
			}
			if(DateTime.TryParse(dto.ProcDate,out DateTime procDate)) {
				proc.ProcDate=procDate;
			}
			if(dto.Fee.HasValue) {
				proc.ProcFee=dto.Fee.Value;
			}
			if(dto.ToothNum!=null) {
				proc.ToothNum=dto.ToothNum;
			}
			if(dto.Surf!=null) {
				proc.Surf=dto.Surf;
			}
			if(dto.Note!=null) {
				proc.Note=dto.Note;
			}
			Procedures.Update(proc,procOld);//real business update: audit trail, updates only changed columns
			return Results.Json(new { procNum=proc.ProcNum });
		});

		//Delete: sets status to Deleted through the business layer, which validates
		//attachments (claims, payments) and throws when deletion is not allowed.
		app.MapDelete("/api/procs/{procNum:long}",(long procNum) => {
			Procedure proc=Procedures.GetOneProc(procNum,false);
			if(proc.ProcNum==0) {
				return Results.NotFound();
			}
			try {
				Procedures.Delete(procNum);
			}
			catch(Exception ex) {
				return Results.Text("Cannot delete: "+ex.Message,statusCode:400);
			}
			return Results.Json(new { deleted=procNum });
		});
	}
}
