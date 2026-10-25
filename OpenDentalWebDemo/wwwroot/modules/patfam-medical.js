/* Medical module (patfam-medical): medications, allergies, problems.
   FormMedical / FormAllergyEdit / FormMedPat as three cards over
   /api/patients/{patNum}/medical; adds and edits go through the same
   MedicationPats/Allergies/Diseases business calls the WinForms dialogs make.
   Supersedes the former clinical-med-history module (same business layer). */
"use strict";
(function(){
const {api,esc,$}=window.ODM;
let medData=null;

registerODModule({
  id:"patfam-medical",
  label:"Medical",
  group:"Patient",
  html:`
  <div class="mod-head"><h1>Medical</h1><span class="sub">medications · allergies · problems — real MedicationPats / Allergies / Diseases</span></div>
  <div style="display:flex;gap:14px;align-items:center;margin-bottom:12px">
    <label style="font-size:12.5px"><input type="checkbox" id="pfm-inactive"> Show discontinued / inactive</label>
    <button type="button" class="quiet" id="pfm-reload">Refresh</button>
    <span class="msg" id="pfm-msg"></span>
  </div>
  <div style="display:grid;gap:14px;grid-template-columns:repeat(auto-fit,minmax(340px,1fr))">
    <div class="card"><h2>Medications</h2>
      <div class="gridwrap scrolly"><table class="grid" id="pfm-meds"><thead>
        <tr><th>Medication</th><th>Pat note</th><th>Start</th><th>Stop</th><th>Status</th><th></th></tr>
      </thead><tbody></tbody></table></div>
      <form id="pfm-medform">
        <input type="hidden" name="medicationPatNum" value="0">
        <select name="medicationNum" style="max-width:180px"><option value="0">— pick medication —</option></select>
        <input name="newMedName" placeholder="…or new medication name" style="max-width:170px">
        <input name="patNote" placeholder="Patient note (dosage etc.)" style="max-width:170px">
        <input name="dateStart" type="date" title="Start date">
        <input name="dateStop" type="date" title="Stop date (past = discontinued)">
        <button class="action" id="pfm-medsave">Add medication</button>
        <button type="button" class="quiet" id="pfm-mednew" hidden>New</button>
        <span class="msg" id="pfm-medmsg"></span>
      </form>
    </div>
    <div class="card"><h2>Allergies</h2>
      <div class="gridwrap scrolly"><table class="grid" id="pfm-algs"><thead>
        <tr><th>Allergy</th><th>Reaction</th><th>Date</th><th>Status</th><th></th></tr>
      </thead><tbody></tbody></table></div>
      <form id="pfm-algform">
        <input type="hidden" name="allergyNum" value="0">
        <select name="allergyDefNum" style="max-width:180px"><option value="0">— pick allergy —</option></select>
        <input name="reaction" placeholder="Reaction" style="max-width:170px">
        <input name="dateAdverseReaction" type="date" title="Adverse reaction date">
        <label style="font-size:12px;align-self:center"><input type="checkbox" name="statusIsActive" checked> Active</label>
        <button class="action" id="pfm-algsave">Add allergy</button>
        <button type="button" class="quiet" id="pfm-algnew" hidden>New</button>
        <span class="msg" id="pfm-algmsg"></span>
      </form>
    </div>
    <div class="card"><h2>Problems</h2>
      <div class="gridwrap scrolly"><table class="grid" id="pfm-probs"><thead>
        <tr><th>Problem</th><th>Note</th><th>Start</th><th>Stop</th><th>Status</th><th></th></tr>
      </thead><tbody></tbody></table></div>
      <form id="pfm-probform">
        <input type="hidden" name="diseaseNum" value="0">
        <select name="diseaseDefNum" style="max-width:180px"><option value="0">— pick problem —</option></select>
        <input name="patNote" placeholder="Note" style="max-width:170px">
        <select name="probStatus"><option>Active</option><option>Resolved</option><option>Inactive</option></select>
        <input name="dateStart" type="date" title="Start date">
        <input name="dateStop" type="date" title="Stop date">
        <button class="action" id="pfm-probsave">Add problem</button>
        <button type="button" class="quiet" id="pfm-probnew" hidden>New</button>
        <span class="msg" id="pfm-probmsg"></span>
      </form>
    </div>
  </div>`,
  init(){
    $("pfm-reload").addEventListener("click",load);
    $("pfm-inactive").addEventListener("change",load);
    /* reload when the patient band changes or the rail button shows this module */
    $("band-patient").addEventListener("change",()=>setTimeout(load,0));
    const railBtn=document.querySelector('nav.rail button[data-mod="patfam-medical"]');
    if(railBtn)railBtn.addEventListener("click",load);
    wireForms();
    load();
  }
});

function pat(){return window.ODM.getActivePatient()}
const empty=(cols,what)=>`<tr><td colspan="${cols}"><div class="empty"><b>No ${what}</b>Add one with the form below.</div></td></tr>`;

async function load(){
  const p=pat();
  if(!p){$("pfm-msg").textContent="Select a patient first.";return}
  try{
    medData=await api(`/api/patients/${p.patNum}/medical?includeInactive=${$("pfm-inactive").checked}`);
    $("pfm-msg").textContent=p.name;
  }catch(e){$("pfm-msg").textContent="medical endpoint unavailable — "+e.message.slice(0,60);return}
  render();
}

function render(){
  const d=medData;
  $("pfm-meds").querySelector("tbody").innerHTML=d.medications.map(m=>
    `<tr class="rowsel" data-medpat="${m.medicationPatNum}"${m.isActive?"":' style="opacity:.55"'}>`+
    `<td>${esc(m.name)}${m.generic?` <span class="tag">${esc(m.generic)}</span>`:""}</td>`+
    `<td>${esc(m.patNote)}</td><td class="mono">${m.dateStart}</td><td class="mono">${m.dateStop}</td>`+
    `<td><span class="tag">${m.isActive?"Active":"Stopped"}</span></td>`+
    `<td><button type="button" class="quiet" data-stopmed="${m.medicationPatNum}">${m.isActive?"Stop":"Resume"}</button></td></tr>`).join("")
    ||empty(6,"medications");
  $("pfm-algs").querySelector("tbody").innerHTML=d.allergies.map(a=>
    `<tr class="rowsel" data-alg="${a.allergyNum}"${a.isActive?"":' style="opacity:.55"'}>`+
    `<td>${esc(a.description)}</td><td>${esc(a.reaction)}</td><td class="mono">${a.dateAdverseReaction}</td>`+
    `<td><span class="tag">${a.isActive?"Active":"Inactive"}</span></td>`+
    `<td><button type="button" class="quiet" data-togalg="${a.allergyNum}">${a.isActive?"Inactivate":"Activate"}</button></td></tr>`).join("")
    ||empty(5,"allergies");
  $("pfm-probs").querySelector("tbody").innerHTML=d.problems.map(pr=>
    `<tr class="rowsel" data-prob="${pr.diseaseNum}"${pr.status==="Active"?"":' style="opacity:.55"'}>`+
    `<td>${esc(pr.name)}</td><td>${esc(pr.patNote)}</td><td class="mono">${pr.dateStart}</td><td class="mono">${pr.dateStop}</td>`+
    `<td><span class="tag">${esc(pr.status)}</span></td><td></td></tr>`).join("")
    ||empty(6,"problems");
  fillPicker($("pfm-medform").elements.medicationNum,"— pick medication —",
    d.pickers.medications.map(x=>[x.medicationNum,x.name]));
  fillPicker($("pfm-algform").elements.allergyDefNum,"— pick allergy —",
    d.pickers.allergyDefs.map(x=>[x.allergyDefNum,x.description]));
  fillPicker($("pfm-probform").elements.diseaseDefNum,"— pick problem —",
    d.pickers.diseaseDefs.map(x=>[x.diseaseDefNum,x.name]));
  wireRows();
}

function fillPicker(sel,placeholder,pairs){
  const cur=sel.value;
  sel.innerHTML=`<option value="0">${placeholder}</option>`+
    pairs.map(([v,t])=>`<option value="${v}">${esc(t)}</option>`).join("");
  sel.value=cur&&[...sel.options].some(o=>o.value===cur)?cur:"0";
}

function wireRows(){
  const today=new Date().toISOString().slice(0,10);
  document.querySelectorAll("#pfm-meds [data-stopmed]").forEach(b=>b.addEventListener("click",async e=>{
    e.stopPropagation();
    const m=medData.medications.find(x=>x.medicationPatNum==b.dataset.stopmed);
    try{
      await api(`/api/medications/pat/${m.medicationPatNum}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({patNote:m.patNote,dateStart:m.dateStart,dateStop:m.isActive?today:""})});
      load();
    }catch(err){$("pfm-medmsg").textContent="Failed: "+err.message.slice(0,60)}
  }));
  document.querySelectorAll("#pfm-algs [data-togalg]").forEach(b=>b.addEventListener("click",async e=>{
    e.stopPropagation();
    const a=medData.allergies.find(x=>x.allergyNum==b.dataset.togalg);
    try{
      await api(`/api/allergies/${a.allergyNum}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({allergyDefNum:a.allergyDefNum,reaction:a.reaction,
          dateAdverseReaction:a.dateAdverseReaction,statusIsActive:!a.isActive})});
      load();
    }catch(err){$("pfm-algmsg").textContent="Failed: "+err.message.slice(0,60)}
  }));
  document.querySelectorAll("#pfm-meds tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
    const m=medData.medications.find(x=>x.medicationPatNum==tr.dataset.medpat);
    const f=$("pfm-medform");
    f.elements.medicationPatNum.value=m.medicationPatNum;
    f.elements.medicationNum.value=m.medicationNum;
    f.elements.medicationNum.disabled=true;
    f.elements.newMedName.value="";f.elements.newMedName.disabled=true;
    f.elements.patNote.value=m.patNote;
    f.elements.dateStart.value=m.dateStart;f.elements.dateStop.value=m.dateStop;
    $("pfm-medsave").textContent="Save medication";$("pfm-mednew").hidden=false;
  }));
  document.querySelectorAll("#pfm-algs tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
    const a=medData.allergies.find(x=>x.allergyNum==tr.dataset.alg);
    const f=$("pfm-algform");
    f.elements.allergyNum.value=a.allergyNum;
    f.elements.allergyDefNum.value=a.allergyDefNum;
    f.elements.reaction.value=a.reaction;
    f.elements.dateAdverseReaction.value=a.dateAdverseReaction;
    f.elements.statusIsActive.checked=a.isActive;
    $("pfm-algsave").textContent="Save allergy";$("pfm-algnew").hidden=false;
  }));
  document.querySelectorAll("#pfm-probs tr.rowsel").forEach(tr=>tr.addEventListener("click",()=>{
    const pr=medData.problems.find(x=>x.diseaseNum==tr.dataset.prob);
    const f=$("pfm-probform");
    f.elements.diseaseNum.value=pr.diseaseNum;
    f.elements.diseaseDefNum.value=pr.diseaseDefNum;
    f.elements.patNote.value=pr.patNote;
    f.elements.probStatus.value=pr.status;
    f.elements.dateStart.value=pr.dateStart;f.elements.dateStop.value=pr.dateStop;
    $("pfm-probsave").textContent="Save problem";$("pfm-probnew").hidden=false;
  }));
}

