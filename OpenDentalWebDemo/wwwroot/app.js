/* Open Dental on Linux — workstation UI.
   Plain browser JavaScript over the ASP.NET Core API (all data flows through
   the real OpenDentBusiness layer). Every view degrades to an instructive
   empty state when its endpoint is unreachable, so the shell is safe to run
   ahead of backend workstreams. */
"use strict";

const $=id=>document.getElementById(id);
const esc=s=>String(s??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));

async function api(path,opts){
  const r=await fetch(path,opts);
  if(r.status===401){ if(window.showLogin)showLogin(); throw new Error("Not authenticated"); }
  if(!r.ok) throw new Error((await r.text())||r.status);
  const ct=r.headers.get("content-type")||"";
  return ct.includes("json")?r.json():r.text();
}

/* ---------- theme ---------- */
const THEMES=["auto","light","dark"];
function applyTheme(t){
  if(t==="auto")document.documentElement.removeAttribute("data-theme");
  else document.documentElement.setAttribute("data-theme",t);
  $("theme-btn").textContent=(t==="light"?"○ light":t==="dark"?"● dark":"◐ auto");
  localStorage.setItem("od-theme",t);
}
$("theme-btn").addEventListener("click",()=>{
  const cur=localStorage.getItem("od-theme")||"light";
  applyTheme(THEMES[(THEMES.indexOf(cur)+1)%THEMES.length]);
});
applyTheme(localStorage.getItem("od-theme")||"light");

/* ---------- module navigation ----------
   showModule(id) is the single entry point used by the rail, the role switcher,
   and the ⌘K command palette, so any module is reachable the same way whether or
   not its rail button is currently visible for the active role. */
function showModule(id){
  const sec=document.getElementById("mod-"+id);
  if(!sec)return;
  document.querySelectorAll("nav.rail button[data-mod]").forEach(b=>b.setAttribute("aria-current",String(b.dataset.mod===id)));
  document.querySelectorAll("section.module").forEach(s=>s.classList.toggle("active",s.id==="mod-"+id));
  document.querySelector("main").scrollTop=0;
  //Re-fetch the tooth chart whenever it's opened, so procedures added elsewhere
  //(e.g. the Procedures module) are reflected without needing a manual Refresh.
  if(id==="chart")loadChart();
}
document.querySelectorAll("nav.rail button[data-mod]").forEach(btn=>{
  btn.addEventListener("click",()=>showModule(btn.dataset.mod));
});
//Disable the browser right-click context menu across the app.
document.addEventListener("contextmenu",e=>e.preventDefault());

/* ---------- patient context ---------- */
let patients=[],activePat=null;
const PROV_COLORS=["#9DC8C4","#C9BAE3","#F2C9A0","#A9CBE8","#E8B9C6","#BFD8A8"];
const provColor=(()=>{const m=new Map();return abbr=>{
  if(!m.has(abbr))m.set(abbr,PROV_COLORS[m.size%PROV_COLORS.length]);return m.get(abbr);};})();

function setActivePatient(patNum){
  activePat=patients.find(p=>p.patNum==patNum)||null;
  if(activePat){
    $("band-name").textContent=activePat.name;
    $("band-meta").textContent=activePat.birthdate&&activePat.birthdate!=="0001-01-01"?"DOB "+activePat.birthdate:"";
    $("band-prov").hidden=!activePat.provider;
    $("band-prov").textContent=activePat.provider||"";
    $("band-search").value=activePat.name;
  }
  fillEditForm();loadChart();loadDocs();loadFamily();loadLedger();
  broadcastPatient();
}

//Tell the pluggable modules the active patient changed. A module subscribes via ONE of
//three hooks, so we fire all three and whichever the currently-visible module uses, it
//reloads right away instead of showing the previous patient until re-navigated:
//  1. a "change" on the hidden #band-patient relay (most modules wire this),
//  2. the "od:patient-changed" document event (newer modules),
//  3. a click on the active rail button (chart-only modules that refresh on open).
function broadcastPatient(){
  const bp=$("band-patient");
  if(bp){ bp.value=activePat?activePat.patNum:""; bp.dispatchEvent(new Event("change")); }
  document.dispatchEvent(new CustomEvent("od:patient-changed",{detail:activePat||null}));
  const btn=document.querySelector('nav.rail button[data-mod][aria-current="true"]');
  if(btn){ btn.dispatchEvent(new Event("click")); }
}

