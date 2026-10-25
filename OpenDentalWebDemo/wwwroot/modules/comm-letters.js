/* Letters module (comm-letters): generate a patient-directed sheet (welcome/recall
   letter, mailing label, referral slip/letter, appointment label) for the active
   patient and view the rendered PDF inline. Auto-loaded by app.js; talks to
   CommLettersModule.cs, which runs the real SheetDefs / SheetUtil / SheetFiller /
   Sheets business layer and renders via the shared GET /api/sheets/{n}/pdf. */
"use strict";
registerODModule({
  id:"comm-letters",
  label:"Letters",
  group:"Patient",
  html:`
  <div class="mod-head"><h1>Letters</h1>
    <span class="sub">patient letters, labels &amp; slips · fill &amp; render for the active patient</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>New letter <span class="msg" id="cl-new-msg"></span></h2>
      <form id="cl-new-form" style="margin-bottom:10px">
        <div class="row"><select id="cl-types" style="min-width:240px"></select>
        <button class="action">Generate</button></div>
        <label id="cl-body-wrap" style="display:none;margin-top:8px">
          <span style="display:block;font-size:12px;color:var(--muted,#666)">Letter body</span>
          <textarea id="cl-body" rows="6" style="width:100%" placeholder="Type the letter text…"></textarea>
        </label>
      </form>
      <div id="cl-result"><div class="empty"><b>No letter generated</b>Pick a type above and Generate to fill and render it.</div></div>
    </div>
    <div class="card">
      <h2>Letters on file <span class="msg" id="cl-list-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="cl-list">
        <thead><tr><th>Sheet</th><th>Description</th><th class="mono">Date</th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    //Map of type key -> whether it carries an editable body (drives the textarea).
    const bodyByType={};

    //Show the body textarea only for the currently-selected type that hasBody.
    function syncBody(){
      const hasBody=!!bodyByType[$("cl-types").value];
      $("cl-body-wrap").style.display=hasBody?"block":"none";
    }

    async function loadTypes(){
      try{
        const types=await api("/api/letter-types");
        types.forEach(t=>{bodyByType[t.key]=!!t.hasBody});
        $("cl-types").innerHTML=types.map(t=>
          `<option value="${esc(t.key)}">${esc(t.description)}</option>`).join("")
          ||`<option value="">No letter types available</option>`;
        $("cl-new-msg").textContent="";
        syncBody();
      }catch(e){$("cl-new-msg").textContent="letter-types endpoint unavailable"}
    }

    function showResult(sheetNum,description){
      $("cl-result").innerHTML=
        `<p>Generated <b>${esc(description)}</b> (sheet ${sheetNum}). `+
        `<a data-view="${esc(description)}" href="/api/sheets/${sheetNum}/pdf">view PDF</a></p>`;
    }

    async function generate(e){
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){$("cl-new-msg").textContent="no active patient";return}
      const type=$("cl-types").value;
      if(!type){$("cl-new-msg").textContent="pick a letter type first";return}
      $("cl-new-msg").textContent="generating…";
      try{
        const payload={type};
        if(bodyByType[type]){payload.body=$("cl-body").value}
        const res=await api(`/api/patients/${pat.patNum}/letters`,{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(payload)});
        $("cl-new-msg").textContent="";
        showResult(res.sheetNum,res.description);
        loadList();
      }catch(e2){$("cl-new-msg").textContent="Generate failed: "+e2.message.slice(0,80)}
    }

    async function loadList(){
      const pat=getActivePatient();
      const tbody=$("cl-list").querySelector("tbody");
      if(!pat){
        tbody.innerHTML=`<tr><td colspan="4"><div class="empty"><b>No active patient</b>Select a patient to see their letters.</div></td></tr>`;
        $("cl-list-msg").textContent="";return;
      }
      try{
        const rows=await api(`/api/patients/${pat.patNum}/letters`);
        tbody.innerHTML=rows.map(r=>
          `<tr><td class="mono">${r.sheetNum}</td><td>${esc(r.description)}</td>`+
          `<td class="mono">${esc(r.dateTimeSheet)}</td>`+
          `<td><a data-view="${esc(r.description)}" href="/api/sheets/${r.sheetNum}/pdf">view</a> `+
          `<button type="button" class="quiet" data-del="${r.sheetNum}" `+
          `data-desc="${esc(r.description)}">Delete</button></td></tr>`).join("")
          ||`<tr><td colspan="4"><div class="empty"><b>No letters on file</b>Generate one with the picker at left.</div></td></tr>`;
        $("cl-list-msg").textContent="";
      }catch(e){$("cl-list-msg").textContent="letters endpoint unavailable"}
    }

    async function deleteLetter(sheetNum,desc){
      if(!confirm(`Delete "${desc}" (sheet ${sheetNum})?`)){return}
      $("cl-list-msg").textContent="deleting…";
      try{
        await api(`/api/sheets/${sheetNum}`,{method:"DELETE"});
        $("cl-list-msg").textContent="";
        loadList();
      }catch(e){$("cl-list-msg").textContent="Delete failed: "+e.message.slice(0,80)}
    }

    $("cl-new-form").addEventListener("submit",generate);
    $("cl-types").addEventListener("change",syncBody);
    $("cl-list").querySelector("tbody").addEventListener("click",e=>{
      const btn=e.target.closest("[data-del]");
      if(btn){deleteLetter(btn.dataset.del,btn.dataset.desc)}
    });
    document.addEventListener("od:patient-changed",()=>{
      $("cl-result").innerHTML=`<div class="empty"><b>No letter generated</b>Pick a type above and Generate to fill and render it.</div>`;
      loadList();});
    loadTypes();
    loadList();
  }
});
