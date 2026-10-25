/* Slot Search — web replacement for FormApptSearch ("Search for Appointment").
   GET /api/slotsearch computes open 5-minute-grid slots from the same reads the
   desktop search makes (Schedules.GetSchedulesForAppointmentSearch +
   Appointments.GetForPeriodList), inside provider schedules only, skipping
   existing appointments and appointment-blocking blockouts.  Clicking a result
   row only highlights the candidate — booking it is a later tranche. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

registerODModule({
  id:"sched-slot-search",
  label:"Slot Search",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Slot Search</h1>
    <span class="sub">next open appointment times · provider schedules minus appointments &amp; blocking blockouts</span></div>
  <div class="card">
    <h2>Criteria</h2>
    <form class="row" id="sls-form">
      <select id="sls-provs" multiple size="5" style="min-width:160px" title="Providers — none selected means all"></select>
      <label>Length <select id="sls-min"></select></label>
      <label>From <input type="date" id="sls-from" required></label>
      <label>Days <input type="number" id="sls-days" value="14" min="1" max="60" style="width:64px"></label>
      <label>After <input type="time" id="sls-after"></label>
      <label>Before <input type="time" id="sls-before"></label>
      <button class="action" id="sls-search">Search</button>
      <span class="msg" id="sls-msg"></span>
    </form>
    <div class="sub" style="font-size:12px;margin-top:6px">Leave the provider list unselected to search every non-hidden provider.</div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2>Open slots</h2>
    <div class="gridwrap scrolly"><table class="grid" id="sls-grid">
      <thead><tr><th>Date</th><th>Day</th><th>Start</th><th>Stop</th><th>Provider</th><th>Operatory</th></tr></thead>
      <tbody></tbody></table></div>
  </div>`,
  init(){
    const msg=t=>{$("sls-msg").textContent=t;};
    const iso=d=>d.toISOString().slice(0,10);
    const tomorrow=new Date();
    tomorrow.setDate(tomorrow.getDate()+1);
    $("sls-from").value=iso(tomorrow);
    $("sls-min").innerHTML=Array.from({length:12},(_,i)=>(i+1)*10)
      .map(m=>`<option value="${m}"${m===60?" selected":""}>${m} min</option>`).join("");

    async function loadProviders(){
      try{
        const provs=await api("/api/setup/providers");
        $("sls-provs").innerHTML=provs.filter(p=>!p.isHidden)
          .map(p=>`<option value="${p.provNum}">${esc(p.abbr||p.lName||("#"+p.provNum))}</option>`).join("")
          ||`<option value="" disabled>(no providers)</option>`;
      }catch(e){msg("Providers unavailable: "+e.message);}
    }

    function renderSlots(res){
      const tbody=document.querySelector("#sls-grid tbody");
      tbody.innerHTML=res.slots.map(s=>
        `<tr class="sls-row" style="cursor:pointer" title="Click to highlight — booking from search is a later tranche">`+
        `<td class="mono">${esc(s.date)}</td><td>${esc(s.dayOfWeek)}</td>`+
        `<td class="mono">${esc(s.startTime)}</td><td class="mono">${esc(s.stopTime)}</td>`+
        `<td>${esc(s.provAbbr)}</td><td>${esc(s.opName)}</td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No open slots</b>Nothing matched — widen the window, shorten the length, or check that the providers have schedules.</div></td></tr>`;
      //Row click just highlights the candidate slot; creating the appointment from a
      //slot (the desktop's pinboard drop) is intentionally not in this tranche.
      tbody.querySelectorAll("tr.sls-row").forEach(tr=>tr.addEventListener("click",()=>{
        tbody.querySelectorAll("tr.sls-row").forEach(x=>{x.style.background="";});
        tr.style.background="color-mix(in srgb,var(--teal-soft) 55%,transparent)";
      }));
    }

    $("sls-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const provs=[...$("sls-provs").selectedOptions].map(o=>o.value).filter(Boolean).join(",");
      const q=new URLSearchParams({
        minutes:$("sls-min").value,
        from:$("sls-from").value,
        days:$("sls-days").value||"14",
      });
      if(provs)q.set("providers",provs);
      if($("sls-after").value)q.set("afterTime",$("sls-after").value);
      if($("sls-before").value)q.set("beforeTime",$("sls-before").value);
      msg("Searching…");
      try{
        const res=await api("/api/slotsearch?"+q.toString());
        renderSlots(res);
        msg(res.slots.length+" open slot"+(res.slots.length===1?"":"s")+" between "+res.from+" and "+res.to+".");
      }catch(e2){msg("Search failed: "+e2.message);}
    });
    loadProviders();
  },
});
})();