/* ---------- patient search combobox (header band) ---------- */
let patMatches=[],patIdx=-1;
function renderPatResults(q){
  const results=$("band-results");
  q=(q||"").trim().toLowerCase();
  patMatches=(!q?patients:patients.filter(p=>
    p.name.toLowerCase().includes(q)||String(p.patNum).startsWith(q))).slice(0,60);
  if(patIdx>=patMatches.length)patIdx=patMatches.length-1;
  results.innerHTML=patMatches.map((p,i)=>
    `<li role="option" data-pat="${p.patNum}" aria-selected="${i===patIdx}" class="${i===patIdx?"sel":""}">`+
    `<span class="pr-name">${esc(p.name)}</span>`+
    `<span class="pr-meta">#${p.patNum}${p.birthdate&&p.birthdate!=="0001-01-01"?" · "+esc(p.birthdate):""}${p.provider?" · "+esc(p.provider):""}</span></li>`).join("")
    ||`<li class="pr-empty">No matching patients</li>`;
  results.hidden=false;$("band-search").setAttribute("aria-expanded","true");
}
function closePatResults(){$("band-results").hidden=true;$("band-search").setAttribute("aria-expanded","false");patIdx=-1;}
function wirePatSearch(){
  const s=$("band-search"),results=$("band-results");
  s.addEventListener("focus",()=>{s.select();patIdx=-1;renderPatResults("");});
  s.addEventListener("input",()=>{patIdx=-1;renderPatResults(s.value);});
  s.addEventListener("keydown",e=>{
    if(results.hidden){if(e.key==="ArrowDown")renderPatResults(s.value);return;}
    if(e.key==="ArrowDown"){e.preventDefault();patIdx=Math.min(patIdx+1,patMatches.length-1);renderPatResults(s.value);}
    else if(e.key==="ArrowUp"){e.preventDefault();patIdx=Math.max(patIdx-1,0);renderPatResults(s.value);}
    else if(e.key==="Enter"){e.preventDefault();const p=patMatches[patIdx<0?0:patIdx];if(p){setActivePatient(p.patNum);closePatResults();s.blur();}}
    else if(e.key==="Escape"){closePatResults();if(activePat)s.value=activePat.name;s.blur();}
  });
  results.addEventListener("mousedown",e=>{const li=e.target.closest("li[data-pat]");if(li){setActivePatient(li.dataset.pat);closePatResults();}});
  document.addEventListener("click",e=>{if(!e.target.closest("#patsearch"))closePatResults();});
}
wirePatSearch();
//Hidden relay that pluggable modules subscribe to for patient-change ("change" event).
//Created before the modules load so their init-time listeners actually attach to it.
if(!document.getElementById("band-patient")){
  const _bp=document.createElement("input");_bp.type="hidden";_bp.id="band-patient";
  document.body.appendChild(_bp);
}

async function loadPatients(keepSelection){
  try{
    patients=await api("/api/patients");
    const opts=patients.map(p=>`<option value="${p.patNum}">${esc(p.name)}</option>`).join("");
    $("appt-pat").innerHTML=opts;
    $("fam-move-pat").innerHTML=opts;$("fam-move-target").innerHTML=opts;
    $("pat-list").querySelector("tbody").innerHTML=patients.map(p=>
      `<tr class="rowsel" data-pat="${p.patNum}"><td class="num">${p.patNum}</td><td>${esc(p.name)}</td>`+
      `<td class="mono">${esc(p.birthdate)}</td><td>${esc(p.provider)}</td></tr>`).join("")
      ||`<tr><td colspan="4"><div class="empty"><b>No patients yet</b>Add the first patient with the form above.</div></td></tr>`;
    document.querySelectorAll("#pat-list tr.rowsel").forEach(tr=>
      tr.addEventListener("click",()=>setActivePatient(tr.dataset.pat)));
    if(!keepSelection||!activePat)setActivePatient(patients[0]?.patNum);
    setConn(true);
  }catch(e){setConn(false,e.message)}
}
function setConn(ok,detail){
  $("conn").classList.toggle("err",!ok);
  $("conn-text").textContent=ok?"connected":"unreachable"+(detail?" — "+detail.slice(0,60):"");
}

$("pat-add").addEventListener("submit",async e=>{
  e.preventDefault();const f=e.target;
  try{
    await api("/api/patients",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({lName:f.lname.value,fName:f.fname.value,birthdate:f.bd.value})});
    f.reset();loadPatients();
  }catch(err){alert("Add failed: "+err.message)}
});

/* ---------- family: edit ---------- */
const EDIT_FIELDS=["lName","fName","preferred","birthdate","hmPhone","wirelessPhone","email","address","city","state","zip"];
async function fillEditForm(){
  if(!activePat)return;
  try{
    const d=await api(`/api/patients/${activePat.patNum}/details`);
    const f=$("pat-edit");EDIT_FIELDS.forEach(k=>{if(f.elements[k])f.elements[k].value=d[k]||"";});
    $("edit-msg").textContent="";
  }catch{$("edit-msg").textContent="details endpoint unavailable"}
}
$("pat-edit").addEventListener("submit",async e=>{
  e.preventDefault();if(!activePat)return;
  const f=e.target,body={};EDIT_FIELDS.forEach(k=>body[k]=f.elements[k].value);
  try{
    await api(`/api/patients/${activePat.patNum}`,{method:"PUT",
      headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
    $("edit-msg").textContent="Saved — change is audit-logged.";loadPatients(true);
  }catch(err){$("edit-msg").textContent="Save failed: "+err.message}
});

/* ---------- family: members / guarantor (same /api/family endpoints as the
   old standalone page: Patients.GetFamily / Insert / Update) ---------- */
async function loadFamily(){
  if(!activePat)return;
  try{
    const fam=await api(`/api/family/${activePat.patNum}`);
    $("fam-list").querySelector("tbody").innerHTML=fam.members.map(m=>
      `<tr><td class="num">${m.patNum}</td><td>${esc(m.name)}${m.isGuarantor?' <span class="tag">Guarantor</span>':""}</td>`+
      `<td>${esc(m.position)}</td><td class="num">${m.age}</td><td>${esc(m.gender)}</td>`+
      `<td><span class="tag">${esc(m.status)}</span></td><td class="mono">${esc(m.birthdate)}</td></tr>`).join("")
      ||`<tr><td colspan="7"><div class="empty"><b>No family members</b></div></td></tr>`;
    $("fam-guar").textContent=fam.guarantor?`Guarantor: ${fam.guarantor.name} (#${fam.guarantor.patNum})`:"";
    $("fam-msg").textContent="";
  }catch{$("fam-msg").textContent="family endpoint unavailable"}
}
$("fam-add").addEventListener("submit",async e=>{
  e.preventDefault();const f=e.target;
  if(!activePat){$("fam-add-msg").textContent="Select a patient first.";return}
  $("fam-add-msg").textContent="Adding…";
  try{
    await api("/api/family/add-member",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({guarantorPatNum:+activePat.patNum,fName:f.fname.value,lName:f.lname.value,
        birthdate:f.bd.value,position:f.position.value})});
    $("fam-add-msg").textContent="Added.";f.reset();
    loadPatients(true);loadFamily();
  }catch(err){$("fam-add-msg").textContent="Add failed: "+err.message.slice(0,80)}
});
$("fam-move").addEventListener("submit",async e=>{
  e.preventDefault();$("fam-move-msg").textContent="Moving…";
  try{
    await api("/api/family/set-guarantor",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({patNum:+$("fam-move-pat").value,guarantorPatNum:+$("fam-move-target").value})});
    $("fam-move-msg").textContent="Moved (audit-logged).";loadFamily();
  }catch(err){$("fam-move-msg").textContent="Move failed: "+err.message.slice(0,80)}
});

