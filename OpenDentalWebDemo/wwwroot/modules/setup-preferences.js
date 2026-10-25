/* Preferences editor — a searchable key/value grid over the real `preference` table.
   Each row edits one pref inline (checkbox for bool-kind values, number/text input
   otherwise) and saves on Enter or blur through PUT /api/prefs/{prefName}, which routes
   the write through the Prefs.Update* helpers, refreshes the cache, and audit-logs it.
   Retires the long tail of tiny Setup dialogs whose only job is toggling a single pref. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

registerODModule({
  id:"setup-preferences",
  label:"Preferences",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Preferences</h1>
    <span class="sub">searchable key/value editor over the preference table</span></div>
  <div class="card" style="border-color:#b8860b;background:rgba(184,134,11,.06)">
    <b>Advanced.</b> Editing preferences directly changes program behavior for the whole
    database. Every change is written through the real Prefs helpers and audit-logged.
    Change a value only if you know exactly what it does.
  </div>
  <div class="card">
    <div class="row" style="display:flex;gap:8px;align-items:center;margin-bottom:10px">
      <input id="pref-search" placeholder="Search pref name…" style="width:280px">
      <button type="button" class="action" id="pref-search-btn">Search</button>
      <span class="msg" id="pref-msg"></span>
    </div>
    <div class="gridwrap scrolly"><table class="grid" id="pref-list">
      <thead><tr><th>Pref name</th><th>Value</th><th></th></tr></thead><tbody></tbody>
    </table></div>
  </div>`,
  init(){
    let rows=[];

    function rowHtml(p){
      const kind=p.kind||"text";
      let field;
      if(kind==="bool"){
        const on=p.valueString==="1"||p.valueString==="True";
        field=`<input type="checkbox" data-pref="${esc(p.prefName)}" data-kind="bool" ${on?"checked":""}>`;
      }else{
        const type=kind==="number"?"number":"text";
        field=`<input type="${type}" data-pref="${esc(p.prefName)}" data-kind="${esc(kind)}" value="${esc(p.valueString)}" style="width:260px">`;
      }
      return `<tr><td class="mono">${esc(p.prefName)}</td>`+
        `<td>${field}</td>`+
        `<td class="mono" data-stat="${esc(p.prefName)}"></td></tr>`;
    }

    async function load(){
      const q=$("pref-search").value.trim();
      $("pref-msg").textContent="Loading…";
      try{
        const data=await api(`/api/prefs?search=${encodeURIComponent(q)}`);
        rows=data.rows||[];
        $("pref-list").querySelector("tbody").innerHTML=rows.map(rowHtml).join("")
          ||`<tr><td colspan="3"><div class="empty"><b>No matching preferences</b>Try a different search term.</div></td></tr>`;
        const shown=rows.length,total=data.totalMatches||0;
        $("pref-msg").textContent=total>shown?`Showing ${shown} of ${total} matches (narrow your search)`:`${total} match${total===1?"":"es"}`;
        wire();
      }catch(err){$("pref-msg").textContent="prefs endpoint unavailable — "+err.message.slice(0,80)}
    }

    async function save(el){
      const prefName=el.dataset.pref,kind=el.dataset.kind;
      const value=kind==="bool"?(el.checked?"1":"0"):el.value;
      const stat=$("pref-list").querySelector(`[data-stat="${CSS.escape(prefName)}"]`);
      if(stat)stat.textContent="…";
      try{
        const updated=await api(`/api/prefs/${encodeURIComponent(prefName)}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify({value})});
        const p=rows.find(x=>x.prefName===prefName);if(p)p.valueString=updated.valueString;
        if(stat){stat.textContent="✓ saved";stat.style.color="var(--ok,#2e7d32)";}
      }catch(err){
        if(stat){stat.textContent="✗ "+err.message.slice(0,60);stat.style.color="var(--bad,#c62828)";}
      }
    }

    function wire(){
      const tbl=$("pref-list");
      tbl.querySelectorAll('input[data-kind="bool"]').forEach(el=>
        el.addEventListener("change",()=>save(el)));
      tbl.querySelectorAll('input[data-kind="number"],input[data-kind="text"]').forEach(el=>{
        el.addEventListener("blur",()=>save(el));
        el.addEventListener("keydown",e=>{if(e.key==="Enter"){e.preventDefault();el.blur();}});
      });
    }

    $("pref-search-btn").addEventListener("click",load);
    $("pref-search").addEventListener("keydown",e=>{if(e.key==="Enter"){e.preventDefault();load();}});
    load();
  }
});
})();
