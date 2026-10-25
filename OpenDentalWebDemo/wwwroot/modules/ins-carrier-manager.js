/* Insurance carrier manager (ins-carrier-manager): searchable carrier grid with
   add/edit, delete guarded by dependent plans, and merge-duplicates.
   Auto-loaded by app.js; talks to InsCarrierManagerModule.cs endpoints, which run
   the real Carriers business layer (GetBigList / Insert / Update / Delete /
   DependentPlans / Combine — all mutations InsEditLog-audited). */
"use strict";
registerODModule({
  id:"ins-carrier-manager",
  label:"Carriers",
  group:"Insurance",
  html:`
  <div class="mod-head"><h1>Insurance carriers</h1>
    <span class="sub">carrier list · add / edit · merge duplicates</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>Carrier list <span class="msg" id="car-list-msg"></span></h2>
      <form class="row" id="car-filter" style="margin-bottom:10px">
        <input id="car-f-name" placeholder="Name contains" style="width:140px">
        <input id="car-f-phone" placeholder="Phone digits" style="width:110px">
        <input id="car-f-electid" placeholder="Electronic ID" style="width:100px">
        <label style="display:flex;align-items:center;gap:4px;font-size:12px">
          <input type="checkbox" id="car-f-hidden">Show hidden</label>
        <button class="action">Search</button>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="car-list">
        <thead><tr><th title="Select for combine"></th><th>Carrier</th><th class="mono">Phone</th>
          <th>City</th><th>ST</th><th class="mono">Elect ID</th><th>Plans</th><th>Flags</th><th></th></tr></thead>
        <tbody></tbody></table></div>
      <div class="row" id="car-combine-bar" style="margin-top:10px;display:none">
        <span style="font-size:12px">Combine selected into</span>
        <select id="car-combine-pick"></select>
        <button type="button" class="action" id="car-combine-btn">Combine</button>
        <span class="msg" id="car-combine-msg"></span>
      </div>
    </div>
    <div class="card">
      <h2 id="car-edit-title">Add carrier <span class="msg" id="car-edit-msg"></span></h2>
      <form id="car-edit-form">
        <div class="row" style="margin-bottom:8px">
          <input id="car-e-name" placeholder="Carrier name" required style="width:220px">
          <input id="car-e-phone" placeholder="Phone" style="width:120px">
        </div>
        <div class="row" style="margin-bottom:8px">
          <input id="car-e-address" placeholder="Address" style="width:180px">
          <input id="car-e-address2" placeholder="Address 2" style="width:120px">
        </div>
        <div class="row" style="margin-bottom:8px">
          <input id="car-e-city" placeholder="City" style="width:130px">
          <input id="car-e-state" placeholder="ST" style="width:44px" maxlength="2">
          <input id="car-e-zip" placeholder="Zip" style="width:80px">
        </div>
        <div class="row" style="margin-bottom:8px">
          <input id="car-e-electid" placeholder="Electronic ID" style="width:110px">
          <input id="car-e-tin" placeholder="TIN" style="width:100px">
          <select id="car-e-nosend">
            <option value="SendElect">Send claims electronically</option>
            <option value="NoSendElect">Don't send electronically</option>
            <option value="NoSendSecondaryElect">Don't send secondary electronically</option>
          </select>
        </div>
        <div class="row" style="margin-bottom:8px">
          <label style="display:flex;align-items:center;gap:4px;font-size:12px">
            <input type="checkbox" id="car-e-hidden">Hidden</label>
          <label style="display:flex;align-items:center;gap:4px;font-size:12px">
            <input type="checkbox" id="car-e-coinsinv">Coinsurance inverted</label>
        </div>
        <div class="row">
          <button class="action" id="car-e-save">Save</button>
          <button type="button" class="quiet" id="car-e-new">New / clear</button>
          <button type="button" class="quiet" id="car-e-delete" style="display:none">Delete</button>
        </div>
      </form>
      <div id="car-dependents" style="margin-top:10px;font-size:12px;opacity:.75"></div>
    </div>
  </div>`,
  init(){
    const {api,esc,$}=window.ODM;
    let editingNum=0;//0 = adding a new carrier

    function filterQuery(){
      const q=new URLSearchParams({
        name:$("car-f-name").value.trim(),
        phone:$("car-f-phone").value.trim(),
        electId:$("car-f-electid").value.trim(),
        showHidden:$("car-f-hidden").checked,
      });
      return "/api/carriers?"+q.toString();
    }

    async function loadList(){
      try{
        const rows=await api(filterQuery());
        $("car-list").querySelector("tbody").innerHTML=rows.map(c=>
          `<tr><td><input type="checkbox" data-sel="${c.carrierNum}" data-name="${esc(c.carrierName)}"></td>`+
          `<td>${esc(c.carrierName)}</td><td class="mono">${esc(c.phone)}</td>`+
          `<td>${esc(c.city)}</td><td>${esc(c.state)}</td><td class="mono">${esc(c.electId)}</td>`+
          `<td>${esc(c.insPlanCount)}</td>`+
          `<td>${c.isHidden?'<span class="tag">Hidden</span>':""}${c.isCDA?' <span class="tag">CDA</span>':""}</td>`+
          `<td><button type="button" class="quiet" data-edit="${c.carrierNum}">Edit</button></td></tr>`).join("")
          ||`<tr><td colspan="9"><div class="empty"><b>No carriers match</b>Adjust the filters or add a carrier at right.</div></td></tr>`;
        $("car-list-msg").textContent="";
        $("car-list").querySelectorAll("[data-edit]").forEach(btn=>
          btn.addEventListener("click",()=>loadEditor(+btn.dataset.edit)));
        $("car-list").querySelectorAll("[data-sel]").forEach(chk=>
          chk.addEventListener("change",updateCombineBar));
        updateCombineBar();
      }catch(e){$("car-list-msg").textContent="carriers endpoint unavailable"}
    }

    function selectedCarriers(){
      return [...$("car-list").querySelectorAll("[data-sel]:checked")]
        .map(x=>({carrierNum:+x.dataset.sel,name:x.dataset.name}));
    }

    function updateCombineBar(){
      const sel=selectedCarriers();
      $("car-combine-bar").style.display=sel.length>=2?"flex":"none";
      if(sel.length>=2){
        $("car-combine-pick").innerHTML=sel.map(s=>
          `<option value="${s.carrierNum}">${esc(s.name)}</option>`).join("");
      }
    }

    $("car-combine-btn").addEventListener("click",async()=>{
      const sel=selectedCarriers();
      if(sel.length<2)return;
      const picked=+$("car-combine-pick").value;
      const keepName=sel.find(s=>s.carrierNum===picked)?.name||"";
      if(!confirm(`Combine ${sel.length} carriers into "${keepName}"? The others will be deleted and their plans moved. This cannot be undone.`))return;
      $("car-combine-msg").textContent="Combining…";
      try{
        await api("/api/carriers/combine",{method:"POST",headers:{"Content-Type":"application/json"},
          body:JSON.stringify({carrierNums:sel.map(s=>s.carrierNum),pickedCarrierNum:picked})});
        $("car-combine-msg").textContent="Combined.";
        clearEditor();loadList();
      }catch(e){$("car-combine-msg").textContent="Combine failed: "+e.message.slice(0,80)}
    });

    async function loadEditor(carrierNum){
      try{
        const c=await api(`/api/carriers/${carrierNum}`);
        editingNum=c.carrierNum;
        $("car-edit-title").firstChild.textContent="Edit carrier ";
        $("car-e-name").value=c.carrierName;$("car-e-phone").value=c.phone;
        $("car-e-address").value=c.address;$("car-e-address2").value=c.address2;
        $("car-e-city").value=c.city;$("car-e-state").value=c.state;$("car-e-zip").value=c.zip;
        $("car-e-electid").value=c.electId;$("car-e-tin").value=c.tin;
        $("car-e-nosend").value=c.noSendElect;
        $("car-e-hidden").checked=c.isHidden;$("car-e-coinsinv").checked=c.isCoinsuranceInverted;
        $("car-e-delete").style.display="";
        $("car-edit-msg").textContent="";
        loadDependents(carrierNum);
      }catch(e){$("car-edit-msg").textContent="Load failed: "+e.message.slice(0,60)}
    }

    async function loadDependents(carrierNum){
      try{
        const deps=await api(`/api/carriers/${carrierNum}/dependents`);
        $("car-dependents").innerHTML=deps.length
          ?`<b>${deps.length} dependent plan${deps.length===1?"":"s"}</b> (subscribers): ${esc(deps.slice(0,20).join("; "))}${deps.length>20?" …":""}`
          :"No insurance plans depend on this carrier.";
      }catch(e){$("car-dependents").textContent=""}
    }

    function clearEditor(){
      editingNum=0;
      $("car-edit-form").reset();
      $("car-edit-title").firstChild.textContent="Add carrier ";
      $("car-e-delete").style.display="none";
      $("car-dependents").textContent="";
      $("car-edit-msg").textContent="";
    }

    function dtoFromForm(){
      return {
        carrierName:$("car-e-name").value,phone:$("car-e-phone").value,
        address:$("car-e-address").value,address2:$("car-e-address2").value,
        city:$("car-e-city").value,state:$("car-e-state").value,zip:$("car-e-zip").value,
        electID:$("car-e-electid").value,tin:$("car-e-tin").value,
        noSendElect:$("car-e-nosend").value,
        isHidden:$("car-e-hidden").checked,
        isCoinsuranceInverted:$("car-e-coinsinv").checked,
      };
    }

    $("car-edit-form").addEventListener("submit",async e=>{
      e.preventDefault();$("car-edit-msg").textContent="Saving…";
      try{
        if(editingNum){
          await api(`/api/carriers/${editingNum}`,{method:"PUT",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(dtoFromForm())});
        }else{
          await api("/api/carriers",{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(dtoFromForm())});
          clearEditor();
        }
        $("car-edit-msg").textContent="Saved.";
        loadList();
      }catch(err){$("car-edit-msg").textContent="Save failed: "+err.message.slice(0,100)}
    });

    $("car-e-new").addEventListener("click",clearEditor);

    $("car-e-delete").addEventListener("click",async()=>{
      if(!editingNum)return;
      if(!confirm(`Delete carrier "${$("car-e-name").value}"? Blocked if any plans or claim history use it.`))return;
      $("car-edit-msg").textContent="Deleting…";
      try{
        await api(`/api/carriers/${editingNum}`,{method:"DELETE"});
        clearEditor();loadList();
      }catch(err){$("car-edit-msg").textContent="Delete blocked: "+err.message.slice(0,120)}
    });

    $("car-filter").addEventListener("submit",e=>{e.preventDefault();loadList();});
    $("car-f-hidden").addEventListener("change",loadList);
    loadList();
  }
});
