/* Insurance claim receive — replaces FormClaimPayTotal / FormClaimPayEdit /
   FormClaimPayBatch / FormClaimPayList.
   Pick a sent claim for the active patient, enter EOB amounts per claimproc
   (or a by-total row), mark the claim Received, then create an insurance
   check (ClaimPayment) and attach the received claimprocs to it. */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const money=v=>"$"+Number(v||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  let claims=[],curClaim=null,lastReceived=[],checks=[];

  registerODModule({
    id:"ins-claim-receive",
    label:"Ins Payments",
    group:"Insurance",
    html:`
      <div class="mod-head"><h1>Insurance Payments</h1>
        <span class="sub">EOB entry &amp; insurance checks · ClaimProcs / ClaimPayments business layer</span></div>
      <div class="card" style="margin-bottom:14px"><h2>Outstanding claims</h2>
        <div class="row" style="margin-bottom:8px">
          <select id="icr-claim"></select>
          <button type="button" class="quiet" id="icr-reload">Refresh</button>
          <span class="msg" id="icr-msg"></span>
        </div>
        <div id="icr-claimhead" class="msg" style="margin-bottom:8px"></div>
        <div class="gridwrap scrolly"><table class="grid" id="icr-procs">
          <thead><tr><th>Code</th><th>Description</th><th>Prov</th><th class="num">Fee</th>
            <th class="num">Ins Est</th><th class="num">Ded</th><th class="num">Ins Paid</th>
            <th class="num">Write-off</th><th>Status</th><th>Remarks</th></tr></thead>
          <tbody></tbody></table></div>
        <div class="row" style="margin-top:10px">
          <label class="msg">As total:</label>
          <input id="icr-tot-ded" type="number" step="0.01" placeholder="Ded" style="width:80px">
          <input id="icr-tot-paid" type="number" step="0.01" placeholder="Ins paid" style="width:90px">
          <input id="icr-tot-wo" type="number" step="0.01" placeholder="Write-off" style="width:90px">
          <button type="button" class="quiet" id="icr-receive">Mark Received &amp; Save</button>
          <span class="msg" id="icr-recmsg"></span>
        </div>
        <div class="gridwrap" style="margin-top:10px"><table class="grid" id="icr-pays">
          <thead><tr><th>Check date</th><th>Check #</th><th>Bank</th><th class="num">Amount on claim</th><th>Type</th><th>Note</th></tr></thead>
          <tbody></tbody></table></div>
      </div>
      <div class="card"><h2>Insurance checks</h2>
        <form class="row" id="icr-newcheck" style="margin-bottom:8px">
          <input id="icr-chk-date" type="date">
          <input id="icr-chk-amt" type="number" step="0.01" placeholder="Amount" style="width:100px" required>
          <input id="icr-chk-num" placeholder="Check #" style="width:90px">
          <input id="icr-chk-carrier" placeholder="Carrier" style="width:140px">
          <button class="quiet">New check</button>
          <span class="msg" id="icr-chkmsg"></span>
        </form>
        <div class="gridwrap scrolly"><table class="grid" id="icr-checks">
          <thead><tr><th>Date</th><th>Check #</th><th>Carrier</th><th class="num">Check amt</th>
            <th class="num">Attached</th><th>Claims</th><th>Type</th><th></th></tr></thead>
          <tbody></tbody></table></div>
      </div>
      <div class="card" style="margin-top:14px"><h2>Claim history</h2>
        <p class="msg" style="margin:0 0 8px">All claims for the active patient — click a row for procedure detail.</p>
        <div class="gridwrap scrolly"><table class="grid" id="icr-hist">
          <thead><tr><th>Date of Service</th><th>Sent</th><th>Type</th><th>Status</th><th>Carrier</th>
            <th class="num">Claim Fee</th><th class="num">Ins Est</th><th class="num">Ins Paid</th>
            <th class="num">Ded</th><th class="num">Write-off</th></tr></thead>
          <tbody></tbody></table></div>
      </div>`,
    init(){
      $("icr-reload").addEventListener("click",loadClaims);
      $("icr-claim").addEventListener("change",loadClaim);
      $("icr-receive").addEventListener("click",receive);
      $("icr-newcheck").addEventListener("submit",newCheck);
      $("band-patient").addEventListener("change",()=>setTimeout(loadClaims,0));
      loadClaims();loadChecks();
    }
  });

  async function loadClaims(){
    const pat=getActivePatient();
    if(!pat){$("icr-msg").textContent="No active patient.";return}
    try{
      claims=await api(`/api/patients/${pat.patNum}/claims`);
      const open=claims.filter(c=>c.status!=="Received");
      $("icr-claim").innerHTML=claims.map(c=>
        `<option value="${c.claimNum}"${c.status==="Received"?"":" data-open=1"}>`+
        `${esc(c.dateService)} · ${esc(c.carrier)} · ${esc(c.status)} · ${money(c.claimFee)}</option>`).join("")
        ||"<option value=''>No claims</option>";
      const first=open[0]||claims[0];
      if(first)$("icr-claim").value=first.claimNum;
      $("icr-msg").textContent=`${claims.length} claim(s), ${open.length} outstanding`;
      renderHistory();
      loadClaim();
    }catch(e){$("icr-msg").textContent="claims endpoint unavailable — "+e.message.slice(0,60)}
  }

  /* Claim history grid: every claim for the patient, expandable to its
     claimproc rows (same /api/claims/{n}/procs the standalone page used). */
  function renderHistory(){
    $("icr-hist").querySelector("tbody").innerHTML=claims.map(c=>
      `<tr class="rowsel" data-hist="${c.claimNum}"><td class="mono">${esc(c.dateService)}</td>`+
      `<td class="mono">${esc(c.dateSent)}</td><td>${esc(c.claimType)}</td>`+
      `<td><span class="tag">${esc(c.status)}</span></td><td>${esc(c.carrier)}</td>`+
      `<td class="num">${money(c.claimFee)}</td><td class="num">${money(c.insPayEst)}</td>`+
      `<td class="num">${money(c.insPayAmt)}</td><td class="num">${money(c.dedApplied)}</td>`+
      `<td class="num">${money(c.writeOff)}</td></tr>`).join("")
      ||`<tr><td colspan="10"><div class="empty"><b>No claims</b>This patient has no insurance claims.</div></td></tr>`;
    $("icr-hist").querySelectorAll("tr[data-hist]").forEach(tr=>
      tr.addEventListener("click",()=>toggleHistDetail(tr)));
  }

  async function toggleHistDetail(tr){
    const next=tr.nextElementSibling;
    if(next&&next.dataset.histdetail){next.remove();return}
    let procs;
    try{procs=await api(`/api/claims/${tr.dataset.hist}/procs`)}
    catch(e){$("icr-msg").textContent="claim procs unavailable — "+e.message.slice(0,60);return}
    const rows=procs.map(p=>
      `<tr><td class="mono">${esc(p.procDate)}</td><td class="mono">${esc(p.codeSent)}</td><td>${esc(p.descript)}</td>`+
      `<td>${esc(p.provider)}</td><td class="num">${money(p.feeBilled)}</td><td class="num">${money(p.dedApplied)}</td>`+
      `<td class="num">${money(p.insPayEst)}</td><td class="num">${money(p.insPayAmt)}</td>`+
      `<td class="num">${money(p.writeOff)}</td><td><span class="tag">${esc(p.status)}</span></td><td>${esc(p.remarks)}</td></tr>`).join("");
    const detail=document.createElement("tr");
    detail.dataset.histdetail="1";
    detail.innerHTML=`<td colspan="10" style="padding:0 8px 12px 24px"><table class="grid" style="font-size:12px">`+
      `<thead><tr><th>Proc Date</th><th>Code</th><th>Description</th><th>Prov</th>`+
      `<th class="num">Fee Billed</th><th class="num">Ded</th><th class="num">Ins Est</th><th class="num">Ins Paid</th>`+
      `<th class="num">Write-off</th><th>Status</th><th>Remarks</th></tr></thead>`+
      `<tbody>${rows||`<tr><td colspan="11">No claim procedures</td></tr>`}</tbody></table></td>`;
    tr.after(detail);
  }

  async function loadClaim(){
    const claimNum=+$("icr-claim").value;
    curClaim=null;
    if(!claimNum){$("icr-procs").querySelector("tbody").innerHTML="";$("icr-claimhead").textContent="";return}
    try{
      curClaim=await api(`/api/claims/${claimNum}/receive`);
      $("icr-claimhead").innerHTML=
        `${esc(curClaim.patient)} · ${esc(curClaim.carrier)} · sent ${esc(curClaim.dateSent)||"—"}`+
        ` · <span class="tag">${esc(curClaim.claimStatus)}</span>`+
        ` · fee ${money(curClaim.claimFee)} · est ${money(curClaim.insPayEst)} · paid ${money(curClaim.insPayAmt)}`;
      $("icr-procs").querySelector("tbody").innerHTML=curClaim.procs.map(p=>{
        const locked=p.claimPaymentNum!==0||!["NotReceived","Received","Supplemental"].includes(p.status);
        const cell=(f,v)=>locked?`<td class="num">${money(v)}</td>`
          :`<td class="num"><input type="number" step="0.01" value="${v}" data-cp="${p.claimProcNum}" data-f="${f}" style="width:80px"></td>`;
        return `<tr><td class="mono">${esc(p.codeSent)}</td><td>${esc(p.descript)}</td><td>${esc(p.provider)}</td>`+
          `<td class="num">${money(p.feeBilled)}</td><td class="num">${money(p.insPayEst)}</td>`+
          cell("ded",p.dedApplied)+cell("paid",p.insPayAmt)+cell("wo",p.writeOff)+
          `<td><span class="tag">${esc(p.status)}</span>${p.claimPaymentNum?" 🔗":""}</td><td>${esc(p.remarks)}</td></tr>`;
      }).join("")||`<tr><td colspan="10"><div class="empty"><b>No claim procedures</b></div></td></tr>`;
      $("icr-pays").querySelector("tbody").innerHTML=curClaim.payments.map(p=>
        `<tr><td>${esc(p.checkDate)}</td><td class="mono">${esc(p.checkNum)}</td><td>${esc(p.bankBranch)}</td>`+
        `<td class="num">${money(p.amount)}</td><td>${esc(p.payType)}</td><td>${esc(p.note)}</td></tr>`).join("")
        ||`<tr><td colspan="6" class="msg">No insurance checks attached to this claim yet.</td></tr>`;
      $("icr-recmsg").textContent="";
    }catch(e){$("icr-claimhead").textContent="Failed: "+e.message.slice(0,80)}
  }

  async function receive(){
    if(!curClaim){$("icr-recmsg").textContent="Load a claim first.";return}
    const rows={};
    document.querySelectorAll("#icr-procs input[data-cp]").forEach(inp=>{
      const cp=inp.dataset.cp;
      rows[cp]=rows[cp]||{claimProcNum:+cp,dedApplied:0,insPayAmt:0,writeOff:0,remarks:null};
      const v=parseFloat(inp.value)||0;
      if(inp.dataset.f==="ded")rows[cp].dedApplied=v;
      if(inp.dataset.f==="paid")rows[cp].insPayAmt=v;
      if(inp.dataset.f==="wo")rows[cp].writeOff=v;
    });
    const asTotal={
      dedApplied:parseFloat($("icr-tot-ded").value)||0,
      insPayAmt:parseFloat($("icr-tot-paid").value)||0,
      writeOff:parseFloat($("icr-tot-wo").value)||0,
      remarks:null
    };
    $("icr-recmsg").textContent="Receiving…";
    try{
      const r=await api(`/api/claims/${curClaim.claimNum}/receive`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({procs:Object.values(rows),markReceived:true,
          asTotal:(asTotal.dedApplied||asTotal.insPayAmt||asTotal.writeOff)?asTotal:null})});
      lastReceived=r.received||[];
      $("icr-recmsg").textContent=`Received — ${lastReceived.length} claimproc(s) ready to attach to a check.`;
      $("icr-tot-ded").value=$("icr-tot-paid").value=$("icr-tot-wo").value="";
      loadClaim();loadChecks();
    }catch(e){$("icr-recmsg").textContent="Failed: "+e.message.slice(0,90)}
  }

  async function loadChecks(){
    try{
      checks=await api("/api/claimpayments?days=90");
      renderChecks();
    }catch(e){$("icr-chkmsg").textContent="claimpayments endpoint unavailable — "+e.message.slice(0,60)}
  }

  function renderChecks(){
    $("icr-checks").querySelector("tbody").innerHTML=checks.map(c=>
      `<tr><td>${esc(c.checkDate)}</td><td class="mono">${esc(c.checkNum)}</td><td>${esc(c.carrierName)}</td>`+
      `<td class="num">${money(c.checkAmt)}</td><td class="num">${money(c.attachedTotal)}</td>`+
      `<td>${c.claimCount}${c.isPartial?' <span class="tag">Partial</span>':""}</td><td>${esc(c.payType)}</td>`+
      `<td>${lastReceived.length?`<button type="button" class="quiet" data-attach="${c.claimPaymentNum}" style="font-size:11px">Attach received</button>`:""}</td></tr>`)
      .join("")||`<tr><td colspan="8"><div class="empty"><b>No insurance checks in the last 90 days</b>
        Create one above after receiving a claim, then attach the received claimprocs to it.</div></td></tr>`;
    document.querySelectorAll("#icr-checks [data-attach]").forEach(btn=>
      btn.addEventListener("click",()=>attach(+btn.dataset.attach)));
  }

  async function newCheck(e){
    e.preventDefault();
    $("icr-chkmsg").textContent="Creating…";
    try{
      await api("/api/claimpayments",{method:"POST",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({checkDate:$("icr-chk-date").value||null,
          checkAmt:parseFloat($("icr-chk-amt").value)||0,
          checkNum:$("icr-chk-num").value,bankBranch:"",note:"",
          carrierName:$("icr-chk-carrier").value||(curClaim?curClaim.carrier:""),
          isPartial:true})});
      $("icr-chkmsg").textContent="Check created.";
      $("icr-chk-amt").value=$("icr-chk-num").value="";
      loadChecks();
    }catch(e2){$("icr-chkmsg").textContent="Failed: "+e2.message.slice(0,80)}
  }

  async function attach(claimPaymentNum){
    if(!lastReceived.length)return;
    $("icr-chkmsg").textContent="Attaching…";
    try{
      const r=await api(`/api/claimpayments/${claimPaymentNum}/attach`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({claimProcNums:lastReceived})});
      $("icr-chkmsg").textContent=`Attached ${r.attached} claimproc(s) to check #${claimPaymentNum}.`;
      lastReceived=[];
      loadChecks();loadClaim();
    }catch(e){$("icr-chkmsg").textContent="Attach failed: "+e.message.slice(0,80)}
  }
})();
