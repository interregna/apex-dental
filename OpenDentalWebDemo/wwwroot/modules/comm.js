/* Communications — per-patient commlog (rail port of /module/comm).
   Same endpoints: GET/POST /api/patients/{patNum}/commlog over the real
   Commlogs business layer; types from DefCat.CommLogTypes. */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const MODES=["None","Phone","Email","Mail","InPerson","Text","Fax"];
  let loadedFor=null;

  registerODModule({
    id:"comm",
    label:"Comm Log",
    group:"Patient",
    html:`
      <div class="mod-head"><h1>Communications</h1>
        <span class="sub" id="cml-patline">per-patient communication log — real Commlogs business layer</span></div>
      <div class="card" style="max-width:900px">
        <h2>Communication log <span class="msg" id="cml-msg"></span></h2>
        <div class="gridwrap scrolly"><table class="grid" id="cml-list">
          <thead><tr><th>When</th><th>Type</th><th>Mode</th><th>Sent/Rcvd</th><th>Note</th></tr></thead>
          <tbody></tbody></table></div>
        <form id="cml-add" style="display:flex;gap:8px;flex-wrap:wrap;margin-top:12px">
          <select id="cml-mode" aria-label="Mode">${MODES.map(m=>
            `<option value="${m}">${m==="InPerson"?"In Person":m}</option>`).join("")}</select>
          <textarea id="cml-note" placeholder="Note…" required
            style="flex:1 1 100%;min-height:60px;font-family:inherit"></textarea>
          <button class="action">Add note</button>
          <span class="msg" id="cml-add-msg"></span>
        </form>
      </div>`,
    init(){
      $("cml-add").addEventListener("submit",addNote);
      $("band-patient").addEventListener("change",()=>setTimeout(load,0));
      document.querySelector('nav.rail button[data-mod="comm"]')
        .addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor)load()});
      load();
    }
  });

  async function load(){
    const pat=getActivePatient();
    if(!pat){$("cml-msg").textContent="Select a patient first.";return}
    loadedFor=pat.patNum;
    $("cml-patline").textContent=`Patient: ${pat.name}`;
    try{
      const comms=await api(`/api/patients/${pat.patNum}/commlog`);
      $("cml-list").querySelector("tbody").innerHTML=comms.map(c=>
        `<tr><td class="mono">${esc(c.dateTime)}</td><td>${esc(c.commType||"")}</td>`+
        `<td><span class="tag">${esc(c.mode)}</span></td><td>${esc(c.sentOrReceived)}</td>`+
        `<td style="white-space:pre-wrap;max-width:480px">${esc(c.note)}</td></tr>`).join("")
        ||`<tr><td colspan="5"><div class="empty"><b>No communications logged</b>Add the first note below.</div></td></tr>`;
      $("cml-msg").textContent="";
    }catch(e){$("cml-msg").textContent="commlog endpoint unavailable — "+e.message.slice(0,60)}
  }

  async function addNote(e){
    e.preventDefault();
    const pat=getActivePatient();
    if(!pat){$("cml-add-msg").textContent="Select a patient first.";return}
    $("cml-add-msg").textContent="Saving…";
    try{
      await api(`/api/patients/${pat.patNum}/commlog`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({note:$("cml-note").value,mode:$("cml-mode").value})});
      $("cml-add-msg").textContent="Saved.";
      $("cml-note").value="";
      load();
    }catch(err){$("cml-add-msg").textContent="Failed: "+err.message.slice(0,80)}
  }
})();