/* ---------- account: ledger + payment entry (same /api/account endpoints as
   the old standalone page: Procedures/Payments/Adjustments + Payments.Insert) ---------- */
const usd=n=>Number(n).toLocaleString(undefined,{style:"currency",currency:"USD"});
async function loadLedger(){
  if(!activePat)return;
  try{
    const d=await api(`/api/account/${activePat.patNum}`);
    const stat=(id,v)=>{const el=$(id);el.querySelector(".v").textContent=usd(v);el.classList.remove("pending")};
    stat("ag-030",d.aging.bal_0_30);stat("ag-3160",d.aging.bal_31_60);
    stat("ag-6190",d.aging.bal_61_90);stat("ag-90",d.aging.balOver90);stat("ag-bal",d.balTotal);
    $("led-line").textContent=
      `${d.patient} — estimated balance ${usd(d.estBalance)} · insurance estimate ${usd(d.insEst)}`;
    $("led-list").querySelector("tbody").innerHTML=d.entries.map(en=>
      `<tr><td class="mono">${esc(en.date)}</td><td><span class="tag">${esc(en.type)}</span></td>`+
      `<td>${esc(en.description)}</td><td>${esc(en.provider)}</td><td>${esc(en.status)}</td>`+
      `<td class="num">${en.charge?usd(en.charge):""}</td><td class="num">${en.credit?usd(en.credit):""}</td></tr>`).join("")
      ||`<tr><td colspan="7"><div class="empty"><b>No ledger entries</b>Chart procedures or post a payment below.</div></td></tr>`;
    $("led-msg").textContent=`${d.entries.length} entries`;
  }catch{$("led-msg").textContent="account endpoint unavailable"}
}
$("pay-form").addEventListener("submit",async e=>{
  e.preventDefault();
  if(!activePat){$("pay-msg").textContent="Select a patient first.";return}
  $("pay-msg").textContent="Posting…";
  try{
    const {payNum}=await api(`/api/account/${activePat.patNum}/payment`,{method:"POST",
      headers:{"Content-Type":"application/json"},
      body:JSON.stringify({amount:+$("pay-amt").value,note:$("pay-note").value})});
    $("pay-msg").textContent=`Payment ${payNum} posted.`;e.target.reset();loadLedger();
  }catch(err){$("pay-msg").textContent="Payment failed: "+err.message.slice(0,80)}
});

/* ---------- appointment book ---------- */
const DAY_START=7*60,DAY_END=18*60,SLOT=10,SLOT_H=26;
function schedDate(){return $("sched-date").value||new Date().toISOString().slice(0,10)}

async function loadSched(){
  const book=$("book");
  let appts=[];
  try{appts=await api(`/api/schedule?date=${schedDate()}`);$("sched-msg").textContent="";}
  catch(e){$("sched-msg").textContent="schedule endpoint unavailable";}
  const provs=[...new Set(appts.map(a=>a.provider))];
  if(provs.length===0)provs.push("DOC");
  let gutter=`<div class="gutter"><div style="height:27px"></div>`;
  for(let m=DAY_START;m<DAY_END;m+=SLOT){
    const hour=m%60===0;
    gutter+=`<div class="slot${hour?" hour":""}">${hour?`<span>${String(Math.floor(m/60)).padStart(2,"0")}:00</span>`:""}</div>`;
  }
  gutter+="</div>";
  let ops=`<div class="ops">`;
  for(const prov of provs){
    ops+=`<div class="op" style="--provcolor:${provColor(prov)}">`+
      `<div class="oph"><span class="provdot"></span>${esc(prov)}</div><div class="bg" style="position:relative">`;
    for(let m=DAY_START;m<DAY_END;m+=SLOT)ops+=`<div class="slot${m%60===0?" hour":""}"></div>`;
    for(const a of appts.filter(x=>x.provider===prov)){
      const [hh,mm]=a.time.split(":").map(Number);
      const topPx=((hh*60+mm)-DAY_START)/SLOT*SLOT_H;
      const height=Math.max(1,a.minutes/SLOT)*SLOT_H-3;
      if(topPx<0)continue;
      ops+=`<div class="appt${a.status==="Broken"?" broken":""}" data-apt="${a.aptNum}" data-time="${a.time}" `+
        `data-min="${a.minutes}" data-status="${esc(a.status)}" data-pat="${esc(a.patient)}" `+
        `style="top:${topPx}px;height:${height}px" role="button" tabindex="0" `+
        `title="${esc(a.patient)} · ${a.time} · ${a.minutes}min · ${esc(a.status)} — click to edit">`+
        `<div class="t">${a.time} · ${a.minutes}m</div><div class="p">${esc(a.patient)}</div></div>`;
    }
    ops+="</div></div>";
  }
  ops+="</div>";
  book.innerHTML=gutter+ops;
  book.querySelectorAll(".appt").forEach(el=>el.addEventListener("click",e=>{e.stopPropagation();openApptEditor(el)}));
  book.querySelectorAll(".op .bg").forEach(bg=>bg.addEventListener("click",e=>{
    const r=bg.getBoundingClientRect();
    const mins=DAY_START+Math.floor((e.clientY-r.top)/SLOT_H)*SLOT;
    $("appt-time").value=`${String(Math.floor(mins/60)).padStart(2,"0")}:${String(mins%60).padStart(2,"0")}`;
    $("appt-msg").textContent="Time set from the book — pick a patient and press Book.";
  }));
  if(appts.length===0){
    $("sched-msg").innerHTML=`No appointments on ${schedDate()} yet — book one below.`;
  }
}
$("sched-date").valueAsDate=new Date();
$("sched-date").addEventListener("change",loadSched);
$("sched-today").addEventListener("click",()=>{$("sched-date").valueAsDate=new Date();loadSched()});
$("sched-prev").addEventListener("click",()=>shiftDay(-1));
$("sched-next").addEventListener("click",()=>shiftDay(1));
function shiftDay(d){const t=new Date(schedDate());t.setDate(t.getDate()+d);$("sched-date").value=t.toISOString().slice(0,10);loadSched()}

