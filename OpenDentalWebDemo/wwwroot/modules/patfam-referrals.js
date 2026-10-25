/* Referrals module (patfam-referrals): patient referred-from / referred-to grid
   plus the practice's referral-source list with inline quick-add.
   Auto-loaded by app.js; talks to PatfamReferralsModule.cs endpoints, which run
   the real RefAttaches / Referrals business layer. */
"use strict";
registerODModule({
  id:"patfam-referrals",
  label:"Referrals",
  group:"Patient",
  html:`
  <div class="mod-head"><h1>Referrals</h1>
    <span class="sub">referred from / referred to · referral sources</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>Patient referrals <span class="msg" id="ref-att-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="ref-att-list">
        <thead><tr><th>Type</th><th>Referral</th><th>Specialty</th><th>Date</th><th>Status</th><th>Note</th><th></th></tr></thead>
        <tbody></tbody></table></div>
      <form class="row" id="ref-att-form" style="margin-top:10px">
        <select id="ref-att-type">
          <option value="RefFrom">Referred From</option>
          <option value="RefTo">Referred To</option>
        </select>
        <select id="ref-att-src"></select>
        <input id="ref-att-note" placeholder="Note">
        <button class="action">Attach</button>
      </form>
    </div>
    <div class="card">
      <h2>Referral sources <span class="msg" id="ref-src-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="ref-src-list">
        <thead><tr><th>Name</th><th>Business</th><th>Specialty</th><th class="mono">Phone</th><th>Type</th></tr></thead>
        <tbody></tbody></table></div>
      <form class="row" id="ref-src-form" style="margin-top:10px">
        <input id="ref-src-lname" placeholder="Last name / source" required style="width:130px">
        <input id="ref-src-fname" placeholder="First name" style="width:100px">
        <input id="ref-src-title" placeholder="Title" style="width:60px">
        <input id="ref-src-phone" placeholder="Phone" style="width:110px">
        <input id="ref-src-biz" placeholder="Business" style="width:120px">
        <label style="display:flex;align-items:center;gap:4px;font-size:12px">
          <input type="checkbox" id="ref-src-doc" checked>Doctor</label>
        <label style="display:flex;align-items:center;gap:4px;font-size:12px">
          <input type="checkbox" id="ref-src-np">Not a person</label>
        <button class="action">Add source</button>
      </form>
    </div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    const STATUSES=["None","Declined","Scheduled","Consulted","InTreatment","Complete"];

    async function loadSources(){
      try{
        const srcs=await api("/api/referrals");
        $("ref-att-src").innerHTML=srcs.map(s=>
          `<option value="${s.referralNum}">${esc(s.name)}${s.business?" — "+esc(s.business):""}</option>`).join("")
          ||`<option value="">No sources yet — add one at right</option>`;
        $("ref-src-list").querySelector("tbody").innerHTML=srcs.map(s=>
          `<tr><td>${esc(s.name)}</td><td>${esc(s.business)}</td><td>${esc(s.specialty)}</td>`+
          `<td class="mono">${esc(s.phone)}</td>`+
          `<td><span class="tag">${s.isDoctor?"Doctor":s.notPerson?"Source":"Person"}</span></td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No referral sources</b>Add the first source with the form below.</div></td></tr>`;
        $("ref-src-msg").textContent="";
      }catch(e){$("ref-src-msg").textContent="referrals endpoint unavailable"}
    }

    async function loadAttaches(){
      const pat=getActivePatient();
      if(!pat){$("ref-att-msg").textContent="no active patient";return}
      try{
        const rows=await api(`/api/patients/${pat.patNum}/referrals`);
        $("ref-att-list").querySelector("tbody").innerHTML=rows.map(r=>
          `<tr><td><span class="tag">${r.refType==="RefFrom"?"From":r.refType==="RefTo"?"To":"Custom"}</span></td>`+
          `<td>${esc(r.name)}</td><td>${esc(r.specialty)}</td><td class="mono">${esc(r.refDate)}</td>`+
          `<td><select data-att="${r.refAttachNum}">`+
            STATUSES.map(s=>`<option${s===r.status?" selected":""}>${s}</option>`).join("")+
          `</select></td>`+
          `<td>${esc(r.note)}</td>`+
          `<td><button type="button" class="quiet" data-del="${r.refAttachNum}">Detach</button></td></tr>`).join("")
          ||`<tr><td colspan="7"><div class="empty"><b>No referrals</b>Attach a referred-from or referred-to source below.</div></td></tr>`;
        $("ref-att-msg").textContent="";
        $("ref-att-list").querySelectorAll("[data-att]").forEach(sel=>
          sel.addEventListener("change",async()=>{
            try{
              await api(`/api/refattaches/${sel.dataset.att}`,{method:"PUT",
                headers:{"Content-Type":"application/json"},body:JSON.stringify({status:sel.value})});
              $("ref-att-msg").textContent="Status saved.";
            }catch(e){$("ref-att-msg").textContent="Status save failed: "+e.message.slice(0,60)}
          }));
        $("ref-att-list").querySelectorAll("[data-del]").forEach(btn=>
          btn.addEventListener("click",async()=>{
            try{await api(`/api/refattaches/${btn.dataset.del}`,{method:"DELETE"});loadAttaches();}
            catch(e){$("ref-att-msg").textContent="Detach failed: "+e.message.slice(0,60)}
          }));
      }catch(e){$("ref-att-msg").textContent="referrals endpoint unavailable"}
    }

    $("ref-att-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){$("ref-att-msg").textContent="Pick a patient first.";return}
      if(!$("ref-att-src").value){$("ref-att-msg").textContent="Add a referral source first.";return}
      $("ref-att-msg").textContent="Attaching…";
      try{
        await api(`/api/patients/${pat.patNum}/referrals`,{method:"POST",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({referralNum:+$("ref-att-src").value,refType:$("ref-att-type").value,
            status:"None",note:$("ref-att-note").value,refDate:""})});
        $("ref-att-note").value="";loadAttaches();
      }catch(err){$("ref-att-msg").textContent="Attach failed: "+err.message.slice(0,80)}
    });

    $("ref-src-form").addEventListener("submit",async e=>{
      e.preventDefault();$("ref-src-msg").textContent="Saving…";
      try{
        await api("/api/referrals",{method:"POST",headers:{"Content-Type":"application/json"},
          body:JSON.stringify({lName:$("ref-src-lname").value,fName:$("ref-src-fname").value,
            title:$("ref-src-title").value,phone:$("ref-src-phone").value,
            businessName:$("ref-src-biz").value,isDoctor:$("ref-src-doc").checked,
            notPerson:$("ref-src-np").checked,note:""})});
        $("ref-src-msg").textContent="Saved.";e.target.reset();$("ref-src-doc").checked=true;
        loadSources();
      }catch(err){$("ref-src-msg").textContent="Save failed: "+err.message.slice(0,80)}
    });

    //Follow the patient band: reload the grid whenever the active patient changes.
    $("band-patient").addEventListener("change",loadAttaches);
    loadSources().then(loadAttaches);
  }
});
