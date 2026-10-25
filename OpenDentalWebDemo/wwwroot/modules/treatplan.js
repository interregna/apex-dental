/* Treatment plan — rail port of ControlTreat's TP surface, now create/edit capable.
   GET /api/treatplan/{patNum}: treatment-planned procedures (ProcStat.TP).
   GET /api/patients/{patNum}/treatplans: all plans w/ attached procs + TxPriorities defs.
   POST creates a saved plan (TreatPlans.CreateArchivedTreatPlan), PUT edits
   heading/note/attachments/priorities, DELETE removes non-active plans. */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const usd=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  let loadedFor=null;
  let planned=[];    //TP procs from /api/treatplan
  let priorities=[]; //TxPriorities defs
  let plans=[];      //saved + active/inactive plans
  let openPlanNum=null;

  registerODModule({
    id:"treatplan",
    label:"Tx Plan",
    group:"Clinical",
    html:`
      <div class="mod-head"><h1>Treatment plan</h1>
        <span class="sub" id="txp-patline">planned procedures and saved plans — real Procedures/TreatPlans/ProcTPs business layer</span></div>
      <div class="cols c2">
        <div class="card">
          <h2>Planned procedures
            <button type="button" class="quiet" id="txp-refresh" style="margin-left:8px">Refresh</button>
            <span class="msg" id="txp-msg"></span></h2>
          <div class="gridwrap scrolly"><table class="grid" id="txp-procs">
            <thead><tr><th></th><th>Code</th><th>Description</th><th>Th</th><th>Surf</th><th>Prov</th><th>Priority</th><th class="num">Fee</th></tr></thead>
            <tbody></tbody><tfoot></tfoot></table></div>
          <form id="txp-newform" class="row" style="margin-top:10px">
            <label>Heading <input type="text" id="txp-new-heading" placeholder="Saved plan heading" required></label>
            <label>Note <input type="text" id="txp-new-note" placeholder="optional note"></label>
            <button type="submit">Save as treatment plan</button>
            <span class="msg" id="txp-new-msg"></span>
          </form>
        </div>
        <div class="card">
          <h2>Treatment plans <span class="msg" id="txp-plans-msg"></span></h2>
          <div class="gridwrap scrolly"><table class="grid" id="txp-plans">
            <thead><tr><th>Date</th><th>Heading</th><th>Status</th><th class="num">Procs</th><th class="num">Fee</th><th></th><th></th></tr></thead>
            <tbody></tbody></table></div>
          <div id="txp-detail" style="display:none;margin-top:12px">
            <h2 id="txp-detail-title">Plan</h2>
            <div class="row" style="margin-bottom:8px">
              <label>Heading <input type="text" id="txp-edit-heading"></label>
              <label>Note <input type="text" id="txp-edit-note"></label>
            </div>
            <div class="gridwrap scrolly"><table class="grid" id="txp-detail-procs">
              <thead><tr><th>Keep</th><th>Code</th><th>Description</th><th>Th</th><th>Prov</th><th>Priority</th><th class="num">Fee</th></tr></thead>
              <tbody></tbody></table></div>
            <div id="txp-addwrap" style="margin-top:8px">
              <h2 style="font-size:13px">Add planned procedures</h2>
              <div class="gridwrap scrolly"><table class="grid" id="txp-detail-add">
                <thead><tr><th>Add</th><th>Code</th><th>Description</th><th>Th</th><th class="num">Fee</th></tr></thead>
                <tbody></tbody></table></div>
            </div>
            <div class="row" style="margin-top:8px">
              <button type="button" id="txp-detail-save">Save changes</button>
              <button type="button" class="quiet" id="txp-detail-close">Close</button>
              <span class="msg" id="txp-detail-msg"></span>
            </div>
          </div>
        </div>
      </div>`,
    init(){
      $("txp-refresh").addEventListener("click",load);
      $("band-patient").addEventListener("change",()=>setTimeout(load,0));
      //Reload when the module is opened for a different patient than last time.
      document.querySelector('nav.rail button[data-mod="treatplan"]')
        .addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor)load()});
      $("txp-newform").addEventListener("submit",createPlan);
      $("txp-plans").querySelector("tbody").addEventListener("click",onPlanRowClick);
      $("txp-detail-save").addEventListener("click",saveDetail);
      $("txp-detail-close").addEventListener("click",()=>{openPlanNum=null;renderDetail()});
      load();
    }
  });

  function prioritySelect(id,procNum,selected){
    return `<select id="${esc(id)}-${procNum}" data-proc="${procNum}"><option value="0">—</option>`+
      priorities.map(p=>`<option value="${p.defNum}"${Number(selected)===Number(p.defNum)?" selected":""}>${esc(p.name)}</option>`).join("")+
      `</select>`;
  }

  async function load(){
    const pat=getActivePatient();
    if(!pat){$("txp-msg").textContent="Select a patient first.";return}
    loadedFor=pat.patNum;
    openPlanNum=null;
    $("txp-patline").textContent=`Patient: ${pat.name}`;
    $("txp-msg").textContent="Loading…";
    let d,tp;
    try{
      [d,tp]=await Promise.all([
        api(`/api/treatplan/${pat.patNum}`),
        api(`/api/patients/${pat.patNum}/treatplans`)
      ]);
    }
    catch(e){$("txp-msg").textContent="treatplan endpoints unavailable — "+e.message.slice(0,60);return}
    planned=d.procs;
    priorities=tp.priorities;
    plans=tp.plans;
    $("txp-msg").textContent=`${planned.length} planned procedure(s), ${plans.length} plan(s)`;
    renderPlanned(d.total);
    renderPlans();
    renderDetail();
  }

  function renderPlanned(total){
    $("txp-procs").querySelector("tbody").innerHTML=planned.map(p=>
      `<tr><td><input type="checkbox" class="txp-sel" data-proc="${p.procNum}"></td>`+
      `<td class="mono">${esc(p.code)}</td><td>${esc(p.descript)}</td><td class="num">${esc(p.toothNum)}</td>`+
      `<td class="mono">${esc(p.surf)}</td><td>${esc(p.provider)}</td>`+
      `<td>${prioritySelect("txp-pri",p.procNum,0)}</td><td class="num">${usd(p.fee)}</td></tr>`).join("")
      ||`<tr><td colspan="8"><div class="empty"><b>No treatment-planned procedures</b>Chart TP procedures in the Procedures module.</div></td></tr>`;
    $("txp-procs").querySelector("tfoot").innerHTML=planned.length
      ?`<tr><td colspan="7" style="font-weight:700">Total</td><td class="num" style="font-weight:700">${usd(total)}</td></tr>`:"";
  }

  function renderPlans(){
    $("txp-plans").querySelector("tbody").innerHTML=plans.map(p=>{
      const fee=p.procs.reduce((s,x)=>s+Number(x.fee),0);
      return `<tr><td class="mono">${esc(p.dateTP)}</td><td>${esc(p.heading)}</td>`+
        `<td><span class="tag">${esc(p.status)}</span></td><td class="num">${p.procCount}</td>`+
        `<td class="num">${usd(fee)}</td>`+
        `<td><button type="button" class="quiet txp-open" data-plan="${p.treatPlanNum}">Open</button></td>`+
        `<td>${p.status==="Active"?"":`<button type="button" class="quiet txp-del" data-plan="${p.treatPlanNum}">Delete</button>`}</td></tr>`;
    }).join("")
      ||`<tr><td colspan="7"><div class="empty"><b>No treatment plans</b>Select planned procedures and save a plan.</div></td></tr>`;
  }

  function renderDetail(){
    const plan=plans.find(p=>p.treatPlanNum===openPlanNum);
    if(!plan){$("txp-detail").style.display="none";return}
    $("txp-detail").style.display="";
    $("txp-detail-title").textContent=`${plan.heading} (${plan.status}, ${plan.dateTP||"no date"})`;
    $("txp-edit-heading").value=plan.heading;
    $("txp-edit-note").value=plan.note;
    $("txp-detail-procs").querySelector("tbody").innerHTML=plan.procs.map(p=>
      `<tr><td><input type="checkbox" class="txp-keep" data-proc="${p.procNum}" checked></td>`+
      `<td class="mono">${esc(p.code)}</td><td>${esc(p.descript)}</td><td class="num">${esc(p.toothNum)}</td>`+
      `<td>${esc(p.provider)}</td><td>${prioritySelect("txp-dpri",p.procNum,p.priority)}</td>`+
      `<td class="num">${usd(p.fee)}</td></tr>`).join("")
      ||`<tr><td colspan="7"><div class="empty"><b>No procedures attached</b></div></td></tr>`;
    const attached=plan.procs.map(p=>p.procNum);
    const addable=planned.filter(p=>!attached.includes(p.procNum));
    $("txp-detail-add").querySelector("tbody").innerHTML=addable.map(p=>
      `<tr><td><input type="checkbox" class="txp-addsel" data-proc="${p.procNum}"></td>`+
      `<td class="mono">${esc(p.code)}</td><td>${esc(p.descript)}</td><td class="num">${esc(p.toothNum)}</td>`+
      `<td class="num">${usd(p.fee)}</td></tr>`).join("")
      ||`<tr><td colspan="5"><div class="empty"><b>All planned procedures are attached</b></div></td></tr>`;
    $("txp-detail-msg").textContent="";
  }

  async function createPlan(ev){
    ev.preventDefault();
    const pat=getActivePatient();
    if(!pat)return;
    const msg=$("txp-new-msg");
    const procNums=[...document.querySelectorAll("#txp-procs .txp-sel:checked")].map(c=>Number(c.dataset.proc));
    if(procNums.length===0){msg.textContent="Check at least one procedure.";return}
    const prios={};
    procNums.forEach(n=>{
      const sel=$(`txp-pri-${n}`);
      if(sel&&Number(sel.value)>0)prios[n]=Number(sel.value);
    });
    msg.textContent="Saving…";
    try{
      await api(`/api/patients/${pat.patNum}/treatplans`,{
        method:"POST",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({heading:$("txp-new-heading").value,note:$("txp-new-note").value,procNums,priorities:prios})
      });
    }
    catch(e){msg.textContent=e.message.slice(0,80);return}
    $("txp-new-heading").value="";
    $("txp-new-note").value="";
    await load();
    $("txp-plans-msg").textContent="Plan saved.";
  }

  async function onPlanRowClick(ev){
    const btn=ev.target.closest("button");
    if(!btn)return;
    const planNum=Number(btn.dataset.plan);
    if(btn.classList.contains("txp-open")){
      openPlanNum=planNum;
      renderDetail();
      return;
    }
    if(btn.classList.contains("txp-del")){
      const plan=plans.find(p=>p.treatPlanNum===planNum);
      if(!plan||!confirm(`Delete treatment plan "${plan.heading}"?`))return;
      $("txp-plans-msg").textContent="Deleting…";
      try{await api(`/api/treatplans/${planNum}`,{method:"DELETE"})}
      catch(e){$("txp-plans-msg").textContent=e.message.slice(0,80);return}
      if(openPlanNum===planNum)openPlanNum=null;
      await load();
      $("txp-plans-msg").textContent="Plan deleted.";
    }
  }

  async function saveDetail(){
    const plan=plans.find(p=>p.treatPlanNum===openPlanNum);
    if(!plan)return;
    const msg=$("txp-detail-msg");
    const heading=$("txp-edit-heading").value;
    if(!heading.trim()){msg.textContent="Heading cannot be empty.";return}
    const kept=[...document.querySelectorAll("#txp-detail-procs .txp-keep:checked")].map(c=>Number(c.dataset.proc));
    const added=[...document.querySelectorAll("#txp-detail-add .txp-addsel:checked")].map(c=>Number(c.dataset.proc));
    const procNums=kept.concat(added);
    if(procNums.length===0){msg.textContent="A plan needs at least one procedure.";return}
    const prios={};
    kept.forEach(n=>{
      const sel=$(`txp-dpri-${n}`);
      if(sel)prios[n]=Number(sel.value);
    });
    msg.textContent="Saving…";
    try{
      await api(`/api/treatplans/${openPlanNum}`,{
        method:"PUT",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({heading,note:$("txp-edit-note").value,procNums,priorities:prios})
      });
    }
    catch(e){msg.textContent=e.message.slice(0,80);return}
    const reopen=openPlanNum;
    await load();
    openPlanNum=reopen;
    renderDetail();
    $("txp-plans-msg").textContent="Plan updated.";
  }
})();