let pop=null;
function closePop(){pop?.remove();pop=null}
document.addEventListener("click",e=>{if(pop&&!pop.contains(e.target))closePop()});
function openApptEditor(el){
  closePop();
  pop=document.createElement("div");pop.className="popover";
  const r=el.getBoundingClientRect();
  pop.style.left=Math.min(window.innerWidth-280,r.right+8)+"px";
  pop.style.top=Math.min(window.innerHeight-230,r.top)+"px";
  pop.innerHTML=`<h3>${esc(el.dataset.pat)}</h3>
   <form>
    <div class="rowline"><input type="time" name="time" value="${el.dataset.time}" step="600">
     <input type="number" name="min" value="${el.dataset.min}" min="10" step="10" style="width:64px"> min</div>
    <div class="rowline"><select name="status">
      ${["Scheduled","Complete","Broken"].map(st=>`<option${st===el.dataset.status?" selected":""}>${st}</option>`).join("")}
    </select></div>
    <div class="rowline"><button class="action">Save changes</button>
     <button type="button" class="quiet danger" data-del>Delete</button></div>
    <span class="msg"></span></form>`;
  document.body.appendChild(pop);
  const form=pop.querySelector("form"),msg=pop.querySelector(".msg");
  form.addEventListener("submit",async ev=>{
    ev.preventDefault();msg.textContent="Saving…";
    try{
      await api(`/api/appointments/${el.dataset.apt}`,{method:"PUT",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({dateTime:schedDate()+"T"+form.time.value,minutes:+form.min.value,status:form.status.value})});
      closePop();loadSched();
    }catch(err){msg.textContent="Save failed: "+err.message.slice(0,60)}
  });
  pop.querySelector("[data-del]").addEventListener("click",async()=>{
    msg.textContent="Deleting…";
    try{await api(`/api/appointments/${el.dataset.apt}`,{method:"DELETE"});closePop();loadSched();}
    catch(err){msg.textContent="Delete failed: "+err.message.slice(0,60)}
  });
}

$("appt-new").addEventListener("submit",async e=>{
  e.preventDefault();
  try{
    await api("/api/appointments",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({patNum:+$("appt-pat").value,dateTime:schedDate()+"T"+$("appt-time").value,
        minutes:+$("appt-min").value,note:""})});
    $("appt-msg").textContent="Booked.";loadSched();
  }catch(err){$("appt-msg").textContent="Booking failed: "+err.message}
});

/* ---------- chart ---------- */
let chartMode="normal";
function loadChart(){
  if(!activePat)return;
  //Interactive, standards-based SVG (chart-render.js). Falls back to the legacy
  //server image only if the renderer failed to load.
  if(window.ODChart){
    window.ODChart.render(activePat.patNum,chartMode,$("chart-canvas"));
  }
  loadChartProcs();
}
async function loadChartProcs(){
  try{
    const d=await api(`/api/patients/${activePat.patNum}/toothchart`);
    $("chart-procs").querySelector("tbody").innerHTML=d.procs.map(p=>
      `<tr><td class="num">${p.toothNum}</td><td class="mono">${esc(p.surf)}</td><td class="mono">${esc(p.code)}</td>`+
      `<td><span class="tag">${esc(p.status)}</span></td><td>${esc(p.descript)}</td></tr>`).join("")
      ||`<tr><td colspan="5"><div class="empty"><b>No charted procedures</b>Seed demo clinical data to see the chart in action.</div></td></tr>`;
    $("chart-msg").textContent="";
  }catch{$("chart-msg").textContent="chart endpoint unavailable"}
}
$("chart-normal").addEventListener("click",()=>{chartMode="normal";segSync();loadChart()});
$("chart-perio").addEventListener("click",()=>{chartMode="perio";segSync();loadChart()});
function segSync(){
  $("chart-normal").setAttribute("aria-pressed",chartMode==="normal");
  $("chart-perio").setAttribute("aria-pressed",chartMode==="perio");
}
$("chart-num-univ").addEventListener("click",()=>setNumbering("universal"));
$("chart-num-fdi").addEventListener("click",()=>setNumbering("fdi"));
function setNumbering(sys){
  if(window.ODChart)window.ODChart.setNumbering(sys);
  $("chart-num-univ").setAttribute("aria-pressed",sys==="universal");
  $("chart-num-fdi").setAttribute("aria-pressed",sys==="fdi");
  loadChart();
}
$("chart-reload").addEventListener("click",loadChart);
$("chart-seed").addEventListener("click",async()=>{
  if(!activePat)return;
  $("chart-msg").textContent="Seeding…";
  try{await api(`/api/patients/${activePat.patNum}/toothchart/demo`,{method:"POST"});
    $("chart-msg").textContent="Seeded.";loadChart();}
  catch(err){$("chart-msg").textContent="Seed failed: "+err.message}
});

