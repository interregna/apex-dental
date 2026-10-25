/* Insurance coverage panel — replaces FormInsPlan / FormInsPlans / FormInsPlanSelectFam /
   FormInsBenefits / FormInsRemain / FormBenefitEdit (view side).
   Reads the PatPlans→InsSubs→InsPlans→Benefits chain via /api/patients/{id}/insurance,
   renders plan cards (details + summary tiles + FormInsRemain figures + per-plan benefit
   rows), the family Benefits.GetDisplayMatrix grid and the family subscriber list, and
   reorders coverage (PatPlans.SetOrdinal) by drag-and-drop or the raise button. */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const money=v=>(v==null||v<0)?"—":"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  const ordName=o=>o===1?"Primary":o===2?"Secondary":"Plan "+o;
  let dragPatPlanNum=null;

  registerODModule({
    id:"ins-coverage-panel",
    label:"Insurance",
    group:"Insurance",
    html:`
      <div class="mod-head"><h1>Insurance</h1>
        <span class="sub">coverage &amp; benefits · PatPlans / InsSubs / InsPlans / Benefits chain</span></div>
      <div class="row" style="margin-bottom:12px">
        <button type="button" class="quiet" id="ins-reload">Refresh</button>
        <span class="msg" id="ins-msg"></span>
      </div>
      <div id="ins-plans" style="display:flex;gap:14px;flex-wrap:wrap;margin-bottom:14px"></div>
      <div class="card" style="margin-bottom:14px"><h2>Benefits — family coverage grid</h2>
        <div class="gridwrap scrolly"><table class="grid" id="ins-bens"><thead></thead><tbody></tbody></table></div>
      </div>
      <div class="card"><h2>Family subscribers</h2>
        <div class="gridwrap scrolly"><table class="grid" id="ins-famsubs">
          <thead><tr><th>Subscriber</th><th>Carrier</th><th>Group</th><th>Type</th><th>Subscriber ID</th><th>Covers</th></tr></thead>
          <tbody></tbody>
        </table></div>
      </div>`,
    init(){
      $("ins-reload").addEventListener("click",load);
      $("band-patient").addEventListener("change",()=>setTimeout(load,0));
      load();
    }
  });

  async function load(){
    const pat=getActivePatient();
    if(!pat){$("ins-msg").textContent="No active patient.";return}
    try{
      const d=await api(`/api/patients/${pat.patNum}/insurance`);
      renderPlans(d);
      renderBens(d);
      renderFamSubs(d);
      $("ins-msg").textContent=d.plans.length
        ?`${d.plans.length} plan(s) — drag a card (or ▲) to change coverage order`
        :"";
    }catch(e){$("ins-msg").textContent="insurance endpoint unavailable — "+e.message.slice(0,60)}
  }

  function renderPlans(d){
    const host=$("ins-plans");
    host.innerHTML=d.plans.map((p,i)=>`
      <div class="card" draggable="true" data-patplan="${p.patPlanNum}" data-ord="${p.ordinal}"
        style="flex:1;min-width:320px;max-width:520px;cursor:grab">
        <h2>${ordName(p.ordinal)} — ${esc(p.carrier)}
          ${p.isPending?'<span class="tag">Pending</span>':""}
          ${i>0?`<button type="button" class="quiet" data-raise="${p.patPlanNum}" data-to="${p.ordinal-1}"
            style="float:right;font-size:11px" title="Raise coverage order">▲</button>`:""}</h2>
        <table class="grid"><tbody>
          <tr><td class="mono">Subscriber</td><td>${esc(p.subscriber)}${p.subscriberId?" · "+esc(p.subscriberId):""}</td></tr>
          <tr><td class="mono">Rel'ship</td><td>${esc(p.relationship)}</td></tr>
          <tr><td class="mono">Group</td><td>${esc(p.groupName)}${p.groupNum?" · "+esc(p.groupNum):""}</td></tr>
          <tr><td class="mono">Employer</td><td>${esc(p.employer)}</td></tr>
          <tr><td class="mono">Type</td><td>${esc(p.planType)}</td></tr>
          <tr><td class="mono">Fee sched</td><td>${esc(p.feeSched)}</td></tr>
          <tr><td class="mono">Period</td><td>${esc(p.benefitPeriod)}</td></tr>
          ${p.dateEffective||p.dateTerm?`<tr><td class="mono">Dates</td>
            <td class="mono">${esc(p.dateEffective)} – ${esc(p.dateTerm)||"…"}</td></tr>`:""}
        </tbody></table>
        <div class="stats" style="margin-top:10px">
          <div class="stat"><div class="k">Annual max</div><div class="v">${money(p.annualMaxInd)}</div></div>
          <div class="stat"><div class="k">Deductible</div><div class="v">${money(p.deductInd)}</div></div>
          <div class="stat"><div class="k">Fam max</div><div class="v">${money(p.annualMaxFam)}</div></div>
          <div class="stat"><div class="k">Fam deduct</div><div class="v">${money(p.deductFam)}</div></div>
        </div>
        <div class="stats" style="margin-top:8px">
          <div class="stat"><div class="k">Ins used</div><div class="v">${money(p.insUsed)}</div></div>
          <div class="stat"><div class="k">Pending</div><div class="v">${money(p.pending)}</div></div>
          <div class="stat"><div class="k">Remaining</div><div class="v"><b>${money(p.remaining)}</b></div></div>
        </div>
        <div class="gridwrap scrolly" style="margin-top:10px"><table class="grid">
          <thead><tr><th>Type</th><th>Category</th><th>%</th><th>Amount</th><th>Period</th><th>Qty</th><th>Level</th><th></th></tr></thead>
          <tbody>${p.benefits.map(b=>`<tr>
            <td>${esc(b.type)}</td><td>${esc(b.category)}</td>
            <td>${b.percent<0?"":b.percent+"%"}</td><td>${b.amount<0?"":money(b.amount)}</td>
            <td>${b.timePeriod==="None"?"":esc(b.timePeriod)}</td>
            <td>${b.quantityQualifier==="None"?"":b.quantity+" "+esc(b.quantityQualifier)}</td>
            <td>${b.coverageLevel==="None"?"":esc(b.coverageLevel)}</td>
            <td>${b.isPatOverride?'<span class="tag">Pat override</span>':""}</td></tr>`).join("")
            ||'<tr><td colspan="8"><div class="empty"><b>No benefits entered</b>Enter benefits in the Plan Editor module.</div></td></tr>'}
          </tbody></table></div>
      </div>`).join("")
      ||`<div class="empty card" style="flex:1"><b>No insurance on file</b>
          This patient has no patplan rows. Coverage added on the desktop client appears here immediately.</div>`;
    host.querySelectorAll("[data-raise]").forEach(btn=>btn.addEventListener("click",e=>{
      e.stopPropagation();
      setOrdinal(+btn.dataset.raise,+btn.dataset.to);
    }));
    host.querySelectorAll("[data-patplan]").forEach(cardEl=>{
      cardEl.addEventListener("dragstart",()=>{dragPatPlanNum=+cardEl.dataset.patplan});
      cardEl.addEventListener("dragover",e=>e.preventDefault());
      cardEl.addEventListener("drop",e=>{
        e.preventDefault();
        const target=+cardEl.dataset.patplan;
        if(!dragPatPlanNum||dragPatPlanNum===target)return;
        setOrdinal(dragPatPlanNum,+cardEl.dataset.ord);
        dragPatPlanNum=null;
      });
    });
  }

  function renderBens(d){
    const table=$("ins-bens");
    table.querySelector("thead").innerHTML=d.plans.length
      ?"<tr><th>Benefit</th>"+d.plans.map(p=>`<th>${ordName(p.ordinal)}</th>`).join("")+"</tr>":"";
    table.querySelector("tbody").innerHTML=d.benefitRows.map(row=>
      `<tr><td>${esc(row.descript)}</td>${row.cells.map(c=>`<td class="mono">${esc(c)}</td>`).join("")}</tr>`).join("")
      ||`<tr><td colspan="${d.plans.length+1}"><div class="empty"><b>No benefits entered</b>
          Benefits attached to these plans render here from Benefits.GetDisplayMatrix.</div></td></tr>`;
  }

  function renderFamSubs(d){
    $("ins-famsubs").querySelector("tbody").innerHTML=d.familySubscribers.map(s=>
      `<tr><td>${esc(s.subscriber)}</td><td>${esc(s.carrier)}</td><td>${esc(s.groupName)}</td>`+
      `<td>${esc(s.planType)}</td><td class="mono">${esc(s.subscriberId)}</td><td>${s.covered.map(esc).join(", ")}</td></tr>`).join("")
      ||'<tr><td colspan="6"><div class="empty"><b>No subscribers</b>No insurance subscribers in this family.</div></td></tr>';
  }

  async function setOrdinal(patPlanNum,ordinal){
    $("ins-msg").textContent="Reordering…";
    try{
      await api(`/api/patplans/${patPlanNum}/ordinal`,{method:"POST",
        headers:{"Content-Type":"application/json"},body:JSON.stringify({ordinal})});
      load();
    }catch(e){$("ins-msg").textContent="Reorder failed: "+e.message.slice(0,60)}
  }
})();
