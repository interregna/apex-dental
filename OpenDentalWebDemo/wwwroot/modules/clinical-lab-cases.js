/* Clinical lab cases — per-patient dental-lab case tracking.
   Web slice of FormLabCases / FormLabCaseEdit / FormLabCaseSelect.
   Add a case (lab + due date + instructions), then edit its milestone
   dates (sent / received / quality-checked) inline; delete with confirm.
   All writes go through the real LabCases.Insert/Update/Delete endpoints. */
"use strict";
(()=>{
const {api,esc,$,getActivePatient}=window.ODM;
const MOD="clinical-lab-cases";
let labs=[];

registerODModule({
  id:MOD,
  label:"Lab Cases",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Lab Cases</h1>
    <span class="sub" id="clc-patline">dental-lab case tracking — real LabCases business layer</span></div>
  <div class="card" style="margin-bottom:14px">
    <h2>New lab case</h2>
    <form id="clc-add" style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end">
      <label>Lab<br><select id="clc-lab" style="min-width:200px"></select></label>
      <label>Due date<br><input id="clc-due" type="date"></label>
      <label style="flex:1;min-width:220px">Instructions<br>
        <input id="clc-instr" placeholder="e.g. PFM crown, shade A2" style="width:100%"></label>
      <button class="action">Add case</button>
      <span id="clc-msg" class="sub"></span>
    </form>
  </div>
  <div class="card">
    <h2>Lab cases for this patient</h2>
    <div class="gridwrap scrolly"><table class="grid" id="clc-grid">
      <thead><tr><th>Lab</th><th>Due</th><th>Sent</th><th>Received</th><th>Checked</th>
      <th>Status</th><th>Procedure</th><th>Instructions</th><th></th></tr></thead>
      <tbody></tbody></table></div>
    <div style="margin-top:8px"><button class="quiet" id="clc-reload">Refresh</button></div>
  </div>`,
  init(){
    loadLabs();
    const railBtn=document.querySelector(`nav.rail button[data-mod="${MOD}"]`);
    railBtn&&railBtn.addEventListener("click",loadGrid);
    $("clc-reload").addEventListener("click",loadGrid);
    $("clc-add").addEventListener("submit",addCase);
    loadGrid();
  }
});

async function loadLabs(){
  try{
    labs=await api("/api/laboratories");
    $("clc-lab").innerHTML=labs.length
      ?labs.map(l=>`<option value="${l.laboratoryNum}">${esc(l.description)}${l.phone?" — "+esc(l.phone):""}</option>`).join("")
      :`<option value="0">No labs set up — seed one (Laboratories.Insert)</option>`;
  }catch{$("clc-lab").innerHTML=`<option value="0">lab list unavailable</option>`}
}

async function addCase(e){
  e.preventDefault();
  const pat=getActivePatient();
  if(!pat){$("clc-msg").textContent="Select a patient first (Patients module).";return}
  const laboratoryNum=+$("clc-lab").value||0;
  if(!laboratoryNum){$("clc-msg").textContent="Pick a lab (none set up yet?).";return}
  $("clc-msg").textContent="Saving…";
  try{
    const body={laboratoryNum,dateDue:$("clc-due").value||null,
      instructions:$("clc-instr").value.trim(),aptNum:0};
    await api(`/api/patients/${pat.patNum}/labcases`,{method:"POST",
      headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
    $("clc-msg").textContent="Lab case added.";
    $("clc-instr").value="";$("clc-due").value="";
    loadGrid();
  }catch(err){$("clc-msg").textContent="Add failed: "+err.message.slice(0,90)}
}

async function loadGrid(){
  const pat=getActivePatient();
  const tbody=$("clc-grid").querySelector("tbody");
  $("clc-patline").textContent=pat?`Patient: ${pat.name}`:"No active patient — pick one in the Patients module.";
  if(!pat){tbody.innerHTML=`<tr><td colspan="9"><div class="empty"><b>No patient selected</b>Pick a patient, then return here.</div></td></tr>`;return}
  try{
    const cases=await api(`/api/patients/${pat.patNum}/labcases`);
    tbody.innerHTML=cases.map(c=>
      `<tr data-lc="${c.labCaseNum}">
        <td>${esc(c.labName)||'<span class="sub">Lab not found</span>'}</td>
        <td class="mono"><input type="date" value="${esc(c.dateDue)}" data-f="dateDue" style="width:132px"></td>
        <td class="mono"><input type="date" value="${esc(c.dateSent)}" data-f="dateSent" style="width:132px"></td>
        <td class="mono"><input type="date" value="${esc(c.dateRecd)}" data-f="dateRecd" style="width:132px"></td>
        <td class="mono"><input type="date" value="${esc(c.dateChecked)}" data-f="dateChecked" style="width:132px"></td>
        <td><span class="tag">${esc(c.status)}</span></td>
        <td>${esc(c.procDescript)}</td>
        <td><input value="${esc(c.instructions)}" data-f="instructions" style="min-width:160px;width:100%"></td>
        <td style="white-space:nowrap"><button class="quiet" data-save>Save</button>
          <button class="quiet" data-del>Del</button></td>
      </tr>`).join("")
      ||`<tr><td colspan="9"><div class="empty"><b>No lab cases</b>Use the form above to add the first one.</div></td></tr>`;
    tbody.querySelectorAll("[data-save]").forEach(b=>b.addEventListener("click",()=>saveRow(b.closest("tr"))));
    tbody.querySelectorAll("[data-del]").forEach(b=>b.addEventListener("click",()=>delRow(b.closest("tr"))));
  }catch(err){
    tbody.innerHTML=`<tr><td colspan="9"><div class="empty"><b>Lab cases endpoint unavailable</b>${esc(err.message.slice(0,80))}</div></td></tr>`;
  }
}

function rowVal(tr,f){const el=tr.querySelector(`[data-f="${f}"]`);return el?el.value:null}

async function saveRow(tr){
  $("clc-msg").textContent="Saving…";
  try{
    await api(`/api/labcases/${tr.dataset.lc}`,{method:"PUT",
      headers:{"Content-Type":"application/json"},
      body:JSON.stringify({dateSent:rowVal(tr,"dateSent")||null,dateRecd:rowVal(tr,"dateRecd")||null,
        dateChecked:rowVal(tr,"dateChecked")||null,dateDue:rowVal(tr,"dateDue")||null,
        instructions:rowVal(tr,"instructions")})});
    $("clc-msg").textContent="Saved.";loadGrid();
  }catch(err){$("clc-msg").textContent="Save failed: "+err.message.slice(0,90)}
}

async function delRow(tr){
  if(!confirm("Delete this lab case?"))return;
  $("clc-msg").textContent="Deleting…";
  try{
    await api(`/api/labcases/${tr.dataset.lc}`,{method:"DELETE"});
    $("clc-msg").textContent="Deleted.";loadGrid();
  }catch(err){$("clc-msg").textContent="Delete failed: "+err.message.slice(0,90)}
}
})();