/* ---------- images ---------- */
async function loadScanSources(){
  try{
    const srcs=await api("/api/scan/sources");
    $("scan-src").innerHTML=srcs.filter(s=>s.kind!=="upload")
      .map(s=>`<option value="${esc(s.id)}">${esc(s.name)}</option>`).join("")
      ||`<option value="">No scanners found</option>`;
  }catch{$("scan-src").innerHTML=`<option value="">scan service unavailable</option>`}
}
async function loadDocs(){
  if(!activePat)return;
  try{
    const docs=await api(`/api/patients/${activePat.patNum}/documents`);
    $("doc-list").querySelector("tbody").innerHTML=docs.map(d=>
      `<tr><td class="num">${d.docNum}</td><td class="mono">${esc(d.fileName)}</td>`+
      `<td><span class="tag">${esc(d.imgType)}</span></td><td class="mono">${esc(d.created)}</td>`+
      `<td><a href="/api/documents/${d.docNum}/file" data-view="${esc(d.fileName)}">view</a></td></tr>`).join("")
      ||`<tr><td colspan="5"><div class="empty"><b>No documents</b>Scan or upload to file the first document for this patient.</div></td></tr>`;
  }catch{/* documents list endpoint unavailable */}
}
$("scan-form").addEventListener("submit",async e=>{
  e.preventDefault();if(!activePat)return;
  $("scan-msg").textContent="Scanning…";
  try{
    await api("/api/scan/acquire",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({source:$("scan-src").value,patNum:+activePat.patNum})});
    $("scan-msg").textContent="Scanned and filed.";loadDocs();
  }catch(err){$("scan-msg").textContent="Scan failed: "+err.message.slice(0,80)}
});
$("up-btn").addEventListener("click",()=>$("up-file").click());
$("up-file").addEventListener("change",async e=>{
  const f=e.target.files[0];if(!f||!activePat)return;
  $("scan-msg").textContent="Uploading…";
  try{
    await fetch(`/api/scan/upload?patNum=${activePat.patNum}&filename=${encodeURIComponent(f.name)}`,
      {method:"POST",body:f});
    $("scan-msg").textContent="Uploaded and filed.";e.target.value="";loadDocs();
  }catch{$("scan-msg").textContent="Upload failed"}
});

/* ---------- reports ---------- */
async function loadReports(){
  try{
    const reps=await api("/api/reports");
    $("rep-sel").innerHTML=reps.map(r=>`<option value="${esc(r.id)}">${esc(r.name)}</option>`).join("");
  }catch{$("rep-sel").innerHTML=`<option value="">reports endpoint unavailable</option>`}
  const to=new Date(),from=new Date();from.setDate(from.getDate()-30);
  $("rep-to").valueAsDate=to;$("rep-from").valueAsDate=from;
}
$("rep-form").addEventListener("submit",async e=>{
  e.preventDefault();$("rep-msg").textContent="Running…";
  try{
    const rep=await api(`/api/reports/${$("rep-sel").value}?from=${$("rep-from").value}&to=${$("rep-to").value}`);
    $("rep-msg").textContent=`${rep.rows.length} rows`;
    $("rep-table").querySelector("thead").innerHTML=
      "<tr>"+rep.columns.map(c=>`<th>${esc(c)}</th>`).join("")+"</tr>";
    $("rep-table").querySelector("tbody").innerHTML=
      rep.rows.map(r=>"<tr>"+r.map(v=>`<td>${esc(v)}</td>`).join("")+"</tr>").join("")
      ||`<tr><td colspan="${rep.columns.length}"><div class="empty"><b>No rows in range</b>Widen the dates and run again.</div></td></tr>`;
  }catch(err){$("rep-msg").textContent="Report failed: "+err.message.slice(0,80)}
});

/* ---------- signature ---------- */
const pad=$("sig-pad"),ctx=pad.getContext("2d");
ctx.lineWidth=2;ctx.lineCap="round";
ctx.strokeStyle=getComputedStyle(document.body).color;
let sigPts=[],drawing=false,last=null;
const fmt=n=>(Math.round(n*10)/10).toString();
function padPos(e){const r=pad.getBoundingClientRect();
  return{x:(e.clientX-r.left)*pad.width/r.width,y:(e.clientY-r.top)*pad.height/r.height}}
pad.addEventListener("pointerdown",e=>{drawing=true;last=padPos(e);sigPts.push(last);pad.setPointerCapture(e.pointerId)});
pad.addEventListener("pointermove",e=>{if(!drawing)return;const p=padPos(e);
  ctx.beginPath();ctx.moveTo(last.x,last.y);ctx.lineTo(p.x,p.y);ctx.stroke();last=p;sigPts.push(p)});
pad.addEventListener("pointerup",()=>{drawing=false;sigPts.push({x:0,y:0})});
$("sig-clear").addEventListener("click",()=>{sigPts=[];ctx.clearRect(0,0,pad.width,pad.height)});
$("sig-form").addEventListener("submit",async e=>{
  e.preventDefault();if(!activePat)return;
  const pts=sigPts.map(p=>fmt(p.x)+","+fmt(p.y)).join(";");
  if(!pts){$("sig-msg").textContent="Draw a signature first.";return}
  $("sig-msg").textContent="Signing…";
  try{
    const {sheetNum}=await api(`/api/patients/${activePat.patNum}/consent`,
      {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({points:pts})});
    $("sig-msg").innerHTML=`Sheet ${sheetNum} saved — <a href="/api/sheets/${sheetNum}/pdf" data-view="Signed consent — sheet ${sheetNum}">view signed PDF</a>`;
  }catch(err){$("sig-msg").textContent="Signing failed: "+err.message.slice(0,80)}
});

