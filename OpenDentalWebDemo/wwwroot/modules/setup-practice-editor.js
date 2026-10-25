/* Practice setup editor — definitions (add/edit/hide), operatories (add/edit),
   provider edit. One list-detail surface over the real Defs/Operatories/Providers
   business methods; retires FormDefinitions, FormDefEdit, FormOperatories,
   FormOperatoryEdit and the edit path of FormProvEdit. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

registerODModule({
id:"setup-practice-editor",
label:"Practice Setup",
group:"Practice",
html:`
<div class="mod-head"><h1>Practice setup</h1>
  <span class="sub">definitions · operatories · providers — edits go through the audit-logged business layer</span></div>
<div class="cols" style="grid-template-columns:repeat(auto-fit,minmax(320px,1fr));align-items:start">

  <div class="card"><h2>Definitions</h2>
    <select id="spe-defcat" aria-label="Definition category" style="margin-bottom:8px;max-width:100%"></select>
    <div class="gridwrap scrolly"><table class="grid" id="spe-defs">
      <thead><tr><th></th><th>Name</th><th>Value</th><th></th><th></th></tr></thead><tbody></tbody></table></div>
    <form id="spe-def-form" style="display:flex;gap:6px;flex-wrap:wrap;margin-top:10px;align-items:center">
      <input name="itemName" placeholder="Name" required style="flex:1 1 120px">
      <input name="itemValue" placeholder="Value" style="flex:1 1 100px">
      <label style="font-size:12px"><input type="checkbox" name="useColor"> color</label>
      <input type="color" name="color" value="#9DC8C4" style="width:38px;padding:2px">
      <label id="spe-def-hiddenwrap" style="font-size:12px" hidden><input type="checkbox" name="isHidden"> hidden</label>
      <button class="action" id="spe-def-save">Add</button>
      <button type="button" class="quiet" id="spe-def-cancel" hidden>Cancel</button>
      <span class="msg" id="spe-def-msg"></span>
    </form>
  </div>

  <div class="card"><h2>Operatories</h2>
    <div class="gridwrap scrolly"><table class="grid" id="spe-ops">
      <thead><tr><th>Op</th><th>Abbrev</th><th>Dentist</th><th>Hygienist</th><th></th><th></th></tr></thead><tbody></tbody></table></div>
    <form id="spe-op-form" style="display:flex;gap:6px;flex-wrap:wrap;margin-top:10px;align-items:center">
      <input name="opName" placeholder="Operatory name" required style="flex:1 1 120px">
      <input name="abbrev" placeholder="Abbrev" required size="6">
      <select name="provDentist" aria-label="Dentist"></select>
      <select name="provHygienist" aria-label="Hygienist"></select>
      <label style="font-size:12px"><input type="checkbox" name="isHygiene"> hygiene</label>
      <label id="spe-op-hiddenwrap" style="font-size:12px" hidden><input type="checkbox" name="isHidden"> hidden</label>
      <button class="action" id="spe-op-save">Add</button>
      <button type="button" class="quiet" id="spe-op-cancel" hidden>Cancel</button>
      <span class="msg" id="spe-op-msg"></span>
    </form>
  </div>

  <div class="card"><h2>Providers
      <button type="button" class="quiet" id="spe-prov-new" style="margin-left:8px">Add provider</button></h2>
    <div class="gridwrap scrolly"><table class="grid" id="spe-provs">
      <thead><tr><th></th><th>Abbr</th><th>Last</th><th>First</th><th></th><th></th></tr></thead><tbody></tbody></table></div>
    <form id="spe-prov-form" style="display:none;gap:6px;flex-wrap:wrap;margin-top:10px;align-items:center">
      <input name="abbr" placeholder="Abbr" required size="6">
      <input name="lName" placeholder="Last name" required style="flex:1 1 100px">
      <input name="fName" placeholder="First name" style="flex:1 1 100px">
      <input name="suffix" placeholder="Suffix" size="5">
      <label style="font-size:12px"><input type="checkbox" name="useColor"> color</label>
      <input type="color" name="color" value="#9DC8C4" style="width:38px;padding:2px">
      <label style="font-size:12px"><input type="checkbox" name="isSecondary"> secondary</label>
      <label style="font-size:12px"><input type="checkbox" name="isHidden"> hidden</label>
      <button class="action">Save provider</button>
      <button type="button" class="quiet" id="spe-prov-cancel">Cancel</button>
      <span class="msg" id="spe-prov-msg"></span>
    </form>
    <p class="msg" id="spe-prov-hint" style="margin:10px 0 0">Click a provider row to edit it, or Add provider for a new one.</p>
  </div>
</div>`,
init(){
  const swatch=c=>c?`<span style="display:inline-block;width:13px;height:13px;border-radius:3px;background:${c};border:1px solid var(--line-strong)"></span>`:"";

  /* ---------- definitions ---------- */
  let editDefNum=null;
  async function loadDefCats(){
    try{
      const cats=await api("/api/setup/defcats");
      $("spe-defcat").innerHTML=cats.map(c=>`<option value="${esc(c.name)}">${esc(c.name)}</option>`).join("");
      loadDefs();
    }catch(e){$("spe-def-msg").textContent="defcats endpoint unavailable"}
  }
  async function loadDefs(){
    const cat=$("spe-defcat").value;if(!cat)return;
    defEditReset();
    try{
      const defs=await api(`/api/setup/defs?category=${encodeURIComponent(cat)}`);
      $("spe-defs").querySelector("tbody").innerHTML=defs.map(d=>
        `<tr data-def="${d.defNum}" data-name="${esc(d.itemName)}" data-value="${esc(d.itemValue)}" `+
        `data-color="${d.color||""}" data-hidden="${d.isHidden?1:""}">`+
        `<td>${swatch(d.color)}</td><td>${esc(d.itemName)}</td><td>${esc(d.itemValue)}</td>`+
        `<td>${d.isHidden?'<span class="tag">hidden</span>':""}</td>`+
        `<td style="white-space:nowrap"><button type="button" class="quiet" data-edit>Edit</button> `+
        `${d.isHidden?"":'<button type="button" class="quiet" data-hide>Hide</button>'}</td></tr>`).join("")
        ||`<tr><td colspan="5"><div class="empty"><b>No definitions</b>Add the first one below.</div></td></tr>`;
      $("spe-defs").querySelectorAll("[data-edit]").forEach(b=>b.addEventListener("click",()=>defEditStart(b.closest("tr"))));
      $("spe-defs").querySelectorAll("[data-hide]").forEach(b=>b.addEventListener("click",()=>defHide(b.closest("tr"))));
    }catch(e){$("spe-def-msg").textContent="defs endpoint unavailable"}
  }
  function defEditStart(tr){
    editDefNum=tr.dataset.def;
    const f=$("spe-def-form");
    f.itemName.value=tr.dataset.name;f.itemValue.value=tr.dataset.value;
    f.useColor.checked=!!tr.dataset.color;
    if(tr.dataset.color)f.color.value=tr.dataset.color;
    f.isHidden.checked=!!tr.dataset.hidden;
    $("spe-def-hiddenwrap").hidden=false;$("spe-def-cancel").hidden=false;
    $("spe-def-save").textContent="Save";$("spe-def-msg").textContent=`Editing “${tr.dataset.name}”`;
  }
  function defEditReset(){
    editDefNum=null;
    const f=$("spe-def-form");f.reset();
    $("spe-def-hiddenwrap").hidden=true;$("spe-def-cancel").hidden=true;
    $("spe-def-save").textContent="Add";$("spe-def-msg").textContent="";
  }
  async function defHide(tr){
    $("spe-def-msg").textContent="Hiding…";
    try{await api(`/api/setup/defs/${tr.dataset.def}/hide`,{method:"POST"});loadDefs();}
    catch(err){$("spe-def-msg").textContent="Hide failed: "+err.message.slice(0,80)}
  }
  $("spe-defcat").addEventListener("change",loadDefs);
  $("spe-def-cancel").addEventListener("click",defEditReset);
  $("spe-def-form").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target;
    const body={itemName:f.itemName.value,itemValue:f.itemValue.value,
      color:f.useColor.checked?f.color.value:""};
    $("spe-def-msg").textContent="Saving…";
    try{
      if(editDefNum){
        body.isHidden=f.isHidden.checked;
        await api(`/api/setup/defs/${editDefNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        body.category=$("spe-defcat").value;
        await api("/api/setup/defs",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      loadDefs();
    }catch(err){$("spe-def-msg").textContent="Save failed: "+err.message.slice(0,80)}
  });

  /* ---------- operatories ---------- */
  let editOpNum=null,provList=[];
  function provOptions(sel,label){
    return `<option value="0">(no ${label})</option>`+
      provList.map(p=>`<option value="${p.provNum}"${p.provNum==sel?" selected":""}>${esc(p.abbr)}</option>`).join("");
  }
  async function loadOps(){
    opEditReset();
    try{
      const ops=await api("/api/setup/operatories");
      $("spe-ops").querySelector("tbody").innerHTML=ops.map(o=>
        `<tr data-op="${o.operatoryNum}">`+
        `<td>${esc(o.opName)}</td><td class="mono">${esc(o.abbrev)}</td>`+
        `<td>${esc(o.provDentist)}</td><td>${esc(o.provHygienist)}</td>`+
        `<td>${o.isHygiene?'<span class="tag">hyg</span>':""}${o.isHidden?' <span class="tag">hidden</span>':""}</td>`+
        `<td><button type="button" class="quiet" data-edit>Edit</button></td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No operatories</b>Add the first one below.</div></td></tr>`;
      $("spe-ops").querySelectorAll("[data-edit]").forEach(b=>b.addEventListener("click",()=>opEditStart(b.closest("tr").dataset.op)));
    }catch(e){$("spe-op-msg").textContent="operatories endpoint unavailable"}
  }
  async function opEditStart(opNum){
    try{
      const o=await api(`/api/setup/operatories/${opNum}`);
      editOpNum=opNum;
      const f=$("spe-op-form");
      f.opName.value=o.opName;f.abbrev.value=o.abbrev;
      f.provDentist.innerHTML=provOptions(o.provDentist,"dentist");
      f.provHygienist.innerHTML=provOptions(o.provHygienist,"hygienist");
      f.isHygiene.checked=o.isHygiene;f.isHidden.checked=o.isHidden;
      $("spe-op-hiddenwrap").hidden=false;$("spe-op-cancel").hidden=false;
      $("spe-op-save").textContent="Save";$("spe-op-msg").textContent=`Editing ${o.opName}`;
    }catch(err){$("spe-op-msg").textContent="Load failed: "+err.message.slice(0,80)}
  }
  function opEditReset(){
    editOpNum=null;
    const f=$("spe-op-form");f.reset();
    f.provDentist.innerHTML=provOptions(0,"dentist");
    f.provHygienist.innerHTML=provOptions(0,"hygienist");
    $("spe-op-hiddenwrap").hidden=true;$("spe-op-cancel").hidden=true;
    $("spe-op-save").textContent="Add";$("spe-op-msg").textContent="";
  }
  $("spe-op-cancel").addEventListener("click",opEditReset);
  $("spe-op-form").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target;
    const body={opName:f.opName.value,abbrev:f.abbrev.value,
      provDentist:+f.provDentist.value,provHygienist:+f.provHygienist.value,
      isHygiene:f.isHygiene.checked};
    $("spe-op-msg").textContent="Saving…";
    try{
      if(editOpNum){
        body.isHidden=f.isHidden.checked;
        await api(`/api/setup/operatories/${editOpNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        await api("/api/setup/operatories",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      loadOps();
    }catch(err){$("spe-op-msg").textContent="Save failed: "+err.message.slice(0,80)}
  });

  /* ---------- providers ---------- */
  let editProvNum=null;
  async function loadProvs(){
    provEditReset();
    try{
      provList=await api("/api/setup/providers");
      $("spe-provs").querySelector("tbody").innerHTML=provList.map(p=>
        `<tr class="rowsel" data-prov="${p.provNum}">`+
        `<td>${swatch(p.color)}</td><td>${esc(p.abbr)}</td><td>${esc(p.lName)}</td><td>${esc(p.fName)}</td>`+
        `<td>${p.isHidden?'<span class="tag">hidden</span>':""}</td>`+
        `<td><button type="button" class="quiet" data-edit>Edit</button></td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No providers</b>Add one from the Setup page first.</div></td></tr>`;
      $("spe-provs").querySelectorAll("tr[data-prov]").forEach(tr=>
        tr.addEventListener("click",()=>provEditStart(tr.dataset.prov)));
      opEditReset();//refresh dentist/hygienist pickers with the current provider list
    }catch(e){$("spe-prov-hint").textContent="providers endpoint unavailable"}
  }
  async function provEditStart(provNum){
    try{
      const p=await api(`/api/setup/providers/${provNum}`);
      editProvNum=provNum;
      const f=$("spe-prov-form");
      f.abbr.value=p.abbr;f.lName.value=p.lName;f.fName.value=p.fName;f.suffix.value=p.suffix;
      f.useColor.checked=!!p.color;
      if(p.color)f.color.value=p.color;
      f.isSecondary.checked=p.isSecondary;f.isHidden.checked=p.isHidden;
      f.style.display="flex";$("spe-prov-hint").hidden=true;
      $("spe-prov-msg").textContent=`Editing ${p.abbr}`;
    }catch(err){$("spe-prov-hint").textContent="Load failed: "+err.message.slice(0,80)}
  }
  function provEditReset(){
    editProvNum=null;
    const f=$("spe-prov-form");f.reset();f.style.display="none";
    $("spe-prov-hint").hidden=false;$("spe-prov-msg").textContent="";
  }
  function provAddStart(){
    provEditReset();
    const f=$("spe-prov-form");
    f.style.display="flex";$("spe-prov-hint").hidden=true;
    $("spe-prov-msg").textContent="New provider";
    f.abbr.focus();
  }
  $("spe-prov-new").addEventListener("click",provAddStart);
  $("spe-prov-cancel").addEventListener("click",provEditReset);
  $("spe-prov-form").addEventListener("submit",async e=>{
    e.preventDefault();
    const f=e.target;
    const body={abbr:f.abbr.value,lName:f.lName.value,fName:f.fName.value,
      suffix:f.suffix.value,color:f.useColor.checked?f.color.value:"",
      isHidden:f.isHidden.checked,isSecondary:f.isSecondary.checked};
    $("spe-prov-msg").textContent="Saving…";
    try{
      let provNum=editProvNum;
      if(!provNum){
        //Create first (Providers.Insert), then apply the extra fields via the edit endpoint.
        const r=await api("/api/setup/providers",{method:"POST",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({abbr:body.abbr,lName:body.lName,fName:body.fName})});
        provNum=r.provNum;
      }
      if(provNum&&(editProvNum||body.suffix||body.color||body.isHidden||body.isSecondary)){
        await api(`/api/setup/providers/${provNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      loadProvs();
    }catch(err){$("spe-prov-msg").textContent="Save failed: "+err.message.slice(0,80)}
  });

  loadDefCats();loadProvs().then(loadOps);
}});
})();
