/* Employees & time clock — roster with add/edit, live clock in/out,
   and a per-employee timecard with inline clock-event correction.
   Replaces FormEmployeeSelect / FormEmployeeEdit / FormTimeCard /
   FormClockEventEdit / FormTimeCardManage over the real business layer. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

registerODModule({
  id:"setup-employees-timeclock",
  label:"Time Clock",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Employees &amp; time clock</h1>
    <span class="sub">roster · clock in/out · timecard review</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>Employees <span class="msg" id="emptc-list-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="emptc-list">
        <thead><tr><th>#</th><th>Name</th><th>Status</th><th>Clock</th><th></th></tr></thead><tbody></tbody>
      </table></div>
      <h2 style="margin-top:14px" id="emptc-form-title">Add employee</h2>
      <form class="row" id="emptc-form">
        <input name="lName" placeholder="Last name" style="width:110px">
        <input name="fName" placeholder="First" style="width:90px">
        <input name="middleI" placeholder="MI" style="width:34px">
        <input name="wirelessPhone" placeholder="Cell phone" style="width:110px">
        <input name="emailWork" placeholder="Work email" style="width:150px">
        <label style="font-size:13px"><input type="checkbox" name="isHidden"> hidden</label>
        <button class="action" id="emptc-save">Add employee</button>
        <button type="button" class="quiet" id="emptc-cancel" hidden>Cancel edit</button>
        <span class="msg" id="emptc-form-msg"></span>
      </form>
    </div>
    <div class="card">
      <h2>Timecard <span class="msg" id="emptc-tc-msg"></span></h2>
      <form class="row" id="emptc-tc-controls" style="margin-bottom:8px">
        <select id="emptc-tc-emp" aria-label="Employee"></select>
        <input type="date" id="emptc-tc-from" aria-label="From">
        <input type="date" id="emptc-tc-to" aria-label="To">
        <button class="action">Load</button>
        <span class="mono" id="emptc-tc-total"></span>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="emptc-tc">
        <thead><tr><th>Date</th><th>In</th><th>Out</th><th>Status</th><th>Hours</th><th>Note</th></tr></thead><tbody></tbody>
      </table></div>
      <h2 style="margin-top:14px">Correct clock event</h2>
      <form class="row" id="emptc-ev-form">
        <input type="datetime-local" name="timeIn" aria-label="Time in" required>
        <input type="datetime-local" name="timeOut" aria-label="Time out (blank = still in)">
        <select name="status"><option>Home</option><option>Lunch</option><option>Break</option></select>
        <input name="note" placeholder="Note" style="width:140px">
        <button class="action">Save correction</button>
        <span class="msg" id="emptc-ev-msg"></span>
      </form>
    </div>
  </div>`,
  init(){
    let employees=[],editingEmp=null,editingEvent=null;

    async function loadEmployees(){
      try{
        employees=await api("/api/setup/employees");
        $("emptc-list").querySelector("tbody").innerHTML=employees.map(e=>
          `<tr class="rowsel" data-emp="${e.employeeNum}"><td class="num">${e.employeeNum}</td>`+
          `<td>${esc(e.lName)}${e.lName&&e.fName?", ":""}${esc(e.fName)}${e.isHidden?' <span class="tag">hidden</span>':""}</td>`+
          `<td><span class="tag">${esc(e.clockStatus||(e.isClockedIn?"Working":"Home"))}</span></td>`+
          `<td>${e.isClockedIn
            ?`<button type="button" class="quiet" data-out="${e.employeeNum}" data-st="Home">Out</button>
              <button type="button" class="quiet" data-out="${e.employeeNum}" data-st="Lunch">Lunch</button>
              <button type="button" class="quiet" data-out="${e.employeeNum}" data-st="Break">Break</button>`
            :`<button type="button" class="action" data-in="${e.employeeNum}">Clock in</button>`}</td>`+
          `<td><button type="button" class="quiet" data-edit="${e.employeeNum}">Edit</button></td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No employees yet</b>Add the first employee below.</div></td></tr>`;
        const sel=$("emptc-tc-emp"),prev=sel.value;
        sel.innerHTML=employees.map(e=>
          `<option value="${e.employeeNum}">${esc((e.fName+" "+e.lName).trim())}</option>`).join("");
        if(prev&&employees.some(e=>e.employeeNum==prev))sel.value=prev;
        $("emptc-list-msg").textContent="";
        wireRoster();
      }catch(err){$("emptc-list-msg").textContent="employees endpoint unavailable — "+err.message.slice(0,60)}
    }

    function wireRoster(){
      const tbl=$("emptc-list");
      tbl.querySelectorAll("[data-in]").forEach(b=>b.addEventListener("click",async()=>{
        $("emptc-list-msg").textContent="Clocking in…";
        try{
          await api(`/api/setup/employees/${b.dataset.in}/clockin`,{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify({isAtHome:false})});
          $("emptc-list-msg").textContent="";loadEmployees();loadTimecard();
        }catch(err){$("emptc-list-msg").textContent="Clock in failed: "+err.message.slice(0,80)}
      }));
      tbl.querySelectorAll("[data-out]").forEach(b=>b.addEventListener("click",async()=>{
        $("emptc-list-msg").textContent="Clocking out…";
        try{
          await api(`/api/setup/employees/${b.dataset.out}/clockout`,{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify({status:b.dataset.st})});
          $("emptc-list-msg").textContent="";loadEmployees();loadTimecard();
        }catch(err){$("emptc-list-msg").textContent="Clock out failed: "+err.message.slice(0,80)}
      }));
      tbl.querySelectorAll("[data-edit]").forEach(b=>b.addEventListener("click",()=>{
        const e=employees.find(x=>x.employeeNum==b.dataset.edit);if(!e)return;
        editingEmp=e.employeeNum;
        const f=$("emptc-form");
        f.lName.value=e.lName||"";f.fName.value=e.fName||"";f.middleI.value=e.middleI||"";
        f.wirelessPhone.value=e.wirelessPhone||"";f.emailWork.value=e.emailWork||"";
        f.isHidden.checked=!!e.isHidden;
        $("emptc-form-title").textContent=`Edit employee #${e.employeeNum}`;
        $("emptc-save").textContent="Save changes";$("emptc-cancel").hidden=false;
      }));
      tbl.querySelectorAll("tr.rowsel").forEach(tr=>tr.addEventListener("click",ev=>{
        if(ev.target.closest("button"))return;
        $("emptc-tc-emp").value=tr.dataset.emp;loadTimecard();
      }));
    }

    function resetForm(){
      editingEmp=null;$("emptc-form").reset();
      $("emptc-form-title").textContent="Add employee";
      $("emptc-save").textContent="Add employee";$("emptc-cancel").hidden=true;
      $("emptc-form-msg").textContent="";
    }
    $("emptc-cancel").addEventListener("click",resetForm);
    $("emptc-form").addEventListener("submit",async e=>{
      e.preventDefault();const f=e.target;
      const body={lName:f.lName.value,fName:f.fName.value,middleI:f.middleI.value,
        wirelessPhone:f.wirelessPhone.value,emailWork:f.emailWork.value,isHidden:f.isHidden.checked};
      $("emptc-form-msg").textContent="Saving…";
      try{
        if(editingEmp)await api(`/api/setup/employees/${editingEmp}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        else await api("/api/setup/employees",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        resetForm();loadEmployees();
      }catch(err){$("emptc-form-msg").textContent="Save failed: "+err.message.slice(0,80)}
    });

    async function loadTimecard(){
      const empNum=$("emptc-tc-emp").value;if(!empNum)return;
      try{
        const tc=await api(`/api/setup/employees/${empNum}/timecard`+
          `?from=${$("emptc-tc-from").value}&to=${$("emptc-tc-to").value}`);
        $("emptc-tc-total").textContent=tc.events.length?`total ${tc.totalHours}`:"";
        $("emptc-tc").querySelector("tbody").innerHTML=tc.events.map(ev=>
          `<tr class="rowsel" data-ev="${ev.clockEventNum}" data-tin="${esc(ev.timeIn)}" data-tout="${esc(ev.timeOut)}" `+
          `data-st="${esc(ev.status)}" data-note="${esc(ev.note)}">`+
          `<td class="mono">${esc(ev.date)}</td><td class="mono">${esc(ev.timeIn.slice(11))}</td>`+
          `<td class="mono">${ev.timeOut?esc(ev.timeOut.slice(11)):'<span class="tag">clocked in</span>'}</td>`+
          `<td><span class="tag">${esc(ev.status)}</span>${ev.isWorkingHome?' <span class="tag">home</span>':""}</td>`+
          `<td class="mono">${esc(ev.hours)}</td><td>${esc(ev.note)}</td></tr>`).join("")
          ||`<tr><td colspan="6"><div class="empty"><b>No clock events in range</b>Widen the dates or clock this employee in.</div></td></tr>`;
        $("emptc-tc-msg").textContent="";
        $("emptc-tc").querySelectorAll("tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
          editingEvent=tr.dataset.ev;
          const f=$("emptc-ev-form");
          f.timeIn.value=tr.dataset.tin.replace(" ","T");
          f.timeOut.value=tr.dataset.tout.replace(" ","T");
          f.status.value=tr.dataset.st;f.note.value=tr.dataset.note;
          $("emptc-ev-msg").textContent=`editing event ${editingEvent}`;
        }));
      }catch(err){$("emptc-tc-msg").textContent="timecard unavailable — "+err.message.slice(0,60)}
    }
    $("emptc-tc-controls").addEventListener("submit",e=>{e.preventDefault();loadTimecard()});
    $("emptc-tc-emp").addEventListener("change",loadTimecard);

    $("emptc-ev-form").addEventListener("submit",async e=>{
      e.preventDefault();const f=e.target;
      if(!editingEvent){$("emptc-ev-msg").textContent="Click a timecard row first.";return}
      $("emptc-ev-msg").textContent="Saving…";
      try{
        await api(`/api/setup/clockevents/${editingEvent}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({timeIn:f.timeIn.value,timeOut:f.timeOut.value,
            status:f.status.value,note:f.note.value})});
        $("emptc-ev-msg").textContent="Corrected.";editingEvent=null;f.reset();
        loadTimecard();loadEmployees();
      }catch(err){$("emptc-ev-msg").textContent="Save failed: "+err.message.slice(0,80)}
    });

    const to=new Date(),from=new Date();from.setDate(from.getDate()-14);
    $("emptc-tc-to").valueAsDate=to;$("emptc-tc-from").valueAsDate=from;
    loadEmployees().then(loadTimecard);
  }
});
})();