/* ---------- authentication + session boot ----------
   The server enforces auth on every /api/* call and drives the role from the
   logged-in user's Open Dental user-group. The client mirrors that: on load we
   ask /api/auth/me; unauthenticated shows the login screen; the resolved role
   (and its module scope) comes from the server, so the rail scoping matches what
   the API will actually permit. Admins may impersonate a role for viewing. */
let sessionUser=null;
function startApp(){ loadModules();loadPatients();loadSched();loadScanSources();loadReports(); }
function applySession(s){
  sessionUser=s;
  currentRole=s.role;
  /* trust the server's module scope for this role over the client's copy */
  if(ROLES[s.role]){ ROLES[s.role]={...ROLES[s.role],label:s.label,landing:s.landing,core:s.core,mods:s.mods}; }
  /* The user's real Open Dental permissions (server-enforced). Modules can call
     ODM.hasPerm("PerioEdit") to disable edit UI the user isn't allowed — the
     server enforces regardless, this just avoids offering a dead button. */
  window.ODM.permissions=s.permissions||[];
  window.ODM.isAdmin=!!s.isAdmin;
  window.ODM.hasPerm=name=>s.isAdmin||(s.permissions||[]).includes(name);
  const sel=$("role-select");
  if(sel){
    sel.value=s.role;
    sel.disabled=!s.isAdmin;                       /* non-admins are locked to their role */
    sel.title=s.isAdmin?"Switch workspace (admin)":"Workspace is set by your user group";
  }
  $("band-user").textContent=s.userName;
  $("band-user").hidden=false;
  $("logout-btn").hidden=false;
  requestRoleNav();
}
async function doLogin(ev){
  ev.preventDefault();
  const u=$("login-user").value.trim(),p=$("login-pass").value;
  $("login-err").textContent="";
  try{
    const s=await api("/api/auth/login",{method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({userName:u,password:p})});
    $("login").classList.remove("open");
    applySession(s);startApp();
  }catch(err){ $("login-err").textContent="Invalid username or password."; }
}
function showLogin(){
  $("login").classList.add("open");
  const u=$("login-user");if(u){u.value="";$("login-pass").value="";u.focus();}
}
window.showLogin=showLogin;
async function logout(){
  try{await fetch("/api/auth/logout",{method:"POST"});}catch{}
  location.reload();
}
$("login-form").addEventListener("submit",doLogin);
$("logout-btn").addEventListener("click",logout);
(async function boot(){
  try{
    const me=await (await fetch("/api/auth/me")).json();
    if(me.authed){ applySession(me);startApp(); }
    else showLogin();
  }catch{ showLogin(); }
})();

/* ---------- inline viewer (images/PDFs never open a new window) ---------- */
function viewInline(url,title){
  let ov=$("od-viewer");
  if(!ov){
    ov=document.createElement("div");ov.id="od-viewer";ov.className="viewer";
    ov.innerHTML=`<div class="viewer-box"><div class="viewer-bar"><span id="od-viewer-title"></span>`+
      `<button class="quiet" id="od-viewer-close" aria-label="Close">✕ close</button></div>`+
      `<div class="viewer-body" id="od-viewer-body"></div></div>`;
    document.body.appendChild(ov);
    ov.addEventListener("click",e=>{if(e.target===ov)viewInline.close()});
    $("od-viewer-close").addEventListener("click",()=>viewInline.close());
    document.addEventListener("keydown",e=>{if(e.key==="Escape")viewInline.close()});
  }
  $("od-viewer-title").textContent=title||"";
  const pdf=/\.pdf(\?|$)|\/pdf(\?|$)/i.test(url);
  $("od-viewer-body").innerHTML=pdf
    ?`<embed src="${esc(url)}" type="application/pdf">`
    :`<img src="${esc(url)}" alt="${esc(title||"document")}">`;
  ov.classList.add("open");
}
viewInline.close=()=>{const ov=$("od-viewer");if(ov){ov.classList.remove("open");$("od-viewer-body").innerHTML=""}};
/* Any <a data-view="Title"> anywhere in the app (core or modules) opens inline. */
document.addEventListener("click",e=>{
  const a=e.target.closest("a[data-view]");
  if(!a)return;
  e.preventDefault();
  viewInline(a.getAttribute("href"),a.dataset.view||a.textContent);
});

/* ---------- pluggable UI modules (wwwroot/modules/*.js) ----------
   Each file calls registerODModule({id,label,group,html,init}) — it gets a rail
   button (under its group's header) + a <section class="module" id="mod-{id}">,
   sharing the existing show/hide behavior. ODM exposes the app's helpers so
   module files never touch this file. */
window.ODM={api,esc,$,getActivePatient:()=>activePat,provColor,view:viewInline};

/* ---------- undo stack for charting actions ----------
   Modules record a reversible action via ODM.recordUndo(label, runFn). The band
   Undo button (and ⌘Z) pops the last one and runs its reversal. */
