/* Ortho chart — web slice of FormOrthoChart's dated charting grid. Columns come from
   DisplayFields.GetForCategory(OrthoChart) via GET /api/ortho/fields; the patient's dated
   rows via GET /api/patients/{p}/orthochart. Adding a visit POSTs {date,values} (real
   OrthoChartRows.Insert + OrthoCharts.Insert per non-empty field); inline cell edits PUT
   /api/orthochart/{n} (empty deletes the cell); "clear row" DELETEs every cell on the row.
   Provider and Signature are computed, read-only columns. All server writes are
   OrthoChartEditFull security-logged. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;
const MOD="clinical-ortho-chart";
let data=null;   //last GET payload {fields,rows}
let fields=[];   //ortho column set

registerODModule({
  id:MOD,
  label:"Ortho",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Ortho Chart</h1>
    <span class="sub" id="oc-patline">dated ortho charting — real OrthoChartRows / OrthoCharts business layer</span></div>
  <div class="chart-tools">
    <button type="button" class="quiet" id="oc-reload">Refresh</button>
    <span class="msg" id="oc-msg"></span>
  </div>
  <div class="card" style="margin-bottom:12px">
    <h2>Visits</h2>
    <div class="gridwrap scrolly" style="max-height:460px"><table class="grid" id="oc-grid">
      <thead></thead><tbody></tbody></table></div>
  </div>
  <div class="card" id="oc-addcard">
    <h2>Add visit</h2>
    <div style="display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap">
      <label style="display:flex;flex-direction:column;gap:2px;font-size:12px">Date
        <input type="date" id="oc-newdate"></label>
      <div id="oc-newfields" style="display:flex;gap:8px;flex-wrap:wrap"></div>
      <button type="button" class="action" id="oc-add">Add visit</button>
    </div>
  </div>`,
  init(){
    const railBtn=document.querySelector(`nav.rail button[data-mod="${MOD}"]`);
    railBtn&&railBtn.addEventListener("click",load);
    $("oc-reload").addEventListener("click",load);
    $("oc-add").addEventListener("click",addVisit);
    load();
  },
});

const msg=t=>{$("oc-msg").textContent=t;};

function editableFields(){return fields.filter(f=>f.editable);}

function cellControl(f,value,attrs){
  //A pick-list column renders as a <select>; anything else as a text input.
  if(f.pickList&&f.pickList.length){
    const opts=[""].concat(f.pickList).map(o=>
      `<option value="${esc(o)}"${o===value?" selected":""}>${esc(o||"—")}</option>`).join("");
    return `<select ${attrs}>${opts}</select>`;
  }
  return `<input ${attrs} value="${esc(value)}" style="width:120px">`;
}

function renderGrid(){
  const thead=document.querySelector("#oc-grid thead");
  const tbody=document.querySelector("#oc-grid tbody");
  if(!fields.length){
    thead.innerHTML="";
    tbody.innerHTML=`<tr><td><div class="empty"><b>No ortho columns configured</b>Set up Ortho Chart display fields in Open Dental (Setup &gt; Display Fields &gt; Ortho Chart) to chart visits.</div></td></tr>`;
    return;
  }
  thead.innerHTML=`<tr><th>Date</th>${fields.map(f=>`<th>${esc(f.description)}</th>`).join("")}<th></th></tr>`;
  const rows=data?data.rows:[];
  if(!rows.length){
    tbody.innerHTML=`<tr><td colspan="${fields.length+2}"><div class="empty"><b>No visits</b>Add a dated visit below.</div></td></tr>`;
    return;
  }
  tbody.innerHTML=rows.map(r=>{
    let tds=`<td class="mono">${esc(r.date)}</td>`;
    for(const f of fields){
      const val=(r.values&&r.values[f.fieldName])||"";
      if(!f.editable){
        //Computed read-only columns: Provider abbr and a signature indicator.
        let disp="";
        if(f.internalName==="Provider"){disp=r.provAbbr||"";}
        else if(f.internalName==="Signature"){disp=r.hasSignature?"signed":"";}
        else{disp=val;}
        tds+=`<td class="mono">${esc(disp)}</td>`;
        continue;
      }
      const cellNum=(r.cellNums&&r.cellNums[f.fieldName])||0;
      const attrs=`data-row="${r.orthoChartRowNum}" data-date="${esc(r.date)}" data-field="${esc(f.fieldName)}" data-cell="${cellNum}"`;
      tds+=`<td>${cellControl(f,val,attrs)}</td>`;
    }
    tds+=`<td><button type="button" class="quiet" data-clear="${r.orthoChartRowNum}">Clear</button></td>`;
    return `<tr>${tds}</tr>`;
  }).join("");
  tbody.querySelectorAll("[data-field]").forEach(inp=>
    inp.addEventListener("change",()=>saveCell(inp)));
  tbody.querySelectorAll("[data-clear]").forEach(btn=>
    btn.addEventListener("click",()=>clearRow(btn.dataset.clear)));
}

function renderAddFields(){
  $("oc-newfields").innerHTML=editableFields().map(f=>
    `<label style="display:flex;flex-direction:column;gap:2px;font-size:12px">${esc(f.description)}`+
    cellControl(f,"",`data-newfield="${esc(f.fieldName)}"`)+`</label>`).join("")
    ||`<span class="sub">no editable columns</span>`;
}

async function load(){
  const p=getActivePatient();
  if(!p){
    data=null;fields=[];
    $("oc-patline").textContent="no active patient — pick one in the patient band above";
    renderGrid();renderAddFields();msg("");
    return;
  }
  msg("Loading…");
  try{
    fields=await api("/api/ortho/fields");
    data=await api(`/api/patients/${p.patNum}/orthochart`);
    fields=data.fields||fields;//authoritative column set for this payload
    $("oc-patline").textContent=p.name+" — "+data.rows.length+" visit"+(data.rows.length===1?"":"s");
    if(!$("oc-newdate").value){$("oc-newdate").value=new Date().toISOString().slice(0,10);}
    renderGrid();renderAddFields();
    msg(fields.length?"Loaded.":"No ortho columns configured.");
  }catch(e){
    data=null;renderGrid();renderAddFields();
    msg("Ortho chart unavailable: "+e.message);
  }
}

async function saveCell(inp){
  const p=getActivePatient();
  if(!p){return;}
  const val=inp.value;
  const cellNum=Number(inp.dataset.cell);
  const date=inp.dataset.date;
  const field=inp.dataset.field;
  msg("Saving…");
  try{
    if(cellNum>0){
      await api(`/api/orthochart/${cellNum}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({value:val})});
    }else{
      if(val===""){msg("Nothing to save.");return;}
      await api(`/api/patients/${p.patNum}/orthochart`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({date,values:{[field]:val}})});
    }
    msg("Saved (security-logged).");
    load();
  }catch(e){msg("Save failed: "+e.message);}
}

async function clearRow(rowNum){
  const row=(data?data.rows:[]).find(r=>String(r.orthoChartRowNum)===String(rowNum));
  if(!row){return;}
  const cellNums=Object.values(row.cellNums||{}).filter(n=>n>0);
  if(!cellNums.length){msg("Row has no stored values.");return;}
  if(!confirm(`Clear all ${cellNums.length} value(s) on ${row.date}?`)){return;}
  msg("Clearing…");
  try{
    for(const n of cellNums){
      await api(`/api/orthochart/${n}`,{method:"DELETE"});
    }
    msg("Row cleared (security-logged).");
    load();
  }catch(e){msg("Clear failed: "+e.message);}
}

async function addVisit(){
  const p=getActivePatient();
  if(!p){msg("Pick a patient first.");return;}
  const date=$("oc-newdate").value;
  if(!date){msg("Pick a date.");return;}
  const values={};
  $("oc-newfields").querySelectorAll("[data-newfield]").forEach(inp=>{
    if(inp.value!==""){values[inp.dataset.newfield]=inp.value;}
  });
  if(!Object.keys(values).length){msg("Enter at least one field value.");return;}
  msg("Adding…");
  try{
    await api(`/api/patients/${p.patNum}/orthochart`,{method:"POST",
      headers:{"Content-Type":"application/json"},
      body:JSON.stringify({date,values})});
    $("oc-newfields").querySelectorAll("[data-newfield]").forEach(inp=>{inp.value="";});
    msg("Visit added (security-logged).");
    load();
  }catch(e){msg("Add failed: "+e.message);}
}
})();
