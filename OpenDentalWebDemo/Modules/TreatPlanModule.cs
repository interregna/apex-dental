/*
 * Treatment plan module.
 * Web slice of ControlTreat / FormTreatPlanEdit / FormTreatPlanCurEdit:
 * lists a patient's treatment-planned procedures (ProcStat.TP) plus saved
 * TreatPlan records (read side, unchanged), and adds full create/edit/delete
 * of saved treatment plans through the same business calls the desktop uses:
 * TreatPlans.CreateArchivedTreatPlan (the "Save TP" toolbar button), which
 * snapshots selected procedures into proctp rows with priorities sourced from
 * treatplanattach; TreatPlans.Update + ProcTPs rebuild / SetPriorityForTreatPlanProcs
 * for edits (FormTreatPlanEdit); TreatPlans.SyncTreatPlanStatusWithProcs for
 * re-attaching procs on Active/Inactive plans (FormTreatPlanCurEdit.butSave);
 * ProcTPs.DeleteForTP + TreatPlans.Delete for deletes (FormTreatPlanEdit.butDelete).
 * Priorities are DefCat.TxPriorities definitions.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class TreatPlanModule {
	///<summary>POST/PUT body. Priorities maps procNum (as a string key) to a TxPriorities DefNum; 0 clears the priority.
	///For PUT, Heading/Note are applied when non-null, ProcNums (when non-null) re-attaches, Priorities re-prioritizes.</summary>
	record PlanDto(string Heading,string Note,List<long> ProcNums,Dictionary<string,long> Priorities);

	static string Hex(System.Drawing.Color c) => (c.ToArgb()==0 || (c.R==0 && c.G==0 && c.B==0))?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";

	///<summary>Parses the {"procNum":defNum} priority map from the dto. Returns empty dict when absent.</summary>
	static Dictionary<long,long> ParsePriorities(PlanDto dto) {
		Dictionary<long,long> dict=new Dictionary<long,long>();
		if(dto.Priorities==null) {
			return dict;
		}
		foreach(KeyValuePair<string,long> kvp in dto.Priorities) {
			if(long.TryParse(kvp.Key,out long procNum)) {
				dict[procNum]=kvp.Value;
			}
		}
		return dict;
	}

	///<summary>Mirrors the display-copy logic of TreatPlans.CreateArchivedTreatPlan for one procedure:
	///tooth/surf/code display strings the way the desktop snapshots them into proctp.</summary>
	static void FillProcTpDisplay(ProcTP procTP,Procedure procedure) {
		procTP.ToothNumTP=Tooth.Display(procedure.ToothNum);
		ProcedureCode procedureCode=ProcedureCodes.GetProcCode(procedure.CodeNum);
		if(procedureCode.TreatArea==TreatmentArea.Surf) {
			procTP.Surf=Tooth.SurfTidyFromDbToDisplay(procedure.Surf,procedure.ToothNum);
		}
		else if(procedureCode.TreatArea==TreatmentArea.Sextant) {
			procTP.Surf=Tooth.GetSextant(procedure.Surf,(ToothNumberingNomenclature)PrefC.GetInt(PrefName.UseInternationalToothNumbers));
		}
		else {
			procTP.Surf=procedure.Surf;//for UR, L, etc.
		}
		procTP.ProcCode=ProcedureCodes.GetStringProcCode(procedure.CodeNum);
	}

	///<summary>Builds the ProcTP "selection stub" that CreateArchivedTreatPlan copies fee/provider/description from,
	///the same fields ControlTreat's grid rows carry into the Save TP call.</summary>
	static ProcTP BuildStub(Procedure procedure) {
		ProcTP procTP=new ProcTP();
		procTP.ProcNumOrig=procedure.ProcNum;
		procTP.Descript=ProcedureCodes.GetLaymanTerm(procedure.CodeNum);
		procTP.FeeAmt=procedure.ProcFee;
		procTP.ProvNum=procedure.ProvNum;
		procTP.ClinicNum=procedure.ClinicNum;
		procTP.DateTP=DateTime.Today;
		procTP.Prognosis="";
		procTP.Dx="";
		procTP.ProcAbbr="";
		return procTP;
	}

	public static void Map(WebApplication app) {
		//Original read-only snapshot (unchanged shape, kept backward-compatible).
		app.MapGet("/api/treatplan/{patNum:long}",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Procedure> listProcs=Procedures.Refresh(patNum)
				.Where(x => x.ProcStatus==ProcStat.TP)
				.OrderBy(x => x.PriorityOrder).ThenBy(x => x.ToothNum).ToList();
			var procsOut=listProcs.Select(x => {
				ProcedureCode procCode=ProcedureCodes.GetProcCode(x.CodeNum);
				return new {
					procNum=x.ProcNum,
					code=procCode.ProcCode,
					descript=procCode.Descript,
					toothNum=x.ToothNum??"",
					surf=x.Surf??"",
					provider=Providers.GetAbbr(x.ProvNum),
					fee=x.ProcFee,
				};
			}).ToList();
			List<TreatPlan> listTreatPlans=TreatPlans.GetAllForPat(patNum);
			var plansOut=listTreatPlans.OrderByDescending(x => x.DateTP).Select(x => new {
				treatPlanNum=x.TreatPlanNum,
				heading=x.Heading,
				dateTP=x.DateTP.Year>1880?x.DateTP.ToString("yyyy-MM-dd"):"",
				status=x.TPStatus.ToString(),
				note=x.Note,
			}).ToList();
			return Results.Json(new {
				patient=pat.GetNameLF(),
				procs=procsOut,
				total=listProcs.Sum(x => x.ProcFee),
				plans=plansOut,
			});
		});

		//All treatment plans for a patient with their attached procedures and priorities,
		//plus the practice's DefCat.TxPriorities definitions for the priority dropdowns.
		//Saved plans read from their proctp snapshot rows; Active/Inactive plans read live
		//through treatplanattach, the same split the desktop grids use.
		app.MapGet("/api/patients/{patNum:long}/treatplans",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.TxPriorities,true);
			string PriorityName(long defNum) => defNum==0?"":(listDefs.FirstOrDefault(x => x.DefNum==defNum)?.ItemName??Defs.GetName(DefCat.TxPriorities,defNum));
			List<TreatPlan> listTreatPlans=TreatPlans.GetAllForPat(patNum);
			List<ProcTP> listProcTPs=ProcTPs.Refresh(patNum);//all saved-plan snapshot rows, ordered by ItemOrder
			List<TreatPlanAttach> listAttaches=TreatPlanAttaches.GetAllForTPs(listTreatPlans.Select(x => x.TreatPlanNum).ToList());
			List<Procedure> listProcsTpTpi=Procedures.GetProcsByStatusForPat(patNum,new[] { ProcStat.TP,ProcStat.TPi });
			var plansOut=listTreatPlans
				.OrderBy(x => x.TPStatus==TreatPlanStatus.Active?0:(x.TPStatus==TreatPlanStatus.Inactive?1:2))
				.ThenByDescending(x => x.DateTP)
				.Select(plan => {
					List<object> procs=new List<object>();
					if(plan.TPStatus==TreatPlanStatus.Saved) {
						procs=listProcTPs.Where(x => x.TreatPlanNum==plan.TreatPlanNum).Select(x => (object)new {
							procNum=x.ProcNumOrig,
							code=x.ProcCode,
							descript=x.Descript,
							toothNum=x.ToothNumTP??"",
							surf=x.Surf??"",
							provider=Providers.GetAbbr(x.ProvNum),
							fee=x.FeeAmt,
							priority=x.Priority,
							priorityName=PriorityName(x.Priority),
						}).ToList();
					}
					else {//Active/Inactive: live procedures through treatplanattach
						procs=listAttaches.Where(x => x.TreatPlanNum==plan.TreatPlanNum)
							.Select(x => new { attach=x,proc=listProcsTpTpi.FirstOrDefault(y => y.ProcNum==x.ProcNum) })
							.Where(x => x.proc!=null)
							.Select(x => {
								ProcedureCode procCode=ProcedureCodes.GetProcCode(x.proc.CodeNum);
								return (object)new {
									procNum=x.proc.ProcNum,
									code=procCode.ProcCode,
									descript=procCode.Descript,
									toothNum=x.proc.ToothNum??"",
									surf=x.proc.Surf??"",
									provider=Providers.GetAbbr(x.proc.ProvNum),
									fee=x.proc.ProcFee,
									priority=x.attach.Priority,
									priorityName=PriorityName(x.attach.Priority),
								};
							}).ToList();
					}
					return new {
						treatPlanNum=plan.TreatPlanNum,
						heading=plan.Heading,
						dateTP=plan.DateTP.Year>1880?plan.DateTP.ToString("yyyy-MM-dd"):"",
						status=plan.TPStatus.ToString(),
						note=plan.Note??"",
						procCount=procs.Count,
						procs,
					};
				}).ToList();
			return Results.Json(new {
				patient=pat.GetNameLF(),
				priorities=listDefs.Select(x => new {
					defNum=x.DefNum,
					name=x.ItemName,
					color=Hex(x.ItemColor),
				}),
				plans=plansOut,
			});
		});

		//Create a saved treatment plan from selected TP/TPi procedures — the web version of
		//ControlTreat's "Save TP" toolbar button. Builds the same TreatPlan header fields the
		//desktop does, then TreatPlans.CreateArchivedTreatPlan inserts the plan and its proctp
		//snapshot rows, taking each proc's priority from the treatplanattach stubs we pass
		//(request priorities override the plan attaches the proc already carries).
		app.MapPost("/api/patients/{patNum:long}/treatplans",(long patNum,PlanDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.Heading)) {
				return Results.Text("Heading cannot be empty.",statusCode:400);
			}
			if(dto.ProcNums==null || dto.ProcNums.Count==0) {
				return Results.Text("Select at least one procedure first.",statusCode:400);
			}
			List<Procedure> listProcsTpTpi=Procedures.GetProcsByStatusForPat(patNum,new[] { ProcStat.TP,ProcStat.TPi });
			List<Procedure> listProcsSelected=dto.ProcNums.Distinct()
				.Select(x => listProcsTpTpi.FirstOrDefault(y => y.ProcNum==x))
				.Where(x => x!=null).ToList();
			if(listProcsSelected.Count!=dto.ProcNums.Distinct().Count()) {
				return Results.Text("One or more procedures are not treatment-planned for this patient.",statusCode:400);
			}
			Dictionary<long,long> dictPriorities=ParsePriorities(dto);
			List<TreatPlanAttach> listAttachesPat=TreatPlanAttaches.GetAllForPatNum(patNum);
			//Priority source per proc: request override, else existing treatplanattach, else the proc's own priority.
			List<TreatPlanAttach> listAttachStubs=listProcsSelected.Select(x => new TreatPlanAttach {
				ProcNum=x.ProcNum,
				Priority=dictPriorities.TryGetValue(x.ProcNum,out long priority)
					?priority
					:(listAttachesPat.FirstOrDefault(y => y.ProcNum==x.ProcNum)?.Priority??x.Priority),
			}).ToList();
			//Same header fields ControlTreat.ToolBarMainCreate_Click sets before CreateArchivedTreatPlan.
			TreatPlan treatPlan=new TreatPlan();
			treatPlan.Heading=dto.Heading.Trim();
			treatPlan.DateTP=DateTime.Today;
			treatPlan.PatNum=patNum;
			treatPlan.Note=dto.Note??"";
			treatPlan.ResponsParty=pat.ResponsParty;
			treatPlan.UserNumPresenter=Security.CurUser.UserNum;
			treatPlan.TPStatus=TreatPlanStatus.Saved;
			treatPlan.TPType=DiscountPlanSubs.HasDiscountPlan(patNum)?TreatPlanType.Discount:TreatPlanType.Insurance;
			List<ProcTP> listStubs=listProcsSelected.Select(BuildStub).ToList();
			long treatPlanNum=TreatPlans.CreateArchivedTreatPlan(treatPlan,pat,listStubs,listAttachStubs);
			SecurityLogs.MakeLogEntry(EnumPermType.TreatPlanEdit,patNum,"Saved TP created: "+treatPlan.Heading);
			return Results.Json(new { treatPlanNum });
		});

		//Edit a treatment plan: heading/note (FormTreatPlanEdit's save), re-attach procedures,
		//and per-proc priorities. Saved plans rebuild their proctp snapshot rows and use
		//ProcTPs.SetPriorityForTreatPlanProcs; Active/Inactive plans go through
		//TreatPlans.SyncTreatPlanStatusWithProcs (FormTreatPlanCurEdit.butSave) and
		//TreatPlanAttaches.SetPriorityForTreatPlanProcs, the same split TreatPlans.SetPriorityForProcs makes.
		app.MapPut("/api/treatplans/{treatPlanNum:long}",(long treatPlanNum,PlanDto dto) => {
			TreatPlan treatPlan=TreatPlans.GetOne(treatPlanNum);
			if(treatPlan==null || treatPlan.TreatPlanNum==0) {
				return Results.NotFound();
			}
			if(dto.Heading!=null && string.IsNullOrWhiteSpace(dto.Heading)) {
				return Results.Text("Heading cannot be empty.",statusCode:400);
			}
			if(dto.Heading!=null) {
				treatPlan.Heading=dto.Heading.Trim();
			}
			if(dto.Note!=null) {
				treatPlan.Note=dto.Note;
			}
			TreatPlans.Update(treatPlan);
			Dictionary<long,long> dictPriorities=ParsePriorities(dto);
			if(treatPlan.TPStatus==TreatPlanStatus.Saved) {
				if(dto.ProcNums!=null) {//rebuild the proctp snapshot to match the requested set, preserving kept rows
					List<long> listProcNums=dto.ProcNums.Distinct().ToList();
					List<ProcTP> listProcTPsOld=ProcTPs.RefreshForTP(treatPlanNum);
					List<Procedure> listProcsTpTpi=Procedures.GetProcsByStatusForPat(treatPlan.PatNum,new[] { ProcStat.TP,ProcStat.TPi });
					int itemOrder=0;
					foreach(long procNum in listProcNums) {
						ProcTP procTP=listProcTPsOld.FirstOrDefault(x => x.ProcNumOrig==procNum);
						bool isNew=procTP==null;
						if(isNew) {
							Procedure procedure=listProcsTpTpi.FirstOrDefault(x => x.ProcNum==procNum);
							if(procedure==null) {
								return Results.Text("Procedure "+procNum+" is not treatment-planned for this patient.",statusCode:400);
							}
							procTP=BuildStub(procedure);
							procTP.TreatPlanNum=treatPlanNum;
							procTP.PatNum=treatPlan.PatNum;
							FillProcTpDisplay(procTP,procedure);
						}
						procTP.ItemOrder=itemOrder++;
						if(dictPriorities.TryGetValue(procNum,out long priority)) {
							procTP.Priority=priority;
						}
						ProcTPs.InsertOrUpdate(procTP,isNew);
					}
					foreach(ProcTP procTP in listProcTPsOld.Where(x => !listProcNums.Contains(x.ProcNumOrig))) {
						ProcTPs.Delete(procTP);
					}
				}
				else {//priorities only, on the existing snapshot
					foreach(IGrouping<long,KeyValuePair<long,long>> group in dictPriorities.GroupBy(x => x.Value)) {
						ProcTPs.SetPriorityForTreatPlanProcs(group.Key,treatPlanNum,group.Select(x => x.Key).ToList());
					}
				}
			}
			else {//Active/Inactive: live attaches
				if(dto.ProcNums!=null) {
					List<TreatPlanAttach> listAttachesAll=TreatPlanAttaches.GetAllForPatNum(treatPlan.PatNum);
					List<TreatPlanAttach> listAttachesPlan=listAttachesAll.FindAll(x => x.TreatPlanNum==treatPlanNum);
					List<Procedure> listProcs=Procedures.GetProcsByStatusForPat(treatPlan.PatNum,new[] { ProcStat.TP,ProcStat.TPi })
						.FindAll(x => dto.ProcNums.Contains(x.ProcNum));
					TreatPlans.SyncTreatPlanStatusWithProcs(treatPlan,isMarkingActive:false,listAttachesPlan,listAttachesAll,listProcs);
				}
				foreach(IGrouping<long,KeyValuePair<long,long>> group in dictPriorities.GroupBy(x => x.Value)) {
					TreatPlanAttaches.SetPriorityForTreatPlanProcs(group.Key,treatPlanNum,group.Select(x => x.Key).ToList());
				}
			}
			SecurityLogs.MakeLogEntry(EnumPermType.TreatPlanEdit,treatPlan.PatNum,"TP edited: "+treatPlan.Heading);
			return Results.Ok();
		});

		//Delete a plan — FormTreatPlanEdit.butDelete for saved plans (proctp rows first, then the
		//plan) and FormTreatPlanCurEdit.butDelete for inactive plans. Active plans cannot be deleted.
		app.MapDelete("/api/treatplans/{treatPlanNum:long}",(long treatPlanNum) => {
			TreatPlan treatPlan=TreatPlans.GetOne(treatPlanNum);
			if(treatPlan==null || treatPlan.TreatPlanNum==0) {
				return Results.NotFound();
			}
			if(treatPlan.TPStatus==TreatPlanStatus.Active) {
				return Results.Text("Cannot delete active treatment plan.",statusCode:400);
			}
			ProcTPs.DeleteForTP(treatPlanNum);
			try {
				TreatPlans.Delete(treatPlan);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.TreatPlanEdit,treatPlan.PatNum,"Delete TP: "+treatPlan.DateTP.ToShortDateString());
			return Results.Ok();
		});

		app.MapGet("/module/treatplan",() => Results.Content(PAGE,"text/html"));
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Treatment Plan — Open Dental Web</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;gap:22px;padding:22px;max-width:900px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
td.num,th.num{text-align:right}
tr.total td{font-weight:700;border-bottom:none}
select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
</style></head><body>
<header><h1>Treatment Plan</h1>
<p>Treatment-planned procedures &middot; real Procedures/TreatPlans business layer &middot; read-only</p></header>
<main>
<section><h2>Patient</h2>
<select id=tppat></select>
<span id=tpmsg style="font-size:12px;opacity:.7;margin-left:8px"></span></section>
<section><h2>Planned procedures</h2>
<table id=tpprocs><thead><tr><th>Code</th><th>Description</th><th>Th</th><th>Surf</th><th>Prov</th><th class=num>Fee</th></tr></thead>
<tbody></tbody><tfoot></tfoot></table></section>
<section><h2>Saved treatment plans</h2>
<table id=tpplans><thead><tr><th>#</th><th>Date</th><th>Heading</th><th>Status</th><th>Note</th></tr></thead>
<tbody></tbody></table></section>
</main>
<script>
const fmt=n=>n.toLocaleString(undefined,{style:'currency',currency:'USD'});
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('tppat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 loadPlan();
}
async function loadPlan(){
 const patNum=document.getElementById('tppat').value;if(!patNum)return;
 const msg=document.getElementById('tpmsg');msg.textContent='Loading…';
 const r=await fetch(`/api/treatplan/${patNum}`);
 if(!r.ok){msg.textContent='Failed to load';return}
 const d=await r.json();
 msg.textContent=`${d.procs.length} planned procedure(s), ${d.plans.length} saved plan(s)`;
 document.querySelector('#tpprocs tbody').innerHTML=d.procs.map(p=>
  `<tr><td>${p.code}</td><td>${p.descript}</td><td>${p.toothNum}</td><td>${p.surf}</td><td>${p.provider}</td><td class=num>${fmt(p.fee)}</td></tr>`).join('')
  ||'<tr><td colspan=6 style="opacity:.6">No treatment-planned procedures</td></tr>';
 document.querySelector('#tpprocs tfoot').innerHTML=d.procs.length?
  `<tr class=total><td colspan=5>Total</td><td class=num>${fmt(d.total)}</td></tr>`:'';
 document.querySelector('#tpplans tbody').innerHTML=d.plans.map(p=>
  `<tr><td>${p.treatPlanNum}</td><td>${p.dateTP}</td><td>${p.heading}</td><td><span class=tag>${p.status}</span></td><td>${p.note||''}</td></tr>`).join('')
  ||'<tr><td colspan=5 style="opacity:.6">No saved treatment plans</td></tr>';
}
document.getElementById('tppat').addEventListener('change',loadPlan);
loadPats();
</script></body></html>
""";
}
