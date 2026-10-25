/* Insurance plan & subscriber editor — replaces the write side of FormInsPlan /
   FormInsPlans / FormInsPlanSelectFam / FormBenefitEdit.
   The add-insurance workflow: search plans by carrier and reuse one (or create a
   new plan), create/edit the InsSub for the subscriber, attach the active patient
   via PatPlan at the next ordinal, and edit the plan's benefit list
   (Benefits.UpdateList diff-save). */
"use strict";
(function(){
  const {api,esc,$,getActivePatient}=window.ODM;
  const PLAN_TYPES=[["","Category Percentage"],["p","PPO Percentage"],["f","Medicaid or Flat Co-pay"],["c","Capitation"]];
  const BEN_TYPES=["ActiveCoverage","CoInsurance","Deductible","CoPayment","Exclusions","Limitations","WaitingPeriod"];
  const TIME_PERIODS=["None","ServiceYear","CalendarYear","Lifetime","Years","NumberInLast12Months"];
  const QTY_QUALS=["None","NumberOfServices","AgeLimit","Visits","Years","Months"];
  const COV_LEVELS=["None","Individual","Family"];
  const RELATS=["Self","Spouse","Child","Employee","HandicapDep","SignifOther","InjuredPlaintiff","LifePartner","Dependent"];
  let curPlan=null;      //selected/created plan (raw shape from /edit)
  let curSub=null;       //selected/created InsSub
  let benRows=[];        //editable benefit rows for curPlan
  let covCats=[],feeScheds=[];

  registerODModule({
    id:"ins-plan-subscriber-editor",
    label:"Plan Editor",
    group:"Insurance",
    html:`
      <div class="mod-head"><h1>Insurance Plan &amp; Subscriber</h1>
        <span class="sub">pick or create a plan &middot; subscribe &middot; attach patient &middot; edit benefits</span></div>
      <div class="card" style="margin-bottom:12px"><h2>1 &middot; Find or create plan</h2>
        <form class="row" id="ipse-searchform">
          <input id="ipse-search" placeholder="Carrier name…" style="min-width:220px">
          <button type="submit" class="quiet">Search plans</button>
          <button type="button" class="quiet" id="ipse-newplan">New blank plan</button>
          <span class="msg" id="ipse-searchmsg"></span>
        </form>
        <div class="gridwrap scrolly" style="max-height:200px;margin-top:10px">
          <table class="grid" id="ipse-plans"><thead><tr>
            <th>Carrier</th><th>Group name</th><th>Group #</th><th>Type</th><th>Employer</th><th>Fee sched</th>
          </tr></thead><tbody></tbody></table></div>
      </div>
      <div class="card" style="margin-bottom:12px"><h2>2 &middot; Plan <span id="ipse-plantag" class="tag" style="display:none"></span></h2>
        <form class="row" id="ipse-planform">
          <input id="ipse-carrier" placeholder="Carrier name" required style="min-width:200px">
          <input id="ipse-carrierphone" placeholder="Carrier phone">
          <input id="ipse-groupname" placeholder="Group name">
          <input id="ipse-groupnum" placeholder="Group #">
          <select id="ipse-plantype">${PLAN_TYPES.map(t=>`<option value="${t[0]}">${t[1]}</option>`).join("")}</select>
          <select id="ipse-feesched"><option value="0">No fee sched</option></select>
          <input id="ipse-employer" placeholder="Employer">
          <select id="ipse-monthrenew"><option value="0">Calendar year</option>
            ${[..."123456789",10,11,12].map((m,i)=>`<option value="${i+1}">Renews month ${i+1}</option>`).join("")}</select>
          <input id="ipse-plannote" placeholder="Plan note" style="min-width:200px">
          <label style="font-size:12px"><input type="checkbox" id="ipse-hidden"> Hidden</label>
          <button type="submit" class="quiet" id="ipse-plansave">Create plan</button>
          <span class="msg" id="ipse-planmsg"></span>
        </form>
      </div>
      <div class="card" style="margin-bottom:12px"><h2>3 &middot; Subscription (InsSub)</h2>
        <div class="gridwrap" style="margin-bottom:10px">
          <table class="grid" id="ipse-subs"><thead><tr>
            <th>Carrier</th><th>Group</th><th>Subscriber ID</th><th>Effective</th><th>Term</th><th>Release</th><th>Assign</th>
          </tr></thead><tbody></tbody></table></div>
        <form class="row" id="ipse-subform">
          <input id="ipse-subid" placeholder="Subscriber ID">
          <input id="ipse-dateeff" type="date" title="Date effective">
          <input id="ipse-dateterm" type="date" title="Date term">
          <label style="font-size:12px"><input type="checkbox" id="ipse-release" checked> Release info</label>
          <label style="font-size:12px"><input type="checkbox" id="ipse-assign" checked> Assign benefits</label>
          <input id="ipse-subnote" placeholder="Subscriber note" style="min-width:160px">
          <button type="submit" class="quiet" id="ipse-subsave">Create subscription</button>
          <span class="msg" id="ipse-submsg"></span>
        </form>
      </div>
      <div class="card" style="margin-bottom:12px"><h2>4 &middot; Attach patient (PatPlan)</h2>
        <form class="row" id="ipse-ppform">
          <select id="ipse-relat">${RELATS.map(r=>`<option>${r}</option>`).join("")}</select>
          <label style="font-size:12px"><input type="checkbox" id="ipse-pending"> Pending</label>
          <button type="submit" class="quiet">Attach active patient at next ordinal</button>
          <span class="msg" id="ipse-ppmsg"></span>
        </form>
      </div>
      <div class="card"><h2>5 &middot; Plan benefits</h2>
        <div class="gridwrap scrolly"><table class="grid" id="ipse-bens"><thead><tr>
          <th>Type</th><th>Category</th><th>%</th><th>Amount</th><th>Time period</th><th>Qualifier</th><th>Qty</th><th>Level</th><th></th>
        </tr></thead><tbody></tbody></table></div>
        <div class="row" style="margin-top:10px">
          <button type="button" class="quiet" id="ipse-benadd">Add benefit row</button>
          <button type="button" class="quiet" id="ipse-bensave">Save benefits</button>
          <span class="msg" id="ipse-benmsg"></span>
        </div>
      </div>`,
    init(){
      $("ipse-searchform").addEventListener("submit",e=>{e.preventDefault();searchPlans()});
      $("ipse-newplan").addEventListener("click",()=>selectPlan(null));
      $("ipse-planform").addEventListener("submit",e=>{e.preventDefault();savePlan()});
      $("ipse-subform").addEventListener("submit",e=>{e.preventDefault();saveSub()});
      $("ipse-ppform").addEventListener("submit",e=>{e.preventDefault();attachPatPlan()});
      $("ipse-benadd").addEventListener("click",addBenRow);
      $("ipse-bensave").addEventListener("click",saveBens);
      $("band-patient").addEventListener("change",()=>setTimeout(loadSubs,0));
      loadPicklists();
      loadSubs();
    }
  });

  async function loadPicklists(){
    try{
      [covCats,feeScheds]=await Promise.all([api("/api/insurance/covcats"),api("/api/insurance/feescheds")]);
      $("ipse-feesched").innerHTML='<option value="0">No fee sched</option>'
        +feeScheds.map(f=>`<option value="${f.feeSchedNum}">${esc(f.description)}</option>`).join("");
    }catch(e){$("ipse-searchmsg").textContent="picklists unavailable — "+e.message.slice(0,60)}
  }

  async function searchPlans(){
    const term=$("ipse-search").value.trim();
    if(!term){$("ipse-searchmsg").textContent="Type a carrier name.";return}
    $("ipse-searchmsg").textContent="Searching…";
    try{
      const d=await api("/api/insplans/search?carrier="+encodeURIComponent(term));
      const tb=$("ipse-plans").querySelector("tbody");
      tb.innerHTML=d.plans.map(p=>`
        <tr class="rowsel" data-plan="${p.planNum}">
          <td>${esc(p.carrier)}</td><td>${esc(p.groupName)}</td><td class="mono">${esc(p.groupNum)}</td>
          <td>${esc(p.planTypeDesc)}</td><td>${esc(p.employer)}</td><td>${esc(p.feeSched)}</td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No matching plans</b>
            No existing plan for that carrier — fill in section 2 to create one.</div></td></tr>`;
      tb.querySelectorAll("[data-plan]").forEach(tr=>tr.addEventListener("click",()=>loadPlan(+tr.dataset.plan)));
      $("ipse-searchmsg").textContent=`${d.plans.length} plan(s) — click a row to reuse it`;
    }catch(e){$("ipse-searchmsg").textContent="Search failed: "+e.message.slice(0,60)}
  }

  async function loadPlan(planNum){
    try{selectPlan(await api(`/api/insplans/${planNum}/edit`))}
    catch(e){$("ipse-planmsg").textContent="Load failed: "+e.message.slice(0,60)}
  }

  function selectPlan(plan){
    curPlan=plan;
    curSub=null;
    const tag=$("ipse-plantag");
    tag.style.display=plan?"":"none";
    tag.textContent=plan?"Plan #"+plan.planNum:"";
    $("ipse-plansave").textContent=plan?"Save plan changes":"Create plan";
    $("ipse-carrier").value=plan?plan.carrierName:"";
    $("ipse-carrierphone").value=plan?plan.carrierPhone:"";
    $("ipse-groupname").value=plan?plan.groupName:"";
    $("ipse-groupnum").value=plan?plan.groupNum:"";
    $("ipse-plantype").value=plan?plan.planType:"";
    $("ipse-feesched").value=plan?String(plan.feeSched):"0";
    $("ipse-employer").value=plan?plan.employerName:"";
    $("ipse-monthrenew").value=plan?String(plan.monthRenew):"0";
    $("ipse-plannote").value=plan?plan.planNote:"";
    $("ipse-hidden").checked=!!(plan&&plan.isHidden);
    $("ipse-planmsg").textContent=plan?"Editing existing plan.":"Blank plan — fill in and create.";
    loadBens();
    renderSubSelection();
  }

  async function savePlan(){
    const body={
      carrierName:$("ipse-carrier").value.trim(),
      carrierPhone:$("ipse-carrierphone").value.trim(),
      groupName:$("ipse-groupname").value,
      groupNum:$("ipse-groupnum").value,
      planType:$("ipse-plantype").value,
      employerName:$("ipse-employer").value,
      feeSched:+$("ipse-feesched").value,
      monthRenew:+$("ipse-monthrenew").value,
      planNote:$("ipse-plannote").value,
      isHidden:$("ipse-hidden").checked,
    };
    $("ipse-planmsg").textContent="Saving…";
    try{
      const plan=curPlan
        ?await api(`/api/insplans/${curPlan.planNum}`,{method:"PUT",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(body)})
        :await api("/api/insplans",{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
      const created=!curPlan;
      selectPlan(plan);
      $("ipse-planmsg").textContent=(created?"Plan created":"Plan saved")+" (InsEditLogs audited).";
    }catch(e){$("ipse-planmsg").textContent="Save failed: "+e.message.slice(0,80)}
  }

  async function loadSubs(){
    const pat=getActivePatient();
    const tb=$("ipse-subs").querySelector("tbody");
    if(!pat){tb.innerHTML="";$("ipse-submsg").textContent="No active patient.";return}
    try{
      const subs=await api(`/api/patients/${pat.patNum}/inssubs`);
      tb.innerHTML=subs.map(s=>`
        <tr class="rowsel" data-sub='${esc(JSON.stringify(s))}'>
          <td>${esc(s.carrier)}</td><td>${esc(s.groupName)}${s.groupNum?" · "+esc(s.groupNum):""}</td>
          <td class="mono">${esc(s.subscriberId)}</td><td class="mono">${esc(s.dateEffective)}</td>
          <td class="mono">${esc(s.dateTerm)}</td><td>${s.releaseInfo?"Yes":"No"}</td><td>${s.assignBen?"Yes":"No"}</td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No subscriptions</b>
            The active patient is not a subscriber on any plan yet.</div></td></tr>`;
      tb.querySelectorAll("[data-sub]").forEach(tr=>tr.addEventListener("click",()=>{
        selectSub(JSON.parse(tr.dataset.sub));
      }));
      $("ipse-submsg").textContent=`${subs.length} subscription(s) for ${pat.name} — click one to edit/reuse`;
    }catch(e){$("ipse-submsg").textContent="inssubs endpoint unavailable — "+e.message.slice(0,60)}
  }

  function selectSub(sub){
    curSub=sub;
    $("ipse-subid").value=sub.subscriberId;
    $("ipse-dateeff").value=sub.dateEffective;
    $("ipse-dateterm").value=sub.dateTerm;
    $("ipse-release").checked=sub.releaseInfo;
    $("ipse-assign").checked=sub.assignBen;
    $("ipse-subnote").value=sub.subscNote;
    $("ipse-subsave").textContent="Save subscription changes";
    $("ipse-submsg").textContent=`Editing InsSub #${sub.insSubNum} (${sub.carrier}).`;
    if(!curPlan||curPlan.planNum!==sub.planNum){loadPlan(sub.planNum)}
  }

  function renderSubSelection(){
    curSub=null;
    $("ipse-subsave").textContent="Create subscription";
  }

  async function saveSub(){
    const pat=getActivePatient();
    if(!pat){$("ipse-submsg").textContent="No active patient.";return}
    const body={
      subscriberId:$("ipse-subid").value,
      dateEffective:$("ipse-dateeff").value,
      dateTerm:$("ipse-dateterm").value,
      releaseInfo:$("ipse-release").checked,
      assignBen:$("ipse-assign").checked,
      subscNote:$("ipse-subnote").value,
    };
    $("ipse-submsg").textContent="Saving…";
    try{
      if(curSub){
        curSub=await api(`/api/inssubs/${curSub.insSubNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        $("ipse-submsg").textContent="Subscription saved.";
      }
      else{
        if(!curPlan){$("ipse-submsg").textContent="Select or create a plan first (sections 1–2).";return}
        curSub=await api("/api/inssubs",{method:"POST",headers:{"Content-Type":"application/json"},
          body:JSON.stringify({...body,planNum:curPlan.planNum,subscriberPatNum:pat.patNum})});
        $("ipse-subsave").textContent="Save subscription changes";
        $("ipse-submsg").textContent=`Subscription created (InsSub #${curSub.insSubNum}, InsEditPatLogs audited).`;
      }
      loadSubs();
    }catch(e){$("ipse-submsg").textContent="Save failed: "+e.message.slice(0,80)}
  }

  async function attachPatPlan(){
    const pat=getActivePatient();
    if(!pat){$("ipse-ppmsg").textContent="No active patient.";return}
    if(!curSub){$("ipse-ppmsg").textContent="Create or pick a subscription first (section 3).";return}
    $("ipse-ppmsg").textContent="Attaching…";
    try{
      const d=await api(`/api/patients/${pat.patNum}/patplans`,{method:"POST",
        headers:{"Content-Type":"application/json"},
        body:JSON.stringify({insSubNum:curSub.insSubNum,relationship:$("ipse-relat").value,
          isPending:$("ipse-pending").checked})});
      $("ipse-ppmsg").textContent=`Attached as ${d.relationship}, ordinal ${d.ordinal} `
        +`(${d.ordinal===1?"Primary":d.ordinal===2?"Secondary":"Plan "+d.ordinal}). View it in the Insurance module.`;
    }catch(e){$("ipse-ppmsg").textContent="Attach failed: "+e.message.slice(0,80)}
  }

  async function loadBens(){
    benRows=[];
    if(curPlan){
      try{
        const d=await api(`/api/insplans/${curPlan.planNum}/benefits/edit`);
        benRows=d.benefits;
      }catch(e){$("ipse-benmsg").textContent="Benefits unavailable — "+e.message.slice(0,60)}
    }
    renderBens();
  }

  function addBenRow(){
    if(!curPlan){$("ipse-benmsg").textContent="Select or create a plan first.";return}
    benRows.push({benefitNum:0,benefitType:"CoInsurance",covCatNum:covCats[0]?covCats[0].covCatNum:0,
      percent:-1,monetaryAmt:-1,timePeriod:"None",quantityQualifier:"None",quantity:0,codeNum:0,coverageLevel:"None"});
    renderBens();
  }

  function renderBens(){
    const tb=$("ipse-bens").querySelector("tbody");
    const sel=(opts,cur)=>opts.map(o=>`<option${o===cur?" selected":""}>${o}</option>`).join("");
    tb.innerHTML=benRows.map((b,i)=>`
      <tr>
        <td><select data-i="${i}" data-f="benefitType">${sel(BEN_TYPES,b.benefitType)}</select></td>
        <td><select data-i="${i}" data-f="covCatNum"><option value="0"${b.covCatNum===0?" selected":""}>—</option>
          ${covCats.map(c=>`<option value="${c.covCatNum}"${c.covCatNum===b.covCatNum?" selected":""}>${esc(c.description)}</option>`).join("")}</select></td>
        <td><input data-i="${i}" data-f="percent" type="number" min="-1" max="100" value="${b.percent}" style="width:64px" title="-1 = blank"></td>
        <td><input data-i="${i}" data-f="monetaryAmt" type="number" step="0.01" min="-1" value="${b.monetaryAmt}" style="width:84px" title="-1 = blank"></td>
        <td><select data-i="${i}" data-f="timePeriod">${sel(TIME_PERIODS,b.timePeriod)}</select></td>
        <td><select data-i="${i}" data-f="quantityQualifier">${sel(QTY_QUALS,b.quantityQualifier)}</select></td>
        <td><input data-i="${i}" data-f="quantity" type="number" min="0" max="255" value="${b.quantity}" style="width:56px"></td>
        <td><select data-i="${i}" data-f="coverageLevel">${sel(COV_LEVELS,b.coverageLevel)}</select></td>
        <td><button type="button" class="quiet" data-del="${i}" title="Remove (deleted on save)">✕</button></td>
      </tr>`).join("")
      ||`<tr><td colspan="9"><div class="empty"><b>No plan-level benefits</b>
          ${curPlan?"Add rows and save — Benefits.UpdateList diffs against the current list.":"Select or create a plan first."}</div></td></tr>`;
    tb.querySelectorAll("[data-f]").forEach(el=>el.addEventListener("change",()=>{
      const row=benRows[+el.dataset.i],f=el.dataset.f;
      row[f]=(f==="covCatNum"||f==="percent"||f==="monetaryAmt"||f==="quantity")?+el.value:el.value;
    }));
    tb.querySelectorAll("[data-del]").forEach(btn=>btn.addEventListener("click",()=>{
      benRows.splice(+btn.dataset.del,1);
      renderBens();
    }));
  }

  async function saveBens(){
    if(!curPlan){$("ipse-benmsg").textContent="Select or create a plan first.";return}
    $("ipse-benmsg").textContent="Saving…";
    try{
      const d=await api(`/api/insplans/${curPlan.planNum}/benefits`,{method:"PUT",
        headers:{"Content-Type":"application/json"},body:JSON.stringify({benefits:benRows})});
      benRows=d.benefits;
      renderBens();
      $("ipse-benmsg").textContent=`Saved — ${d.benefits.length} benefit(s) on plan (audit-logged).`;
    }catch(e){$("ipse-benmsg").textContent="Save failed: "+e.message.slice(0,80)}
  }
})();
