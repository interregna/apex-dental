/* Perio exam entry — web slice of FormPerio's charting side. Exams and measures
   via GET/POST /api/patients/{p}/perio, batch measure saves via
   PUT /api/perio/{examNum}/measures (real PerioMeasures Insert/Update/Delete),
   skip-teeth via PUT /api/perio/{examNum}/skipped, note via PUT /api/perio/{examNum},
   exam delete via DELETE. Visualization reuses the ported ToothChartHelper render
   at /api/patients/{p}/toothchart.png?perio=true — inline <img>, cache-busted
   after every save. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;
const MOD="clinical-perio-entry";
const SEQS=[
  {id:"Probing",label:"Probing",sites:true,hint:"pocket depth per site, mm"},
  {id:"GingMargin",label:"Ging Margin",sites:true,hint:"gingival margin / recession per site, mm"},
  {id:"MGJ",label:"MGJ",sites:true,hint:"mucogingival junction per site, mm"},
  {id:"Furcation",label:"Furcation",sites:true,hint:"furcation class 0–3 per site"},
  {id:"BleedSupPlaqCalc",label:"Bleed/Sup/Plq/Calc",sites:true,hint:"flag sum per site: 1=blood 2=suppuration 4=plaque 8=calculus"},
  {id:"Mobility",label:"Mobility",sites:false,hint:"whole-tooth mobility class 0–3"},
];
let data=null;              //last GET payload
let model={};               //"tooth|seq" -> {mb,b,db,ml,l,dl,toothValue,dirty}
let skipSet=new Set();      //skipped tooth numbers
let skipDirty=false;
let curSeq="Probing";

registerODModule({
  id:MOD,
  label:"Perio",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Perio Exam</h1>
    <span class="sub" id="pe-patline">periodontal charting entry — real PerioExams / PerioMeasures business layer</span></div>
  <div class="chart-tools">
    <select id="pe-exam" style="min-width:200px"></select>
    <label style="display:flex;align-items:center;gap:4px"><input type="checkbox" id="pe-copy" checked> copy forward</label>
    <button type="button" class="quiet" id="pe-new">New exam</button>
    <button type="button" class="quiet" id="pe-del">Delete exam</button>
    <button type="button" class="quiet" id="pe-reload">Refresh</button>
    <span class="msg" id="pe-msg"></span>
  </div>
  <div class="card" style="margin-bottom:12px">
    <h2>Measurements <span class="sub" id="pe-hint" style="font-size:12px"></span></h2>
    <div id="pe-tabs" style="display:flex;gap:6px;flex-wrap:wrap;margin:6px 0 10px"></div>
    <div class="gridwrap scrolly" style="max-height:460px"><table class="grid" id="pe-grid">
      <thead></thead><tbody></tbody></table></div>
    <div style="display:flex;gap:8px;align-items:center;margin-top:10px;flex-wrap:wrap">
      <input id="pe-note" placeholder="Exam note" style="flex:1;min-width:220px">
      <button type="button" class="action" id="pe-save">Save exam</button>
    </div>
  </div>
  <div class="card">
    <h2>Perio chart</h2>
    <div id="pe-chart" class="odc-host"></div>
    <div class="empty" id="pe-chart-empty" style="display:none"><b>No chart</b>Select a patient with a perio exam to render the chart.</div>
  </div>`,
  init(){
    const railBtn=document.querySelector(`nav.rail button[data-mod="${MOD}"]`);
    railBtn&&railBtn.addEventListener("click",()=>load(0));
    $("pe-reload").addEventListener("click",()=>load(currentExamNum()));
    $("pe-exam").addEventListener("change",()=>load(Number($("pe-exam").value)));
    $("pe-new").addEventListener("click",newExam);
    $("pe-del").addEventListener("click",delExam);
    $("pe-save").addEventListener("click",save);
    renderTabs();
    load(0);
  },
});

const msg=t=>{$("pe-msg").textContent=t;};
const key=(t,s)=>t+"|"+s;
function getM(t,s){
  return model[key(t,s)]||{mb:-1,b:-1,db:-1,ml:-1,l:-1,dl:-1,toothValue:-1,dirty:false};
}
function currentExamNum(){return data?data.selectedExamNum:0;}

function renderTabs(){
  $("pe-tabs").innerHTML=SEQS.map(s=>
    `<button type="button" class="${s.id===curSeq?"action":"quiet"}" data-seq="${s.id}">${esc(s.label)}</button>`).join("");
  $("pe-tabs").querySelectorAll("button[data-seq]").forEach(btn=>btn.addEventListener("click",()=>{
    curSeq=btn.dataset.seq;
    renderTabs();renderGrid();
  }));
  const seq=SEQS.find(s=>s.id===curSeq);
  $("pe-hint").textContent="— "+seq.hint+" (blank = no value)";
}

function renderGrid(){
  const seq=SEQS.find(s=>s.id===curSeq);
  const thead=document.querySelector("#pe-grid thead");
  const tbody=document.querySelector("#pe-grid tbody");
  if(!data||!data.selectedExamNum){
    thead.innerHTML="";
    tbody.innerHTML=`<tr><td><div class="empty"><b>No exam selected</b>Create a new exam to start charting.</div></td></tr>`;
    return;
  }
  thead.innerHTML=seq.sites
    ?`<tr><th>Tooth</th><th>Skip</th><th>MB</th><th>B</th><th>DB</th><th>ML</th><th>L</th><th>DL</th></tr>`
    :`<tr><th>Tooth</th><th>Skip</th><th>Value</th></tr>`;
  let rows="";
  for(let t=1;t<=32;t++){
    const m=getM(t,curSeq);
    const skipped=skipSet.has(t);
    const cell=f=>`<td><input data-t="${t}" data-f="${f}" value="${m[f]===-1?"":m[f]}"`+
      `${skipped?" disabled":""} inputmode="numeric" maxlength="3" class="mono" style="width:44px"></td>`;
    rows+=`<tr${skipped?' style="opacity:.45"':""}>`+
      `<td class="mono"${m.dirty?' style="font-weight:700"':""}>${t}${m.dirty?" •":""}</td>`+
      `<td><input type="checkbox" data-skip="${t}"${skipped?" checked":""}></td>`+
      (seq.sites?cell("mb")+cell("b")+cell("db")+cell("ml")+cell("l")+cell("dl"):cell("toothValue"))+
      `</tr>`;
  }
  tbody.innerHTML=rows;
  tbody.querySelectorAll("input[data-f]").forEach(inp=>inp.addEventListener("change",()=>{
    const t=Number(inp.dataset.t),f=inp.dataset.f;
    const raw=inp.value.trim();
    let v=raw===""?-1:parseInt(raw,10);
    if(isNaN(v)||v<-1){v=-1;}
    inp.value=v===-1?"":String(v);
    const k=key(t,curSeq);
    if(!model[k]){model[k]={mb:-1,b:-1,db:-1,ml:-1,l:-1,dl:-1,toothValue:-1,dirty:false};}
    if(model[k][f]!==v){
      model[k][f]=v;
      model[k].dirty=true;
      msg("Unsaved changes — press Save exam.");
    }
  }));
  tbody.querySelectorAll("input[data-skip]").forEach(chk=>chk.addEventListener("change",()=>{
    const t=Number(chk.dataset.skip);
    if(chk.checked){skipSet.add(t);}else{skipSet.delete(t);}
    skipDirty=true;
    msg("Unsaved changes — press Save exam.");
    renderGrid();
  }));
}

function refreshChart(){
  const p=getActivePatient();
  const box=$("pe-chart");
  if(!p){ box.innerHTML=""; $("pe-chart-empty").style.display=""; return; }
  $("pe-chart-empty").style.display="none";
  //Same interactive perio chart the Chart tab draws, so the two are identical.
  if(window.ODChart)window.ODChart.render(p.patNum,"perio",box);
}

async function load(examNum,keepMsg){
  const p=getActivePatient();
  if(!p){
    data=null;model={};skipSet=new Set();skipDirty=false;
    $("pe-patline").textContent="no active patient — pick one in the patient band above";
    $("pe-exam").innerHTML=`<option value="0">(no patient)</option>`;
    $("pe-note").value="";
    renderGrid();refreshChart();
    msg("");
    return;
  }
  if(!keepMsg){msg("Loading…");}
  try{
    data=await api(`/api/patients/${p.patNum}/perio`+(examNum?`?examNum=${examNum}`:""));
    model={};
    for(const m of data.measures){
      model[key(m.intTooth,m.sequenceType)]={mb:m.mb,b:m.b,db:m.db,ml:m.ml,l:m.l,dl:m.dl,toothValue:m.toothValue,dirty:false};
    }
    skipSet=new Set(data.skipped);
    skipDirty=false;
    $("pe-patline").textContent=p.name+" — "+data.exams.length+" exam"+(data.exams.length===1?"":"s");
    $("pe-exam").innerHTML=data.exams.map(x=>
      `<option value="${x.perioExamNum}">${esc(x.examDate)} — ${esc(x.provAbbr||"")}${x.note?" — "+esc(x.note.slice(0,30)):""}</option>`).join("")
      ||`<option value="0">(no exams yet)</option>`;
    $("pe-exam").value=String(data.selectedExamNum);
    $("pe-note").value=data.note||"";
    renderGrid();refreshChart();
    if(!keepMsg){msg(data.selectedExamNum?"Exam loaded.":"No exams yet — create one.");}
  }catch(e){
    data=null;renderGrid();refreshChart();
    msg("Perio unavailable: "+e.message);
  }
}

async function newExam(){
  const p=getActivePatient();
  if(!p){msg("Pick a patient first.");return;}
  const copy=$("pe-copy").checked&&data&&data.exams.length>0;
  if(!confirm(copy?"Start a new exam copied forward from the most recent exam?":"Start a new blank exam?")){return;}
  msg("Creating…");
  try{
    const r=await api(`/api/patients/${p.patNum}/perio`,{method:"POST",
      headers:{"Content-Type":"application/json"},
      body:JSON.stringify({copyForward:copy,note:""})});
    msg("Exam created (security-logged).");
    load(r.perioExamNum);
  }catch(e){msg("Create failed: "+e.message);}
}

async function delExam(){
  const examNum=currentExamNum();
  if(!examNum){msg("No exam selected.");return;}
  if(!confirm("Delete this exam and all of its measurements?")){return;}
  msg("Deleting…");
  try{
    await api(`/api/perio/${examNum}`,{method:"DELETE"});
    msg("Exam deleted (security-logged).");
    load(0);
  }catch(e){msg("Delete failed: "+e.message);}
}

async function save(){
  const examNum=currentExamNum();
  if(!examNum){msg("No exam selected.");return;}
  const dirty=Object.entries(model).filter(([,m])=>m.dirty);
  const noteChanged=$("pe-note").value!==(data.note||"");
  if(!dirty.length&&!skipDirty&&!noteChanged){msg("Nothing to save.");return;}
  msg("Saving…");
  try{
    if(skipDirty){
      await api(`/api/perio/${examNum}/skipped`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({teeth:[...skipSet]})});
    }
    if(dirty.length){
      const measures=dirty.map(([k,m])=>{
        const parts=k.split("|");
        return {intTooth:Number(parts[0]),sequenceType:parts[1],
          mb:m.mb,b:m.b,db:m.db,ml:m.ml,l:m.l,dl:m.dl,toothValue:m.toothValue};
      });
      const r=await api(`/api/perio/${examNum}/measures`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({measures})});
      msg(`Saved: ${r.inserted} added, ${r.updated} updated, ${r.deleted} cleared (security-logged).`);
    }else{
      msg("Saved (security-logged).");
    }
    if(noteChanged){
      await api(`/api/perio/${examNum}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({note:$("pe-note").value})});
    }
    load(examNum,true);
  }catch(e){msg("Save failed: "+e.message);}
}
})();