const _undo=[];
function updateUndoBtn(){
  const b=$("undo-btn"); if(!b)return;
  b.hidden=_undo.length===0;
  const lbl=b.querySelector(".undo-label"); if(lbl)lbl.textContent=_undo.length?_undo[_undo.length-1].label:"";
}
function recordUndo(label,run){ _undo.push({label,run}); if(_undo.length>25)_undo.shift(); updateUndoBtn(); }
async function doUndo(){
  const e=_undo.pop(); updateUndoBtn(); if(!e)return;
  const b=$("undo-btn"); if(b)b.disabled=true;
  try{ await e.run(); }catch(err){ alert("Undo failed: "+(err.message||err)); }
  if(b)b.disabled=false;
}
window.ODM.recordUndo=recordUndo;
if($("undo-btn"))$("undo-btn").addEventListener("click",doUndo);
document.addEventListener("keydown",e=>{ if((e.metaKey||e.ctrlKey)&&(e.key==="z"||e.key==="Z")&&_undo.length){ e.preventDefault(); doUndo(); }});
const RAIL_GROUPS=["Schedule","Patient","Clinical","Billing","Insurance","Practice","More"];
/* Pre-create the group containers once, in declared order, so module load order
   never affects their sequence. Each group has a collapsible header (state kept
   in localStorage) and its own items box; empty groups stay hidden. */
function railCollapsed(){try{return JSON.parse(localStorage.getItem("railCollapsed")||"{}")}catch{return{}}}
function initRailGroups(){
  const rail=document.querySelector("nav.rail"),spacer=rail.querySelector(".spacer");
  const collapsed=railCollapsed();
  for(const name of RAIL_GROUPS){
    const g=document.createElement("div");g.dataset.group=name;g.className="railgroup";g.hidden=true;
    if(collapsed[name])g.classList.add("collapsed");
    const h=document.createElement("button");h.type="button";h.className="railgroup-label";
    h.innerHTML=`<span class="rg-caret">▾</span><span>${esc(name)}</span>`;
    h.setAttribute("aria-expanded",String(!collapsed[name]));
    h.addEventListener("click",()=>{
      const on=g.classList.toggle("collapsed");
      h.setAttribute("aria-expanded",String(!on));
      const c=railCollapsed();c[name]=on;localStorage.setItem("railCollapsed",JSON.stringify(c));
    });
    const items=document.createElement("div");items.className="railgroup-items";
    g.appendChild(h);g.appendChild(items);
    rail.insertBefore(g,spacer);
  }
}
initRailGroups();
function railGroupItems(name){
  const g=document.querySelector(`nav.rail [data-group="${RAIL_GROUPS.includes(name)?name:"More"}"]`);
  g.hidden=false;
  return g.querySelector(".railgroup-items");
}
window.registerODModule=function(def){
  const host=document.querySelector("section.module").parentElement;
  const sec=document.createElement("section");
  sec.className="module";sec.id="mod-"+def.id;sec.innerHTML=def.html;
  host.appendChild(sec);
  const btn=document.createElement("button");
  btn.dataset.mod=def.id;btn.textContent=def.label;btn.setAttribute("aria-current","false");
  btn.addEventListener("click",()=>showModule(def.id));
  /* keep each group's buttons alphabetized for scannability */
  const items=railGroupItems(def.group);
  const before=[...items.querySelectorAll("button[data-mod]")].find(b=>b.textContent.localeCompare(def.label)>0);
  items.insertBefore(btn,before||null);
  scheduleApplyRole();
  try{def.init&&def.init()}catch(e){console.error("module "+def.id,e)}
};

/* ---------- role-based workspaces ----------
   Six practice personas. Each scopes the rail to the modules that role actually
   works in, so the nav fits the viewport without hunting/scrolling, and lands on
   that role's primary screen. Hidden modules stay reachable via ⌘K. "Full" shows
   everything (setup / power users). */
const ROLES={
  full:{label:"Full access",landing:"appts",all:true},
  frontdesk:{label:"Front Desk",landing:"appts",core:["appts","family","account","sign"],
    mods:["schedweek","sched-appt-edit","sched-appt-booking","sched-confirm-list","sched-planned-unsched","sched-recall-asap","sched-slot-search","sched-blockouts","comm","misc-sheets","comm-letters","patfam-referrals","patfam-notes","billing-payment-entry","billing-statements"]},
  dentist:{label:"Dentist",landing:"chart",core:["chart","appts","images"],
    mods:["clinical-perio-entry","clinical-proc-entry","treatplan","clinical-rx-entry","clinical-ortho-chart","chart-documents","clinical-lab-cases","patfam-medical","patfam-notes","comm-letters","sched-appt-edit","sched-confirm-list"]},
  hygienist:{label:"Hygienist",landing:"chart",core:["chart","appts"],
    mods:["clinical-perio-entry","clinical-proc-entry","chart-documents","patfam-medical","patfam-notes","sched-recall-asap","sched-confirm-list"]},
  assistant:{label:"Dental Assistant",landing:"chart",core:["chart","images","appts"],
    mods:["clinical-proc-entry","clinical-lab-cases","chart-documents","clinical-ortho-chart","misc-sheets","patfam-medical","sched-appt-edit","sched-appt-booking"]},
  billing:{label:"Billing / Insurance",landing:"account",core:["account","family"],
    mods:["billing-payment-entry","billing-adjustments","billing-claim-builder","billing-payplans","billing-statements","billing-statement-run","billing-deposits","billing-aging-dashboard","ins-coverage-panel","ins-carrier-manager","ins-plan-subscriber-editor","ins-claim-receive","ins-verification","comm"]},
  manager:{label:"Office Manager",landing:"reports",core:["reports","appts","account"],
    mods:["comm-tasks-browser","setup-practice-editor","setup-security-view","setup-feescheds","setup-fee-tools","setup-preferences","setup-clinics","setup-employees-timeclock","setup-appttypes-autocodes","billing-statements","billing-statement-run","billing-aging-dashboard","reports-daysheet","ins-verification"]},
};
let currentRole=localStorage.getItem("odRole")||"full";
function roleAllows(btn){
  const role=ROLES[currentRole]||ROLES.full;
  if(role.all)return true;
  const id=btn.dataset.mod;
  return btn.closest(".railgroup")?(role.mods||[]).includes(id):(role.core||[]).includes(id);
}
let _roleNavigate=false;
function applyRole(){
  const role=ROLES[currentRole]||ROLES.full;
  document.querySelectorAll("nav.rail button[data-mod]").forEach(b=>{b.style.display=roleAllows(b)?"":"none";});
  document.querySelectorAll("nav.rail .railgroup").forEach(g=>{
    g.hidden=![...g.querySelectorAll("button[data-mod]")].some(b=>b.style.display!=="none");
  });
  const active=document.querySelector("nav.rail button[data-mod][aria-current='true']");
  /* Land on the role's primary screen when the role is (re)selected, or when the
     current screen isn't part of this role. Lazy module registration never yanks. */
  if(_roleNavigate||!active||active.style.display==="none")showModule(role.landing||"appts");
  _roleNavigate=false;
  const sel=$("role-select");if(sel&&sel.value!==currentRole)sel.value=currentRole;
}
let _roleRAF;function scheduleApplyRole(){cancelAnimationFrame(_roleRAF);_roleRAF=requestAnimationFrame(applyRole);}
function requestRoleNav(){_roleNavigate=true;scheduleApplyRole();}
(function wireRole(){
  const sel=$("role-select");if(!sel)return;
  sel.innerHTML=Object.entries(ROLES).map(([k,v])=>`<option value="${k}">${esc(v.label)}</option>`).join("");
  sel.value=currentRole;
  sel.addEventListener("change",()=>{currentRole=sel.value;localStorage.setItem("odRole",currentRole);requestRoleNav();});
  /* initial role is applied by applySession() once /api/auth/me resolves */
})();

