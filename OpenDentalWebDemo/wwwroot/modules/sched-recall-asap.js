/* Recall & ASAP worklists + per-patient recall editor — retires FormRecallList,
   FormASAP, FormRecallsPat and FormRecallEdit.
   Three-tab surface over GET /api/recalls (Recalls.GetRecallList), GET /api/asap
   (Appointments.RefreshASAP + ASAP-flagged recalls) and
   GET /api/patients/{patNum}/recalls (real Recalls / RecallTypes business layer)
   with inline edit of status, due date, interval override, note, disable and
   priority fields. Synch re-derives due dates via Recalls.Synch exactly like the
   desktop form does on open. Booking reuses the existing POST /api/appointments. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;
let patRecalls=[],lookups=null;

registerODModule({
  id:"sched-recall-asap",
  label:"Recall / ASAP",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Recall &amp; ASAP</h1>
    <span class="sub">patients due for recall · ASAP list · per-patient recall editing</span></div>
  <div class="chart-tools">
    <span class="seg" id="rcl-tabs">
      <button type="button" data-tab="recall" aria-pressed="true">Recall due</button>
      <button type="button" data-tab="asap" aria-pressed="false">ASAP</button>
      <button type="button" data-tab="patient" aria-pressed="false">Patient recalls</button>
    </span>
    <span class="msg" id="rcl-msg"></span>
  </div>
  <div id="rcl-pane-recall">
    <div class="card">
      <h2>Recall due list</h2>
      <form class="row" id="rcl-filters" style="margin-bottom:10px">
        <label>From <input type="date" id="rcl-from"></label>
        <label>To <input type="date" id="rcl-to"></label>
        <select id="rcl-prov"><option value="0">All providers</option></select>
        <select id="rcl-sort">
          <option value="DueDate">Sort: due date</option>
          <option value="Alphabetical">Sort: name</option>
          <option value="BillingType">Sort: billing type</option>
        </select>
        <label><input type="checkbox" id="rcl-fam"> Group by families</label>
        <button class="action">Refresh</button>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="rcl-grid">
        <thead><tr><th>Due</th><th>Patient</th><th>Age</th><th>Type</th><th>Interval</th>
        <th>Contact</th><th>Status</th><th>Reminders</th><th>Last reminder</th><th>Note</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>
  <div id="rcl-pane-asap" hidden>
    <div class="card">
      <h2>ASAP list — flagged appointments</h2>
      <form class="row" id="asap-filters" style="margin-bottom:10px">
        <select id="asap-prov"><option value="0">All providers</option></select>
        <select id="asap-status">
          <option value="">Any status (default set)</option>
          <option value="Scheduled">Scheduled</option>
          <option value="UnschedList">Unscheduled</option>
          <option value="Broken">Broken</option>
          <option value="Planned">Planned</option>
        </select>
        <button class="action">Refresh</button>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="asap-grid">
        <thead><tr><th>Patient</th><th>Phone</th><th>Scheduled for</th><th>Status</th>
        <th>Prov</th><th>Min</th><th>Procedures</th><th>Note</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
    <div class="card" style="margin-top:12px">
      <h2>ASAP list — recalls flagged ASAP</h2>
      <div class="gridwrap scrolly"><table class="grid" id="asap-recall-grid">
        <thead><tr><th>Due</th><th>Patient</th><th>Age</th><th>Type</th>
        <th>Contact</th><th>Note</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>
  <div id="rcl-pane-patient" hidden>
    <div style="display:flex;gap:14px;align-items:center;margin-bottom:12px">
      <button type="button" class="action" id="pre-synch">Synch due dates</button>
      <button type="button" class="quiet" id="pre-reload">Refresh</button>
      <span class="msg" id="pre-msg"></span>
    </div>
    <div class="card"><h2>Patient recalls</h2>
      <div class="gridwrap scrolly"><table class="grid" id="pre-grid"><thead>
        <tr><th>Type</th><th>Due</th><th>Previous</th><th>Scheduled</th><th>Interval</th><th>Status</th><th>Note</th><th>Disabled</th></tr>
      </thead><tbody></tbody></table></div>
      <form id="pre-form">
        <input type="hidden" name="recallNum" value="0">
        <select name="recallTypeNum" style="max-width:170px"><option value="0">— recall type —</option></select>
        <input name="dateDue" type="date" title="Due date">
        <span style="display:inline-flex;gap:4px;align-items:center;font-size:12px">
          <input name="intervalYears" type="number" min="0" max="127" value="0" style="width:52px" title="Years">y
          <input name="intervalMonths" type="number" min="0" max="255" value="0" style="width:52px" title="Months">m
          <input name="intervalWeeks" type="number" min="0" max="255" value="0" style="width:52px" title="Weeks">w
          <input name="intervalDays" type="number" min="0" max="255" value="0" style="width:52px" title="Days">d
        </span>
        <select name="recallStatus" style="max-width:150px"><option value="0">— status —</option></select>
        <input name="note" placeholder="Administrative note" style="max-width:200px">
        <label style="font-size:12px;align-self:center" title="ASAP priority"><input type="checkbox" name="priorityAsap"> ASAP</label>
        <label style="font-size:12px;align-self:center"><input type="checkbox" name="isDisabled"> Disabled</label>
        <input name="disableUntilDate" type="date" title="Disable until date">
        <input name="disableUntilBalance" type="number" step="0.01" min="0" placeholder="Disable until balance &lt;" style="width:150px" title="Disable until family balance below">
        <button class="action" id="pre-save">Add recall</button>
        <button type="button" class="quiet" id="pre-new" hidden>New</button>
        <span class="msg" id="pre-formmsg"></span>
      </form>
    </div>
  </div>`,
  init(){
    const msg=t=>{$("rcl-msg").textContent=t;};
    const iso=d=>d.toISOString().slice(0,10);
    const from=new Date();from.setFullYear(from.getFullYear()-3);
    $("rcl-from").value=iso(from);
    $("rcl-to").value=iso(new Date());

    /* tabs */
    document.querySelectorAll("#rcl-tabs button").forEach(btn=>{
      btn.addEventListener("click",()=>{
        document.querySelectorAll("#rcl-tabs button").forEach(b=>b.setAttribute("aria-pressed",b===btn));
        $("rcl-pane-recall").hidden=btn.dataset.tab!=="recall";
        $("rcl-pane-asap").hidden=btn.dataset.tab!=="asap";
        $("rcl-pane-patient").hidden=btn.dataset.tab!=="patient";
        if(btn.dataset.tab==="asap")loadAsap();
        else if(btn.dataset.tab==="patient")loadPatRecalls();
        else loadRecalls();
      });
    });

    async function loadProviders(){
      try{
        const provs=await api("/api/setup/providers");
        const opts=provs.filter(p=>!p.isHidden)
          .map(p=>`<option value="${p.provNum}">${esc(p.abbr||p.lName)}</option>`).join("");
        $("rcl-prov").insertAdjacentHTML("beforeend",opts);
        $("asap-prov").insertAdjacentHTML("beforeend",opts);
      }catch(e){/* provider filter degrades to "all" */}
    }

    function bookCell(patNum,patient){
      return `<td><button type="button" class="quiet" data-book="${patNum}" data-name="${esc(patient)}">Book</button></td>`;
    }
    function wireBook(tbody){
      tbody.querySelectorAll("button[data-book]").forEach(btn=>{
        btn.addEventListener("click",async()=>{
          const when=prompt(`Book ${btn.dataset.name} — date & time (YYYY-MM-DD HH:MM):`);
          if(!when)return;
          try{
            await api("/api/appointments",{method:"POST",headers:{"Content-Type":"application/json"},
              body:JSON.stringify({patNum:Number(btn.dataset.book),dateTime:when,minutes:60,note:"Booked from Recall/ASAP list"})});
            msg("Appointment booked for "+btn.dataset.name+".");
          }catch(e){msg("Booking failed: "+e.message);}
        });
      });
    }

    async function loadRecalls(){
      msg("Loading recalls…");
      try{
        const q=new URLSearchParams({from:$("rcl-from").value,to:$("rcl-to").value,
          provNum:$("rcl-prov").value,sortBy:$("rcl-sort").value,
          groupByFamilies:$("rcl-fam").checked});
        const rows=await api("/api/recalls?"+q);
        const tbody=document.querySelector("#rcl-grid tbody");
        tbody.innerHTML=rows.map(r=>
          `<tr><td class="mono">${esc(r.dueDate)}</td><td>${esc(r.patient)}</td><td class="num">${esc(r.age)}</td>`+
          `<td>${esc(r.recallType)}</td><td>${esc(r.interval)}</td><td>${esc(r.contactMethod)}</td>`+
          `<td>${esc(r.status)}</td><td class="num">${esc(r.numberOfReminders)}</td>`+
          `<td class="mono">${esc(r.dateLastReminder)}</td><td>${esc(r.note)}</td>${bookCell(r.patNum,r.patient)}</tr>`).join("")
          ||`<tr><td colspan="11"><div class="empty"><b>No recalls due</b>in this date range with these filters.</div></td></tr>`;
        wireBook(tbody);
        msg(rows.length+" recall"+(rows.length===1?"":"s")+" due.");
      }catch(e){msg("Recall list unavailable: "+e.message);}
    }

    async function loadAsap(){
      msg("Loading ASAP lists…");
      try{
        const q=new URLSearchParams({provNum:$("asap-prov").value,statuses:$("asap-status").value});
        const [appts,recalls]=await Promise.all([
          api("/api/asap?"+q),
          api("/api/recalls?"+new URLSearchParams({provNum:$("asap-prov").value,asap:true,
            from:iso(from),to:iso(new Date(Date.now()+366*24*3600*1000))})),
        ]);
        const tbody=document.querySelector("#asap-grid tbody");
        tbody.innerHTML=appts.map(a=>
          `<tr><td>${esc(a.patient)}</td><td class="mono">${esc(a.phone)}</td>`+
          `<td class="mono">${esc(a.dateTime)}</td><td><span class="tag">${esc(a.status)}</span></td>`+
          `<td>${esc(a.provAbbr)}</td><td class="num">${a.minutes}</td>`+
          `<td>${esc(a.procDescript)}</td><td>${esc(a.note)}</td>${bookCell(a.patNum,a.patient)}</tr>`).join("")
          ||`<tr><td colspan="9"><div class="empty"><b>No ASAP appointments</b>Nothing is flagged ASAP right now.</div></td></tr>`;
        wireBook(tbody);
        const tbody2=document.querySelector("#asap-recall-grid tbody");
        tbody2.innerHTML=recalls.map(r=>
          `<tr><td class="mono">${esc(r.dueDate)}</td><td>${esc(r.patient)}</td><td class="num">${esc(r.age)}</td>`+
          `<td>${esc(r.recallType)}</td><td>${esc(r.contactMethod)}</td><td>${esc(r.note)}</td>${bookCell(r.patNum,r.patient)}</tr>`).join("")
          ||`<tr><td colspan="7"><div class="empty"><b>No ASAP recalls</b>No recalls are flagged ASAP.</div></td></tr>`;
        wireBook(tbody2);
        msg(appts.length+" ASAP appointment"+(appts.length===1?"":"s")+", "+recalls.length+" ASAP recall"+(recalls.length===1?"":"s")+".");
      }catch(e){msg("ASAP list unavailable: "+e.message);}
    }

    $("rcl-filters").addEventListener("submit",e=>{e.preventDefault();loadRecalls();});
    $("asap-filters").addEventListener("submit",e=>{e.preventDefault();loadAsap();});
    $("pre-reload").addEventListener("click",loadPatRecalls);
    $("pre-synch").addEventListener("click",synchPatRecalls);
    $("band-patient").addEventListener("change",()=>{
      if(!$("rcl-pane-patient").hidden)setTimeout(loadPatRecalls,0);
    });
    wirePatForm();
    loadProviders();
    loadRecalls();
  },
});

