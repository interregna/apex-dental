/* Insurance benefit verification tracking — replaces FormInsVerificationList.
   Worklist of insplans/patplans due for verification, split into Benefits
   (plan-level) and Enrollment (per-patient) tabs.  Each row can be marked
   verified (optionally with a note + next-due date), which stamps
   DateLastVerified=today and writes an insverifyhist history row through the
   real InsVerifies / InsVerifyHists business layer.  No patient context. */
"use strict";
(function(){
  const {api,esc,$}=window.ODM;
  let curType="Benefit",rows=[];

  registerODModule({
    id:"ins-verification",
    label:"Ins Verify",
    group:"Insurance",
    html:`
      <div class="mod-head"><h1>Insurance Verification</h1>
        <span class="sub">Benefit &amp; enrollment verification worklist · InsVerifies / InsVerifyHists business layer</span></div>
      <div class="card">
        <div class="row" style="margin-bottom:10px">
          <button type="button" class="quiet" id="iv-tab-benefit">Benefits</button>
          <button type="button" class="quiet" id="iv-tab-enroll">Enrollment</button>
          <label class="msg" style="margin-left:12px">Due window (days):</label>
          <input id="iv-days" type="number" min="0" placeholder="default" style="width:90px">
          <button type="button" class="quiet" id="iv-reload">Refresh</button>
          <span class="msg" id="iv-msg"></span>
        </div>
        <div class="gridwrap scrolly"><table class="grid" id="iv-grid">
          <thead><tr><th>Type</th><th id="iv-col2">Carrier</th><th>Patient</th>
            <th>Last Verified</th><th>Assigned</th><th>Status Note</th><th></th></tr></thead>
          <tbody></tbody></table></div>
      </div>
      <div class="card" id="iv-seed" style="margin-top:14px"><h2>No rows?</h2>
        <p class="msg" style="margin:0">insverify rows are created by the nightly verification process or when a
        plan is created (InsVerifies.InsertForPlanNum / InsertForPatPlanNum). If the worklist is empty, none are
        currently due for this type — seed one server-side to demo the mark-verified flow.</p>
      </div>`,
    init(){
      $("iv-tab-benefit").addEventListener("click",()=>{curType="Benefit";load();});
      $("iv-tab-enroll").addEventListener("click",()=>{curType="PatEnroll";load();});
      $("iv-reload").addEventListener("click",load);
      load();
    }
  });

  function setTabs(){
    $("iv-tab-benefit").classList.toggle("active",curType==="Benefit");
    $("iv-tab-enroll").classList.toggle("active",curType==="PatEnroll");
    $("iv-col2").textContent="Carrier";
  }

  async function load(){
    setTabs();
    const days=$("iv-days").value.trim();
    let url=`/api/insverify?type=${encodeURIComponent(curType)}`;
    if(days!==""){url+=`&daysDue=${encodeURIComponent(days)}`;}
    $("iv-msg").textContent="Loading…";
    try{
      rows=await api(url);
      const due=rows.filter(r=>r.isDue).length;
      $("iv-msg").textContent=`${rows.length} row(s), ${due} due`;
      render();
    }catch(e){
      rows=[];render();
      $("iv-msg").textContent="insverify endpoint unavailable — "+e.message.slice(0,60);
    }
  }

  function render(){
    $("iv-grid").querySelector("tbody").innerHTML=rows.map((r,i)=>
      `<tr${r.isDue?' class="rowsel"':""}>`+
      `<td><span class="tag">${esc(r.verifyType)}</span>${r.isDue?' <span class="tag">Due</span>':""}</td>`+
      `<td>${esc(r.carrier)}</td>`+
      `<td>${esc(r.patient)}</td>`+
      `<td class="mono">${esc(r.dateLastVerified)||"never"}</td>`+
      `<td>${esc(r.userName)}</td>`+
      `<td>${esc(r.note)}</td>`+
      `<td><button type="button" class="quiet" data-verify="${i}" style="font-size:11px">Mark verified</button></td></tr>`+
      `<tr data-form="${i}" style="display:none"><td colspan="7" style="padding:0 8px 12px 24px">`+
        `<div class="row"><input data-note="${i}" placeholder="Status note (optional)" style="width:240px">`+
        `<input data-due="${i}" type="date" title="Next due date (optional)">`+
        `<button type="button" class="quiet" data-confirm="${i}">Confirm verify</button>`+
        `<button type="button" class="quiet" data-cancel="${i}">Cancel</button>`+
        `<span class="msg" data-rowmsg="${i}"></span></div></td></tr>`).join("")
      ||`<tr><td colspan="7"><div class="empty"><b>Nothing to verify</b>No ${esc(curType==="Benefit"?"benefit":"enrollment")} rows are currently due.</div></td></tr>`;
    document.querySelectorAll("#iv-grid [data-verify]").forEach(b=>
      b.addEventListener("click",()=>toggleForm(+b.dataset.verify)));
    document.querySelectorAll("#iv-grid [data-cancel]").forEach(b=>
      b.addEventListener("click",()=>toggleForm(+b.dataset.cancel,true)));
    document.querySelectorAll("#iv-grid [data-confirm]").forEach(b=>
      b.addEventListener("click",()=>verify(+b.dataset.confirm)));
  }

  function toggleForm(i,forceHide){
    const tr=document.querySelector(`#iv-grid tr[data-form="${i}"]`);
    if(!tr){return;}
    tr.style.display=(forceHide||tr.style.display!=="none")?"none":"";
  }

  async function verify(i){
    const r=rows[i];
    if(!r){return;}
    const note=document.querySelector(`#iv-grid input[data-note="${i}"]`).value;
    const nextDueDate=document.querySelector(`#iv-grid input[data-due="${i}"]`).value;
    const msg=document.querySelector(`#iv-grid [data-rowmsg="${i}"]`);
    msg.textContent="Verifying…";
    try{
      await api(`/api/insverify/${r.insVerifyNum}/verify`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({note:note||null,nextDueDate:nextDueDate||null})});
      msg.textContent="Verified.";
      load();
    }catch(e){msg.textContent="Failed: "+e.message.slice(0,80);}
  }
})();
