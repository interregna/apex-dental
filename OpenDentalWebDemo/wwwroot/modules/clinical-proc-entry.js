/* Clinical procedure entry — quick-add via code search plus inline edit/delete.
   Web slice of FormProcEdit / FormProcSelect / FormProcCodes2 / FormProcEditAll.
   All writes go through the real Procedures.Insert/Update/Delete endpoints. */
"use strict";
(()=>{
const {api,esc,$,getActivePatient}=window.ODM;
const MOD="clinical-proc-entry";
const STATUSES=["TP","C","EC","EO","R","Cn"];
const STATUS_NAMES={TP:"Treatment planned",C:"Complete",EC:"Existing (this office)",
  EO:"Existing (other office)",R:"Referred out",Cn:"Condition",TPi:"Treatment planned (inactive)"};
const statusOpts=(sel)=>STATUSES.concat(sel&&!STATUSES.includes(sel)?[sel]:[])
  .map(s=>`<option value="${s}"${s===sel?" selected":""}>${s} — ${esc(STATUS_NAMES[s]||s)}</option>`).join("");
let provs=[],selCode=null,procMap={};
const recordUndo=(label,run)=>window.ODM.recordUndo&&window.ODM.recordUndo(label,run);

registerODModule({
  id:MOD,
  label:"Procedures",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Procedures</h1>
    <span class="sub" id="cpe-patline">quick-add and edit — real Procedures business layer</span></div>
  <div class="card" style="margin-bottom:14px">
    <h2>Quick add</h2>
    <form id="cpe-add" style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
      <span style="position:relative">
        <input id="cpe-search" placeholder="D-code or description…" autocomplete="off" style="width:240px">
        <div id="cpe-hits" class="card" style="display:none;position:absolute;left:0;top:100%;z-index:5;width:340px;max-height:260px;overflow-y:auto;padding:4px"></div>
      </span>
      <input id="cpe-tooth" placeholder="Tooth" style="width:56px">
      <input id="cpe-surf" placeholder="Surface" style="width:70px">
      <select id="cpe-status">${statusOpts("TP")}</select>
      <select id="cpe-prov"></select>
      <input id="cpe-fee" type="number" step="0.01" placeholder="Fee (default)" style="width:110px">
      <input id="cpe-note" placeholder="Clinical note (optional)" style="width:220px">
      <button class="action">Add</button>
      <span id="cpe-msg" class="sub"></span>
    </form>
  </div>
  <div class="card">
    <h2>Charted procedures</h2>
    <div class="gridwrap scrolly"><table class="grid" id="cpe-grid">
      <thead><tr><th>Date</th><th>Code</th><th>Description</th><th>Tooth</th><th>Surface</th>
      <th>Status</th><th>Provider</th><th>Fee</th><th>Note</th><th></th></tr></thead>
      <tbody></tbody></table></div>
    <div class="keyrow" style="margin-top:10px">
      <span class="keyrow-h">Status key</span>
      ${STATUSES.map(s=>`<span class="keyrow-item"><b>${s}</b> ${esc(STATUS_NAMES[s])}</span>`).join("")}
    </div>
    <div style="margin-top:10px"><button class="quiet" id="cpe-reload">Refresh</button></div>
  </div>`,
  init(){
    loadProvs();
    const railBtn=document.querySelector(`nav.rail button[data-mod="${MOD}"]`);
    railBtn&&railBtn.addEventListener("click",loadGrid);
    $("cpe-reload").addEventListener("click",loadGrid);
    $("cpe-search").addEventListener("input",searchCodes);
    $("cpe-search").addEventListener("focus",searchCodes);
    document.addEventListener("click",e=>{
      if(!e.target.closest("#cpe-hits")&&e.target.id!=="cpe-search")$("cpe-hits").style.display="none";
    });
    $("cpe-add").addEventListener("submit",addProc);
    loadGrid();
  }
});

async function loadProvs(){
  try{
    provs=(await api("/api/setup/providers")).filter(p=>!p.isHidden);
    $("cpe-prov").innerHTML=provs.map(p=>`<option value="${p.provNum}">${esc(p.abbr)}</option>`).join("");
  }catch{$("cpe-prov").innerHTML=`<option value="0">provider list unavailable</option>`}
}

let searchTimer=null;
function searchCodes(){
  clearTimeout(searchTimer);
  searchTimer=setTimeout(async()=>{
    const q=$("cpe-search").value.trim();
    const box=$("cpe-hits");
    if(q.length<2){box.style.display="none";return}
    try{
      const hits=await api(`/api/proccodes?search=${encodeURIComponent(q)}`);
      box.innerHTML=hits.map(h=>
        `<div class="rowsel" data-code="${esc(h.procCode)}" data-area="${esc(h.treatArea)}"
          style="padding:5px 8px;cursor:pointer;border-bottom:1px solid var(--line)">
          <b class="mono">${esc(h.procCode)}</b> ${esc(h.descript)}
          <span class="tag">${esc(h.treatArea)}</span></div>`).join("")
        ||`<div class="empty">No matching codes</div>`;
      box.style.display="block";
      box.querySelectorAll("[data-code]").forEach(el=>el.addEventListener("click",()=>{
        selCode={procCode:el.dataset.code,treatArea:el.dataset.area};
        $("cpe-search").value=el.dataset.code;
        box.style.display="none";
        const needsTooth=["Tooth","Surf"].includes(el.dataset.area);
        $("cpe-tooth").disabled=!needsTooth;
        $("cpe-surf").disabled=el.dataset.area!=="Surf";
        (needsTooth?$("cpe-tooth"):$("cpe-status")).focus();
      }));
    }catch{box.style.display="none"}
  },200);
}

async function addProc(e){
  e.preventDefault();
  const pat=getActivePatient();
  if(!pat){$("cpe-msg").textContent="Select a patient first (Patients module).";return}
  const procCode=$("cpe-search").value.trim();
  if(!procCode){$("cpe-msg").textContent="Pick a procedure code.";return}
  $("cpe-msg").textContent="Saving…";
  try{
    const body={procCode,toothNum:$("cpe-tooth").value.trim(),surf:$("cpe-surf").value.trim().toUpperCase(),
      status:$("cpe-status").value,provNum:+$("cpe-prov").value||0,
      fee:$("cpe-fee").value===""?null:+$("cpe-fee").value,note:$("cpe-note").value};
    const r=await api(`/api/patients/${pat.patNum}/procs`,{method:"POST",
      headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
    $("cpe-msg").textContent=`Added — fee $${r.fee.toFixed(2)} (audit-logged).`;
    recordUndo(`Add ${procCode}${body.toothNum?" #"+body.toothNum:""}`,async()=>{
      await api(`/api/procs/${r.procNum}`,{method:"DELETE"}); loadGrid();
      $("cpe-msg").textContent="Undid add of "+procCode;});
    $("cpe-search").value="";$("cpe-tooth").value="";$("cpe-surf").value="";$("cpe-fee").value="";$("cpe-note").value="";
    selCode=null;loadGrid();
  }catch(err){$("cpe-msg").textContent="Add failed: "+err.message.slice(0,90)}
}

async function loadGrid(){
  const pat=getActivePatient();
  const tbody=$("cpe-grid").querySelector("tbody");
  $("cpe-patline").textContent=pat?`Patient: ${pat.name}`:"No active patient — pick one in the Patients module.";
  if(!pat){tbody.innerHTML=`<tr><td colspan="9"><div class="empty"><b>No patient selected</b>Pick a patient, then return here.</div></td></tr>`;return}
  try{
    const procs=await api(`/api/patients/${pat.patNum}/procs`);
    procMap={};procs.forEach(p=>{procMap[p.procNum]={...p,patNum:pat.patNum};});
    tbody.innerHTML=procs.map(p=>
      `<tr data-proc="${p.procNum}">
        <td class="mono"><input type="date" value="${esc(p.procDate)}" data-f="procDate" style="width:130px"></td>
        <td class="mono">${esc(p.procCode)}</td>
        <td>${esc(p.descript)}</td>
        <td class="num"><input value="${esc(p.toothNum)}" data-f="toothNum" style="width:44px"></td>
        <td class="mono"><input value="${esc(p.surf)}" data-f="surf" style="width:52px"></td>
        <td><select data-f="status" title="${esc(STATUS_NAMES[p.status]||p.status)}">${statusOpts(p.status)}</select></td>
        <td><select data-f="provNum">${provs.map(v=>
          `<option value="${v.provNum}"${v.provNum===p.provNum?" selected":""}>${esc(v.abbr)}</option>`).join("")
          ||`<option value="${p.provNum}" selected>${esc(p.provAbbr)}</option>`}</select></td>
        <td class="num"><input type="number" step="0.01" value="${p.fee.toFixed(2)}" data-f="fee" style="width:88px"></td>
        <td><input value="${esc(p.note||"")}" data-f="note" placeholder="clinical note" style="width:200px"></td>
        <td style="white-space:nowrap"><button class="quiet" data-save>Save</button>
          <button class="quiet" data-del>Del</button></td>
      </tr>`).join("")
      ||`<tr><td colspan="10"><div class="empty"><b>No procedures charted</b>Use quick-add above to chart the first one.</div></td></tr>`;
    tbody.querySelectorAll("[data-save]").forEach(b=>b.addEventListener("click",()=>saveRow(b.closest("tr"))));
    tbody.querySelectorAll("[data-del]").forEach(b=>b.addEventListener("click",()=>delRow(b.closest("tr"))));
  }catch(err){
    tbody.innerHTML=`<tr><td colspan="9"><div class="empty"><b>Procedures endpoint unavailable</b>${esc(err.message.slice(0,80))}</div></td></tr>`;
  }
}

function rowVal(tr,f){const el=tr.querySelector(`[data-f="${f}"]`);return el?el.value:null}

async function saveRow(tr){
  const old=procMap[tr.dataset.proc];
  $("cpe-msg").textContent="Saving…";
  try{
    await api(`/api/procs/${tr.dataset.proc}`,{method:"PUT",
      headers:{"Content-Type":"application/json"},
      body:JSON.stringify({toothNum:rowVal(tr,"toothNum").trim(),surf:rowVal(tr,"surf").trim().toUpperCase(),
        status:rowVal(tr,"status"),provNum:+rowVal(tr,"provNum")||0,
        procDate:rowVal(tr,"procDate"),fee:+rowVal(tr,"fee"),note:rowVal(tr,"note")})});
    if(old)recordUndo(`Edit ${old.procCode}${old.toothNum?" #"+old.toothNum:""}`,async()=>{
      await api(`/api/procs/${old.procNum}`,{method:"PUT",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({toothNum:old.toothNum,surf:old.surf,status:old.status,provNum:old.provNum,
          procDate:old.procDate,fee:old.fee,note:old.note})}); loadGrid();
      $("cpe-msg").textContent="Reverted edit of "+old.procCode;});
    $("cpe-msg").textContent="Saved (audit-logged).";loadGrid();
  }catch(err){$("cpe-msg").textContent="Save failed: "+err.message.slice(0,90)}
}

async function delRow(tr){
  const p=procMap[tr.dataset.proc];
  if(!confirm("Delete this procedure?"))return;
  $("cpe-msg").textContent="Deleting…";
  try{
    await api(`/api/procs/${tr.dataset.proc}`,{method:"DELETE"});
    if(p)recordUndo(`Delete ${p.procCode}${p.toothNum?" #"+p.toothNum:""}`,async()=>{
      await api(`/api/patients/${p.patNum}/procs`,{method:"POST",headers:{"Content-Type":"application/json"},
        body:JSON.stringify({procCode:p.procCode,toothNum:p.toothNum,surf:p.surf,status:p.status,
          provNum:p.provNum,fee:p.fee,note:p.note||""})}); loadGrid();
      $("cpe-msg").textContent="Restored "+p.procCode;});
    $("cpe-msg").textContent="Deleted.";loadGrid();
  }catch(err){$("cpe-msg").textContent="Delete failed: "+err.message.slice(0,90)}
}
})();
