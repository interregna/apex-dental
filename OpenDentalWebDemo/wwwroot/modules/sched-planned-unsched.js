/* Planned tracker & unscheduled list — retires FormTrackNext, FormUnsched,
   FormUnschedListPatient and FormApptsOther.
   Two-tab surface over GET /api/planned (Appointments.RefreshPlannedTracker),
   GET /api/unsched (Appointments.RefreshUnsched) and
   GET /api/patients/{patNum}/unsched (Appointments.GetUnschedApptsForPat).
   Scheduling a planned row calls POST /api/planned/{aptNum}/schedule
   (Appointments.SchedulePlannedApt) with the date/time + operatory picked in
   the schedule bar — the pinboard-drop conversion, one click. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;

registerODModule({
  id:"sched-planned-unsched",
  label:"Planned / Unsched",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Planned tracker &amp; Unscheduled</h1>
    <span class="sub">planned appts awaiting scheduling · unscheduled/broken worklist</span></div>
  <div class="chart-tools">
    <span class="seg" id="pu-tabs">
      <button type="button" data-tab="planned" aria-pressed="true">Planned tracker</button>
      <button type="button" data-tab="unsched" aria-pressed="false">Unscheduled</button>
    </span>
    <span class="msg" id="pu-msg"></span>
  </div>
  <div id="pu-pane-planned">
    <div class="card">
      <h2>Planned appointments not yet scheduled</h2>
      <form class="row" id="pu-pl-filters" style="margin-bottom:10px">
        <label>From <input type="date" id="pu-pl-from"></label>
        <label>To <input type="date" id="pu-pl-to"></label>
        <select id="pu-pl-prov"><option value="0">All providers</option></select>
        <input id="pu-pl-code1" placeholder="Proc code from" size="10">
        <input id="pu-pl-code2" placeholder="Proc code to" size="10">
        <select id="pu-pl-sort">
          <option value="date">Sort: date</option>
          <option value="alph">Sort: name</option>
          <option value="status">Sort: status</option>
        </select>
        <button class="action">Refresh</button>
      </form>
      <form class="row" id="pu-schedbar" style="margin-bottom:10px">
        <label>Schedule at <input type="datetime-local" id="pu-when" required></label>
        <select id="pu-op" required><option value="">Operatory…</option></select>
        <span class="sub" style="align-self:center">then click Schedule on a row</span>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="pu-pl-grid">
        <thead><tr><th>Patient</th><th>Phone</th><th>Planned date</th><th>Prov</th><th>Min</th>
        <th>Status</th><th>Procedures</th><th>Note</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>
  <div id="pu-pane-unsched" hidden>
    <div class="card">
      <h2>Unscheduled list</h2>
      <form class="row" id="pu-un-filters" style="margin-bottom:10px">
        <label>From <input type="date" id="pu-un-from"></label>
        <label>To <input type="date" id="pu-un-to"></label>
        <select id="pu-un-prov"><option value="0">All providers</option></select>
        <select id="pu-un-sort">
          <option value="date">Sort: date</option>
          <option value="alph">Sort: name</option>
          <option value="status">Sort: status</option>
        </select>
        <label><input type="checkbox" id="pu-un-broken" checked> Include broken</label>
        <button class="action">Refresh</button>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="pu-un-grid">
        <thead><tr><th>Patient</th><th>Phone</th><th>Was scheduled for</th><th>Status</th>
        <th>Unsched status</th><th>Prov</th><th>Min</th><th>Procedures</th><th>Note</th></tr></thead>
        <tbody></tbody></table></div>
    </div>
    <div class="card" style="margin-top:12px">
      <h2 id="pu-pat-title">Unscheduled for active patient</h2>
      <div class="gridwrap scrolly"><table class="grid" id="pu-pat-grid">
        <thead><tr><th>Was scheduled for</th><th>Prov</th><th>Min</th><th>Procedures</th><th>Note</th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>`,
  init(){
    const msg=t=>{$("pu-msg").textContent=t;};

    /* tabs */
    document.querySelectorAll("#pu-tabs button").forEach(btn=>{
      btn.addEventListener("click",()=>{
        document.querySelectorAll("#pu-tabs button").forEach(b=>b.setAttribute("aria-pressed",b===btn));
        $("pu-pane-planned").hidden=btn.dataset.tab!=="planned";
        $("pu-pane-unsched").hidden=btn.dataset.tab!=="unsched";
        if(btn.dataset.tab==="unsched")loadUnsched();else loadPlanned();
      });
    });

    async function loadPickers(){
      try{
        const [provs,ops]=await Promise.all([api("/api/setup/providers"),api("/api/setup/operatories")]);
        const provOpts=provs.filter(p=>!p.isHidden)
          .map(p=>`<option value="${p.provNum}">${esc(p.abbr||p.lName)}</option>`).join("");
        $("pu-pl-prov").insertAdjacentHTML("beforeend",provOpts);
        $("pu-un-prov").insertAdjacentHTML("beforeend",provOpts);
        $("pu-op").insertAdjacentHTML("beforeend",ops.filter(o=>!o.isHidden)
          .map(o=>`<option value="${o.operatoryNum}">${esc(o.abbrev||o.opName)}</option>`).join(""));
      }catch(e){/* filters degrade to "all" */}
    }

    async function loadPlanned(){
      msg("Loading planned tracker…");
      try{
        const q=new URLSearchParams({from:$("pu-pl-from").value,to:$("pu-pl-to").value,
          provNum:$("pu-pl-prov").value,orderBy:$("pu-pl-sort").value,
          codeStart:$("pu-pl-code1").value.trim(),codeEnd:$("pu-pl-code2").value.trim()});
        const rows=await api("/api/planned?"+q);
        const tbody=document.querySelector("#pu-pl-grid tbody");
        tbody.innerHTML=rows.map(r=>
          `<tr><td>${esc(r.patient)}</td><td class="mono">${esc(r.phone)}</td>`+
          `<td class="mono">${esc(r.dateTime)}</td><td>${esc(r.provAbbr)}</td><td class="num">${r.minutes}</td>`+
          `<td>${esc(r.unschedStatus)}</td><td>${esc(r.procDescript)}</td><td>${esc(r.note)}</td>`+
          `<td>${r.scheduledAptNum?`<span class="tag">Sched ${esc(r.scheduledFor)}</span>`
            :`<button type="button" class="quiet" data-sched="${r.aptNum}" data-name="${esc(r.patient)}">Schedule</button>`}</td></tr>`).join("")
          ||`<tr><td colspan="9"><div class="empty"><b>No planned appointments</b>Nothing is waiting to be scheduled with these filters.</div></td></tr>`;
        tbody.querySelectorAll("button[data-sched]").forEach(btn=>{
          btn.addEventListener("click",()=>schedulePlanned(btn.dataset.sched,btn.dataset.name));
        });
        msg(rows.length+" planned appointment"+(rows.length===1?"":"s")+" awaiting scheduling.");
      }catch(e){msg("Planned tracker unavailable: "+e.message);}
    }

    async function schedulePlanned(aptNum,name){
      const when=$("pu-when").value,opNum=Number($("pu-op").value);
      if(!when||!opNum){msg("Pick a date/time and operatory in the schedule bar first.");return;}
      msg("Scheduling "+name+"…");
      try{
        const r=await api(`/api/planned/${aptNum}/schedule`,{method:"POST",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({dateTime:when.replace("T"," "),opNum})});
        msg(`Scheduled ${name} — appt #${r.aptNum} at ${r.dateTime}.`);
        loadPlanned();
      }catch(e){msg("Scheduling failed: "+e.message);}
    }

    async function loadUnsched(){
      msg("Loading unscheduled list…");
      try{
        const q=new URLSearchParams({from:$("pu-un-from").value,to:$("pu-un-to").value,
          provNum:$("pu-un-prov").value,orderBy:$("pu-un-sort").value,
          includeBroken:$("pu-un-broken").checked});
        const rows=await api("/api/unsched?"+q);
        document.querySelector("#pu-un-grid tbody").innerHTML=rows.map(r=>
          `<tr><td>${esc(r.patient)}</td><td class="mono">${esc(r.phone)}</td>`+
          `<td class="mono">${esc(r.dateTime)}</td><td><span class="tag">${esc(r.status)}</span></td>`+
          `<td>${esc(r.unschedStatus)}</td><td>${esc(r.provAbbr)}</td><td class="num">${r.minutes}</td>`+
          `<td>${esc(r.procDescript)}</td><td>${esc(r.note)}</td></tr>`).join("")
          ||`<tr><td colspan="9"><div class="empty"><b>No unscheduled appointments</b>The unscheduled list is empty with these filters.</div></td></tr>`;
        msg(rows.length+" unscheduled appointment"+(rows.length===1?"":"s")+".");
        loadPatUnsched();
      }catch(e){msg("Unscheduled list unavailable: "+e.message);}
    }

    async function loadPatUnsched(){
      const pat=getActivePatient();
      const tbody=document.querySelector("#pu-pat-grid tbody");
      if(!pat){
        $("pu-pat-title").textContent="Unscheduled for active patient — none selected";
        tbody.innerHTML="";
        return;
      }
      $("pu-pat-title").textContent="Unscheduled for "+pat.name;
      try{
        const rows=await api(`/api/patients/${pat.patNum}/unsched`);
        tbody.innerHTML=rows.map(r=>
          `<tr><td class="mono">${esc(r.dateTime)}</td><td>${esc(r.provAbbr)}</td><td class="num">${r.minutes}</td>`+
          `<td>${esc(r.procDescript)}</td><td>${esc(r.note)}</td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>None</b>${esc(pat.name)} has no unscheduled appointments.</div></td></tr>`;
      }catch(e){tbody.innerHTML=`<tr><td colspan="5">${esc(e.message)}</td></tr>`;}
    }

    $("pu-pl-filters").addEventListener("submit",e=>{e.preventDefault();loadPlanned();});
    $("pu-schedbar").addEventListener("submit",e=>{e.preventDefault();});
    $("pu-un-filters").addEventListener("submit",e=>{e.preventDefault();loadUnsched();});
    loadPickers();
    loadPlanned();
  },
});
})();
