/* Adjustments — retires FormAdjust (and the adjustments slice of the Account
   module). Grid of the active patient's adjustments with a running total
   (GET /api/patients/{n}/adjustments = Adjustments.Refresh), create/edit via
   POST/PUT with the amount entered as a positive number — the server applies
   the sign from the AdjTypes def ("+" addition, "-"/"dp" subtraction) exactly
   like the desktop dialog — and delete with the form's paysplit/payplan guards
   coming back as 409 messages. Types from /api/adjustment-types, providers
   from /api/setup/providers, optional attached procedure from the patient's
   procedure list. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;
const usd=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});

registerODModule({
  id:"billing-adjustments",
  label:"Adjustments",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Adjustments</h1>
    <span class="sub">additions, discounts &amp; write-offs · types from Definitions (AdjTypes)</span></div>
  <div class="chart-tools">
    <button type="button" class="quiet" id="adj-reload">Refresh</button>
    <span class="msg" id="adj-msg"></span>
  </div>
  <div class="card">
    <h2 id="adj-grid-title">Adjustments</h2>
    <div class="gridwrap scrolly"><table class="grid" id="adj-grid">
      <thead><tr><th>Date</th><th>Type</th><th>Provider</th><th>Procedure</th><th>Note</th>
        <th class="num">Amount</th><th class="num">Running</th><th></th><th></th></tr></thead>
      <tbody></tbody></table></div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2 id="adj-form-title">New adjustment</h2>
    <form class="row" id="adj-form">
      <input type="date" id="adj-date" required>
      <select id="adj-type" required></select>
      <select id="adj-prov" required></select>
      <label>Amount $ <input type="number" id="adj-amt" step="0.01" min="0" style="width:100px" required></label>
      <select id="adj-proc" style="max-width:220px"><option value="0">No procedure</option></select>
      <input id="adj-note" placeholder="Note" style="flex:1;min-width:160px">
      <button class="action" id="adj-save">Create</button>
      <button type="button" class="quiet" id="adj-cancel" hidden>Cancel edit</button>
    </form>
    <div class="sub" style="font-size:12px;margin-top:6px">
      Enter the amount as a positive number — the sign comes from the adjustment type,
      same as the desktop Additions / Subtractions lists.</div>
  </div>`,
  init(){
    const msg=t=>{$("adj-msg").textContent=t;};
    let types=[],data=null,editNum=0,loadedFor=0;
    $("adj-date").value=new Date().toISOString().slice(0,10);

    function signTag(sign){
      if(sign==="+")return`<span class="tag">+</span>`;
      if(sign==="dp")return`<span class="tag">dp</span>`;
      return`<span class="tag">&minus;</span>`;
    }

    function renderGrid(){
      const tbody=document.querySelector("#adj-grid tbody");
      const rows=data?data.adjustments:[];
      //Running balance oldest-first, shown against the newest-first grid order.
      const running=new Map();
      let sum=0;
      for(let i=rows.length-1;i>=0;i--){sum+=rows[i].amount;running.set(rows[i].adjNum,sum);}
      $("adj-grid-title").textContent=data
        ?`Adjustments — ${data.patient} · total ${usd(data.total)}`
        :"Adjustments";
      tbody.innerHTML=rows.map(a=>
        `<tr><td class="mono">${a.adjDate}</td>`+
        `<td>${signTag(a.sign)} ${esc(a.typeName)}</td>`+
        `<td>${esc(a.provAbbr||"")}</td>`+
        `<td>${a.procNum?procLabel(a.procNum):""}</td>`+
        `<td>${esc(a.note||"")}</td>`+
        `<td class="num mono">${usd(a.amount)}</td>`+
        `<td class="num mono">${usd(running.get(a.adjNum))}</td>`+
        `<td><button type="button" class="quiet" data-edit="${a.adjNum}">Edit</button></td>`+
        `<td><button type="button" class="quiet" data-del="${a.adjNum}">Delete</button></td></tr>`).join("")
        ||`<tr><td colspan="9"><div class="empty"><b>No adjustments</b>Add the first one with the form below.</div></td></tr>`;
      tbody.querySelectorAll("button[data-edit]").forEach(btn=>btn.addEventListener("click",()=>startEdit(Number(btn.dataset.edit))));
      tbody.querySelectorAll("button[data-del]").forEach(btn=>btn.addEventListener("click",async()=>{
        if(!confirm("Delete this adjustment?"))return;
        try{
          await api(`/api/adjustments/${btn.dataset.del}`,{method:"DELETE"});
          msg("Adjustment deleted (security-logged).");
          if(editNum===Number(btn.dataset.del))resetForm();
          load();
        }catch(e){msg("Delete failed: "+e.message);}
      }));
    }

    let procs=[];
    function procLabel(procNum){
      const p=procs.find(x=>x.procNum===procNum);
      return p?esc(p.procCode+(p.toothNum?" #"+p.toothNum:"")+" "+p.procDate):("#"+procNum);
    }

    function renderPickers(provs){
      $("adj-type").innerHTML=types.map(t=>
        `<option value="${t.defNum}">${t.sign==="+"?"[+] ":t.sign==="dp"?"[dp] ":"[−] "}${esc(t.name)}</option>`).join("")
        ||`<option value="0">(no adjustment types defined)</option>`;
      $("adj-prov").innerHTML=provs.filter(p=>!p.isHidden).map(p=>
        `<option value="${p.provNum}">${esc(p.abbr)}</option>`).join("")
        ||`<option value="0">(no providers)</option>`;
      $("adj-proc").innerHTML=`<option value="0">No procedure</option>`+procs.map(p=>
        `<option value="${p.procNum}">${esc(p.procCode)}${p.toothNum?" #"+esc(p.toothNum):""} · ${esc(p.procDate||"TP")} · ${usd(p.fee)} (${esc(p.status)})</option>`).join("");
    }

    function startEdit(adjNum){
      const a=data&&data.adjustments.find(x=>x.adjNum===adjNum);
      if(!a)return;
      editNum=adjNum;
      $("adj-form-title").textContent="Edit adjustment "+a.adjDate+" · "+esc(a.typeName);
      $("adj-save").textContent="Save";
      $("adj-cancel").hidden=false;
      $("adj-date").value=a.adjDate;
      $("adj-type").value=String(a.adjType);
      $("adj-prov").value=String(a.provNum);
      $("adj-amt").value=Math.abs(a.amount).toFixed(2);
      $("adj-proc").value=String(a.procNum||0);
      $("adj-note").value=a.note||"";
    }

    function resetForm(){
      editNum=0;
      $("adj-form-title").textContent="New adjustment";
      $("adj-save").textContent="Create";
      $("adj-cancel").hidden=true;
      $("adj-amt").value="";
      $("adj-note").value="";
      $("adj-proc").value="0";
      $("adj-date").value=new Date().toISOString().slice(0,10);
    }

    async function load(){
      const pat=getActivePatient();
      if(!pat){
        data=null;procs=[];loadedFor=0;
        document.querySelector("#adj-grid tbody").innerHTML=
          `<tr><td colspan="9"><div class="empty"><b>No patient selected</b>Pick a patient in the band above to work their adjustments.</div></td></tr>`;
        $("adj-grid-title").textContent="Adjustments";
        msg("");
        return;
      }
      msg("Loading…");
      try{
        const [adj,provs,typeList,procList]=await Promise.all([
          api(`/api/patients/${pat.patNum}/adjustments`),
          api("/api/setup/providers"),
          api("/api/adjustment-types"),
          api(`/api/patients/${pat.patNum}/procs`),
        ]);
        data=adj;types=typeList;procs=procList;loadedFor=pat.patNum;
        renderPickers(provs);
        renderGrid();
        msg(adj.adjustments.length+" adjustment"+(adj.adjustments.length===1?"":"s")+" · total "+usd(adj.total)+".");
      }catch(e){msg("Adjustments unavailable: "+e.message);}
    }

    $("adj-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){msg("Select a patient first.");return;}
      const body=JSON.stringify({
        date:$("adj-date").value,
        adjType:Number($("adj-type").value),
        amount:Number($("adj-amt").value),
        provNum:Number($("adj-prov").value),
        note:$("adj-note").value,
        procNum:Number($("adj-proc").value),
      });
      msg("Saving…");
      try{
        if(editNum)await api(`/api/adjustments/${editNum}`,{method:"PUT",headers:{"Content-Type":"application/json"},body});
        else await api(`/api/patients/${pat.patNum}/adjustments`,{method:"POST",headers:{"Content-Type":"application/json"},body});
        msg(editNum?"Adjustment updated (security-logged).":"Adjustment created (security-logged).");
        resetForm();load();
      }catch(e2){msg("Save failed: "+e2.message);}
    });
    $("adj-cancel").addEventListener("click",resetForm);
    $("adj-reload").addEventListener("click",load);
    //Follow the patient band, and refresh when the rail shows this module for a new patient.
    $("band-patient").addEventListener("change",()=>{resetForm();setTimeout(load,0);});
    const railBtn=document.querySelector('nav.rail button[data-mod="billing-adjustments"]');
    if(railBtn)railBtn.addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor)load();});
    load();
  },
});
})();
