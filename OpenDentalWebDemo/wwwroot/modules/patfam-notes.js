/* Patient notes module (patfam-notes): the per-patient note spread the desktop keeps
   across FormMedical / FormPatientEdit / FormApptsOther / the Account module — family
   financial notes (guarantor-stored), appointment scheduling note, medical notes,
   service/chart notes and the ICE contact. Auto-loaded by app.js; talks to
   PatfamNotesModule.cs, which saves through the real PatientNotes.Update /
   Patients.Update business calls. */
"use strict";
registerODModule({
  id:"patfam-notes",
  label:"Pt Notes",
  group:"Patient",
  html:`
  <div class="mod-head"><h1>Patient notes</h1>
    <span class="sub">family financial &middot; appointment &middot; medical &middot; service &middot; ICE</span></div>
  <div id="ptn-empty" class="card" style="display:none">
    <div class="empty"><b>No active patient</b>Pick a patient in the patient band above to edit their notes.</div>
  </div>
  <div id="ptn-body" style="display:none">
    <div class="card" style="margin-bottom:10px">
      <h2 id="ptn-who"></h2>
      <div class="row">
        <button type="button" class="action" id="ptn-save" disabled>Save</button>
        <span class="msg" id="ptn-msg"></span>
      </div>
    </div>
    <div class="cols c2">
      <div class="card">
        <h2>Family financial <span class="sub" id="ptn-guar"></span></h2>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-famFinUrgNote">Urgent family financial note</label>
        <textarea id="ptn-famFinUrgNote" data-f="famFinUrgNote" rows="3" style="width:100%"></textarea>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-famFinancial">Family financial note</label>
        <textarea id="ptn-famFinancial" data-f="famFinancial" rows="4" style="width:100%"></textarea>
      </div>
      <div class="card">
        <h2>Appointment</h2>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-apptModNote">Appointment scheduling note</label>
        <textarea id="ptn-apptModNote" data-f="apptModNote" rows="4" style="width:100%"></textarea>
        <h2 style="margin-top:12px">ICE — in case of emergency</h2>
        <div class="row">
          <input id="ptn-iceName" data-f="iceName" placeholder="ICE contact name" style="flex:1">
          <input id="ptn-icePhone" data-f="icePhone" placeholder="ICE phone" style="width:140px">
        </div>
      </div>
      <div class="card">
        <h2>Medical</h2>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-medUrgNote">Urgent medical note</label>
        <textarea id="ptn-medUrgNote" data-f="medUrgNote" rows="2" style="width:100%"></textarea>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-medical">Medical summary</label>
        <textarea id="ptn-medical" data-f="medical" rows="4" style="width:100%"></textarea>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-medicalComp">Complete medical history</label>
        <textarea id="ptn-medicalComp" data-f="medicalComp" rows="4" style="width:100%"></textarea>
      </div>
      <div class="card">
        <h2>Service</h2>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-service">Service notes</label>
        <textarea id="ptn-service" data-f="service" rows="4" style="width:100%"></textarea>
        <label style="display:block;font-size:12px;margin:8px 0 3px;opacity:.75" for="ptn-treatment">Chart / odontogram note</label>
        <textarea id="ptn-treatment" data-f="treatment" rows="4" style="width:100%"></textarea>
      </div>
    </div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    const FIELDS=["famFinUrgNote","famFinancial","apptModNote","iceName","icePhone",
      "medUrgNote","medical","medicalComp","service","treatment"];
    let snapshot=null;   //values as loaded, for dirty tracking
    let loadedPatNum=0;

    const ctl=f=>$("ptn-"+f);

    function isDirty(){
      return snapshot!==null && FIELDS.some(f=>ctl(f).value!==snapshot[f]);
    }

    function refreshDirty(){
      const dirty=isDirty();
      $("ptn-save").disabled=!dirty;
      if(dirty){$("ptn-msg").textContent="Unsaved changes";}
      else if($("ptn-msg").textContent==="Unsaved changes"){$("ptn-msg").textContent="";}
    }

    async function load(){
      const pat=getActivePatient();
      if(!pat){
        $("ptn-empty").style.display="";
        $("ptn-body").style.display="none";
        snapshot=null;loadedPatNum=0;
        return;
      }
      try{
        const d=await api(`/api/patients/${pat.patNum}/notes`);
        $("ptn-empty").style.display="none";
        $("ptn-body").style.display="";
        $("ptn-who").innerHTML=`Notes for ${esc(d.patientName)} <span class="sub">#${d.patNum}</span>`;
        $("ptn-guar").textContent=d.isGuarantor
          ?"(this patient is the guarantor)"
          :`(stored with guarantor ${d.guarantorName} #${d.guarantorNum})`;
        snapshot={};
        FIELDS.forEach(f=>{snapshot[f]=d[f]??"";ctl(f).value=snapshot[f];});
        loadedPatNum=d.patNum;
        $("ptn-msg").textContent="";
        refreshDirty();
      }catch(e){
        $("ptn-empty").style.display="none";
        $("ptn-body").style.display="";
        snapshot=null;
        $("ptn-msg").textContent="Load failed: "+e.message.slice(0,80);
      }
    }

    $("ptn-save").addEventListener("click",async()=>{
      if(snapshot===null||!loadedPatNum){return;}
      const body={};
      FIELDS.forEach(f=>body[f]=ctl(f).value);
      $("ptn-msg").textContent="Saving…";
      try{
        await api(`/api/patients/${loadedPatNum}/notes`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        snapshot={...body};
        refreshDirty();
        $("ptn-msg").textContent="Saved.";
      }catch(e){$("ptn-msg").textContent="Save failed: "+e.message.slice(0,80);}
    });

    FIELDS.forEach(f=>ctl(f).addEventListener("input",refreshDirty));

    //Follow the patient band: reload whenever the active patient changes.
    $("band-patient").addEventListener("change",load);
    load();
  }
});
