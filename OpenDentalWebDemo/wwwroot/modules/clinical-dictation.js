/* Clinical dictation (voice-to-note) — STAGED / NON-PRODUCTION.
   Record in the browser -> transcribe/translate with the configured engine (local
   Whisper on this server, or an external service; the panel says which) -> propose an
   editable draft -> accept, which
   files the note through the real business layer (Commlog / Patient note / Procedure
   note), audit-logged. Nothing reaches the chart until you press Accept; Discard is
   gated behind a strong confirmation, and an open draft survives a page refresh.
   Talks to DictationModule.cs. Mic capture needs a secure context (HTTPS or localhost). */
"use strict";
(()=>{
const {api,esc,$,getActivePatient}=window.ODM;
const MOD="clinical-dictation";
const TARGETS={
  "commlog":"Communication log entry",
  "note-medical":"Patient note — Medical",
  "note-treatment":"Patient note — Treatment / chart",
};
let rec=null,chunks=[],timer=null,secs=0,stream=null;
let draft=null;      // {draftId,transcript,text,target,task,language,duration}
let dirty=false,saveTimer=null,sidecarOk=false;

registerODModule({
  id:MOD,
  label:"Dictation",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Dictation</h1>
    <span class="sub" id="dic-patline">voice-to-note — review before it reaches the chart</span></div>

  <div class="card" style="border-left:3px solid var(--warn,#b8860b);margin-bottom:14px">
    <div class="sub" style="line-height:1.5">
      <b>Staged / non-production.</b> <span id="dic-engine">Checking which transcription engine is active…</span>
      This is a working
      pipeline, <b>not yet a HIPAA-hardened deployment</b>: it still needs TLS at the origin,
      encryption-at-rest for drafts, and a formal risk review before real PHI. Nothing is
      written to the chart until you press <b>Accept</b>.
      <span id="dic-health"></span>
    </div>
  </div>

  <div class="card" style="margin-bottom:14px">
    <h2>Record</h2>
    <div class="row" style="gap:10px;flex-wrap:wrap;align-items:center">
      <label class="sub">File into
        <select id="dic-target">${Object.entries(TARGETS).map(([k,v])=>`<option value="${k}">${esc(v)}</option>`).join("")}</select>
      </label>
      <label class="sub">Mode
        <select id="dic-task">
          <option value="transcribe">Transcribe (verbatim)</option>
          <option value="translate">Translate to English</option>
        </select>
      </label>
      <button class="action" id="dic-rec" type="button" disabled>● Record</button>
      <span class="mono" id="dic-timer" style="min-width:52px">00:00</span>
      <span class="sub" id="dic-status"></span>
    </div>
    <div class="sub" style="margin-top:6px">Speak, then Stop. The transcript appears below as an editable proposal — it is never filed automatically.</div>
  </div>

  <div class="card" id="dic-draftcard" style="display:none">
    <h2>Proposed note <span class="sub" id="dic-meta"></span></h2>
    <textarea id="dic-text" rows="7" style="width:100%;box-sizing:border-box;font-size:14px;line-height:1.5" placeholder="Proposed note text — edit freely before accepting."></textarea>
    <div class="sub" style="margin-top:4px"><span id="dic-count"></span> · <span id="dic-dirty"></span></div>

    <details style="margin-top:10px">
      <summary class="sub" style="cursor:pointer">Show verbatim transcript (unedited)</summary>
      <div id="dic-raw" class="mono" style="white-space:pre-wrap;opacity:.8;font-size:12px;margin-top:6px;padding:8px;border:1px solid var(--line);border-radius:8px"></div>
    </details>

    <div class="row" style="gap:10px;margin-top:14px;align-items:center">
      <button class="action" id="dic-accept" type="button">Accept &amp; file note</button>
      <button class="quiet" id="dic-discard" type="button">Discard…</button>
      <span class="msg" id="dic-msg"></span>
    </div>
  </div>`,

  init(){
    const railBtn=document.querySelector(`nav.rail button[data-mod="${MOD}"]`);
    railBtn&&railBtn.addEventListener("click",()=>{ checkHealth(); loadDraft(); });
    $("dic-rec").addEventListener("click",toggleRec);
    $("dic-accept").addEventListener("click",accept);
    $("dic-discard").addEventListener("click",discard);
    $("dic-text").addEventListener("input",onEdit);
    $("dic-target").addEventListener("change",()=>{ if(draft)draft.target=$("dic-target").value; });
    const band=$("band-patient"); band&&band.addEventListener("change",()=>{ resetUI(); loadDraft(); });
    window.addEventListener("beforeunload",e=>{ if(draft&&dirty){ e.preventDefault(); e.returnValue=""; } });
    checkHealth(); loadDraft();
  }
});

const setStatus=t=>{$("dic-status").textContent=t;};
const setMsg=t=>{$("dic-msg").textContent=t;};

//Set from /api/dictation/health, which reports the engine from server configuration.
let offHost=false;
async function checkHealth(){
  try{
    const h=await api("/api/dictation/health");
    if(!["local","service"].includes(h.engine) || h.offHost!==(h.engine==="service")
      || typeof h.ok!=="boolean" || (h.offHost && !h.host)) throw new Error("Unknown engine status");
    sidecarOk=!!h.ok; offHost=!!h.offHost;
    $("dic-engine").innerHTML=offHost
      ? `Audio is <b>sent to an external transcription service (${esc(h.host)})</b> and processed there. Use it only if your practice has authorized that service for patient audio.`
      : `Audio is transcribed by a <b>local Whisper model on this server</b> and does not leave it.`;
    $("dic-health").innerHTML=h.ok
      ? ` <span class="tag" style="background:#1c7c3411;color:#1c7c34">${offHost?"external service ready":"local model ready"}${h.model?": "+esc(h.model):""}</span>`
      : ` <span class="tag" style="background:#a3161611;color:#a31616">transcription engine offline</span>`;
  }catch{ sidecarOk=false; $("dic-engine").textContent="Transcription engine status unavailable."; $("dic-health").innerHTML=` <span class="tag" style="background:#a3161611;color:#a31616">transcription engine offline</span>`; }
  $("dic-rec").disabled=!sidecarOk;
}

function fmt(s){const m=Math.floor(s/60),ss=s%60;return String(m).padStart(2,"0")+":"+String(ss).padStart(2,"0");}

async function toggleRec(){
  if(rec&&rec.state==="recording"){ rec.stop(); return; }
  if(!sidecarOk){ setStatus("Check the transcription engine before recording."); return; }
  const pat=getActivePatient();
  if(!pat){ setStatus("Pick a patient first (Patients module)."); return; }
  if(!navigator.mediaDevices||!navigator.mediaDevices.getUserMedia){
    setStatus("Microphone unavailable — needs HTTPS or localhost."); return;
  }
  try{ stream=await navigator.mediaDevices.getUserMedia({audio:true}); }
  catch(e){ setStatus("Microphone blocked: "+String(e.message||e).slice(0,60)); return; }
  chunks=[]; rec=new MediaRecorder(stream);
  rec.ondataavailable=e=>{ if(e.data&&e.data.size)chunks.push(e.data); };
  rec.onstop=onStop;
  rec.start();
  secs=0; $("dic-timer").textContent="00:00";
  timer=setInterval(()=>{ secs++; $("dic-timer").textContent=fmt(secs); },1000);
  $("dic-rec").textContent="■ Stop"; $("dic-rec").classList.add("recording");
  setStatus("Recording… speak now.");
}

async function onStop(){
  clearInterval(timer);
  $("dic-rec").textContent="● Record"; $("dic-rec").classList.remove("recording");
  if(stream){ stream.getTracks().forEach(t=>t.stop()); stream=null; }
  const blob=new Blob(chunks,{type:rec&&rec.mimeType?rec.mimeType:"audio/webm"});
  if(blob.size<1024){ setStatus("Clip too short — nothing captured."); return; }
  const pat=getActivePatient();
  const task=$("dic-task").value, target=$("dic-target").value;
  setStatus(`Transcribing ${(blob.size/1024).toFixed(0)} KB ${offHost?"via the external service":"locally"}…`);
  try{
    draft=await api(`/api/dictation/transcribe?patNum=${pat.patNum}&task=${task}&target=${encodeURIComponent(target)}`,
      {method:"POST",headers:{"Content-Type":"application/octet-stream"},body:blob});
    dirty=false; renderDraft(); setStatus("Transcribed — review and edit below.");
  }catch(e){ setStatus("Transcription failed: "+String(e.message||e).slice(0,90)); }
}

function renderDraft(){
  $("dic-draftcard").style.display=draft?"":"none";
  if(!draft)return;
  $("dic-text").value=draft.text||"";
  $("dic-raw").textContent=draft.transcript||"(empty)";
  $("dic-target").value=draft.target||"commlog";
  $("dic-task").value=draft.task||"transcribe";
  const bits=[];
  if(draft.task==="translate")bits.push("translated to English");
  if(draft.language)bits.push("heard: "+draft.language);
  if(draft.duration)bits.push(draft.duration+"s audio");
  $("dic-meta").textContent=bits.length?"("+bits.join(" · ")+")":"";
  updateCount(); $("dic-dirty").textContent="saved";
}

function updateCount(){ $("dic-count").textContent=($("dic-text").value.length)+" chars"; }

function onEdit(){
  updateCount(); dirty=true; $("dic-dirty").textContent="unsaved edits…";
  clearTimeout(saveTimer);
  saveTimer=setTimeout(saveEdits,700);
}

async function saveEdits(){
  if(!draft)return;
  try{
    await api(`/api/dictation/drafts/${draft.draftId}`,{method:"PUT",
      headers:{"Content-Type":"application/json"},body:JSON.stringify({text:$("dic-text").value})});
    draft.text=$("dic-text").value; dirty=false; $("dic-dirty").textContent="saved";
  }catch(e){ $("dic-dirty").textContent="save failed"; }
}

async function accept(){
  if(!draft)return;
  const text=$("dic-text").value.trim();
  if(!text){ setMsg("Nothing to accept — the note is empty."); return; }
  const target=$("dic-target").value;
  setMsg("Filing…");
  try{
    const r=await api(`/api/dictation/drafts/${draft.draftId}/accept`,{method:"POST",
      headers:{"Content-Type":"application/json"},body:JSON.stringify({text,target})});
    setMsg("");
    draft=null; dirty=false; resetUI();
    setStatus(`Filed to ${esc(r.reference||target)} (audit-logged).`);
  }catch(e){ setMsg("Accept failed: "+String(e.message||e).slice(0,90)); }
}

async function discard(){
  if(!draft)return;
  const ok=confirm("Discard this dictation?\n\nThe transcript and your edits will be permanently deleted and cannot be recovered. Nothing will be filed to the chart.");
  if(!ok)return;
  try{
    await api(`/api/dictation/drafts/${draft.draftId}`,{method:"DELETE"});
  }catch(e){/* already gone */}
  draft=null; dirty=false; resetUI(); setStatus("Discarded.");
}

function resetUI(){
  draft=null; dirty=false; clearTimeout(saveTimer);
  $("dic-draftcard").style.display="none"; $("dic-text").value=""; setMsg("");
}

async function loadDraft(){
  const pat=getActivePatient();
  $("dic-patline").textContent=pat?`Patient: ${pat.name}`:"No active patient — pick one in the Patients module.";
  if(!pat){ resetUI(); return; }
  try{
    const list=await api(`/api/dictation/drafts?patNum=${pat.patNum}`);
    if(list&&list.length){ draft=list[0]; dirty=false; renderDraft(); setStatus("Recovered an open draft for this patient."); }
  }catch(e){/* none */}
}
})();
