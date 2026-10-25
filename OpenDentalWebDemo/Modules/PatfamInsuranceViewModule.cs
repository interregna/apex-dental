/*
 * Family insurance module: read-heavy web slice of FormInsPlan / FormInsPlanSelectFam /
 * FormInsBenefits / FormInsRemain. Uses the exact Refresh chain the desktop uses:
 * Patients.GetFamily -> PatPlans.Refresh -> InsSubs.RefreshForFam -> InsPlans.RefreshForSubList
 * -> Benefits.Refresh, plus the display helpers FormInsRemain relies on
 * (ClaimProcs.GetHistList, InsPlans.GetInsUsedDisplay/GetPendingDisplay,
 * Benefits.GetAnnualMaxDisplay/GetDeductGeneralDisplay). The only mutation is the
 * ordinal reorder, which goes through PatPlans.SetOrdinal (InsEditPatLog audited).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class PatfamInsuranceViewModule {
	record SetOrdinalDto(long PatPlanNum,int NewOrdinal);

	public static void Map(WebApplication app) {
		//Per-patient insurance: every PatPlan in ordinal order with subscriber, carrier,
		//group, plan type, benefit summary (annual max / deductible / used / pending /
		//remaining, as FormInsRemain computes them) and the full benefit rows the
		//coverage tab of FormInsPlan shows. Also the family-wide subscriber list
		//mirroring FormInsPlanSelectFam.
		app.MapGet("/api/patients/{patNum:long}/insurance",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Family family=Patients.GetFamily(patNum);
			List<PatPlan> listPatPlans=PatPlans.Refresh(patNum);
			List<InsSub> listInsSubs=InsSubs.RefreshForFam(family);
			List<InsPlan> listInsPlans=InsPlans.RefreshForSubList(listInsSubs);
			List<Benefit> listBenefits=Benefits.Refresh(listPatPlans,listInsSubs);
			List<ClaimProcHist> listHist=ClaimProcs.GetHistList(patNum,listBenefits,listPatPlans,listInsPlans,DateTime.Today,listInsSubs);
			//PatPlans for the whole family, so each subscriber row can list who is covered.
			List<PatPlan> listPatPlansFam=PatPlans.GetPatPlansForPats(family.ListPats.Select(x => x.PatNum).ToList());
			var plans=listPatPlans.OrderBy(x => x.Ordinal).Select(x => {
				InsSub insSub=InsSubs.GetSub(x.InsSubNum,listInsSubs);
				InsPlan insPlan=InsPlans.GetPlan(insSub.PlanNum,listInsPlans);
				Patient patSubscriber=Patients.GetPat(insSub.Subscriber);
				double annualMax=Benefits.GetAnnualMaxDisplay(listBenefits,insSub.PlanNum,x.PatPlanNum,false);
				double annualMaxFam=Benefits.GetAnnualMaxDisplay(listBenefits,insSub.PlanNum,x.PatPlanNum,true);
				double deductible=Benefits.GetDeductGeneralDisplay(listBenefits,insSub.PlanNum,x.PatPlanNum,BenefitCoverageLevel.Individual);
				double deductFam=Benefits.GetDeductGeneralDisplay(listBenefits,insSub.PlanNum,x.PatPlanNum,BenefitCoverageLevel.Family);
				double insUsed=InsPlans.GetInsUsedDisplay(listHist,DateTime.Today,insSub.PlanNum,x.PatPlanNum,-1,listInsPlans,listBenefits,patNum,x.InsSubNum);
				double pending=insPlan==null?0:InsPlans.GetPendingDisplay(listHist,DateTime.Today,insPlan,x.PatPlanNum,-1,patNum,x.InsSubNum,listBenefits);
				return new {
					patPlanNum=x.PatPlanNum,
					ordinal=(int)x.Ordinal,
					relationship=x.Relationship.ToString(),
					isPending=x.IsPending,
					patID=x.PatID,
					insSubNum=x.InsSubNum,
					planNum=insSub.PlanNum,
					subscriber=patSubscriber?.GetNameLF()??"",
					subscriberId=insSub.SubscriberID,
					carrier=insPlan==null?"":Carriers.GetName(insPlan.CarrierNum),
					employer=insPlan==null||insPlan.EmployerNum==0?"":Employers.GetName(insPlan.EmployerNum),
					groupName=insPlan?.GroupName??"",
					groupNum=insPlan?.GroupNum??"",
					planType=PlanTypeDescript(insPlan?.PlanType??""),
					feeSched=insPlan==null?"":FeeScheds.GetDescription(insPlan.FeeSched),
					benefitPeriod=insPlan==null?"":InsCoveragePanelModule.GetBenefitPeriodDesc(insPlan),
					dateEffective=insSub.DateEffective.Year>1880?insSub.DateEffective.ToString("yyyy-MM-dd"):"",
					dateTerm=insSub.DateTerm.Year>1880?insSub.DateTerm.ToString("yyyy-MM-dd"):"",
					annualMax,
					annualMaxInd=annualMax,//alias for the ins-coverage-panel UI
					annualMaxFam,
					deductible,
					deductInd=deductible,//alias for the ins-coverage-panel UI
					deductFam,
					insUsed,
					pending,
					remaining=annualMax<0?-1:Math.Max(0,annualMax-insUsed-pending),
					benefits=BenefitRows(listBenefits,insSub.PlanNum,x.PatPlanNum),
				};
			});
			var subscribers=listInsSubs.Select(sub => {
				InsPlan insPlan=InsPlans.GetPlan(sub.PlanNum,listInsPlans);
				Patient patSubscriber=Patients.GetPat(sub.Subscriber);
				return new {
					insSubNum=sub.InsSubNum,
					planNum=sub.PlanNum,
					subscriber=patSubscriber?.GetNameLF()??"",
					subscriberId=sub.SubscriberID,
					carrier=insPlan==null?"":Carriers.GetName(insPlan.CarrierNum),
					groupName=insPlan?.GroupName??"",
					planType=PlanTypeDescript(insPlan?.PlanType??""),
					covered=listPatPlansFam.Where(x => x.InsSubNum==sub.InsSubNum)
						.Select(x => family.ListPats.FirstOrDefault(y => y.PatNum==x.PatNum)?.GetNameLF()??("#"+x.PatNum))
						.ToList(),
				};
			});
			//Benefit display matrix: columns follow listPatPlans (ordinal) order — the
			//same rows ControlFamily's coverage grid shows, for the ins-coverage-panel UI.
			Benefit[,] benefitMatrix=Benefits.GetDisplayMatrix(listBenefits,listPatPlans,listInsSubs);
			var benefitRows=new List<object>();
			for(int y=0;y<benefitMatrix.GetLength(1);y++) {
				string descript="";
				for(int x=0;x<benefitMatrix.GetLength(0);x++) {//first non-null cell names the row
					if(benefitMatrix[x,y]==null) {
						continue;
					}
					descript=InsCoveragePanelModule.GetBenefitDesc(benefitMatrix[x,y]);
					break;
				}
				List<string> cells=new List<string>();
				for(int x=0;x<benefitMatrix.GetLength(0);x++) {
					cells.Add(benefitMatrix[x,y]==null?"":InsCoveragePanelModule.GetBenefitValueDesc(benefitMatrix[x,y]));
				}
				benefitRows.Add(new { descript,cells });
			}
			return Results.Json(new {
				patNum,
				patient=pat.GetNameLF(),
				plans,
				benefitRows,
				familySubscribers=subscribers,
			});
		});

		//Plan detail: carrier block plus the plan-level benefit rows, like the top of FormInsPlan.
		app.MapGet("/api/insplans/{planNum:long}",(long planNum) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			Carrier carrier=Carriers.GetCarrier(insPlan.CarrierNum);
			List<Benefit> listBenefits=Benefits.GetForPlanOrPatPlan(planNum,0);
			return Results.Json(new {
				planNum=insPlan.PlanNum,
				groupName=insPlan.GroupName,
				groupNum=insPlan.GroupNum,
				planType=PlanTypeDescript(insPlan.PlanType),
				isMedical=insPlan.IsMedical,
				isHidden=insPlan.IsHidden,
				monthRenew=(int)insPlan.MonthRenew,
				employer=insPlan.EmployerNum==0?"":Employers.GetName(insPlan.EmployerNum),
				planNote=insPlan.PlanNote,
				carrier=carrier==null?null:new {
					carrierNum=carrier.CarrierNum,
					name=carrier.CarrierName,
					phone=carrier.Phone,
					address=carrier.Address,
					city=carrier.City,
					state=carrier.State,
					zip=carrier.Zip,
					electId=carrier.ElectID,
				},
				benefits=BenefitRows(listBenefits,planNum,0),
			});
		});

		//Reorder: set a PatPlan's ordinal (primary/secondary/...). PatPlans.SetOrdinal
		//keeps the sequence contiguous and writes InsEditPatLog entries, exactly as
		//FormInsPlan's ordinal change does. Estimates are recomputed by later surfaces.
		app.MapPost("/api/patients/{patNum:long}/insurance/order",(long patNum,SetOrdinalDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			PatPlan patPlan=PatPlans.GetByPatPlanNum(dto.PatPlanNum);
			if(patPlan==null || patPlan.PatNum!=patNum) {
				return Results.Text("PatPlan does not belong to this patient.",statusCode:400);
			}
			int newOrdinal=PatPlans.SetOrdinal(dto.PatPlanNum,dto.NewOrdinal);
			return Results.Json(new { patPlanNum=dto.PatPlanNum,ordinal=newOrdinal });
		});

		app.MapGet("/module/insurance",() => Results.Content(PAGE,"text/html"));
	}

	//Benefit rows for one column of the coverage grid: everything attached to either
	//the plan or this specific patplan, ordered roughly as FormInsBenefits lists them.
	static object BenefitRows(List<Benefit> listBenefits,long planNum,long patPlanNum) {
		return listBenefits
			.Where(x => (x.PlanNum!=0 && x.PlanNum==planNum) || (x.PatPlanNum!=0 && x.PatPlanNum==patPlanNum))
			.OrderBy(x => x.BenefitType!=InsBenefitType.CoInsurance)
			.ThenBy(x => (int)x.BenefitType)
			.ThenBy(x => Benefits.GetCategoryString(x))
			.Select(x => new {
				benefitNum=x.BenefitNum,
				type=x.BenefitType.ToString(),
				category=Benefits.GetCategoryString(x),
				percent=x.Percent,//-1 means blank
				amount=x.MonetaryAmt,//-1 means blank
				timePeriod=x.TimePeriod.ToString(),
				quantity=(int)x.Quantity,
				quantityQualifier=x.QuantityQualifier.ToString(),
				coverageLevel=x.CoverageLevel.ToString(),
				isPatOverride=x.PatPlanNum!=0,
			})
			.ToList();
	}

	//Same single-letter codes FormInsPlan's Plan Type combo maps.
	static string PlanTypeDescript(string planType) {
		return planType switch {
			"" => "Category Percentage",
			"p" => "PPO Percentage",
			"f" => "Flat Copay",
			"c" => "Capitation",
			_ => planType,
		};
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Insurance</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr;gap:22px;padding:22px;max-width:1000px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}h3{margin:14px 0 6px;font-size:13px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent);vertical-align:top}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.num{text-align:right;font-variant-numeric:tabular-nums}
</style></head><body>
<header><h1>Insurance — Plans, Subscribers, Benefits</h1>
<p>Real PatPlans/InsSubs/InsPlans/Benefits refresh chain &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form style="margin-top:0"><select id=inspat required></select></form></section>
<section><h2>Coverage</h2><div id=insplans></div></section>
<section><h2>Family subscribers</h2>
<table id=famsubs><thead><tr><th>Subscriber</th><th>Carrier</th><th>Group</th><th>Type</th><th>Subscriber ID</th><th>Covers</th></tr></thead><tbody></tbody></table></section>
</main>
<script>
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money=v=>v<0?'':'$'+v.toFixed(2);
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('inspat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${esc(p.name)}</option>`).join('');
 loadIns();
}
async function loadIns(){
 const patNum=document.getElementById('inspat').value;if(!patNum)return;
 const d=await (await fetch(`/api/patients/${patNum}/insurance`)).json();
 document.getElementById('insplans').innerHTML=d.plans.map(p=>`
  <h3>${p.ordinal===1?'Primary':p.ordinal===2?'Secondary':'Ordinal '+p.ordinal} — ${esc(p.carrier)}
   ${p.isPending?'<span class=tag>Pending</span>':''}
   ${d.plans.length>1?`<button data-pp=${p.patPlanNum} data-ord=${p.ordinal} style="font-size:11px;margin-left:8px">Make primary</button>`:''}</h3>
  <table><tbody>
  <tr><th>Subscriber</th><td>${esc(p.subscriber)} (${esc(p.relationship)})</td><th>Subscriber ID</th><td>${esc(p.subscriberId)}</td></tr>
  <tr><th>Group</th><td>${esc(p.groupName)} ${esc(p.groupNum)}</td><th>Plan type</th><td>${esc(p.planType)}</td></tr>
  <tr><th>Employer</th><td>${esc(p.employer)}</td><th>Effective</th><td>${esc(p.dateEffective)}${p.dateTerm?' — '+esc(p.dateTerm):''}</td></tr>
  <tr><th>Annual max</th><td class=num>${money(p.annualMax)}</td><th>Deductible</th><td class=num>${money(p.deductible)}</td></tr>
  <tr><th>Ins used</th><td class=num>${money(p.insUsed)}</td><th>Pending</th><td class=num>${money(p.pending)}</td></tr>
  <tr><th>Remaining</th><td class=num><b>${money(p.remaining)}</b></td><th></th><td></td></tr>
  </tbody></table>
  <table style="margin-top:6px"><thead><tr><th>Type</th><th>Category</th><th>%</th><th>Amount</th><th>Period</th><th>Qty</th><th>Level</th><th></th></tr></thead><tbody>
  ${p.benefits.map(b=>`<tr><td>${esc(b.type)}</td><td>${esc(b.category)}</td>
   <td class=num>${b.percent<0?'':b.percent+'%'}</td><td class=num>${money(b.amount)}</td>
   <td>${b.timePeriod==='None'?'':esc(b.timePeriod)}</td>
   <td>${b.quantityQualifier==='None'?'':b.quantity+' '+esc(b.quantityQualifier)}</td>
   <td>${b.coverageLevel==='None'?'':esc(b.coverageLevel)}</td>
   <td>${b.isPatOverride?'<span class=tag>Pat override</span>':''}</td></tr>`).join('')
   ||'<tr><td colspan=8 style="opacity:.6">No benefits entered.</td></tr>'}
  </tbody></table>`).join('')||'<p style="opacity:.6;font-size:13px">No insurance plans for this patient.</p>';
 document.querySelector('#famsubs tbody').innerHTML=d.familySubscribers.map(s=>
  `<tr><td>${esc(s.subscriber)}</td><td>${esc(s.carrier)}</td><td>${esc(s.groupName)}</td>
   <td>${esc(s.planType)}</td><td>${esc(s.subscriberId)}</td><td>${s.covered.map(esc).join(', ')}</td></tr>`).join('')
  ||'<tr><td colspan=6 style="opacity:.6">No subscribers in family.</td></tr>';
 document.querySelectorAll('#insplans button[data-pp]').forEach(btn=>btn.addEventListener('click',async()=>{
  await fetch(`/api/patients/${patNum}/insurance/order`,{method:'POST',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({patPlanNum:+btn.dataset.pp,newOrdinal:1})});
  loadIns();
 }));
}
document.getElementById('inspat').addEventListener('change',loadIns);
loadPats();
</script></body></html>
""";
}