/* ---------- per-patient recall editor (former patfam-recall-edit) ---------- */
function pat(){return window.ODM.getActivePatient()}

async function loadPatRecalls(){
  const p=pat();
  if(!p){$("pre-msg").textContent="Select a patient first.";return}
  try{
    if(!lookups)lookups=await api("/api/recall-types");
    patRecalls=await api(`/api/patients/${p.patNum}/recalls`);
    $("pre-msg").textContent=p.name;
  }catch(e){$("pre-msg").textContent="recalls endpoint unavailable — "+e.message.slice(0,60);return}
  renderPatRecalls();
}

async function synchPatRecalls(){
  const p=pat();if(!p)return;
  $("pre-msg").textContent="Synching…";
  try{
    await api(`/api/patients/${p.patNum}/recalls/synch`,{method:"POST"});
    await loadPatRecalls();
    $("pre-msg").textContent="Synched — due dates re-derived.";
  }catch(e){$("pre-msg").textContent="Synch failed: "+e.message.slice(0,60)}
}

function renderPatRecalls(){
  $("pre-grid").querySelector("tbody").innerHTML=patRecalls.map(r=>
    `<tr class="rowsel" data-recall="${r.recallNum}"${r.isDisabled?' style="opacity:.55"':""}>`+
    `<td>${esc(r.recallType)}</td><td class="mono">${r.dateDue}</td><td class="mono">${r.datePrevious}</td>`+
    `<td class="mono">${r.dateScheduled}</td><td class="mono">${esc(r.interval)}</td>`+
    `<td>${r.statusName?`<span class="tag">${esc(r.statusName)}</span>`:""}${r.priority==="ASAP"?' <span class="tag">ASAP</span>':""}</td>`+
    `<td>${esc(r.note)}</td>`+
    `<td>${r.isDisabled?"Yes":""}${r.disableUntilDate?` until ${r.disableUntilDate}`:""}${r.disableUntilBalance>0?` bal&lt;${r.disableUntilBalance}`:""}</td></tr>`).join("")
    ||`<tr><td colspan="8"><div class="empty"><b>No recalls</b>Synch to derive from trigger procedures, or add one below.</div></td></tr>`;
  const f=$("pre-form");
  fillPicker(f.elements.recallTypeNum,"— recall type —",
    lookups.types.map(x=>[x.recallTypeNum,x.description+(x.defaultInterval?` (${x.defaultInterval})`:"")]));
  fillPicker(f.elements.recallStatus,"— status —",
    lookups.statuses.map(x=>[x.defNum,x.name]));
  wirePatRows();
}

