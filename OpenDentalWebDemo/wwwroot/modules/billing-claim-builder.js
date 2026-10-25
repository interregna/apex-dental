/* New claims — web replacement for the desktop "New Claim" flow in ContrAccount
   (ClaimL.CreateClaimFromWrapper / AccountModules.CreateClaim).
   Shows the active patient's completed procedures that are not yet on a claim,
   lets the user pick procedures + an insurance ordinal (Primary/Secondary),
   creates the claim (status W, claimprocs attached and computed the same way
   the desktop does), then shows the patient's claim list below. */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const money=v=>"$"+Number(v||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  let data=null,lastCreated=null;

  registerODModule({
    id:"billing-claim-builder",
    label:"New Claims",
    group:"Billing",
    html:`
      <div class="mod-head"><h1>New Claims</h1>
        <span class="sub">Create claims from unbilled procedures · AccountModules.CreateClaim business layer</span></div>
      <div class="card" style="margin-bottom:14px"><h2>Unbilled completed procedures</h2>
        <div class="row" style="margin-bottom:8px">
          <select id="bcb-ordinal"></select>
          <button type="button" class="quiet" id="bcb-reload">Refresh</button>
          <span class="msg" id="bcb-insmsg"></span>
        </div>
        <div class="gridwrap scrolly"><table class="grid" id="bcb-procs">
          <thead><tr><th></th><th>Date</th><th>Code</th><th>Description</th><th>Th</th><th>Surf</th>
            <th>Prov</th><th class="num">Fee</th><th>Billable to</th></tr></thead>
          <tbody></tbody></table></div>
        <div class="row" style="margin-top:10px">
          <button type="button" class="quiet" id="bcb-create">Create Claim</button>
          <span class="msg" id="bcb-msg"></span>
        </div>
        <div id="bcb-result" class="msg" style="margin-top:6px"></div>
      </div>
      <div class="card"><h2>Claims</h2>
        <p class="msg" style="margin:0 0 8px">All claims for the active patient — click a row for procedure detail.</p>
        <div class="gridwrap scrolly"><table class="grid" id="bcb-claims">
          <thead><tr><th>Date of Service</th><th>Sent</th><th>Type</th><th>Status</th><th>Carrier</th>
            <th class="num">Claim Fee</th><th class="num">Ins Est</th><th class="num">Ins Paid</th></tr></thead>
          <tbody></tbody></table></div>
      </div>`,
    init(){
      $("bcb-reload").addEventListener("click",loadAll);
      $("bcb-ordinal").addEventListener("change",renderProcs);
      $("bcb-create").addEventListener("click",createClaim);
      const band=document.getElementById("band-patient");
      if(band)band.addEventListener("change",()=>setTimeout(loadAll,0));
      loadAll();
    }
  });

  function emptyState(title,detail){
    $("bcb-procs").querySelector("tbody").innerHTML=
      `<tr><td colspan="9"><div class="empty"><b>${esc(title)}</b>${esc(detail)}</div></td></tr>`;
  }

  async function loadAll(){
    lastCreated=null;
    $("bcb-result").textContent="";
    $("bcb-msg").textContent="";
    const pat=getActivePatient();
    if(!pat){
      data=null;
      $("bcb-ordinal").innerHTML="";
      $("bcb-insmsg").textContent="No active patient.";
      emptyState("No active patient","Pick a patient in the band above to see their unbilled procedures.");
      $("bcb-claims").querySelector("tbody").innerHTML="";
      return;
    }
    try{
      data=await api(`/api/patients/${pat.patNum}/unbilled-procs`);
    }catch(e){
      data=null;
      $("bcb-insmsg").textContent="unbilled-procs endpoint unavailable — "+e.message.slice(0,60);
      return;
    }
    $("bcb-ordinal").innerHTML=data.insurance.map(i=>
      `<option value="${i.ordinal}">${esc(i.label)} · ${esc(i.carrier)}${i.subscriberId?" · ID "+esc(i.subscriberId):""}</option>`).join("");
    if(!data.insurance.length){
      $("bcb-insmsg").textContent="Patient has no insurance — claims cannot be created.";
    }else{
      $("bcb-insmsg").textContent=`${data.procs.length} unbilled procedure(s)`;
    }
    renderProcs();
    loadClaims();
  }

  function renderProcs(){
    if(!data)return;
    const ord=+$("bcb-ordinal").value||1;
    if(!data.insurance.length){
      emptyState("No insurance","Add a patplan to this patient before creating claims.");
      return;
    }
    if(!data.procs.length){
      emptyState("No unbilled procedures","Every completed procedure is already attached to a claim for this insurance.");
      return;
    }
    $("bcb-procs").querySelector("tbody").innerHTML=data.procs.map(p=>{
      const can=ord===2?p.canBillSec:p.canBillPri;
      const tags=(p.canBillPri?'<span class="tag">Pri</span> ':"")+(p.canBillSec?'<span class="tag">Sec</span>':"");
      return `<tr${can?"":' style="opacity:.5"'}>`+
        `<td><input type="checkbox" data-proc="${p.procNum}"${can?"":" disabled"}></td>`+
        `<td class="mono">${esc(p.procDate)}</td><td class="mono">${esc(p.procCode)}</td>`+
        `<td>${esc(p.descript)}</td><td class="mono">${esc(p.toothNum)}</td><td class="mono">${esc(p.surf)}</td>`+
        `<td>${esc(p.provider)}</td><td class="num">${money(p.fee)}</td><td>${tags}</td></tr>`;
    }).join("");
  }

  async function createClaim(){
    const pat=getActivePatient();
    if(!pat||!data){$("bcb-msg").textContent="Pick a patient first.";return}
    const ord=+$("bcb-ordinal").value||1;
    const procNums=[...document.querySelectorAll("#bcb-procs input[data-proc]:checked")].map(x=>+x.dataset.proc);
    if(!procNums.length){$("bcb-msg").textContent="Select at least one procedure.";return}
    $("bcb-msg").textContent="Creating claim…";
    try{
      lastCreated=await api(`/api/patients/${pat.patNum}/claims`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({procNums,ordinal:ord})});
      $("bcb-msg").textContent="";
      const excl=(lastCreated.excludedNoBillIns||[]);
      $("bcb-result").innerHTML=
        `Created claim <b>#${lastCreated.claimNum}</b> · <span class="tag">${esc(lastCreated.claimStatus)}</span>`+
        ` ${esc(lastCreated.claimType)} · ${esc(lastCreated.carrier)} · service ${esc(lastCreated.dateService)}`+
        ` · fee ${money(lastCreated.claimFee)} · ins est ${money(lastCreated.insPayEst)}`+
        ` · ${lastCreated.procCount} procedure(s)`+
        (excl.length?` · excluded NoBillIns: ${excl.map(esc).join(", ")}`:"");
      loadAll();
    }catch(e){$("bcb-msg").textContent="Failed: "+e.message.slice(0,120)}
  }

  async function loadClaims(){
    const pat=getActivePatient();
    if(!pat)return;
    let claims;
    try{claims=await api(`/api/patients/${pat.patNum}/claims`)}
    catch(e){$("bcb-msg").textContent="claims endpoint unavailable — "+e.message.slice(0,60);return}
    $("bcb-claims").querySelector("tbody").innerHTML=claims.map(c=>
      `<tr class="rowsel" data-claim="${c.claimNum}"><td class="mono">${esc(c.dateService)}</td>`+
      `<td class="mono">${esc(c.dateSent)}</td><td>${esc(c.claimType)}</td>`+
      `<td><span class="tag">${esc(c.status)}</span></td><td>${esc(c.carrier)}</td>`+
      `<td class="num">${money(c.claimFee)}</td><td class="num">${money(c.insPayEst)}</td>`+
      `<td class="num">${money(c.insPayAmt)}</td></tr>`).join("")
      ||`<tr><td colspan="8"><div class="empty"><b>No claims</b>This patient has no insurance claims yet.</div></td></tr>`;
    $("bcb-claims").querySelectorAll("tr[data-claim]").forEach(tr=>
      tr.addEventListener("click",()=>toggleDetail(tr)));
  }

  async function toggleDetail(tr){
    const next=tr.nextElementSibling;
    if(next&&next.dataset.detail){next.remove();return}
    let procs;
    try{procs=await api(`/api/claims/${tr.dataset.claim}/procs`)}
    catch(e){$("bcb-msg").textContent="claim procs unavailable — "+e.message.slice(0,60);return}
    const rows=procs.map(p=>
      `<tr><td class="mono">${esc(p.procDate)}</td><td class="mono">${esc(p.codeSent)}</td><td>${esc(p.descript)}</td>`+
      `<td>${esc(p.provider)}</td><td class="num">${money(p.feeBilled)}</td><td class="num">${money(p.insPayEst)}</td>`+
      `<td class="num">${money(p.insPayAmt)}</td><td class="num">${money(p.writeOff)}</td>`+
      `<td><span class="tag">${esc(p.status)}</span></td></tr>`).join("");
    const detail=document.createElement("tr");
    detail.dataset.detail="1";
    detail.innerHTML=`<td colspan="8" style="padding:0 8px 12px 24px"><table class="grid" style="font-size:12px">`+
      `<thead><tr><th>Proc Date</th><th>Code</th><th>Description</th><th>Prov</th>`+
      `<th class="num">Fee Billed</th><th class="num">Ins Est</th><th class="num">Ins Paid</th>`+
      `<th class="num">Write-off</th><th>Status</th></tr></thead>`+
      `<tbody>${rows||`<tr><td colspan="9">No claim procedures</td></tr>`}</tbody></table></td>`;
    tr.after(detail);
  }
})();
