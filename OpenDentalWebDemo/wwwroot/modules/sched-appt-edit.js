/* Appointment detail & edit — retires FormApptEdit's core surface.
   Left: the active patient's appointments (GET /api/patients/{n}/appointments).
   Click one to load its detail (GET /api/appointments/{n}/detail) into an editor
   for date/time, operatory, provider, hygienist, status and note, saved via
   PUT /api/appointments/{n}/detail (Appointments.Update — the form's diff+audit
   path). Below the editor, the attached-procedures table with detach
   (DELETE /api/appointments/{n}/procedures/{procNum}) and an attach picker fed by
   the patient's charted procs (GET /api/patients/{n}/procs) via
   POST /api/appointments/{n}/procedures. Operatory/provider lists come from
   /api/setup/operatories and /api/setup/providers. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;

const STATUSES=["Scheduled","Complete","UnschedList","Broken","Planned","PtNote","PtNoteCompleted"];
const money=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});

registerODModule({
  id:"sched-appt-edit",
  label:"Appt Detail",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Appointment detail &amp; edit</h1>
    <span class="sub">open an appointment · edit scheduling fields · attach/detach procedures</span></div>
  <div class="chart-tools"><button type="button" class="action" id="ae-refresh">Refresh</button>
    <span class="msg" id="ae-msg"></span></div>
  <div class="row" style="align-items:flex-start;gap:16px">
    <div class="card" style="flex:0 0 340px">
      <h2 id="ae-list-title">Appointments</h2>
      <div class="gridwrap scrolly" style="max-height:520px"><table class="grid" id="ae-list">
        <thead><tr><th>Date</th><th>Status</th><th>Prov</th><th>Min</th></tr></thead>
        <tbody></tbody></table></div>
    </div>
    <div class="card" style="flex:1 1 420px" id="ae-detail" hidden>
      <h2 id="ae-detail-title">Appointment</h2>
      <form id="ae-form" class="row" style="flex-direction:column;align-items:stretch;gap:10px;max-width:520px">
        <label>Date/time <input type="datetime-local" id="ae-when" required></label>
        <label>Length (minutes) <input type="number" id="ae-min" min="5" step="5" style="width:120px"></label>
        <label>Operatory <select id="ae-op"><option value="0">(none)</option></select></label>
        <label>Provider <select id="ae-prov"></select></label>
        <label>Hygienist <select id="ae-hyg"><option value="0">(none)</option></select></label>
        <label>Status <select id="ae-status">${STATUSES.map(s=>`<option value="${s}">${s}</option>`).join("")}</select></label>
        <label>Confirmed <input id="ae-confirmed" readonly></label>
        <label>Note <textarea id="ae-note" rows="3"></textarea></label>
        <div class="row"><button class="action" type="submit">Save appointment</button></div>
      </form>
      <h2 style="margin-top:18px">Attached procedures</h2>
      <div class="gridwrap scrolly"><table class="grid" id="ae-procs">
        <thead><tr><th>Code</th><th>Description</th><th>Tooth</th><th>Surf</th><th>Fee</th><th>Status</th><th></th></tr></thead>
        <tbody></tbody></table></div>
      <div class="row" style="margin-top:10px">
        <select id="ae-attach-pick"><option value="">Attach a procedure…</option></select>
        <button class="quiet" type="button" id="ae-attach-btn">Attach</button>
      </div>
    </div>
  </div>`,
  init(){
    const msg=t=>{$("ae-msg").textContent=t;};
    let ops=[],provs=[],currentApt=null;

    async function loadPickers(){
      try{
        [provs,ops]=await Promise.all([api("/api/setup/providers"),api("/api/setup/operatories")]);
        $("ae-op").insertAdjacentHTML("beforeend",ops.filter(o=>!o.isHidden)
          .map(o=>`<option value="${o.operatoryNum}">${esc(o.abbrev||o.opName)}</option>`).join(""));
        const provOpts=provs.filter(p=>!p.isHidden)
          .map(p=>`<option value="${p.provNum}">${esc(p.abbr||p.lName)}</option>`).join("");
        $("ae-prov").insertAdjacentHTML("beforeend",provOpts);
        $("ae-hyg").insertAdjacentHTML("beforeend",provOpts);
      }catch(e){/* editor still opens; dropdowns degrade to their placeholder */}
    }

    async function loadList(){
      const pat=getActivePatient();
      const tbody=document.querySelector("#ae-list tbody");
      if(!pat){
        $("ae-list-title").textContent="Appointments — no patient selected";
        tbody.innerHTML=`<tr><td colspan="4"><div class="empty"><b>No patient</b>Select a patient to see their appointments.</div></td></tr>`;
        $("ae-detail").hidden=true;
        msg("");
        return;
      }
      $("ae-list-title").textContent="Appointments · "+pat.name;
      msg("Loading appointments…");
      try{
        const rows=await api(`/api/patients/${pat.patNum}/appointments`);
        tbody.innerHTML=rows.map(r=>
          `<tr class="rowsel" data-apt="${r.aptNum}"><td class="mono">${esc(r.dateTime)}</td>`+
          `<td><span class="tag">${esc(r.aptStatus)}</span></td><td>${esc(r.provAbbr)}</td>`+
          `<td class="num">${r.minutes}</td></tr>`).join("")
          ||`<tr><td colspan="4"><div class="empty"><b>None</b>${esc(pat.name)} has no appointments.</div></td></tr>`;
        tbody.querySelectorAll("tr[data-apt]").forEach(tr=>{
          tr.addEventListener("click",()=>loadDetail(tr.dataset.apt));
        });
        msg(rows.length+" appointment"+(rows.length===1?"":"s")+".");
      }catch(e){
        tbody.innerHTML=`<tr><td colspan="4">${esc(e.message)}</td></tr>`;
        msg("Appointments unavailable: "+e.message);
      }
    }

    async function loadDetail(aptNum){
      msg("Loading appointment "+aptNum+"…");
      try{
        const d=await api(`/api/appointments/${aptNum}/detail`);
        currentApt=d;
        $("ae-detail").hidden=false;
        $("ae-detail-title").textContent="Appointment #"+d.aptNum+" · "+d.patient;
        $("ae-when").value=d.dateTime?d.dateTime.replace(" ","T"):"";
        $("ae-min").value=d.minutes||"";
        $("ae-op").value=String(d.opNum||0);
        $("ae-prov").value=String(d.provNum||0);
        $("ae-hyg").value=String(d.provHyg||0);
        $("ae-status").value=STATUSES.includes(d.aptStatus)?d.aptStatus:"Scheduled";
        $("ae-confirmed").value=d.confirmed||"(none)";
        $("ae-note").value=d.note||"";
        fillProcs(d.procedures);
        await fillAttachPicker();
        msg("Loaded appointment #"+d.aptNum+".");
      }catch(e){msg("Could not load appointment: "+e.message);}
    }

    function fillProcs(procs){
      document.querySelector("#ae-procs tbody").innerHTML=(procs||[]).map(p=>
        `<tr><td class="mono">${esc(p.procCode)}</td><td>${esc(p.descript)}</td>`+
        `<td class="num">${esc(p.toothNum)}</td><td>${esc(p.surf)}</td>`+
        `<td class="num">${money(p.fee)}</td><td>${esc(p.status)}</td>`+
        `<td><button class="quiet" type="button" data-detach="${p.procNum}">Detach</button></td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No procedures</b>Nothing is attached to this appointment.</div></td></tr>`;
      document.querySelectorAll("#ae-procs button[data-detach]").forEach(btn=>{
        btn.addEventListener("click",()=>detachProc(btn.dataset.detach));
      });
    }

    async function fillAttachPicker(){
      const pick=$("ae-attach-pick");
      pick.innerHTML=`<option value="">Attach a procedure…</option>`;
      if(!currentApt)return;
      try{
        const procs=await api(`/api/patients/${currentApt.patNum}/procs`);
        const attached=new Set((currentApt.procedures||[]).map(p=>p.procNum));
        const avail=procs.filter(p=>!attached.has(p.procNum));
        pick.insertAdjacentHTML("beforeend",avail.map(p=>
          `<option value="${p.procNum}">${esc(p.procCode)} ${esc(p.descript)}`+
          `${p.toothNum?" · #"+esc(p.toothNum):""} · ${esc(p.status)}</option>`).join(""));
      }catch(e){/* attach picker stays empty when procs are unavailable */}
    }

    async function saveDetail(e){
      e.preventDefault();
      if(!currentApt)return;
      const when=$("ae-when").value;
      if(!when){msg("Pick a date/time first.");return;}
      msg("Saving appointment…");
      try{
        const body={
          dateTime:when.replace("T"," "),
          minutes:Number($("ae-min").value)||0,
          opNum:Number($("ae-op").value)||0,
          provNum:Number($("ae-prov").value)||0,
          provHyg:Number($("ae-hyg").value)||0,
          aptStatus:$("ae-status").value,
          note:$("ae-note").value,
        };
        const r=await api(`/api/appointments/${currentApt.aptNum}/detail`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        currentApt.minutes=r.minutes;
        $("ae-min").value=r.minutes||"";
        msg("Saved appointment #"+r.aptNum+" — "+r.minutes+" min.");
        loadList();
      }catch(err){msg("Save failed: "+err.message);}
    }

    async function attachProc(){
      if(!currentApt)return;
      const procNum=Number($("ae-attach-pick").value);
      if(!procNum){msg("Pick a procedure to attach first.");return;}
      msg("Attaching procedure…");
      try{
        const r=await api(`/api/appointments/${currentApt.aptNum}/procedures`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify({procNums:[procNum]})});
        currentApt.procedures=r.procedures;
        currentApt.minutes=r.minutes;
        $("ae-min").value=r.minutes||"";
        fillProcs(r.procedures);
        await fillAttachPicker();
        msg("Attached — appointment now "+r.minutes+" min.");
      }catch(e){msg("Attach failed: "+e.message);}
    }

    async function detachProc(procNum){
      if(!currentApt)return;
      msg("Detaching procedure…");
      try{
        const r=await api(`/api/appointments/${currentApt.aptNum}/procedures/${procNum}`,{method:"DELETE"});
        currentApt.procedures=r.procedures;
        currentApt.minutes=r.minutes;
        $("ae-min").value=r.minutes||"";
        fillProcs(r.procedures);
        await fillAttachPicker();
        msg("Detached — appointment now "+r.minutes+" min.");
      }catch(e){msg("Detach failed: "+e.message);}
    }

    $("ae-form").addEventListener("submit",saveDetail);
    $("ae-attach-btn").addEventListener("click",attachProc);
    $("ae-refresh").addEventListener("click",()=>{currentApt=null;$("ae-detail").hidden=true;loadList();});
    //Reload the list whenever this module's rail button is clicked, so switching the
    //active patient elsewhere and returning here shows the right appointments.
    const railBtn=document.querySelector('nav.rail button[data-mod="sched-appt-edit"]');
    if(railBtn){railBtn.addEventListener("click",()=>{if(currentApt&&getActivePatient()&&currentApt.patNum!==getActivePatient().patNum){currentApt=null;$("ae-detail").hidden=true;}loadList();});}
    loadPickers().then(loadList);
  },
});
})();
