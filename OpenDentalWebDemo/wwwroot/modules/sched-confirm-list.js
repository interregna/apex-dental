/* Appointment confirmation list — retires FormConfirmList.
   GET /api/confirmlist (Appointments.GetConfirmList: scheduled + ASAP appts in
   the date range with contact info, plus the ApptConfirmed defs for the status
   dropdown). Picking a status on a row calls
   POST /api/appointments/{aptNum}/confirm — the desktop combo path
   (Appointments.Update + ApptConfirmStatusEdit security log) — and updates the
   row chip in place. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

registerODModule({
  id:"sched-confirm-list",
  label:"Confirmations",
  group:"Schedule",
  html:`
  <div class="mod-head"><h1>Confirmation list</h1>
    <span class="sub">upcoming appointments · contact info · confirmation status</span></div>
  <div class="card">
    <form class="row" id="cl-filters" style="margin-bottom:10px">
      <label>From <input type="date" id="cl-from"></label>
      <label>To <input type="date" id="cl-to"></label>
      <button class="action">Refresh</button>
      <span class="msg" id="cl-msg"></span>
    </form>
    <div class="gridwrap scrolly"><table class="grid" id="cl-grid">
      <thead><tr><th>Date/time</th><th>Patient</th><th>Age</th><th>Contact</th><th>Home</th>
      <th>Cell</th><th>Email</th><th>Prov</th><th>Procedures</th><th>Med notes</th>
      <th>Status</th><th>Set status</th></tr></thead>
      <tbody></tbody></table></div>
  </div>`,
  init(){
    const msg=t=>{$("cl-msg").textContent=t;};
    let defs=[];

    /* default range: today through +7 days, same as the API default */
    const fmt=d=>{
      const p=n=>String(n).padStart(2,"0");
      return d.getFullYear()+"-"+p(d.getMonth()+1)+"-"+p(d.getDate());
    };
    const today=new Date();
    $("cl-from").value=fmt(today);
    $("cl-to").value=fmt(new Date(today.getTime()+7*86400000));

    function chip(name,color){
      const style=color?` style="background:${esc(color)};color:#000"`:"";
      return `<span class="tag"${style}>${esc(name||"None")}</span>`;
    }

    function statusSelect(aptNum,current){
      return `<select data-apt="${aptNum}">`+
        `<option value="">Set status…</option>`+
        defs.map(d=>`<option value="${d.defNum}"${d.defNum===current?" selected":""}>${esc(d.name)}</option>`).join("")+
        `</select>`;
    }

    async function load(){
      msg("Loading confirmation list…");
      try{
        const q=new URLSearchParams({from:$("cl-from").value,to:$("cl-to").value});
        const data=await api("/api/confirmlist?"+q);
        defs=data.defs;
        const tbody=document.querySelector("#cl-grid tbody");
        tbody.innerHTML=data.appointments.map(r=>
          `<tr data-row="${r.aptNum}"><td class="mono">${esc(r.dateTime)}</td>`+
          `<td>${esc(r.patient)}</td><td class="num">${esc(r.age)}</td>`+
          `<td class="mono">${esc(r.contactMethod)}</td><td class="mono">${esc(r.homePhone)}</td>`+
          `<td class="mono">${esc(r.wirelessPhone)}</td><td>${esc(r.email)}</td>`+
          `<td>${esc(r.provAbbr)}</td><td>${esc(r.procDescript)}</td><td>${esc(r.medNotes)}</td>`+
          `<td class="cl-status">${chip(r.confirmed,r.confirmedColor)}</td>`+
          `<td>${statusSelect(r.aptNum,r.confirmedDefNum)}</td></tr>`).join("")
          ||`<tr><td colspan="12"><div class="empty"><b>No appointments</b>Nothing scheduled in this date range.</div></td></tr>`;
        tbody.querySelectorAll("select[data-apt]").forEach(sel=>{
          sel.addEventListener("change",()=>setStatus(sel));
        });
        msg(data.appointments.length+" appointment"+(data.appointments.length===1?"":"s")+".");
      }catch(e){msg("Confirmation list unavailable: "+e.message);}
    }

    async function setStatus(sel){
      const defNum=Number(sel.value);
      if(!defNum)return;
      msg("Updating status…");
      try{
        const r=await api(`/api/appointments/${sel.dataset.apt}/confirm`,{method:"POST",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({defNum})});
        const row=document.querySelector(`#cl-grid tr[data-row="${r.aptNum}"]`);
        if(row)row.querySelector(".cl-status").innerHTML=chip(r.confirmed,r.confirmedColor);
        msg("Appt #"+r.aptNum+" set to "+r.confirmed+".");
      }catch(e){msg("Status update failed: "+e.message);}
    }

    $("cl-filters").addEventListener("submit",e=>{e.preventDefault();load();});
    load();
  },
});
})();
