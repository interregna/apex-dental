/* Fee tools — the copy / increase halves of FormFeeSchedTools, practice-level
   fees only (ClinicNum 0, ProvNum 0).  Panel 1 copies every fee from a source
   schedule into a target (optionally clearing the target first).  Panel 2 raises
   or lowers every fee in one schedule by a percent or a flat amount, with the
   same rounding the desktop uses, an optional preview, and a confirm.  Both
   dropdowns reuse GET /api/feescheds. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

const money=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});

registerODModule({
  id:"setup-fee-tools",
  label:"Fee Tools",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Fee tools</h1>
    <span class="sub">copy a fee schedule · raise or lower fees</span></div>
  <div class="card">
    <h2>Copy fees <span class="msg" id="cp-msg"></span></h2>
    <p class="msg" style="margin:0 0 8px">Copies every practice-level fee from the source schedule into the target.</p>
    <div class="row" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
      <label>From <select id="cp-from" style="min-width:240px"></select></label>
      <label>To <select id="cp-to" style="min-width:240px"></select></label>
      <label style="font-size:13px"><input type="checkbox" id="cp-clear"> clear target first</label>
      <button type="button" class="action" id="cp-go">Copy</button>
      <span class="msg" id="cp-result"></span>
    </div>
  </div>
  <div class="card">
    <h2>Adjust fees <span class="msg" id="aj-msg"></span></h2>
    <p class="msg" style="margin:0 0 8px">Raises or lowers every fee in the schedule. Zero fees are left unchanged.</p>
    <div class="row" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:8px">
      <label>Schedule <select id="aj-sched" style="min-width:240px"></select></label>
      <label>Mode
        <select id="aj-mode">
          <option value="percent">Percent</option>
          <option value="amount">Flat amount</option>
        </select>
      </label>
      <label>Value <input id="aj-value" type="number" step="0.01" style="width:90px" value="0"></label>
      <label>Round to
        <select id="aj-round">
          <option value="0">Nearest dollar</option>
          <option value="1">Nearest dime</option>
          <option value="2">Nearest penny</option>
        </select>
      </label>
      <button type="button" class="quiet" id="aj-preview">Preview</button>
      <button type="button" class="action" id="aj-go">Apply</button>
      <span class="msg" id="aj-result"></span>
    </div>
    <div class="gridwrap scrolly"><table class="grid" id="aj-grid">
      <thead><tr><th>Code</th><th style="text-align:right">Before</th><th style="text-align:right">After</th></tr></thead>
      <tbody></tbody>
    </table></div>
  </div>`,
  init(){
    let scheds=[];

    function fillPickers(){
      const opts=scheds.map(s=>
        `<option value="${s.feeSchedNum}">${esc(s.description)} (${esc(s.feeSchedType)})`+
        `${s.isHidden?" — hidden":""}</option>`).join("")
        ||`<option value="">(no fee schedules)</option>`;
      ["cp-from","cp-to","aj-sched"].forEach(id=>{$(id).innerHTML=opts;});
      if(scheds.length>1)$("cp-to").selectedIndex=1;
    }

    async function loadScheds(){
      try{
        scheds=await api("/api/feescheds");
        fillPickers();
      }catch(err){
        $("cp-msg").textContent="feescheds endpoint unavailable — "+err.message.slice(0,60);
        $("aj-msg").textContent="feescheds endpoint unavailable — "+err.message.slice(0,60);
      }
    }

    //---------- copy ----------
    $("cp-go").addEventListener("click",async()=>{
      const from=$("cp-from").value,to=$("cp-to").value;
      if(!from||!to){$("cp-result").textContent="Pick both schedules.";return;}
      if(from===to){$("cp-result").textContent="Source and target must differ.";return;}
      const clearFirst=$("cp-clear").checked;
      const fromName=(scheds.find(s=>s.feeSchedNum==from)||{}).description||from;
      const toName=(scheds.find(s=>s.feeSchedNum==to)||{}).description||to;
      if(!confirm(`Copy all fees from "${fromName}" to "${toName}"`+
        (clearFirst?", clearing the target first":"")+"?"))return;
      $("cp-result").textContent="Copying…";
      try{
        const res=await api("/api/feetools/copy",{method:"POST",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({fromFeeSchedNum:Number(from),toFeeSchedNum:Number(to),clearFirst})});
        $("cp-result").textContent=`✓ copied ${res.copied} fee(s)`;
      }catch(err){$("cp-result").textContent="Copy failed: "+err.message.slice(0,120);}
    });

    //---------- adjust ----------
    function readAdjust(){
      return {
        feeSchedNum:Number($("aj-sched").value),
        mode:$("aj-mode").value,
        value:Number($("aj-value").value),
        roundTo:Number($("aj-round").value),
      };
    }

    async function preview(){
      const a=readAdjust();
      const tbody=$("aj-grid").querySelector("tbody");
      if(!a.feeSchedNum){$("aj-result").textContent="Pick a schedule.";return;}
      $("aj-result").textContent="Loading preview…";
      try{
        const q=`?feeSchedNum=${a.feeSchedNum}&mode=${encodeURIComponent(a.mode)}`+
          `&value=${encodeURIComponent(a.value)}&roundTo=${a.roundTo}`;
        const data=await api("/api/feetools/preview"+q);
        $("aj-result").textContent=`${esc(data.description)} — ${data.adjustable} of ${data.feeCount} `+
          `fee(s) would change · showing ${Math.min(data.feeCount,data.limit)}`;
        tbody.innerHTML=data.rows.map(r=>
          `<tr><td class="mono">${esc(r.procCode||"")}</td>`+
          `<td style="text-align:right">${esc(money(r.before))}</td>`+
          `<td style="text-align:right">${esc(money(r.after))}</td></tr>`).join("")
          ||`<tr><td colspan="3"><div class="empty"><b>No fees</b>This schedule has no fees set.</div></td></tr>`;
      }catch(err){
        tbody.innerHTML="";
        $("aj-result").textContent="Preview failed: "+err.message.slice(0,120);
      }
    }

    $("aj-preview").addEventListener("click",preview);

    $("aj-go").addEventListener("click",async()=>{
      const a=readAdjust();
      if(!a.feeSchedNum){$("aj-result").textContent="Pick a schedule.";return;}
      if(a.mode==="percent"&&(a.value<-99||a.value>99)){
        $("aj-result").textContent="Percent must be between -99 and 99.";return;
      }
      const name=(scheds.find(s=>s.feeSchedNum==a.feeSchedNum)||{}).description||a.feeSchedNum;
      const desc=a.mode==="amount"?money(a.value):a.value+"%";
      if(!confirm(`Adjust every fee in "${name}" by ${desc}? `+
        "Previous amounts will be overwritten. Consider copying to a backup schedule first."))return;
      $("aj-result").textContent="Applying…";
      try{
        const res=await api("/api/feetools/adjust",{method:"POST",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(a)});
        $("aj-result").textContent=`✓ adjusted ${res.adjusted} fee(s)`;
        preview();
      }catch(err){$("aj-result").textContent="Adjust failed: "+err.message.slice(0,120);}
    });

    loadScheds();
  }
});
})();
