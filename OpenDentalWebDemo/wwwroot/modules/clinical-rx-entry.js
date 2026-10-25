/* Rx entry module: prescriptions for the active patient — web replacement for
   FormRxSelect / FormRxEdit / FormRxManage. New Rx starts from an RxDef template
   (drug, sig, disp, refills, controlled prefilled) or blank; edit/delete existing
   RxPats over the real RxPats business layer. Complements the read-only
   RpPrescriptions report. */
"use strict";
registerODModule({
  id:"clinical-rx-entry",
  label:"Rx",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Prescriptions</h1>
    <span class="sub">Rx entry from templates — real RxDefs / RxPats business layer</span>
    <button type="button" class="quiet" id="rx-reload">Refresh</button>
  </div>
  <div class="cols" style="grid-template-columns:minmax(380px,3fr) minmax(320px,2fr)">

    <div class="card"><h2>Patient prescriptions</h2>
      <div class="gridwrap scrolly" style="max-height:420px"><table class="grid" id="rx-grid">
        <thead><tr><th>Date</th><th>Drug</th><th>Sig</th><th>Disp</th><th>Refills</th><th>Prov</th><th></th><th>Status</th><th></th></tr></thead>
        <tbody></tbody>
      </table></div>
    </div>

    <div class="card"><h2 id="rx-form-title">New prescription</h2>
      <form id="rx-form" style="display:grid;gap:6px">
        <select name="template" title="Rx template (prefills drug, sig, disp, refills)">
          <option value="0">— blank Rx (no template) —</option>
        </select>
        <input name="drug" placeholder="Drug (e.g. PenVK 500 mg capsules)" required>
        <input name="sig" placeholder="Sig — directions for the pharmacist">
        <div style="display:flex;gap:8px">
          <input name="disp" placeholder="Disp" style="flex:1">
          <input name="refills" placeholder="Refills" style="flex:1">
        </div>
        <textarea name="patientInstruction" placeholder="Patient instructions" rows="2"></textarea>
        <input name="notes" placeholder="Notes (staff only, not printed)">
        <div style="display:flex;gap:8px;align-items:center">
          <input name="date" type="date" title="Rx date">
          <select name="prov" title="Provider" style="flex:1"></select>
        </div>
        <div style="display:flex;gap:8px;align-items:center">
          <label class="msg"><input type="checkbox" name="controlled"> controlled substance</label>
          <select name="status" id="rx-status" hidden title="Send status">
            <option>Unsent</option><option>Printed</option><option>Faxed</option>
            <option>CalledIn</option><option>GaveScript</option><option>Pending</option>
          </select>
        </div>
        <div style="display:flex;gap:8px;align-items:center">
          <button class="action" id="rx-save">Add Rx</button>
          <button type="button" class="quiet" id="rx-new" hidden>New</button>
          <button type="button" class="quiet" id="rx-delete" hidden>Delete</button>
          <span class="msg" id="rx-msg"></span>
        </div>
      </form>
    </div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    let selRxNum=0;//0 = adding new
    let templates=[];
    const pat=()=>getActivePatient()?.patNum;
    const form=$("rx-form");

    async function loadDefs(){
      const [defs,provs]=await Promise.all([api("/api/rxdefs"),api("/api/setup/providers")]);
      templates=defs;
      form.template.innerHTML=`<option value="0">— blank Rx (no template) —</option>`+
        defs.map(d=>`<option value="${d.rxDefNum}">${esc(d.drug)}${d.isControlled?" (C)":""}</option>`).join("");
      form.prov.innerHTML=`<option value="0">— patient's provider —</option>`+
        provs.filter(p=>!p.isHidden).map(p=>
        `<option value="${p.provNum}">${esc(p.abbr)}</option>`).join("");
    }

    async function loadRx(){
      if(!pat())return;
      const rows=await api(`/api/patients/${pat()}/rx`);
      $("rx-grid").querySelector("tbody").innerHTML=rows.map(r=>
        `<tr class="rowsel" data-row='${esc(JSON.stringify(r))}'>`+
        `<td class="mono">${esc(r.rxDate)}</td><td>${esc(r.drug)}</td><td>${esc(r.sig)}</td>`+
        `<td>${esc(r.disp)}</td><td>${esc(r.refills)}</td><td>${esc(r.provAbbr)}</td>`+
        `<td>${r.isControlled?'<span class="tag">C</span>':""}</td>`+
        `<td><span class="tag">${esc(r.sendStatus)}</span></td>`+
        `<td><a data-view="Rx — ${esc(r.drug)}" href="/api/rx/${r.rxNum}/pdf" onclick="event.stopPropagation()">print</a></td></tr>`).join("")
        ||`<tr><td colspan="9"><div class="empty"><b>No prescriptions</b>Write one on the right.</div></td></tr>`;
      $("rx-grid").querySelectorAll("tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
        const r=JSON.parse(tr.dataset.row);
        selRxNum=r.rxNum;
        form.template.value="0";form.template.disabled=true;
        form.drug.value=r.drug;form.sig.value=r.sig;form.disp.value=r.disp;
        form.refills.value=r.refills;form.notes.value=r.notes;
        form.patientInstruction.value=r.patientInstruction;
        form.date.value=r.rxDate;form.controlled.checked=r.isControlled;
        if(r.provNum)form.prov.value=r.provNum;
        form.status.value=r.sendStatus;$("rx-status").hidden=false;
        $("rx-form-title").textContent="Edit prescription";
        $("rx-save").textContent="Save Rx";$("rx-new").hidden=false;$("rx-delete").hidden=false;
      }));
    }

    function reset(){
      form.reset();selRxNum=0;form.template.disabled=false;
      $("rx-status").hidden=true;$("rx-form-title").textContent="New prescription";
      $("rx-save").textContent="Add Rx";$("rx-new").hidden=true;$("rx-delete").hidden=true;
    }

    //Template pick prefills the editable fields, like FormRxSelect handing off to FormRxEdit.
    form.template.addEventListener("change",()=>{
      const d=templates.find(t=>t.rxDefNum==form.template.value);
      if(!d)return;
      form.drug.value=d.drug;form.sig.value=d.sig;form.disp.value=d.disp;
      form.refills.value=d.refills;form.patientInstruction.value=d.patientInstruction;
      form.controlled.checked=d.isControlled;//Notes intentionally not copied from the template
    });

    form.addEventListener("submit",async e=>{
      e.preventDefault();if(!pat())return;
      const msg=$("rx-msg");msg.textContent="Saving…";
      const body={rxDefNum:0,rxDate:form.date.value,drug:form.drug.value,
        sig:form.sig.value,disp:form.disp.value,refills:form.refills.value,
        notes:form.notes.value,patientInstruction:form.patientInstruction.value,
        isControlled:form.controlled.checked,provNum:+form.prov.value||0,
        sendStatus:selRxNum?form.status.value:"Unsent"};
      try{
        if(selRxNum)await api(`/api/rx/${selRxNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        else await api(`/api/patients/${pat()}/rx`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        msg.textContent="Saved.";reset();loadRx();
      }catch(err){msg.textContent="Failed: "+err.message.slice(0,60)}
    });

    $("rx-delete").addEventListener("click",async ()=>{
      if(!selRxNum||!confirm("Delete this prescription?"))return;
      const msg=$("rx-msg");msg.textContent="Deleting…";
      try{
        await api(`/api/rx/${selRxNum}`,{method:"DELETE"});
        msg.textContent="Deleted.";reset();loadRx();
      }catch(err){msg.textContent="Failed: "+err.message.slice(0,60)}
    });

    $("rx-new").addEventListener("click",reset);
    function loadAll(){reset();loadRx();}
    $("rx-reload").addEventListener("click",loadAll);
    $("band-patient").addEventListener("change",loadAll);
    loadDefs().then(loadAll).catch(e=>{$("rx-msg").textContent="rx endpoints unavailable";console.error(e)});
  }
});
