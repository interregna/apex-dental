/* Chart Documents module (chart-documents): the web replacement for the Imaging
   module's document-tree management. Browses the active patient's documents grouped
   by image category (DefCat.ImageCats), edits a document's category/description/date,
   and deletes a document. Auto-loaded by app.js; talks to ChartDocumentsModule.cs,
   which runs the real Documents / ImageStore business layer and stamps the same
   ImageEdit / ImageDelete SecurityLogs the desktop writes.
   View links reuse the pre-existing GET /api/documents/{n}/file endpoint and open
   inline through the shared lightbox (data-view) — never a new window. */
"use strict";
registerODModule({
  id:"chart-documents",
  label:"Documents",
  group:"Clinical",
  html:`
  <div class="mod-head"><h1>Documents</h1>
    <span class="sub">imaging document tree · category / description / date · real Documents + ImageStore</span></div>
  <div class="card">
    <h2>Patient documents <span class="msg" id="cdoc-msg"></span></h2>
    <div class="row" style="margin-bottom:10px;gap:8px;align-items:center">
      <button type="button" class="action" id="cdoc-up-btn">Upload document…</button>
      <input id="cdoc-up-file" type="file" hidden accept="image/*,application/pdf,.pdf">
      <span class="sub">image or PDF — filed to this patient's documents</span>
    </div>
    <div id="cdoc-tree" class="scrolly"></div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    let cats=[];

    const upBtn=$("cdoc-up-btn"),upFile=$("cdoc-up-file");
    upBtn.addEventListener("click",()=>{
      if(!getActivePatient()){ $("cdoc-msg").textContent="Pick a patient first."; return; }
      upFile.click();
    });
    upFile.addEventListener("change",async()=>{
      const pat=getActivePatient(),f=upFile.files[0];
      if(!pat||!f)return;
      $("cdoc-msg").textContent="Uploading "+f.name+"…";
      try{
        const buf=await f.arrayBuffer();
        await api(`/api/scan/upload?patNum=${pat.patNum}&filename=${encodeURIComponent(f.name)}`,
          {method:"POST",headers:{"Content-Type":"application/octet-stream"},body:buf});
        $("cdoc-msg").textContent="Uploaded — set its category below if needed.";
        upFile.value="";loadTree();
      }catch(e){ $("cdoc-msg").textContent="Upload failed: "+String(e.message||e).slice(0,80); }
    });

    function catOptions(sel){
      return cats.filter(c=>!c.isHidden||c.defNum===sel)
        .map(c=>`<option value="${c.defNum}"${c.defNum===sel?" selected":""}>${esc(c.name)}</option>`).join("");
    }

    async function loadCats(){
      try{ cats=await api("/api/image-categories"); }
      catch(e){ cats=[]; }
    }

    async function loadTree(){
      const pat=getActivePatient();
      if(!pat){ $("cdoc-tree").innerHTML=`<div class="empty"><b>No active patient</b>Pick a patient in the band above to manage documents.</div>`; return; }
      $("cdoc-msg").textContent="";
      let groups;
      try{ groups=await api(`/api/patients/${pat.patNum}/documents-tree`); }
      catch(e){ $("cdoc-msg").textContent="documents endpoint unavailable"; return; }
      const anyDocs=groups.some(g=>g.docs.length>0);
      if(!anyDocs){
        $("cdoc-tree").innerHTML=`<div class="empty"><b>No documents</b>Scan or upload from the Images panel to file the first document for this patient.</div>`;
        return;
      }
      $("cdoc-tree").innerHTML=groups.filter(g=>g.docs.length>0).map(g=>
        `<div class="cdoc-group"><h3 style="margin:14px 0 6px;font-size:13px;opacity:.75">${esc(g.catName)} <span class="tag">${g.docs.length}</span></h3>`+
        g.docs.map(d=>
          `<div class="cdoc-row" data-doc="${d.docNum}" style="border:1px solid color-mix(in srgb,CanvasText 14%,transparent);border-radius:8px;padding:10px;margin-bottom:8px">
            <div class="row" style="gap:10px;align-items:center;flex-wrap:wrap">
              <span class="mono">${esc(d.fileName)}</span>
              <span class="tag">${esc(d.imgType)}</span>
              <a href="/api/documents/${d.docNum}/file" data-view="${esc(d.fileName)}">view</a>
            </div>
            <form class="row cdoc-edit" style="gap:8px;margin-top:8px;flex-wrap:wrap;align-items:center">
              <select class="cdoc-cat">${catOptions(g.catDefNum)}</select>
              <input class="cdoc-desc" placeholder="Description" value="${esc(d.description)}" style="min-width:180px">
              <input class="cdoc-date" type="date" value="${esc(d.dateCreated)}">
              <button class="action">Save</button>
              <button type="button" class="quiet cdoc-del">Delete</button>
              <span class="msg cdoc-rowmsg"></span>
            </form>
          </div>`).join("")+
        `</div>`).join("");

      $("cdoc-tree").querySelectorAll(".cdoc-row").forEach(row=>{
        const docNum=row.dataset.doc;
        const msg=row.querySelector(".cdoc-rowmsg");
        row.querySelector(".cdoc-edit").addEventListener("submit",async e=>{
          e.preventDefault();
          msg.textContent="Saving…";
          try{
            await api(`/api/documents/${docNum}`,{method:"PUT",
              headers:{"Content-Type":"application/json"},
              body:JSON.stringify({
                docCategory:+row.querySelector(".cdoc-cat").value,
                description:row.querySelector(".cdoc-desc").value,
                dateCreated:row.querySelector(".cdoc-date").value})});
            msg.textContent="Saved.";
            loadTree();
          }catch(err){ msg.textContent="Save failed: "+err.message.slice(0,80); }
        });
        row.querySelector(".cdoc-del").addEventListener("click",async()=>{
          if(!confirm("Delete this document? This removes the file and its record.")) return;
          msg.textContent="Deleting…";
          try{
            await api(`/api/documents/${docNum}`,{method:"DELETE"});
            loadTree();
          }catch(err){ msg.textContent="Delete failed: "+err.message.slice(0,120); }
        });
      });
    }

    //Follow the patient band: reload the tree whenever the active patient changes.
    $("band-patient").addEventListener("change",loadTree);
    loadCats().then(loadTree);
  }
});
