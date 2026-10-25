/* Deposits — retires FormDeposits / FormDepositEdit. Practice-wide (no patient
   context): a date-range list of deposit slips (GET /api/deposits), a "New deposit"
   flow that picks from the unattached pool of patient payments and insurance checks
   (GET /api/deposits/unattached?date=; each item a checkbox with a running selected
   total) and creates the slip (POST /api/deposits sets DepositNum on the chosen
   items), opening a deposit to see its frozen items (GET /api/deposits/{n}), and
   delete with confirm (DELETE clears DepositNum back to 0 on every item). The slip
   Amount is never typed — it is the sum of the checks, matching the desktop form. */
"use strict";
(function(){
const {api,esc,$}=window.ODM;
const usd=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
const today=()=>new Date().toISOString().slice(0,10);
const monthsAgo=n=>{const d=new Date();d.setMonth(d.getMonth()-n);return d.toISOString().slice(0,10);};

registerODModule({
  id:"billing-deposits",
  label:"Deposits",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Deposits</h1>
    <span class="sub">group not-yet-deposited patient payments &amp; insurance checks into deposit slips · practice-wide</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>Deposit slips <span class="msg" id="dep-list-msg"></span></h2>
      <form class="row" id="dep-range" style="margin-bottom:10px;align-items:center">
        <label>From <input type="date" id="dep-from"></label>
        <label>To <input type="date" id="dep-to"></label>
        <button class="action">Show</button>
      </form>
      <div class="gridwrap scrolly"><table class="grid" id="dep-list">
        <thead><tr><th>Date</th><th>Bank acct info</th><th class="num">Items</th>
          <th class="num">Amount</th><th></th><th></th></tr></thead>
        <tbody></tbody></table></div>
    </div>
    <div class="card">
      <h2 id="dep-detail-title">Deposit</h2>
      <div id="dep-detail" class="sub">Select a deposit from the list, or build a new one below.</div>
    </div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2>New deposit <span class="msg" id="dep-new-msg"></span></h2>
    <form class="row" id="dep-pool-form" style="align-items:center;margin-bottom:8px">
      <label>Include undeposited money on/after <input type="date" id="dep-pool-date"></label>
      <button class="action">Load pool</button>
      <span class="sub" id="dep-pool-count" style="margin-left:auto"></span>
    </form>
    <div class="gridwrap scrolly"><table class="grid" id="dep-pool">
      <thead><tr><th></th><th>Kind</th><th>Date</th><th>Patient / Carrier</th>
        <th>Type</th><th>Check #</th><th class="num">Amount</th></tr></thead>
      <tbody><tr><td colspan="7" class="sub">Load the pool to pick payments.</td></tr></tbody></table></div>
    <form id="dep-create-form">
      <div class="row" style="margin-top:10px;align-items:center">
        <label>Deposit date <input type="date" id="dep-date" required></label>
        <input id="dep-bank" placeholder="Bank account info" style="width:200px">
        <input id="dep-note" placeholder="Memo" style="flex:1;min-width:140px">
        <b id="dep-sel-total" style="margin-left:auto">Selected: ${usd(0)}</b>
      </div>
      <div class="row" style="margin-top:10px">
        <button class="action" id="dep-create-btn">Create deposit</button>
        <span class="sub" id="dep-sel-count"></span>
      </div>
    </form>
  </div>`,
  init(){
    const listMsg=t=>{$("dep-list-msg").textContent=t;};
    const newMsg=t=>{$("dep-new-msg").textContent=t;};
    let pool=[],selected=new Set();

    $("dep-from").value=monthsAgo(3);
    $("dep-to").value=today();
    $("dep-pool-date").value=monthsAgo(3);
    $("dep-date").value=today();

    function selKey(it){return it.kind+":"+it.num;}

    async function loadList(){
      listMsg("Loading…");
      try{
        const rows=await api(`/api/deposits?from=${encodeURIComponent($("dep-from").value)}&to=${encodeURIComponent($("dep-to").value)}`);
        const tbody=document.querySelector("#dep-list tbody");
        tbody.innerHTML=rows.map(d=>
          `<tr><td class="mono">${esc(d.date)}</td>`+
          `<td>${esc(d.bankAcctInfo||"")}</td>`+
          `<td class="num">${d.itemCount}</td>`+
          `<td class="num mono">${usd(d.amount)}</td>`+
          `<td><button type="button" class="quiet" data-open="${d.depositNum}">Open</button></td>`+
          `<td><button type="button" class="quiet" data-del="${d.depositNum}">Delete</button></td></tr>`).join("")
          ||`<tr><td colspan="6"><div class="empty"><b>No deposits in range</b>Build one from the pool below.</div></td></tr>`;
        tbody.querySelectorAll("button[data-open]").forEach(b=>b.addEventListener("click",()=>openDeposit(Number(b.dataset.open))));
        tbody.querySelectorAll("button[data-del]").forEach(b=>b.addEventListener("click",()=>delDeposit(Number(b.dataset.del))));
        listMsg(rows.length+" deposit"+(rows.length===1?"":"s")+".");
      }catch(e){listMsg("Unavailable: "+e.message);}
    }

    async function openDeposit(depositNum){
      $("dep-detail-title").textContent="Deposit "+depositNum;
      $("dep-detail").innerHTML="Loading…";
      try{
        const d=await api(`/api/deposits/${depositNum}`);
        const items=d.items.map(it=>
          `<tr><td>${it.kind==="ins"?"Insurance":"Patient"}</td>`+
          `<td class="mono">${esc(it.date)}</td>`+
          `<td>${esc(it.patientOrCarrier)}</td>`+
          `<td>${esc(it.payType||"")}</td>`+
          `<td class="mono">${esc(it.checkNum||"")}</td>`+
          `<td class="num mono">${usd(it.amount)}</td></tr>`).join("")
          ||`<tr><td colspan="6" class="sub">No items attached.</td></tr>`;
        $("dep-detail-title").textContent=`Deposit ${esc(d.date)} · ${usd(d.amount)}`;
        $("dep-detail").innerHTML=
          `<div class="row" style="gap:16px;margin-bottom:8px">`+
          `<span><b>Date:</b> ${esc(d.date)}</span>`+
          `<span><b>Bank acct:</b> ${esc(d.bankAcctInfo||"—")}</span>`+
          `<span><b>Memo:</b> ${esc(d.note||"—")}</span></div>`+
          `<div class="gridwrap"><table class="grid"><thead><tr><th>Kind</th><th>Date</th>`+
          `<th>Patient / Carrier</th><th>Type</th><th>Check #</th><th class="num">Amount</th></tr></thead>`+
          `<tbody>${items}</tbody></table></div>`;
      }catch(e){$("dep-detail").innerHTML="Failed: "+esc(e.message);}
    }

    async function delDeposit(depositNum){
      if(!confirm("Delete this deposit? Its payments and insurance checks will be released back to the undeposited pool."))return;
      try{
        await api(`/api/deposits/${depositNum}`,{method:"DELETE"});
        listMsg("Deposit deleted (security-logged).");
        $("dep-detail-title").textContent="Deposit";
        $("dep-detail").innerHTML="Deposit deleted; items returned to the pool.";
        loadList();
      }catch(e){listMsg("Delete blocked: "+e.message);}
    }

    function selTotal(){
      let sum=0,n=0;
      pool.forEach(it=>{if(selected.has(selKey(it))){sum+=Number(it.amount)||0;n++;}});
      $("dep-sel-total").textContent="Selected: "+usd(sum);
      $("dep-sel-count").textContent=n+" item"+(n===1?"":"s")+" selected";
    }

    function renderPool(){
      const tbody=document.querySelector("#dep-pool tbody");
      tbody.innerHTML=pool.map((it,i)=>
        `<tr><td><input type="checkbox" data-i="${i}"${selected.has(selKey(it))?" checked":""}></td>`+
        `<td>${it.kind==="ins"?"Insurance":"Patient"}</td>`+
        `<td class="mono">${esc(it.date)}</td>`+
        `<td>${esc(it.patientOrCarrier)}</td>`+
        `<td>${esc(it.payType||"")}</td>`+
        `<td class="mono">${esc(it.checkNum||"")}</td>`+
        `<td class="num mono">${usd(it.amount)}</td></tr>`).join("")
        ||`<tr><td colspan="7" class="sub">No undeposited payments on/after that date.</td></tr>`;
      tbody.querySelectorAll("input[data-i]").forEach(cb=>cb.addEventListener("change",()=>{
        const it=pool[Number(cb.dataset.i)];
        if(cb.checked)selected.add(selKey(it));else selected.delete(selKey(it));
        selTotal();
      }));
      selTotal();
    }

    async function loadPool(){
      newMsg("Loading pool…");
      try{
        pool=await api(`/api/deposits/unattached?date=${encodeURIComponent($("dep-pool-date").value)}`);
        selected=new Set();
        renderPool();
        $("dep-pool-count").textContent=pool.length+" undeposited item"+(pool.length===1?"":"s")+".";
        newMsg("");
      }catch(e){newMsg("Pool unavailable: "+e.message);}
    }

    $("dep-range").addEventListener("submit",e=>{e.preventDefault();loadList();});
    $("dep-pool-form").addEventListener("submit",e=>{e.preventDefault();loadPool();});

    $("dep-create-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const chosen=pool.filter(it=>selected.has(selKey(it)));
      if(chosen.length===0){newMsg("Select at least one payment first.");return;}
      const body=JSON.stringify({
        date:$("dep-date").value,
        bankAcctInfo:$("dep-bank").value,
        note:$("dep-note").value,
        payNums:chosen.filter(it=>it.kind==="pat").map(it=>it.num),
        claimPaymentNums:chosen.filter(it=>it.kind==="ins").map(it=>it.num),
      });
      newMsg("Creating…");
      try{
        const res=await api("/api/deposits",{method:"POST",headers:{"Content-Type":"application/json"},body});
        newMsg(`Deposit ${res.depositNum} created for ${usd(res.amount)} (security-logged).`);
        selected=new Set();
        loadPool();
        loadList();
        openDeposit(res.depositNum);
      }catch(e2){newMsg("Create failed: "+e2.message);}
    });

    loadList();
  },
});
})();