/* ---------- ⌘K command palette (keyboard-first navigation + search) ---------- */
function moduleIndex(){
  return [...document.querySelectorAll("nav.rail button[data-mod]")].map(b=>({
    id:b.dataset.mod,label:b.textContent.trim(),
    group:b.closest(".railgroup")?.querySelector(".railgroup-label span:last-child")?.textContent||"View"}));
}
let cmdkOpen=false,cmdkItems=[],cmdkIdx=0;
function openCmdk(){
  cmdkOpen=true;$("cmdk").classList.add("open");const i=$("cmdk-input");i.value="";cmdkIdx=0;renderCmdk("");i.focus();
}
function closeCmdk(){cmdkOpen=false;$("cmdk").classList.remove("open");}
function renderCmdk(q){
  q=(q||"").trim().toLowerCase();
  const mods=moduleIndex().filter(m=>!q||m.label.toLowerCase().includes(q)||m.group.toLowerCase().includes(q))
    .map(m=>({type:"mod",...m}));
  const pats=(q?patients.filter(p=>p.name.toLowerCase().includes(q)||String(p.patNum).startsWith(q)):[])
    .slice(0,6).map(p=>({type:"pat",id:p.patNum,label:p.name,group:"Patient #"+p.patNum}));
  cmdkItems=[...pats,...mods].slice(0,40);
  if(cmdkIdx>=cmdkItems.length)cmdkIdx=Math.max(0,cmdkItems.length-1);
  $("cmdk-list").innerHTML=cmdkItems.map((it,i)=>
    `<li role="option" data-i="${i}" aria-selected="${i===cmdkIdx}" class="${i===cmdkIdx?"sel":""}">`+
    `<span class="ck-ic">${it.type==="pat"?"◍":"▸"}</span><span class="ck-label">${esc(it.label)}</span>`+
    `<span class="ck-grp">${esc(it.group)}</span></li>`).join("")||`<li class="ck-empty">No matches</li>`;
}
function activateCmdk(i){
  const it=cmdkItems[i];if(!it)return;
  if(it.type==="pat")setActivePatient(it.id);else showModule(it.id);
  closeCmdk();
}
(function wireCmdk(){
  document.addEventListener("keydown",e=>{
    if((e.metaKey||e.ctrlKey)&&(e.key==="k"||e.key==="K")){e.preventDefault();cmdkOpen?closeCmdk():openCmdk();return;}
    if(!cmdkOpen)return;
    if(e.key==="Escape"){e.preventDefault();closeCmdk();}
    else if(e.key==="ArrowDown"){e.preventDefault();cmdkIdx=Math.min(cmdkIdx+1,cmdkItems.length-1);renderCmdk($("cmdk-input").value);}
    else if(e.key==="ArrowUp"){e.preventDefault();cmdkIdx=Math.max(cmdkIdx-1,0);renderCmdk($("cmdk-input").value);}
    else if(e.key==="Enter"){e.preventDefault();activateCmdk(cmdkIdx);}
  });
  const inp=$("cmdk-input");if(inp)inp.addEventListener("input",()=>{cmdkIdx=0;renderCmdk(inp.value);});
  const list=$("cmdk-list");if(list)list.addEventListener("mousedown",e=>{const li=e.target.closest("li[data-i]");if(li)activateCmdk(+li.dataset.i);});
  const ov=$("cmdk");if(ov)ov.addEventListener("mousedown",e=>{if(e.target===ov)closeCmdk();});
  const btn=$("cmdk-btn");if(btn)btn.addEventListener("click",openCmdk);
})();
/* Module scripts are loaded after authentication (the manifest is under /api/,
   which the server gates). startApp() calls this once the session is established. */
let _modulesLoaded=false;
async function loadModules(){
  if(_modulesLoaded)return; _modulesLoaded=true;
  try{
    const files=await api("/api/ui/modules");
    for(const f of files){
      const s=document.createElement("script");s.src="/modules/"+f;document.body.appendChild(s);
    }
  }catch(e){/* endpoint absent: no dynamic modules */}
}