function resetMedForm(){
  const f=$("pfm-medform");f.reset();
  f.elements.medicationPatNum.value="0";
  f.elements.medicationNum.disabled=false;f.elements.newMedName.disabled=false;
  $("pfm-medsave").textContent="Add medication";$("pfm-mednew").hidden=true;
}
function resetAlgForm(){
  const f=$("pfm-algform");f.reset();
  f.elements.allergyNum.value="0";f.elements.statusIsActive.checked=true;
  $("pfm-algsave").textContent="Add allergy";$("pfm-algnew").hidden=true;
}
function resetProbForm(){
  const f=$("pfm-probform");f.reset();
  f.elements.diseaseNum.value="0";
  $("pfm-probsave").textContent="Add problem";$("pfm-probnew").hidden=true;
}

function wireForms(){
  $("pfm-mednew").addEventListener("click",resetMedForm);
  $("pfm-algnew").addEventListener("click",resetAlgForm);
  $("pfm-probnew").addEventListener("click",resetProbForm);
  $("pfm-medform").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target,p=pat();if(!p)return;
    const editing=+f.elements.medicationPatNum.value;
    $("pfm-medmsg").textContent="Saving…";
    const body={patNote:f.elements.patNote.value,
      dateStart:f.elements.dateStart.value,dateStop:f.elements.dateStop.value};
    try{
      if(editing){
        await api(`/api/medications/pat/${editing}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        body.medicationNum=+f.elements.medicationNum.value;
        body.newMedName=f.elements.newMedName.value;
        await api(`/api/patients/${p.patNum}/medications`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      $("pfm-medmsg").textContent="Saved.";resetMedForm();load();
    }catch(err){$("pfm-medmsg").textContent="Failed: "+err.message.slice(0,80)}
  });
  $("pfm-algform").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target,p=pat();if(!p)return;
    const editing=+f.elements.allergyNum.value;
    $("pfm-algmsg").textContent="Saving…";
    const body={allergyDefNum:+f.elements.allergyDefNum.value,
      reaction:f.elements.reaction.value,
      dateAdverseReaction:f.elements.dateAdverseReaction.value,
      statusIsActive:f.elements.statusIsActive.checked};
    try{
      if(editing){
        await api(`/api/allergies/${editing}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        await api(`/api/patients/${p.patNum}/allergies`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      $("pfm-algmsg").textContent="Saved.";resetAlgForm();load();
    }catch(err){$("pfm-algmsg").textContent="Failed: "+err.message.slice(0,80)}
  });
  $("pfm-probform").addEventListener("submit",async e=>{
    e.preventDefault();const f=e.target,p=pat();if(!p)return;
    const editing=+f.elements.diseaseNum.value;
    $("pfm-probmsg").textContent="Saving…";
    const body={diseaseDefNum:+f.elements.diseaseDefNum.value,
      patNote:f.elements.patNote.value,probStatus:f.elements.probStatus.value,
      dateStart:f.elements.dateStart.value,dateStop:f.elements.dateStop.value};
    try{
      if(editing){
        await api(`/api/problems/${editing}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }else{
        await api(`/api/patients/${p.patNum}/problems`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      }
      $("pfm-probmsg").textContent="Saved.";resetProbForm();load();
    }catch(err){$("pfm-probmsg").textContent="Failed: "+err.message.slice(0,80)}
  });
}
})();
