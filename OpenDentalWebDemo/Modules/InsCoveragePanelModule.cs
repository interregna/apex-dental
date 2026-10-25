/*
 * Insurance coverage panel: web slice of FormInsPlan / FormInsBenefits / FormBenefitEdit
 * (read-heavy core, no estimate-recompute machinery).
 * All data flows through the real business layer:
 * PatPlans.Refresh -> InsSubs.RefreshForFam -> InsPlans.RefreshForSubList -> Benefits.Refresh,
 * with the benefits grid built by Benefits.GetDisplayMatrix and the summary tiles by
 * Benefits.GetAnnualMaxDisplay / Benefits.GetDeductGeneralDisplay.
 * Reordering coverage calls PatPlans.SetOrdinal (InsEditPatLogs audit-logged).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class InsCoveragePanelModule {
	record SetOrdinalDto(int Ordinal);

	public static void Map(WebApplication app) {
		//GET /api/patients/{patNum}/insurance is owned by PatfamInsuranceViewModule
		//(single registration returning the union shape: plans with per-ordinal tiles,
		//the benefit display matrix rows, and the family subscriber list).

		//Raw benefit list for one plan (FormInsBenefits' underlying rows), sorted the same way.
		app.MapGet("/api/insplans/{planNum:long}/benefits",(long planNum) => {
			InsPlan insPlan=InsPlans.RefreshOne(planNum);
			if(insPlan==null) {
				return Results.NotFound();
			}
			List<Benefit> listBenefits=Benefits.GetForPlanOrPatPlan(planNum,0);
			return Results.Json(new {
				planNum,
				carrier=Carriers.GetName(insPlan.CarrierNum),
				benefits=listBenefits.Select(x => new {
					benefitNum=x.BenefitNum,
					descript=GetBenefitDesc(x),
					benefitType=x.BenefitType.ToString(),
					coverageLevel=x.CoverageLevel.ToString(),
					percent=x.Percent,
					monetaryAmt=x.MonetaryAmt,
					timePeriod=x.TimePeriod.ToString(),
					quantityQualifier=x.QuantityQualifier.ToString(),
					quantity=(int)x.Quantity,
					value=GetBenefitValueDesc(x),
				}),
			});
		});

		//Reorder coverage: same call FormInsPlan makes when changing the ordinal.
		//PatPlans.SetOrdinal keeps the sequence contiguous and writes InsEditPatLogs entries.
		app.MapPost("/api/patplans/{patPlanNum:long}/ordinal",(long patPlanNum,SetOrdinalDto dto) => {
			if(dto.Ordinal<1) {
				return Results.Text("Ordinal must be 1 or greater",statusCode:400);
			}
			int newOrdinal=PatPlans.SetOrdinal(patPlanNum,dto.Ordinal);
			return Results.Json(new { patPlanNum,ordinal=newOrdinal });
		});

		//GET /module/insurance is owned by PatfamInsuranceViewModule.
	}

	//Ported from ControlFamily's plan type switch (FormInsPlan shows the same names).
	internal static string GetPlanTypeDesc(InsPlan insPlan) {
		switch(insPlan.PlanType) {
			case "":
				return "Category Percentage";
			case "p":
				FeeSched feeSchedCopay=FeeScheds.GetFirstOrDefault(x => x.FeeSchedNum==insPlan.CopayFeeSched);
				if(feeSchedCopay!=null && feeSchedCopay.FeeSchedType==FeeScheduleType.FixedBenefit) {
					return "PPO Fixed Benefit";
				}
				return "PPO Percentage";
			case "f":
				return "Medicaid or Flat Co-pay";
			case "c":
				return "Capitation";
			default:
				return insPlan.PlanType;
		}
	}

	internal static string GetBenefitPeriodDesc(InsPlan insPlan) {
		if(insPlan.MonthRenew==0) {
			return "Calendar Year";
		}
		return "Service year begins: "+new DateTime(2000,insPlan.MonthRenew,1).ToString("MMMM");
	}

	//Row description for one benefit, ported from ControlFamily's coverage grid logic.
	internal static string GetBenefitDesc(Benefit benefit) {
		string strDesc="";
		if(benefit.PatPlanNum!=0) {
			strDesc+="(pat) ";
		}
		if(benefit.CoverageLevel==BenefitCoverageLevel.Family) {
			strDesc+="Fam ";
		}
		if(benefit.BenefitType==InsBenefitType.CoInsurance && benefit.Percent!=-1) {
			if(benefit.CodeNum>0) {
				ProcedureCode procedureCode=ProcedureCodes.GetProcCode(benefit.CodeNum);
				strDesc+=procedureCode.ProcCode+"-"+procedureCode.AbbrDesc+" %";
			}
			else if(benefit.CodeGroupNum>0) {
				strDesc+=CodeGroups.GetGroupName(benefit.CodeGroupNum)+" %";
			}
			else {
				strDesc+=CovCats.GetDesc(benefit.CovCatNum)+" %";
			}
		}
		else if(benefit.BenefitType==InsBenefitType.Deductible) {
			strDesc+="Deductible "+CovCats.GetDesc(benefit.CovCatNum);
		}
		else if(benefit.BenefitType==InsBenefitType.Limitations
			&& benefit.QuantityQualifier==BenefitQuantity.None
			&& (benefit.TimePeriod==BenefitTimePeriod.ServiceYear || benefit.TimePeriod==BenefitTimePeriod.CalendarYear))
		{
			strDesc+="Annual Max";
		}
		else if(benefit.BenefitType==InsBenefitType.Limitations
			&& CovCats.GetForEbenCat(EbenefitCategory.Orthodontics)!=null
			&& benefit.CovCatNum==CovCats.GetForEbenCat(EbenefitCategory.Orthodontics).CovCatNum
			&& benefit.QuantityQualifier==BenefitQuantity.None
			&& benefit.TimePeriod==BenefitTimePeriod.Lifetime)
		{
			strDesc+="Ortho Max";
		}
		else if(Benefits.IsAgeLimit(benefit)) {
			strDesc+=CodeGroups.GetGroupName(benefit.CodeGroupNum)+" age limit";
		}
		else if(Benefits.IsFrequencyLimitation(benefit)) {
			strDesc+=CodeGroups.GetGroupName(benefit.CodeGroupNum)+" frequency";
		}
		else if(benefit.CodeNum>0) {
			ProcedureCode procedureCode=ProcedureCodes.GetProcCode(benefit.CodeNum);
			strDesc+=procedureCode.ProcCode+"-"+procedureCode.AbbrDesc;
		}
		else {
			strDesc+=benefit.BenefitType.ToString();
		}
		return strDesc.Trim();
	}

	//Cell value for one benefit, following the desktop grid's percent/amount/quantity rendering.
	internal static string GetBenefitValueDesc(Benefit benefit) {
		string strVal="";
		if(benefit.Percent!=-1) {
			strVal+=benefit.Percent.ToString()+"% ";
		}
		if(benefit.MonetaryAmt!=-1) {
			strVal+=benefit.MonetaryAmt.ToString("c0")+" ";
		}
		if(benefit.QuantityQualifier!=BenefitQuantity.None && benefit.Quantity>0) {
			switch(benefit.QuantityQualifier) {
				case BenefitQuantity.AgeLimit:
					strVal+="through age "+benefit.Quantity+" ";
					break;
				case BenefitQuantity.Months:
					strVal+="every "+benefit.Quantity+(benefit.Quantity==1?" month ":" months ");
					break;
				case BenefitQuantity.Years:
					strVal+="every "+benefit.Quantity+(benefit.Quantity==1?" year ":" years ");
					break;
				case BenefitQuantity.NumberOfServices:
					strVal+=benefit.Quantity+(benefit.Quantity==1?" service ":" services ");
					break;
				case BenefitQuantity.Visits:
					strVal+=benefit.Quantity+(benefit.Quantity==1?" visit ":" visits ");
					break;
				default:
					strVal+=benefit.Quantity+" "+benefit.QuantityQualifier.ToString()+" ";
					break;
			}
		}
		if(benefit.TimePeriod==BenefitTimePeriod.Lifetime) {
			strVal+="lifetime ";
		}
		return strVal.Trim();
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
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent);vertical-align:top}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:0}
select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.plans{display:flex;gap:14px;flex-wrap:wrap}
.plan{border:1px solid color-mix(in srgb,CanvasText 20%,transparent);border-radius:8px;padding:12px;min-width:270px;flex:1}
.plan h3{margin:0 0 6px;font-size:14px}
.plan dl{display:grid;grid-template-columns:auto 1fr;gap:2px 10px;margin:0;font-size:12.5px}
.plan dt{opacity:.6}.plan dd{margin:0}
.tiles{display:flex;gap:10px;flex-wrap:wrap;margin-top:10px}
.tile{border:1px solid color-mix(in srgb,CanvasText 20%,transparent);border-radius:8px;padding:8px 12px}
.tile .k{font-size:11px;opacity:.6}.tile .v{font-size:16px;font-weight:600}
</style></head><body>
<header><h1>Insurance — Coverage &amp; Benefits</h1>
<p>PatPlans / InsSubs / InsPlans / Benefits business chain &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form><select id=inspat></select><span id=insmsg style="font-size:12px;opacity:.7;align-self:center"></span></form></section>
<section><h2>Coverage</h2><div class=plans id=plans></div></section>
<section><h2>Benefits</h2><table id=bens><thead></thead><tbody></tbody></table></section>
</main>
<script>
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('inspat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${esc(p.name)}</option>`).join('');
 loadIns();
}
async function loadIns(){
 const patNum=document.getElementById('inspat').value;if(!patNum)return;
 const r=await fetch(`/api/patients/${patNum}/insurance`);
 if(!r.ok){document.getElementById('insmsg').textContent='Not found';return}
 const d=await r.json();
 const money=v=>v<0?'—':'$'+v.toLocaleString();
 document.getElementById('plans').innerHTML=d.plans.map((p,i)=>`
  <div class=plan>
   <h3>${p.ordinal===1?'Primary':p.ordinal===2?'Secondary':'Plan '+p.ordinal}
    ${p.isPending?'<span class=tag>Pending</span>':''}
    ${i>0?`<button style="float:right;font-size:11px" onclick="setOrd(${p.patPlanNum},${p.ordinal-1})">▲ raise</button>`:''}</h3>
   <dl><dt>Carrier</dt><dd>${esc(p.carrier)}</dd>
   <dt>Subscriber</dt><dd>${esc(p.subscriber)} ${esc(p.subscriberID)}</dd>
   <dt>Relationship</dt><dd>${esc(p.relationship)}</dd>
   <dt>Group</dt><dd>${esc(p.groupName)} ${esc(p.groupNum)}</dd>
   <dt>Employer</dt><dd>${esc(p.employer)}</dd>
   <dt>Type</dt><dd>${esc(p.planType)}</dd>
   <dt>Fee sched</dt><dd>${esc(p.feeSched)}</dd>
   <dt>Period</dt><dd>${esc(p.benefitPeriod)}</dd></dl>
   <div class=tiles>
    <div class=tile><div class=k>Annual max</div><div class=v>${money(p.annualMaxInd)}</div></div>
    <div class=tile><div class=k>Deductible</div><div class=v>${money(p.deductInd)}</div></div>
    <div class=tile><div class=k>Fam max</div><div class=v>${money(p.annualMaxFam)}</div></div>
    <div class=tile><div class=k>Fam deduct</div><div class=v>${money(p.deductFam)}</div></div>
   </div></div>`).join('')||'<span style="opacity:.6">No insurance on file for this patient.</span>';
 document.querySelector('#bens thead').innerHTML='<tr><th>Benefit</th>'+
  d.plans.map(p=>`<th>${p.ordinal===1?'Primary':p.ordinal===2?'Secondary':'Plan '+p.ordinal}</th>`).join('')+'</tr>';
 document.querySelector('#bens tbody').innerHTML=d.benefitRows.map(row=>
  `<tr><td>${esc(row.descript)}</td>${row.cells.map(c=>`<td>${esc(c)}</td>`).join('')}</tr>`).join('')
  ||`<tr><td colspan=${d.plans.length+1} style="opacity:.6">No benefits entered.</td></tr>`;
 document.getElementById('insmsg').textContent=`${d.plans.length} plan(s)`;
}
async function setOrd(patPlanNum,ordinal){
 await fetch(`/api/patplans/${patPlanNum}/ordinal`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({ordinal})});
 loadIns();
}
document.getElementById('inspat').addEventListener('change',loadIns);
loadPats();
</script></body></html>
""";
}
