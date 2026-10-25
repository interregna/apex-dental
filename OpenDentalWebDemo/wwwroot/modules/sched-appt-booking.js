/* Book a new appointment — retires FormApptEdit's *create* branch.
   For the active patient, pick a date/time (leave blank to park the appt on the
   unscheduled list), an operatory, a provider + optional hygienist, and check off
   any of the patient's bookable treatment-planned procedures
   (GET /api/patients/{n}/bookable-procs). The checked procs drive the computed
   length live. Book posts to POST /api/patients/{n}/appointments
   (Appointments.Insert + CalculatePattern + proc attach) and reports the new
   aptNum with its computed pattern/minutes. The appt detail/edit surface and the
   schedule day view own their own routes; this only creates. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;

registerODModule({
  id:"sched-appt-booking",
  label:"Book Appt",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Book a new appointment</h1>
    <span class="sub">schedule into an operatory &amp; time · or park on the unscheduled list · from treatment-planned procedures</span></div>
  <div class="chart-tools"><button type="button" class="action" id="bk-refresh">Refresh</button>
    <span class="msg" id="bk-msg"></span></div>
  <div id="bk-nopat" class="card" hidden>
    <div class="empty"><b>No patient selected</b>Pick a patient to book an appointment for them.</div>
  </div>
  <div class="row" id="bk-body" style="align-items:flex-start;gap:16px" hidden>
    <div class="card" style="flex:1 1 380px;max-width:520px">
      <h2 id="bk-title">New appointment</h2>
      <form id="bk-form" class="row" style="flex-direction:column;align-items:stretch;gap:10px">
        <label>Date/time <input type="datetime-local" id="bk-when">
          <span class="sub">leave blank to park on the unscheduled list</span></label>
        <label>Operatory <select id="bk-op"><option value="0">(none)</option></select></label>
        <label>Provider <select id="bk-prov"></select></label>
        <label>Hygienist <select id="bk-hyg"><option value="0">(none)</option></select></label>
        <label>Length (minutes) <input type="number" id="bk-min" min="5" step="5" style="width:120px"
          placeholder="from procedures"></label>
        <label>Note <textarea id="bk-note" rows="3"></textarea></label>
        <div class="row" style="align-items:center;gap:12px">
          <button class="action" type="submit">Book appointment</button>
          <span class="sub" id="bk-computed"></span>
        </div>
      </form>
      <p class="sub" style="margin-top:12px">Need an open slot? Use <b>Slot Search</b> in the Schedule group to find a time, then enter it above.</p>
    </div>
    <div class="card" style="flex:1 1 420px">
      <h2>Bookable procedures</h2>
      <span class="sub">treatment-planned procedures not yet on an appointment — checked procs set the length</span>
      <div class="gridwrap scrolly" style="max-height:420px;margin-top:8px"><table class="grid" id="bk-procs">
        <thead><tr><th></th><th>Code</th><th>Description</th><th>Tooth</th><th>Surf</th><th>Min</th><th>Prov</th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>`,
  init(){
    const msg=t=>{$("bk-msg").textContent=t;};
    let procs=[];

    async function loadPickers(){
      try{
        const [provs,ops]=await Promise.all([api("/api/setup/providers"),api("/api/setup/operatories")]);
        $("bk-op").insertAdjacentHTML("beforeend",ops.filter(o=>!o.isHidden)
          .map(o=>`<option value="${o.operatoryNum}">${esc(o.abbrev||o.opName)}</option>`).join(""));
        const provOpts=provs.filter(p=>!p.isHidden)
          .map(p=>`<option value="${p.provNum}">${esc(p.abbr||p.lName)}</option>`).join("");
        $("bk-prov").insertAdjacentHTML("beforeend",provOpts);
        $("bk-hyg").insertAdjacentHTML("beforeend",provOpts);
      }catch(e){/* form still works; dropdowns degrade to placeholders */}
    }

    function checkedProcNums(){
      return Array.from(document.querySelectorAll("#bk-procs input[data-proc]:checked"))
        .map(cb=>Number(cb.dataset.proc));
    }

    function recomputeLength(){
      const chosen=procs.filter(p=>checkedProcNums().includes(p.procNum));
      const total=chosen.reduce((s,p)=>s+(p.minutes||0),0);
      if(total>0){
        $("bk-computed").textContent="Selected procedures ≈ "+total+" min.";
      }else if($("bk-min").value){
        $("bk-computed").textContent="Using entered length of "+Number($("bk-min").value)+" min.";
      }else{
        $("bk-computed").textContent="No procedures selected — practice default length applies.";
      }
    }

    function fillProcs(){
      const tbody=document.querySelector("#bk-procs tbody");
      tbody.innerHTML=procs.map(p=>
        `<tr><td><input type="checkbox" data-proc="${p.procNum}"></td>`+
        `<td class="mono">${esc(p.procCode)}</td><td>${esc(p.descript)}</td>`+
        `<td class="num">${esc(p.toothNum)}</td><td>${esc(p.surf)}</td>`+
        `<td class="num">${p.minutes}</td><td>${esc(p.provAbbr)}</td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No bookable procedures</b>This patient has no treatment-planned procedures waiting to be scheduled.</div></td></tr>`;
      tbody.querySelectorAll("input[data-proc]").forEach(cb=>cb.addEventListener("change",recomputeLength));
    }

    async function loadPatient(){
      const pat=getActivePatient();
      if(!pat){
        $("bk-nopat").hidden=false;
        $("bk-body").hidden=true;
        msg("");
        return;
      }
      $("bk-nopat").hidden=true;
      $("bk-body").hidden=false;
      $("bk-title").textContent="New appointment · "+pat.name;
      msg("Loading bookable procedures…");
      try{
        procs=await api(`/api/patients/${pat.patNum}/bookable-procs`);
        fillProcs();
        recomputeLength();
        msg(procs.length+" bookable procedure"+(procs.length===1?"":"s")+".");
      }catch(e){
        procs=[];
        fillProcs();
        msg("Bookable procedures unavailable: "+e.message);
      }
    }

    async function book(e){
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){msg("Select a patient first.");return;}
      const when=$("bk-when").value;
      const body={
        dateTime:when?when.replace("T"," "):"",
        opNum:Number($("bk-op").value)||0,
        provNum:Number($("bk-prov").value)||0,
        provHyg:Number($("bk-hyg").value)||0,
        minutes:Number($("bk-min").value)||0,
        note:$("bk-note").value,
        procNums:checkedProcNums(),
      };
      msg("Booking appointment…");
      try{
        const r=await api(`/api/patients/${pat.patNum}/appointments`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        const where=r.dateTime?("scheduled for "+esc(r.dateTime)):"parked on the unscheduled list";
        msg(`Booked appt #${r.aptNum} — ${where}. Pattern ${esc(r.pattern)} · ${r.minutes} min.`);
        $("bk-computed").textContent="Booked appt #"+r.aptNum+" ("+r.minutes+" min).";
        loadPatient();
      }catch(err){msg("Booking failed: "+err.message);}
    }

    $("bk-form").addEventListener("submit",book);
    $("bk-min").addEventListener("input",recomputeLength);
    $("bk-refresh").addEventListener("click",loadPatient);
    const railBtn=document.querySelector('nav.rail button[data-mod="sched-appt-booking"]');
    if(railBtn){railBtn.addEventListener("click",loadPatient);}
    loadPickers().then(loadPatient);
  },
});
})();
