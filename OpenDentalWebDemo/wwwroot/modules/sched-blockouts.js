/* Blockouts — retires FormScheduleBlockEdit and the cut/clear half of
   FormBlockoutCutCopyPaste. Day view of ScheduleType.Blockout rows as colored
   bands per operatory (GET /api/blockouts = Schedules.RefreshPeriodBlockouts),
   create/edit/delete via POST/PUT/DELETE /api/blockouts, bulk clear-day /
   clear-op via POST /api/blockouts/clear. Overlap + time validation come back
   from the business layer as 400/409 messages. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

registerODModule({
  id:"sched-blockouts",
  label:"Blockouts",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Blockouts</h1>
    <span class="sub">schedule blockouts per operatory · types &amp; colors from Definitions</span></div>
  <div class="chart-tools">
    <button type="button" class="quiet" id="blk-prev">&larr;</button>
    <input type="date" id="blk-date">
    <button type="button" class="quiet" id="blk-next">&rarr;</button>
    <button type="button" class="quiet" id="blk-today">Today</button>
    <span class="msg" id="blk-msg"></span>
  </div>
  <div class="card">
    <h2>Day view</h2>
    <div class="gridwrap"><div id="blk-band" style="display:grid;gap:2px;min-width:520px"></div></div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2>Blockouts on this day</h2>
    <div class="gridwrap scrolly"><table class="grid" id="blk-grid">
      <thead><tr><th>Start</th><th>Stop</th><th>Type</th><th>Operatories</th><th>Note</th><th></th><th></th></tr></thead>
      <tbody></tbody></table></div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2 id="blk-form-title">New blockout</h2>
    <form class="row" id="blk-form">
      <label>Start <input type="time" id="blk-start" value="08:00" required></label>
      <label>Stop <input type="time" id="blk-stop" value="09:00" required></label>
      <select id="blk-type" required></select>
      <select id="blk-ops" multiple size="4" style="min-width:140px" required></select>
      <input id="blk-note" placeholder="Note" style="flex:1;min-width:160px">
      <button class="action" id="blk-save">Create</button>
      <button type="button" class="quiet" id="blk-cancel" hidden>Cancel edit</button>
    </form>
  </div>
  <div class="card" style="margin-top:12px">
    <h2>Clear blockouts</h2>
    <div class="row" style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
      <select id="blk-clear-op"><option value="0">Entire day (all operatories)</option></select>
      <button type="button" class="quiet" id="blk-clear">Clear</button>
      <span class="sub" style="font-size:12px">Deletes every blockout for the selection — same as the desktop Clear commands.</span>
    </div>
  </div>`,
  init(){
    const msg=t=>{$("blk-msg").textContent=t;};
    const iso=d=>d.toISOString().slice(0,10);
    const DAYSTART=7*60,DAYEND=19*60,H=560,PX=H/(DAYEND-DAYSTART);
    let day={ops:[],types:[],blockouts:[]};
    let editNum=0;//scheduleNum being edited, 0 = create mode
    $("blk-date").value=iso(new Date());

    function opName(opNum){
      const op=day.ops.find(o=>o.opNum===opNum);
      return op?op.name:("#"+opNum);
    }

    function renderBand(){
      const host=$("blk-band");
      host.style.gridTemplateColumns="44px repeat("+Math.max(1,day.ops.length)+",1fr)";
      let s=`<div></div>`+day.ops.map(o=>`<div style="text-align:center;font-size:12px;font-weight:600;padding:3px 0">${esc(o.name)}</div>`).join("");
      s+=`<div style="position:relative;height:${H}px">`;
      for(let m=DAYSTART;m<=DAYEND;m+=60)
        s+=`<span style="position:absolute;right:5px;top:${(m-DAYSTART)*PX}px;transform:translateY(-50%);font-size:10px;opacity:.55">${String(m/60).padStart(2,"0")}:00</span>`;
      s+=`</div>`;
      for(const o of day.ops){
        s+=`<div style="position:relative;height:${H}px;border:1px solid var(--line);border-radius:4px">`;
        for(let m=DAYSTART;m<=DAYEND;m+=60)
          s+=`<div style="position:absolute;left:0;right:0;top:${(m-DAYSTART)*PX}px;border-top:1px solid var(--line)"></div>`;
        for(const b of day.blockouts.filter(b=>b.ops.includes(o.opNum))){
          const top=Math.max(0,(b.startMinutes-DAYSTART)*PX);
          const h=Math.max(14,(b.stopMinutes-b.startMinutes)*PX);
          const bg=b.color||"var(--teal-soft)";
          s+=`<div data-edit="${b.scheduleNum}" title="${esc(b.typeName)} ${b.startTime}–${b.stopTime}${b.note?" — "+esc(b.note):""}"`+
            ` style="position:absolute;left:2px;right:2px;top:${top}px;height:${h}px;border-radius:4px;cursor:pointer;`+
            `background:color-mix(in srgb,${bg} 45%,transparent);border:1px solid ${bg};`+
            `font-size:10.5px;padding:1px 4px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis">${esc(b.typeName)}</div>`;
        }
        s+=`</div>`;
      }
      host.innerHTML=s;
      host.querySelectorAll("[data-edit]").forEach(el=>el.addEventListener("click",()=>startEdit(Number(el.dataset.edit))));
    }

    function renderGrid(){
      const tbody=document.querySelector("#blk-grid tbody");
      tbody.innerHTML=day.blockouts.map(b=>
        `<tr><td class="mono">${b.startTime}</td><td class="mono">${b.stopTime}</td>`+
        `<td><span class="tag" style="border-color:${b.color||"var(--line-strong)"}">${esc(b.typeName)}</span></td>`+
        `<td>${b.ops.map(opName).map(esc).join(", ")}</td><td>${esc(b.note||"")}</td>`+
        `<td><button type="button" class="quiet" data-edit="${b.scheduleNum}">Edit</button></td>`+
        `<td><button type="button" class="quiet" data-del="${b.scheduleNum}">Delete</button></td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No blockouts</b>Nothing is blocked out on this day.</div></td></tr>`;
      tbody.querySelectorAll("button[data-edit]").forEach(btn=>btn.addEventListener("click",()=>startEdit(Number(btn.dataset.edit))));
      tbody.querySelectorAll("button[data-del]").forEach(btn=>btn.addEventListener("click",async()=>{
        if(!confirm("Delete this blockout?"))return;
        try{
          await api(`/api/blockouts/${btn.dataset.del}?date=${$("blk-date").value}`,{method:"DELETE"});
          msg("Blockout deleted (security-logged).");
          if(editNum===Number(btn.dataset.del))resetForm();
          load();
        }catch(e){msg("Delete failed: "+e.message);}
      }));
    }

    function renderPickers(){
      $("blk-type").innerHTML=day.types.map(t=>
        `<option value="${t.defNum}">${esc(t.name)}${t.isAppointmentBlocking?" (blocks scheduling)":""}</option>`).join("")
        ||`<option value="0">(no blockout types defined)</option>`;
      $("blk-ops").innerHTML=day.ops.map(o=>`<option value="${o.opNum}">${esc(o.name)}</option>`).join("");
      const cur=$("blk-clear-op").value;
      $("blk-clear-op").innerHTML=`<option value="0">Entire day (all operatories)</option>`+
        day.ops.map(o=>`<option value="${o.opNum}">Operatory: ${esc(o.name)}</option>`).join("");
      $("blk-clear-op").value=[...$("blk-clear-op").options].some(x=>x.value===cur)?cur:"0";
    }

    function startEdit(scheduleNum){
      const b=day.blockouts.find(x=>x.scheduleNum===scheduleNum);
      if(!b)return;
      editNum=scheduleNum;
      $("blk-form-title").textContent="Edit blockout "+b.startTime+"–"+b.stopTime;
      $("blk-save").textContent="Save";
      $("blk-cancel").hidden=false;
      $("blk-start").value=b.startTime;
      $("blk-stop").value=b.stopTime;
      $("blk-type").value=String(b.blockoutType);
      $("blk-note").value=b.note||"";
      [...$("blk-ops").options].forEach(o=>{o.selected=b.ops.includes(Number(o.value));});
    }

    function resetForm(){
      editNum=0;
      $("blk-form-title").textContent="New blockout";
      $("blk-save").textContent="Create";
      $("blk-cancel").hidden=true;
      $("blk-note").value="";
      [...$("blk-ops").options].forEach(o=>{o.selected=false;});
    }

    async function load(){
      msg("Loading…");
      try{
        day=await api("/api/blockouts?date="+$("blk-date").value);
        renderPickers();renderBand();renderGrid();
        msg(day.blockouts.length+" blockout"+(day.blockouts.length===1?"":"s")+" on "+day.date+".");
      }catch(e){msg("Blockouts unavailable: "+e.message);}
    }

    $("blk-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const body=JSON.stringify({
        date:$("blk-date").value,
        startTime:$("blk-start").value,
        stopTime:$("blk-stop").value,
        blockoutType:Number($("blk-type").value),
        note:$("blk-note").value,
        ops:[...$("blk-ops").selectedOptions].map(o=>Number(o.value)),
        clinicNum:0,
      });
      msg("Saving…");
      try{
        if(editNum)await api(`/api/blockouts/${editNum}`,{method:"PUT",headers:{"Content-Type":"application/json"},body});
        else await api("/api/blockouts",{method:"POST",headers:{"Content-Type":"application/json"},body});
        msg(editNum?"Blockout updated (security-logged).":"Blockout created (security-logged).");
        resetForm();load();
      }catch(e2){msg("Save failed: "+e2.message);}
    });
    $("blk-cancel").addEventListener("click",resetForm);

    $("blk-clear").addEventListener("click",async()=>{
      const opNum=Number($("blk-clear-op").value);
      const what=opNum?$("blk-clear-op").selectedOptions[0].textContent:"the entire day";
      if(!confirm(`Clear all blockouts for ${what} on ${$("blk-date").value}?`))return;
      msg("Clearing…");
      try{
        await api("/api/blockouts/clear",{method:"POST",headers:{"Content-Type":"application/json"},
          body:JSON.stringify({date:$("blk-date").value,opNum})});
        msg("Blockouts cleared (security-logged).");
        load();
      }catch(e){msg("Clear failed: "+e.message);}
    });

    function shift(days){
      const dt=new Date($("blk-date").value+"T00:00:00");
      dt.setDate(dt.getDate()+days);
      $("blk-date").value=iso(dt);
      resetForm();load();
    }
    $("blk-prev").addEventListener("click",()=>shift(-1));
    $("blk-next").addEventListener("click",()=>shift(1));
    $("blk-today").addEventListener("click",()=>{$("blk-date").value=iso(new Date());resetForm();load();});
    $("blk-date").addEventListener("change",()=>{resetForm();load();});
    load();
  },
});
})();
