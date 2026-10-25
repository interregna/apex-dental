/* Clinics setup — list / add / edit / hide clinics over the real Clinics
   business layer (Clinics.Insert/Update/RefreshCache, audit-logged). One
   list-detail surface; retires FormClinics and the core of FormClinicEdit. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;

registerODModule({
id:"setup-clinics",
label:"Clinics",
group:"Practice",
html:`
<div class="mod-head"><h1>Clinics</h1>
  <span class="sub">separate office locations — add/edit/hide through the audit-logged business layer</span></div>
<div class="cols" style="grid-template-columns:minmax(320px,1fr) minmax(300px,420px);align-items:start">

  <div class="card"><h2>Clinics
      <label style="font-size:12px;font-weight:400;margin-left:10px"><input type="checkbox" id="sc-showhidden"> show hidden</label>
      <button type="button" class="quiet" id="sc-new" style="margin-left:8px">Add clinic</button></h2>
    <div class="gridwrap scrolly"><table class="grid" id="sc-list">
      <thead><tr><th>Abbr</th><th>Description</th><th>City</th><th>Phone</th><th></th><th></th></tr></thead><tbody></tbody></table></div>
  </div>

  <div class="card" id="sc-formcard" style="display:none"><h2 id="sc-formtitle">Add clinic</h2>
    <form id="sc-form" style="display:flex;flex-direction:column;gap:8px">
      <div style="display:flex;gap:6px;flex-wrap:wrap">
        <input name="abbr" placeholder="Abbr" required size="8">
        <input name="description" placeholder="Description" required style="flex:1 1 160px">
      </div>
      <input name="address" placeholder="Address">
      <input name="address2" placeholder="Address line 2">
      <div style="display:flex;gap:6px;flex-wrap:wrap">
        <input name="city" placeholder="City" style="flex:1 1 120px">
        <input name="state" placeholder="ST" maxlength="3" size="4">
        <input name="zip" placeholder="Zip" size="8">
      </div>
      <div style="display:flex;gap:6px;flex-wrap:wrap;align-items:center">
        <input name="phone" placeholder="Phone" style="flex:1 1 120px">
        <label style="font-size:12px">Default prov
          <select name="defaultProv" aria-label="Default provider"></select></label>
      </div>
      <label style="font-size:12px"><input type="checkbox" name="isHidden"> hidden</label>
      <div style="display:flex;gap:6px;align-items:center">
        <button class="action" id="sc-save">Add</button>
        <button type="button" class="quiet" id="sc-cancel">Cancel</button>
        <span class="msg" id="sc-msg"></span>
      </div>
    </form>
  </div>
</div>`,
init(){
  let editClinicNum=null,provList=[];

  function provOptions(sel){
    return `<option value="0">(none)</option>`+
      provList.map(p=>`<option value="${p.provNum}"${p.provNum==sel?" selected":""}>${esc(p.abbr)}</option>`).join("");
  }

  async function loadProvs(){
    try{ provList=await api("/api/setup/providers"); }
    catch(e){ provList=[]; }
  }

  async function loadList(){
    const includeHidden=$("sc-showhidden").checked;
    try{
      const clinics=await api(`/api/clinics?includeHidden=${includeHidden?"true":"false"}`);
      $("sc-list").querySelector("tbody").innerHTML=clinics.map(c=>
        `<tr data-clinic="${c.clinicNum}">`+
        `<td class="mono">${esc(c.abbr)}</td><td>${esc(c.description)}</td>`+
        `<td>${esc(c.city)}</td><td>${esc(c.phone)}</td>`+
        `<td>${c.isHidden?'<span class="tag">hidden</span>':""}</td>`+
        `<td style="white-space:nowrap"><button type="button" class="quiet" data-edit>Edit</button> `+
        `<button type="button" class="quiet" data-hide>${c.isHidden?"Unhide":"Hide"}</button></td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No clinics</b>Add the first one.</div></td></tr>`;
      $("sc-list").querySelectorAll("[data-edit]").forEach(b=>
        b.addEventListener("click",()=>editStart(b.closest("tr").dataset.clinic)));
      $("sc-list").querySelectorAll("[data-hide]").forEach(b=>
        b.addEventListener("click",()=>toggleHide(b.closest("tr").dataset.clinic)));
    }catch(e){$("sc-msg").textContent="clinics endpoint unavailable"}
  }

  function fillForm(c){
    const f=$("sc-form");
    f.abbr.value=c.abbr||"";f.description.value=c.description||"";
    f.address.value=c.address||"";f.address2.value=c.address2||"";
    f.city.value=c.city||"";f.state.value=c.state||"";f.zip.value=c.zip||"";
    f.phone.value=c.phone||"";
    f.defaultProv.innerHTML=provOptions(c.defaultProv||0);
    f.isHidden.checked=!!c.isHidden;
  }

  function showForm(title,saveLabel){
    $("sc-formtitle").textContent=title;
    $("sc-save").textContent=saveLabel;
    $("sc-formcard").style.display="";
    $("sc-msg").textContent="";
  }

  function addStart(){
    editClinicNum=null;
    $("sc-form").reset();
    fillForm({});
    showForm("Add clinic","Add");
    $("sc-form").abbr.focus();
  }

  async function editStart(clinicNum){
    try{
      const c=await api(`/api/clinics/${clinicNum}`);
      editClinicNum=clinicNum;
      fillForm(c);
      showForm(`Edit ${c.abbr}`,"Save");
    }catch(err){$("sc-msg").textContent="Load failed: "+err.message.slice(0,80)}
  }

  function cancelForm(){
    editClinicNum=null;
    $("sc-form").reset();
    $("sc-formcard").style.display="none";
  }

  async function toggleHide(clinicNum){
    try{
      const c=await api(`/api/clinics/${clinicNum}`);
      await api(`/api/clinics/${clinicNum}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({
          description:c.description,abbr:c.abbr,address:c.address,address2:c.address2,
          city:c.city,state:c.state,zip:c.zip,phone:c.phone,
          defaultProv:c.defaultProv,isHidden:!c.isHidden})});
      loadList();
    }catch(err){$("sc-msg").textContent="Failed: "+err.message.slice(0,80)}
  }

  $("sc-showhidden").addEventListener("change",loadList);
  $("sc-new").addEventListener("click",addStart);
  $("sc-cancel").addEventListener("click",cancelForm);
  $("sc-form").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target;
    const body={
      description:f.description.value,abbr:f.abbr.value,
      address:f.address.value,address2:f.address2.value,
      city:f.city.value,state:f.state.value,zip:f.zip.value,
      phone:f.phone.value,defaultProv:+f.defaultProv.value,isHidden:f.isHidden.checked};
    $("sc-msg").textContent="Saving…";
    try{
      if(editClinicNum){
        await api(`/api/clinics/${editClinicNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        await api("/api/clinics",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      cancelForm();
      loadList();
    }catch(err){$("sc-msg").textContent="Save failed: "+err.message.slice(0,80)}
  });

  loadProvs().then(loadList);
}});
})();
