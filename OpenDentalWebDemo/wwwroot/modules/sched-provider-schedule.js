/* Provider Hours — retires FormSchedule's provider-schedule day editing
   (FormScheduleDayEdit + FormScheduleEdit, Provider rows). Day view of one
   provider's ScheduleType.Provider blocks (start/stop, Open/Closed/Holiday,
   operatory assignment via ScheduleOps). GET /api/provider-schedules lists the
   day's rows plus the operatory columns; create/edit/delete via
   POST/PUT/DELETE /api/provider-schedules. Overlap + time validation come back
   from the business layer as 400/409 messages; a 409 overlap is a soft warning
   the desktop lets you override, so the form offers "save again to continue".
   Seeding hours here is what gives Slot Search real Open provider time. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

const STATUS=[{v:0,name:"Open"},{v:1,name:"Closed"},{v:2,name:"Holiday"}];

registerODModule({
  id:"sched-provider-schedule",
  label:"Provider Hours",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Provider Hours</h1>
    <span class="sub">provider working hours per day &middot; operatory assignment feeds Slot Search</span></div>
  <div class="chart-tools">
    <button type="button" class="quiet" id="ph-prev">&larr;</button>
    <input type="date" id="ph-date">
    <button type="button" class="quiet" id="ph-next">&rarr;</button>
    <button type="button" class="quiet" id="ph-today">Today</button>
    <label>Provider <select id="ph-prov"></select></label>
    <span class="msg" id="ph-msg"></span>
  </div>
  <div class="card">
    <h2>Schedule blocks on this day</h2>
    <div class="gridwrap scrolly"><table class="grid" id="ph-grid">
      <thead><tr><th>Provider</th><th>Start</th><th>Stop</th><th>Status</th><th>Operatories</th><th></th><th></th></tr></thead>
      <tbody></tbody></table></div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2 id="ph-form-title">New schedule block</h2>
    <form class="row" id="ph-form">
      <label>Start <input type="time" id="ph-start" value="08:00" required></label>
      <label>Stop <input type="time" id="ph-stop" value="17:00" required></label>
      <label>Status <select id="ph-status"></select></label>
      <label>Operatories <select id="ph-ops" multiple size="4" style="min-width:150px"></select></label>
      <button class="action" id="ph-save">Create</button>
      <button type="button" class="quiet" id="ph-cancel" hidden>Cancel edit</button>
    </form>
    <div class="sub" style="font-size:12px;margin-top:6px">Leave operatories unselected for a dynamic block (Slot Search falls back to the provider's assigned operatories).</div>
  </div>`,
  init(){
    const msg=t=>{$("ph-msg").textContent=t;};
    const iso=d=>d.toISOString().slice(0,10);
    let day={ops:[],schedules:[]};
    let provs=[];
    let editNum=0;//scheduleNum being edited, 0 = create mode
    $("ph-date").value=iso(new Date());
    $("ph-status").innerHTML=STATUS.map(s=>`<option value="${s.v}">${esc(s.name)}</option>`).join("");

    function opName(opNum){
      const op=day.ops.find(o=>o.opNum===opNum);
      return op?op.name:("#"+opNum);
    }
    function statusName(v){
      const s=STATUS.find(x=>x.v===v);
      return s?s.name:String(v);
    }

    function renderProvFilter(){
      const cur=$("ph-prov").value;
      $("ph-prov").innerHTML=`<option value="0">All providers</option>`+
        provs.filter(p=>!p.isHidden).map(p=>`<option value="${p.provNum}">${esc(p.abbr||("#"+p.provNum))}</option>`).join("");
      $("ph-prov").value=[...$("ph-prov").options].some(o=>o.value===cur)?cur:"0";
    }

    function renderOpsPicker(){
      const selected=[...$("ph-ops").selectedOptions].map(o=>Number(o.value));
      $("ph-ops").innerHTML=day.ops.map(o=>`<option value="${o.opNum}">${esc(o.name)}</option>`).join("")
        ||`<option value="" disabled>(no operatories)</option>`;
      [...$("ph-ops").options].forEach(o=>{o.selected=selected.includes(Number(o.value));});
    }

    function renderGrid(){
      const tbody=document.querySelector("#ph-grid tbody");
      tbody.innerHTML=day.schedules.map(s=>
        `<tr><td>${esc(s.provAbbr||("#"+s.provNum))}</td>`+
        `<td class="mono">${s.startTime}</td><td class="mono">${s.stopTime}</td>`+
        `<td>${esc(s.statusName)}</td>`+
        `<td>${s.ops.length?s.ops.map(opName).map(esc).join(", "):'<span class="sub">(dynamic)</span>'}</td>`+
        `<td><button type="button" class="quiet" data-edit="${s.scheduleNum}">Edit</button></td>`+
        `<td><button type="button" class="quiet" data-del="${s.scheduleNum}">Delete</button></td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No schedule blocks</b>This provider has no working hours on this day.</div></td></tr>`;
      tbody.querySelectorAll("button[data-edit]").forEach(btn=>btn.addEventListener("click",()=>startEdit(Number(btn.dataset.edit))));
      tbody.querySelectorAll("button[data-del]").forEach(btn=>btn.addEventListener("click",async()=>{
        if(!confirm("Delete this schedule block?"))return;
        try{
          await api(`/api/provider-schedules/${btn.dataset.del}?date=${$("ph-date").value}`,{method:"DELETE"});
          msg("Schedule block deleted (security-logged).");
          if(editNum===Number(btn.dataset.del))resetForm();
          load();
        }catch(e){msg("Delete failed: "+e.message);}
      }));
    }

    function startEdit(scheduleNum){
      const s=day.schedules.find(x=>x.scheduleNum===scheduleNum);
      if(!s)return;
      editNum=scheduleNum;
      $("ph-form-title").textContent="Edit block "+(s.provAbbr||("#"+s.provNum))+" "+s.startTime+"–"+s.stopTime;
      $("ph-save").textContent="Save";
      $("ph-cancel").hidden=false;
      $("ph-start").value=s.startTime;
      $("ph-stop").value=s.stopTime;
      $("ph-status").value=String(s.status);
      $("ph-prov").value=String(s.provNum);
      [...$("ph-ops").options].forEach(o=>{o.selected=s.ops.includes(Number(o.value));});
    }

    function resetForm(){
      editNum=0;
      $("ph-form-title").textContent="New schedule block";
      $("ph-save").textContent="Create";
      $("ph-cancel").hidden=true;
      $("ph-start").value="08:00";
      $("ph-stop").value="17:00";
      $("ph-status").value="0";
      [...$("ph-ops").options].forEach(o=>{o.selected=false;});
    }

    async function load(){
      msg("Loading…");
      try{
        const prov=$("ph-prov").value||"0";
        day=await api(`/api/provider-schedules?date=${$("ph-date").value}&provNum=${prov}`);
        renderOpsPicker();renderGrid();
        msg(day.schedules.length+" schedule block"+(day.schedules.length===1?"":"s")+" on "+day.date+".");
      }catch(e){msg("Provider hours unavailable: "+e.message);}
    }

    async function save(force){
      const provNum=Number($("ph-prov").value);
      if(!editNum && provNum<=0){
        msg("Pick a specific provider (not “All”) before creating a block.");
        return;
      }
      const body=JSON.stringify({
        date:$("ph-date").value,
        provNum,
        startTime:$("ph-start").value,
        stopTime:$("ph-stop").value,
        status:Number($("ph-status").value),
        ops:[...$("ph-ops").selectedOptions].map(o=>Number(o.value)),
        force:!!force,
      });
      msg("Saving…");
      try{
        if(editNum)await api(`/api/provider-schedules/${editNum}`,{method:"PUT",headers:{"Content-Type":"application/json"},body});
        else await api("/api/provider-schedules",{method:"POST",headers:{"Content-Type":"application/json"},body});
        msg(editNum?"Schedule block updated (security-logged).":"Schedule block created (security-logged).");
        resetForm();load();
      }catch(e){
        if(/Overlapping provider schedules/.test(e.message) && confirm(e.message+"\n\nSave anyway?")){
          save(true);
          return;
        }
        msg("Save failed: "+e.message);
      }
    }

    $("ph-form").addEventListener("submit",e=>{e.preventDefault();save(false);});
    $("ph-cancel").addEventListener("click",resetForm);

    function shift(days){
      const dt=new Date($("ph-date").value+"T00:00:00");
      dt.setDate(dt.getDate()+days);
      $("ph-date").value=iso(dt);
      resetForm();load();
    }
    $("ph-prev").addEventListener("click",()=>shift(-1));
    $("ph-next").addEventListener("click",()=>shift(1));
    $("ph-today").addEventListener("click",()=>{$("ph-date").value=iso(new Date());resetForm();load();});
    $("ph-date").addEventListener("change",()=>{resetForm();load();});
    $("ph-prov").addEventListener("change",()=>{resetForm();load();});

    (async function boot(){
      try{
        provs=await api("/api/setup/providers");
        renderProvFilter();
      }catch(e){msg("Could not load providers: "+e.message);}
      load();
    })();
  },
});
})();