function fillPicker(sel,placeholder,pairs){
  const cur=sel.value;
  sel.innerHTML=`<option value="0">${placeholder}</option>`+
    pairs.map(([v,t])=>`<option value="${v}">${esc(t)}</option>`).join("");
  sel.value=cur&&[...sel.options].some(o=>o.value===cur)?cur:"0";
}

function wirePatRows(){
  document.querySelectorAll("#pre-grid tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
    const r=patRecalls.find(x=>x.recallNum==tr.dataset.recall);
    const f=$("pre-form");
    f.elements.recallNum.value=r.recallNum;
    f.elements.recallTypeNum.value=r.recallTypeNum;
    if(f.elements.recallTypeNum.value!==String(r.recallTypeNum)){
      /* hidden type not in picker: inject so the edit round-trips unchanged */
      f.elements.recallTypeNum.insertAdjacentHTML("beforeend",
        `<option value="${r.recallTypeNum}">${esc(r.recallType)}</option>`);
      f.elements.recallTypeNum.value=r.recallTypeNum;
    }
    f.elements.dateDue.value=r.dateDue;
    f.elements.intervalYears.value=r.intervalYears;
    f.elements.intervalMonths.value=r.intervalMonths;
    f.elements.intervalWeeks.value=r.intervalWeeks;
    f.elements.intervalDays.value=r.intervalDays;
    f.elements.recallStatus.value=r.recallStatus;
    f.elements.note.value=r.note;
    f.elements.priorityAsap.checked=r.priority==="ASAP";
    f.elements.isDisabled.checked=r.isDisabled;
    f.elements.disableUntilDate.value=r.disableUntilDate;
    f.elements.disableUntilBalance.value=r.disableUntilBalance>0?r.disableUntilBalance:"";
    $("pre-save").textContent="Save recall";$("pre-new").hidden=false;
  }));
}

