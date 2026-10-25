/* Forms module (misc-sheets): web replacement for FormSheetPicker + FormSheetFillEdit.
   Pick a SheetDef, create a Sheet for the active patient (auto-filling patient
   fields), edit the input/checkbox fields, save, and view the rendered PDF inline.
   Auto-loaded by app.js; talks to MiscSheetsModule.cs, which runs the real
   SheetDefs / SheetUtil / SheetFiller / Sheets business layer. */
"use strict";
registerODModule({
  id:"misc-sheets",
  label:"Forms",
  group:"Patient",
  html:`
  <div class="mod-head"><h1>Forms</h1>
    <span class="sub">sheet library · fill &amp; render for the active patient</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>New form <span class="msg" id="ms-new-msg"></span></h2>
      <form class="row" id="ms-new-form" style="margin-bottom:10px">
        <select id="ms-defs" style="min-width:240px"></select>
        <button class="action">Create</button>
      </form>
      <div id="ms-editor"><div class="empty"><b>No form open</b>Pick a form above and Create to fill it out.</div></div>
    </div>
    <div class="card">
      <h2>Forms on file <span class="msg" id="ms-list-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="ms-list">
        <thead><tr><th>Sheet</th><th>Description</th><th>Type</th><th class="mono">Date</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    let curSheetNum=null;

    async function loadDefs(){
      try{
        const defs=await api("/api/sheetdefs");
        $("ms-defs").innerHTML=defs.map(d=>
          `<option value="${d.sheetDefNum}">${esc(d.description)} — ${esc(d.sheetType)}${d.isInternal?" (built-in)":""}</option>`).join("")
          ||`<option value="">No sheet definitions available</option>`;
        $("ms-new-msg").textContent="";
      }catch(e){$("ms-new-msg").textContent="sheetdefs endpoint unavailable"}
    }

    function renderEditor(sheetNum,fields){
      curSheetNum=sheetNum;
      if(!fields||!fields.length){
        $("ms-editor").innerHTML=
          `<p>Sheet ${sheetNum} created — no editable fields. `+
          `<a data-view="Form ${sheetNum}" href="/api/sheets/${sheetNum}/pdf">view PDF</a></p>`;
        return;
      }
      const rows=fields.map((f,i)=>{
        const id=`ms-f-${i}`;
        if(f.fieldType==="CheckBox"){
          return `<label style="display:flex;align-items:center;gap:6px;margin:4px 0">`+
            `<input type="checkbox" id="${id}" data-name="${esc(f.fieldName)}" data-cb="1"`+
            `${f.fieldValue?" checked":""}> ${esc(f.fieldName)}</label>`;
        }
        return `<label style="display:block;margin:6px 0">`+
          `<span style="display:block;font-size:12px;color:var(--muted,#666)">${esc(f.fieldName)}</span>`+
          `<input type="text" id="${id}" data-name="${esc(f.fieldName)}" `+
          `value="${esc(f.fieldValue)}" style="width:100%"></label>`;
      }).join("");
      $("ms-editor").innerHTML=
        `<div style="margin-bottom:6px"><b>Sheet ${sheetNum}</b></div>`+
        `<div class="scrolly" style="max-height:320px">${rows}</div>`+
        `<div class="row" style="margin-top:10px">`+
          `<button type="button" class="action" id="ms-save">Save</button>`+
          `<a data-view="Form ${sheetNum}" href="/api/sheets/${sheetNum}/pdf">view PDF</a>`+
          `<span class="msg" id="ms-edit-msg"></span></div>`;
      $("ms-save").addEventListener("click",saveFields);
    }

    async function saveFields(){
      if(!curSheetNum)return;
      const map={};
      $("ms-editor").querySelectorAll("[data-name]").forEach(el=>{
        map[el.dataset.name]=el.dataset.cb?(el.checked?"X":""):el.value;
      });
      try{
        const res=await api(`/api/sheets/${curSheetNum}/fields`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify({fields:map})});
        $("ms-edit-msg").textContent="Saved.";
        renderEditor(res.sheetNum,res.fields);
        loadList();
      }catch(e){$("ms-edit-msg").textContent="Save failed: "+e.message.slice(0,60)}
    }

    async function createSheet(e){
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){$("ms-new-msg").textContent="no active patient";return}
      const sel=$("ms-defs").value;
      if(!sel){$("ms-new-msg").textContent="pick a form first";return}
      try{
        const res=await api(`/api/patients/${pat.patNum}/sheets`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify({sheetDefNum:Number(sel)})});
        $("ms-new-msg").textContent="";
        renderEditor(res.sheetNum,res.fields);
        loadList();
      }catch(e2){$("ms-new-msg").textContent="Create failed: "+e2.message.slice(0,60)}
    }

    async function loadList(){
      const pat=getActivePatient();
      const tbody=$("ms-list").querySelector("tbody");
      if(!pat){
        tbody.innerHTML=`<tr><td colspan="5"><div class="empty"><b>No active patient</b>Select a patient to see their forms.</div></td></tr>`;
        $("ms-list-msg").textContent="";return;
      }
      try{
        const rows=await api(`/api/patients/${pat.patNum}/sheets`);
        tbody.innerHTML=rows.map(r=>
          `<tr><td class="mono">${r.sheetNum}</td><td>${esc(r.description)}</td>`+
          `<td>${esc(r.sheetType)}</td><td class="mono">${esc(r.dateTimeSheet)}</td>`+
          `<td><a data-view="Form ${r.sheetNum}" href="/api/sheets/${r.sheetNum}/pdf">view</a></td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No forms on file</b>Create one with the picker at left.</div></td></tr>`;
        $("ms-list-msg").textContent="";
      }catch(e){$("ms-list-msg").textContent="sheets endpoint unavailable"}
    }

    $("ms-new-form").addEventListener("submit",createSheet);
    document.addEventListener("od:patient-changed",()=>{curSheetNum=null;
      $("ms-editor").innerHTML=`<div class="empty"><b>No form open</b>Pick a form above and Create to fill it out.</div>`;
      loadList();});
    loadDefs();
    loadList();
  }
});
