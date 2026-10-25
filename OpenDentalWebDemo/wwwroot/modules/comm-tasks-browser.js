/* Tasks browser module (comm-tasks-browser): task-list tree, task grid with
   show-done toggle, task detail pane with the full note thread, and open
   tickets for the active patient. Auto-loaded by app.js; talks to
   CommTasksBrowserModule.cs endpoints, which run the real TaskLists / Tasks /
   TaskNotes business layer. */
"use strict";
registerODModule({
  id:"comm-tasks-browser",
  label:"Tasks",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Tasks</h1>
    <span class="sub">task lists · tasks · note threads · patient tickets</span></div>
  <div class="cols" style="grid-template-columns:minmax(220px,300px) 1fr minmax(280px,360px)">
    <div class="card">
      <h2>Task lists <span class="msg" id="tsk-tree-msg"></span></h2>
      <div class="scrolly" id="tsk-tree" style="max-height:420px"></div>
    </div>
    <div class="card">
      <h2>Tasks <span class="msg" id="tsk-grid-msg"></span></h2>
      <label style="display:flex;align-items:center;gap:4px;font-size:12px;margin-bottom:6px">
        <input type="checkbox" id="tsk-showdone">Show done (last week)</label>
      <div class="gridwrap scrolly"><table class="grid" id="tsk-grid">
        <thead><tr><th>Status</th><th>Priority</th><th>Patient</th><th>Description</th><th>Entered</th></tr></thead>
        <tbody></tbody></table></div>
    </div>
    <div class="card">
      <h2>Task detail <span class="msg" id="tsk-det-msg"></span></h2>
      <div id="tsk-detail"><div class="empty"><b>No task selected</b>Pick a task from the grid.</div></div>
    </div>
  </div>
  <div class="card" style="margin-top:14px">
    <h2>Patient tickets <span class="msg" id="tsk-pat-msg"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="tsk-pat-grid">
      <thead><tr><th>Status</th><th>Priority</th><th>Task list</th><th>Description</th><th>Author</th><th>Entered</th></tr></thead>
      <tbody></tbody></table></div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    let activeListNum=0;

    const prioTag=t=>t.priority
      ?`<span class="tag"${t.priorityColor?` style="border-color:${t.priorityColor};color:${t.priorityColor}"`:""}>${esc(t.priority)}</span>`:"";
    const statusTag=t=>`<span class="tag">${t.isDone?"Done":esc(t.status)}${t.isUnread?" · new":""}</span>`;
    const clip=(s,n)=>s.length>n?s.slice(0,n)+"…":s;

    async function loadTree(){
      try{
        const lists=await api("/api/tasks/lists");
        const byParent={};
        lists.filter(l=>!l.isArchived).forEach(l=>{(byParent[l.parent]=byParent[l.parent]||[]).push(l)});
        const render=(parent,depth)=>(byParent[parent]||[]).map(l=>
          `<div class="tsk-node" data-list="${l.taskListNum}" style="padding:3px 6px 3px ${8+depth*16}px;cursor:pointer;border-radius:5px${l.taskListNum===activeListNum?";background:color-mix(in srgb,CanvasText 10%,transparent)":""}">`+
          `${byParent[l.taskListNum]?"▸ ":""}${esc(l.descript)}`+
          `${l.isRepeating?` <span class="tag">rep</span>`:""}`+
          `${l.newTaskCount>0?` <span class="tag">${l.newTaskCount} new</span>`:""}</div>`+
          render(l.taskListNum,depth+1)).join("");
        $("tsk-tree").innerHTML=render(0,0)
          ||`<div class="empty"><b>No task lists</b>Create task lists in the desktop app first.</div>`;
        $("tsk-tree").querySelectorAll("[data-list]").forEach(node=>
          node.addEventListener("click",()=>{activeListNum=+node.dataset.list;loadTree();loadTasks();}));
        $("tsk-tree-msg").textContent="";
      }catch(e){$("tsk-tree-msg").textContent="task lists endpoint unavailable"}
    }

    async function loadTasks(){
      if(!activeListNum){return}
      try{
        const showDone=$("tsk-showdone").checked;
        const data=await api(`/api/tasks/lists/${activeListNum}?showDone=${showDone}`);
        $("tsk-grid").querySelector("tbody").innerHTML=data.tasks.map(t=>
          `<tr data-task="${t.taskNum}" style="cursor:pointer${t.isDone?";opacity:.55":""}">`+
          `<td>${statusTag(t)}</td><td>${prioTag(t)}</td>`+
          `<td>${esc(t.patientName)}</td><td>${esc(clip(t.descript,120))}</td>`+
          `<td class="mono">${esc(t.dateTimeEntry)}</td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No tasks</b>This list has no ${showDone?"":"open "}tasks.</div></td></tr>`;
        $("tsk-grid-msg").textContent=esc(data.descript);
        $("tsk-grid").querySelectorAll("[data-task]").forEach(row=>
          row.addEventListener("click",()=>loadDetail(+row.dataset.task)));
      }catch(e){$("tsk-grid-msg").textContent="tasks endpoint unavailable"}
    }

    async function loadDetail(taskNum){
      $("tsk-det-msg").textContent="Loading…";
      try{
        const t=await api(`/api/tasks/${taskNum}`);
        $("tsk-detail").innerHTML=
          `<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:6px">`+
            `${statusTag(t)}${prioTag(t)}`+
            `${t.isRepeating?`<span class="tag">repeating</span>`:""}`+
            `<span class="tag">${esc(t.objectType)}</span></div>`+
          `<div style="font-size:12px;opacity:.7;margin-bottom:6px">`+
            `${esc(t.taskListDescript)||"(no list)"} · ${esc(t.author)||"(no author)"} · ${esc(t.dateTimeEntry)}`+
            `${t.dateTimeFinished?" · finished "+esc(t.dateTimeFinished):""}</div>`+
          `<div style="white-space:pre-wrap;font-size:13px;margin-bottom:10px">${esc(t.descript)}</div>`+
          (t.descriptOverride?`<div style="white-space:pre-wrap;font-size:12px;opacity:.8;margin-bottom:10px"><b>Override:</b> ${esc(t.descriptOverride)}</div>`:"")+
          `<h2 style="margin-top:8px">Notes (${t.notes.length})</h2>`+
          (t.notes.map(n=>
            `<div style="border-top:1px solid color-mix(in srgb,CanvasText 12%,transparent);padding:6px 0">`+
            `<div style="font-size:11px;opacity:.65">${esc(n.user)} · ${esc(n.dateTimeNote)}</div>`+
            `<div style="white-space:pre-wrap;font-size:13px">${esc(n.note)}</div></div>`).join("")
            ||`<div class="empty"><b>No notes</b>This task has no note thread.</div>`);
        $("tsk-det-msg").textContent="";
      }catch(e){$("tsk-det-msg").textContent="task detail unavailable"}
    }

    async function loadPatientTickets(){
      const pat=getActivePatient();
      if(!pat){$("tsk-pat-msg").textContent="no active patient";return}
      try{
        const rows=await api(`/api/tasks/patient/${pat.patNum}`);
        $("tsk-pat-grid").querySelector("tbody").innerHTML=rows.map(t=>
          `<tr data-task="${t.taskNum}" style="cursor:pointer">`+
          `<td>${statusTag(t)}</td><td>${prioTag(t)}</td>`+
          `<td>${esc(t.taskListDescript)}</td><td>${esc(clip(t.descript,140))}</td>`+
          `<td>${esc(t.author)}</td><td class="mono">${esc(t.dateTimeEntry)}</td></tr>`).join("")
          ||`<tr><td colspan="6"><div class="empty"><b>No open tickets</b>No open tasks reference this patient.</div></td></tr>`;
        $("tsk-pat-msg").textContent="";
        $("tsk-pat-grid").querySelectorAll("[data-task]").forEach(row=>
          row.addEventListener("click",()=>loadDetail(+row.dataset.task)));
      }catch(e){$("tsk-pat-msg").textContent="patient tickets endpoint unavailable"}
    }

    $("tsk-showdone").addEventListener("change",loadTasks);
    //Follow the patient band: reload the tickets whenever the active patient changes.
    $("band-patient").addEventListener("change",loadPatientTickets);
    loadTree();
    loadPatientTickets();
  }
});