function resetPatForm(){
  const f=$("pre-form");f.reset();
  f.elements.recallNum.value="0";
  $("pre-save").textContent="Add recall";$("pre-new").hidden=true;
}

function wirePatForm(){
  $("pre-new").addEventListener("click",resetPatForm);
  $("pre-form").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target,p=pat();if(!p)return;
    const editing=+f.elements.recallNum.value;
    $("pre-formmsg").textContent="Saving…";
    const body={
      recallTypeNum:+f.elements.recallTypeNum.value,
      dateDue:f.elements.dateDue.value,
      intervalYears:+f.elements.intervalYears.value||0,
      intervalMonths:+f.elements.intervalMonths.value||0,
      intervalWeeks:+f.elements.intervalWeeks.value||0,
      intervalDays:+f.elements.intervalDays.value||0,
      recallStatus:+f.elements.recallStatus.value,
      note:f.elements.note.value,
      isDisabled:f.elements.isDisabled.checked,
      disableUntilDate:f.elements.disableUntilDate.value,
      disableUntilBalance:+f.elements.disableUntilBalance.value||0,
      priority:f.elements.priorityAsap.checked?"ASAP":"Normal"};
    try{
      if(editing){
        await api(`/api/recalls/${editing}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        await api(`/api/patients/${p.patNum}/recalls`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      $("pre-formmsg").textContent="Saved.";resetPatForm();loadPatRecalls();
    }catch(err){$("pre-formmsg").textContent="Failed: "+err.message.slice(0,80)}
  });
}
})();
