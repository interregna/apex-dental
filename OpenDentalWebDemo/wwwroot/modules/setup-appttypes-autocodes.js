/* Appointment types & auto codes — two tabbed grids with inline add/edit forms.
   Appointment types show color swatch, time pattern, and codes, with delete guarded
   by AppointmentTypes.CheckInUse server-side; auto codes have an expandable row
   listing each AutoCodeItem's procedure code and conditions.
   Replaces FormApptTypes / FormApptTypeEdit / FormAutoCode / FormAutoCodeEdit. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

registerODModule({
  id:"setup-appttypes-autocodes",
  label:"Appt Types",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Appointment types &amp; auto codes</h1>
    <span class="sub">scheduling type presets · procedure auto codes</span></div>
  <div class="row" style="display:flex;gap:8px;margin-bottom:12px">
    <button type="button" class="action" id="atac-tab-at">Appointment types</button>
    <button type="button" class="quiet" id="atac-tab-ac">Auto codes</button>
  </div>
  <div class="card" id="atac-panel-at">
    <h2>Appointment types <span class="msg" id="atac-at-msg"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="atac-at-list">
      <thead><tr><th>#</th><th></th><th>Name</th><th>Pattern</th><th>Codes</th><th>Blockouts</th><th></th><th></th></tr></thead><tbody></tbody>
    </table></div>
    <h2 style="margin-top:14px" id="atac-at-form-title">Add appointment type</h2>
    <form class="row" id="atac-at-form">
      <input name="appointmentTypeName" placeholder="Name" style="width:150px" required>
      <input type="color" name="color" value="#a0c8f0" title="Color" style="width:44px;padding:2px">
      <input name="pattern" placeholder="Pattern (X=prov, /=asst)" style="width:160px">
      <input name="codeStr" placeholder="Proc codes (comma sep)" style="width:160px">
      <input name="codeStrRequired" placeholder="Required codes" style="width:130px">
      <select name="requiredProcCodesNeeded" title="Required codes rule">
        <option value="None">Required: none</option>
        <option value="AtLeastOne">Required: at least one</option>
        <option value="All">Required: all</option>
      </select>
      <input name="blockoutTypes" placeholder="Blockout DefNums (comma sep)" style="width:180px">
      <label style="font-size:13px"><input type="checkbox" name="isHidden"> hidden</label>
      <button class="action" id="atac-at-save">Add type</button>
      <button type="button" class="quiet" id="atac-at-cancel" hidden>Cancel edit</button>
      <span class="msg" id="atac-at-form-msg"></span>
    </form>
  </div>
  <div class="card" id="atac-panel-ac" hidden>
    <h2>Auto codes <span class="msg" id="atac-ac-msg"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="atac-ac-list">
      <thead><tr><th>#</th><th>Description</th><th>Items</th><th>Flags</th><th></th><th></th><th></th></tr></thead><tbody></tbody>
    </table></div>
    <h2 style="margin-top:14px" id="atac-ac-form-title">Add auto code</h2>
    <form class="row" id="atac-ac-form">
      <input name="description" placeholder="Description" style="width:180px" required>
      <label style="font-size:13px"><input type="checkbox" name="isHidden"> hidden</label>
      <label style="font-size:13px"><input type="checkbox" name="lessIntrusive"> less intrusive</label>
      <button class="action" id="atac-ac-save">Add auto code</button>
      <button type="button" class="quiet" id="atac-ac-cancel" hidden>Cancel edit</button>
      <span class="msg" id="atac-ac-form-msg"></span>
    </form>
    <p class="msg" style="margin:8px 0 0">Item conditions (surfaces, anterior/posterior, arch…) come from each code's
      AutoCodeItem rows — expand a row to review them.</p>
  </div>`,
  init(){
    let apptTypes=[],autoCodes=[],editingAt=null,editingAc=null,expandedAc=null;

    //---------- tabs ----------
    function showTab(which){
      $("atac-panel-at").hidden=which!=="at";
      $("atac-panel-ac").hidden=which!=="ac";
      $("atac-tab-at").className=which==="at"?"action":"quiet";
      $("atac-tab-ac").className=which==="ac"?"action":"quiet";
    }
    $("atac-tab-at").addEventListener("click",()=>showTab("at"));
    $("atac-tab-ac").addEventListener("click",()=>showTab("ac"));

    //---------- appointment types ----------
    async function loadApptTypes(){
      try{
        apptTypes=await api("/api/setup/appttypes");
        $("atac-at-list").querySelector("tbody").innerHTML=apptTypes.map(t=>
          `<tr><td class="num">${t.appointmentTypeNum}</td>`+
          `<td>${t.color?`<span style="display:inline-block;width:16px;height:16px;border-radius:3px;border:1px solid var(--line-strong);background:${esc(t.color)}"></span>`:""}</td>`+
          `<td>${esc(t.appointmentTypeName)}${t.isHidden?' <span class="tag">hidden</span>':""}</td>`+
          `<td class="mono">${esc(t.pattern||"")||'<span class="tag">dynamic</span>'}</td>`+
          `<td class="mono">${esc(t.codeStr||"")}${t.codeStrRequired?` <span class="tag" title="${esc(t.requiredProcCodesNeeded)}">req: ${esc(t.codeStrRequired)}</span>`:""}</td>`+
          `<td>${esc(t.blockoutTypeNames||"")}</td>`+
          `<td><button type="button" class="quiet" data-atedit="${t.appointmentTypeNum}">Edit</button></td>`+
          `<td><button type="button" class="quiet" data-atdel="${t.appointmentTypeNum}">Delete</button></td></tr>`).join("")
          ||`<tr><td colspan="8"><div class="empty"><b>No appointment types yet</b>Add the first one below.</div></td></tr>`;
        $("atac-at-msg").textContent="";
        wireApptTypeRows();
      }catch(err){$("atac-at-msg").textContent="appttypes endpoint unavailable — "+err.message.slice(0,60)}
    }

    function wireApptTypeRows(){
      const tbl=$("atac-at-list");
      tbl.querySelectorAll("[data-atedit]").forEach(b=>b.addEventListener("click",()=>{
        const t=apptTypes.find(x=>x.appointmentTypeNum==b.dataset.atedit);if(!t)return;
        editingAt=t.appointmentTypeNum;
        const f=$("atac-at-form");
        f.appointmentTypeName.value=t.appointmentTypeName||"";
        f.color.value=t.color||"#a0c8f0";
        f.pattern.value=t.pattern||"";
        f.codeStr.value=t.codeStr||"";
        f.codeStrRequired.value=t.codeStrRequired||"";
        f.requiredProcCodesNeeded.value=t.requiredProcCodesNeeded||"None";
        f.blockoutTypes.value=t.blockoutTypes||"";
        f.isHidden.checked=!!t.isHidden;
        $("atac-at-form-title").textContent=`Edit appointment type #${t.appointmentTypeNum}`;
        $("atac-at-save").textContent="Save changes";$("atac-at-cancel").hidden=false;
      }));
      tbl.querySelectorAll("[data-atdel]").forEach(b=>b.addEventListener("click",async()=>{
        const t=apptTypes.find(x=>x.appointmentTypeNum==b.dataset.atdel);if(!t)return;
        if(!confirm(`Delete appointment type "${t.appointmentTypeName}"?`))return;
        $("atac-at-msg").textContent="Deleting…";
        try{
          await api(`/api/setup/appttypes/${t.appointmentTypeNum}`,{method:"DELETE"});
          $("atac-at-msg").textContent="";if(editingAt==t.appointmentTypeNum)resetApptTypeForm();
          loadApptTypes();
        }catch(err){$("atac-at-msg").textContent="Delete blocked: "+err.message.slice(0,120)}
      }));
    }

    function resetApptTypeForm(){
      editingAt=null;$("atac-at-form").reset();$("atac-at-form").color.value="#a0c8f0";
      $("atac-at-form-title").textContent="Add appointment type";
      $("atac-at-save").textContent="Add type";$("atac-at-cancel").hidden=true;
      $("atac-at-form-msg").textContent="";
    }
    $("atac-at-cancel").addEventListener("click",resetApptTypeForm);
    $("atac-at-form").addEventListener("submit",async e=>{
      e.preventDefault();const f=e.target;
      const pattern=f.pattern.value.trim().toUpperCase().replace(/ /g,"");
      if(pattern&&!/^[X/]+$/.test(pattern)){$("atac-at-form-msg").textContent="Pattern may only contain X and /";return}
      const body={appointmentTypeName:f.appointmentTypeName.value,color:f.color.value,pattern,
        codeStr:f.codeStr.value.trim(),codeStrRequired:f.codeStrRequired.value.trim(),
        requiredProcCodesNeeded:f.requiredProcCodesNeeded.value,
        blockoutTypes:f.blockoutTypes.value.trim(),isHidden:f.isHidden.checked};
      $("atac-at-form-msg").textContent="Saving…";
      try{
        if(editingAt)await api(`/api/setup/appttypes/${editingAt}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        else await api("/api/setup/appttypes",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        resetApptTypeForm();loadApptTypes();
      }catch(err){$("atac-at-form-msg").textContent="Save failed: "+err.message.slice(0,100)}
    });

    //---------- auto codes ----------
    function itemsDetailHtml(c){
      if(!c.items.length)return `<div class="empty"><b>No items</b>This auto code has no procedure code items yet.</div>`;
      return `<table class="grid" style="margin:4px 0"><thead><tr><th>Proc code</th><th>Conditions</th></tr></thead><tbody>`+
        c.items.map(i=>`<tr><td class="mono">${esc(i.procCode||String(i.codeNum))}</td>`+
          `<td>${i.conditions.length?i.conditions.map(x=>`<span class="tag">${esc(x.replace(/_/g," "))}</span>`).join(" "):'<span class="msg">always</span>'}</td></tr>`).join("")+
        `</tbody></table>`;
    }

    async function loadAutoCodes(){
      try{
        autoCodes=await api("/api/setup/autocodes");
        $("atac-ac-list").querySelector("tbody").innerHTML=autoCodes.map(c=>
          `<tr><td class="num">${c.autoCodeNum}</td>`+
          `<td>${esc(c.description)}${c.isHidden?' <span class="tag">hidden</span>':""}</td>`+
          `<td class="num">${c.items.length}</td>`+
          `<td>${c.lessIntrusive?'<span class="tag">less intrusive</span>':""}</td>`+
          `<td><button type="button" class="quiet" data-acexp="${c.autoCodeNum}">${expandedAc==c.autoCodeNum?"Hide items":"Items"}</button></td>`+
          `<td><button type="button" class="quiet" data-acedit="${c.autoCodeNum}">Edit</button></td>`+
          `<td><button type="button" class="quiet" data-acdel="${c.autoCodeNum}">Delete</button></td></tr>`+
          (expandedAc==c.autoCodeNum?`<tr><td></td><td colspan="6">${itemsDetailHtml(c)}</td></tr>`:"")).join("")
          ||`<tr><td colspan="7"><div class="empty"><b>No auto codes yet</b>Add the first one below.</div></td></tr>`;
        $("atac-ac-msg").textContent="";
        wireAutoCodeRows();
      }catch(err){$("atac-ac-msg").textContent="autocodes endpoint unavailable — "+err.message.slice(0,60)}
    }

    function wireAutoCodeRows(){
      const tbl=$("atac-ac-list");
      tbl.querySelectorAll("[data-acexp]").forEach(b=>b.addEventListener("click",()=>{
        expandedAc=expandedAc==b.dataset.acexp?null:b.dataset.acexp;
        loadAutoCodes();
      }));
      tbl.querySelectorAll("[data-acedit]").forEach(b=>b.addEventListener("click",()=>{
        const c=autoCodes.find(x=>x.autoCodeNum==b.dataset.acedit);if(!c)return;
        editingAc=c.autoCodeNum;
        const f=$("atac-ac-form");
        f.description.value=c.description||"";
        f.isHidden.checked=!!c.isHidden;f.lessIntrusive.checked=!!c.lessIntrusive;
        $("atac-ac-form-title").textContent=`Edit auto code #${c.autoCodeNum}`;
        $("atac-ac-save").textContent="Save changes";$("atac-ac-cancel").hidden=false;
      }));
      tbl.querySelectorAll("[data-acdel]").forEach(b=>b.addEventListener("click",async()=>{
        const c=autoCodes.find(x=>x.autoCodeNum==b.dataset.acdel);if(!c)return;
        if(!confirm(`Delete auto code "${c.description}" and its ${c.items.length} item(s)?`))return;
        $("atac-ac-msg").textContent="Deleting…";
        try{
          await api(`/api/setup/autocodes/${c.autoCodeNum}`,{method:"DELETE"});
          $("atac-ac-msg").textContent="";if(editingAc==c.autoCodeNum)resetAutoCodeForm();
          loadAutoCodes();
        }catch(err){$("atac-ac-msg").textContent="Delete blocked: "+err.message.slice(0,120)}
      }));
    }

    function resetAutoCodeForm(){
      editingAc=null;$("atac-ac-form").reset();
      $("atac-ac-form-title").textContent="Add auto code";
      $("atac-ac-save").textContent="Add auto code";$("atac-ac-cancel").hidden=true;
      $("atac-ac-form-msg").textContent="";
    }
    $("atac-ac-cancel").addEventListener("click",resetAutoCodeForm);
    $("atac-ac-form").addEventListener("submit",async e=>{
      e.preventDefault();const f=e.target;
      const body={description:f.description.value,isHidden:f.isHidden.checked,lessIntrusive:f.lessIntrusive.checked};
      $("atac-ac-form-msg").textContent="Saving…";
      try{
        if(editingAc)await api(`/api/setup/autocodes/${editingAc}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        else await api("/api/setup/autocodes",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        resetAutoCodeForm();loadAutoCodes();
      }catch(err){$("atac-ac-form-msg").textContent="Save failed: "+err.message.slice(0,100)}
    });

    loadApptTypes();loadAutoCodes();
  }
});
})();
