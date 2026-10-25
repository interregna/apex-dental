/*
 * Claims module: read-only view of a patient's insurance claims.
 * Mirrors the claims grid in ContrAccount plus the read-only core of
 * FormClaimEdit (claim header) and FormClaimProc (procedure rows).
 * All data flows through the real business layer:
 * Claims.Refresh / ClaimProcs.RefreshForClaim / InsPlans.RefreshOne / Carriers.GetCarrierDB.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClaimsModule {
	public static void Map(WebApplication app) {
		app.MapGet("/api/patients/{patNum:long}/claims",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Claim> listClaims=Claims.Refresh(patNum);
			return Results.Json(listClaims.OrderByDescending(x => x.DateService).Select(x => new {
				claimNum=x.ClaimNum,
				dateService=x.DateService.Year>1880?x.DateService.ToString("yyyy-MM-dd"):"",
				dateSent=x.DateSent.Year>1880?x.DateSent.ToString("yyyy-MM-dd"):"",
				status=StatusDescript(x.ClaimStatus),
				claimType=TypeDescript(x.ClaimType),
				carrier=GetCarrierName(x.PlanNum),
				claimFee=x.ClaimFee,
				insPayEst=x.InsPayEst,
				insPayAmt=x.InsPayAmt,
				dedApplied=x.DedApplied,
				writeOff=x.WriteOff,
			}));
		});

		app.MapGet("/api/claims/{claimNum:long}/procs",(long claimNum) => {
			List<ClaimProc> listClaimProcs=ClaimProcs.RefreshForClaim(claimNum);
			return Results.Json(listClaimProcs.Select(x => {
				ProcedureCode code=string.IsNullOrEmpty(x.CodeSent)?null:ProcedureCodes.GetProcCode(x.CodeSent);
				return new {
					claimProcNum=x.ClaimProcNum,
					procDate=x.ProcDate.Year>1880?x.ProcDate.ToString("yyyy-MM-dd"):"",
					codeSent=x.CodeSent??"",
					descript=code?.Descript??"",
					provider=Providers.GetAbbr(x.ProvNum),
					feeBilled=x.FeeBilled,
					dedApplied=x.DedApplied,
					insPayEst=x.InsPayEst,
					insPayAmt=x.InsPayAmt,
					writeOff=x.WriteOff,
					status=x.Status.ToString(),
					remarks=x.Remarks??"",
				};
			}));
		});

		app.MapGet("/module/claims",() => Results.Content(PAGE,"text/html"));
	}

	///<summary>Carrier name for a claim's insplan, straight from the db (no cache dependency).</summary>
	private static string GetCarrierName(long planNum) {
		InsPlan insPlan=InsPlans.RefreshOne(planNum);
		if(insPlan==null) {
			return "";
		}
		Carrier carrier=Carriers.GetCarrierDB(insPlan.CarrierNum);
		return carrier?.CarrierName??"";
	}

	///<summary>U=Unsent, H=Hold until pri received, W=Waiting in queue, P=Probably sent, S=Sent, R=Received (see Claim.ClaimStatus remarks).</summary>
	private static string StatusDescript(string claimStatus) {
		switch(claimStatus) {
			case "U": return "Unsent";
			case "H": return "Hold until Pri received";
			case "W": return "Waiting in queue";
			case "P": return "Probably sent";
			case "S": return "Sent";
			case "R": return "Received";
			default: return claimStatus??"";
		}
	}

	private static string TypeDescript(string claimType) {
		switch(claimType) {
			case "P": return "Primary";
			case "S": return "Secondary";
			case "PreAuth": return "PreAuth";
			case "Cap": return "Capitation";
			case "Other": return "Other";
			default: return claimType??"";
		}
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Claims — Open Dental Web Demo</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{padding:22px;max-width:1100px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
td.num,th.num{text-align:right}
form{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 12px}
select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
tr.claim{cursor:pointer}
tr.claim:hover td{background:color-mix(in srgb,CanvasText 6%,transparent)}
tr.detail td{padding:0 8px 12px 24px}
tr.detail table{font-size:12px}
</style></head><body>
<header><h1>Insurance Claims</h1>
<p>Read-only claims list &middot; Claims.Refresh / ClaimProcs.RefreshForClaim &middot; click a row for procedure detail</p></header>
<main>
<section><h2>Claims</h2>
<form id=pickform><select id=pickpat></select><button type=button id=loadbtn>Load claims</button>
<span id=msg style="font-size:12px;opacity:.7"></span></form>
<table id=claims><thead><tr><th>Date of Service</th><th>Type</th><th>Status</th><th>Carrier</th>
<th class=num>Claim Fee</th><th class=num>Ins Est</th><th class=num>Ins Paid</th><th class=num>Ded</th><th class=num>Write-off</th></tr></thead>
<tbody></tbody></table></section>
</main>
<script>
const money=v=>v.toLocaleString(undefined,{style:'currency',currency:'USD'});
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('pickpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
}
async function loadClaims(){
 const patNum=document.getElementById('pickpat').value;if(!patNum)return;
 const msg=document.getElementById('msg');msg.textContent='Loading…';
 const r=await fetch(`/api/patients/${patNum}/claims`);
 if(!r.ok){msg.textContent='Failed: '+r.status;return}
 const claims=await r.json();
 msg.textContent=`${claims.length} claim(s)`;
 document.querySelector('#claims tbody').innerHTML=claims.map(c=>
  `<tr class=claim data-claim=${c.claimNum}><td>${c.dateService}</td><td>${c.claimType}</td>
   <td><span class=tag>${c.status}</span></td><td>${c.carrier}</td>
   <td class=num>${money(c.claimFee)}</td><td class=num>${money(c.insPayEst)}</td>
   <td class=num>${money(c.insPayAmt)}</td><td class=num>${money(c.dedApplied)}</td>
   <td class=num>${money(c.writeOff)}</td></tr>`).join('');
 document.querySelectorAll('#claims tr.claim').forEach(tr=>tr.addEventListener('click',()=>toggleDetail(tr)));
}
async function toggleDetail(tr){
 const next=tr.nextElementSibling;
 if(next&&next.classList.contains('detail')){next.remove();return}
 const procs=await (await fetch(`/api/claims/${tr.dataset.claim}/procs`)).json();
 const rows=procs.map(p=>`<tr><td>${p.procDate}</td><td>${p.codeSent}</td><td>${p.descript}</td><td>${p.provider}</td>
  <td class=num>${money(p.feeBilled)}</td><td class=num>${money(p.dedApplied)}</td><td class=num>${money(p.insPayEst)}</td>
  <td class=num>${money(p.insPayAmt)}</td><td class=num>${money(p.writeOff)}</td>
  <td><span class=tag>${p.status}</span></td><td>${p.remarks}</td></tr>`).join('');
 const detail=document.createElement('tr');detail.className='detail';
 detail.innerHTML=`<td colspan=9><table><thead><tr><th>Proc Date</th><th>Code</th><th>Description</th><th>Prov</th>
  <th class=num>Fee Billed</th><th class=num>Ded</th><th class=num>Ins Est</th><th class=num>Ins Paid</th>
  <th class=num>Write-off</th><th>Status</th><th>Remarks</th></tr></thead>
  <tbody>${rows||'<tr><td colspan=11>No claim procedures</td></tr>'}</tbody></table></td>`;
 tr.after(detail);
}
document.getElementById('loadbtn').addEventListener('click',loadClaims);
document.getElementById('pickpat').addEventListener('change',loadClaims);
loadPats().then(loadClaims);
</script></body></html>
""";
}
