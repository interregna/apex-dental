/* Payments — retires FormPayment for patient (non-insurance) payments. Grid of the
   active patient's payments with a running total (GET /api/patients/{n}/payments =
   Payments.Refresh + their PaySplits), create/edit through POST/PUT, delete with the
   form's deposit guard surfaced as a 409. A payment carries a date, a pay type from
   /api/payment-types (DefCat.PaymentTypes; 0 = income transfer), an amount and a note,
   and is broken into splits: each split allocates part of the money to a procedure
   (from the patient's /api/patients/{n}/procs) and provider (/api/setup/providers).
   The live "unallocated" figure = payAmt − Σ splits; whatever is left unallocated is
   posted by the server as one unallocated split, exactly like the desktop dialog. */
"use strict";
(function(){
const {api,esc,$,getActivePatient}=window.ODM;
const usd=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});

registerODModule({
  id:"billing-payment-entry",
  label:"Payments",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Payments</h1>
    <span class="sub">patient payments split across procedures &amp; providers · types from Definitions (PaymentTypes)</span></div>
  <div class="chart-tools">
    <button type="button" class="quiet" id="pay-reload">Refresh</button>
    <span class="msg" id="pay-msg"></span>
  </div>
  <div class="card">
    <h2 id="pay-grid-title">Payments</h2>
    <div class="gridwrap scrolly"><table class="grid" id="pay-grid">
      <thead><tr><th>Date</th><th>Type</th><th>Note</th><th>Splits</th>
        <th class="num">Amount</th><th class="num">Running</th><th></th><th></th></tr></thead>
      <tbody></tbody></table></div>
  </div>
  <div class="card" style="margin-top:12px">
    <h2 id="pay-form-title">New payment</h2>
    <form id="pay-form">
      <div class="row">
        <input type="date" id="pay-date" required>
        <select id="pay-type" required></select>
        <label>Amount $ <input type="number" id="pay-amt" step="0.01" style="width:110px" required></label>
        <input id="pay-note" placeholder="Note" style="flex:1;min-width:160px">
      </div>
      <div style="margin-top:10px">
        <div class="row" style="align-items:center">
          <b>Splits</b>
          <button type="button" class="quiet" id="pay-addsplit">Add split</button>
          <span class="sub" id="pay-unalloc" style="margin-left:auto"></span>
        </div>
        <div class="gridwrap"><table class="grid" id="pay-splits">
          <thead><tr><th>Procedure</th><th>Provider</th><th class="num">Amount $</th><th></th></tr></thead>
          <tbody></tbody></table></div>
      </div>
      <div class="row" style="margin-top:10px">
        <button class="action" id="pay-save">Create</button>
        <button type="button" class="quiet" id="pay-cancel" hidden>Cancel edit</button>
      </div>
    </form>
    <div class="sub" style="font-size:12px;margin-top:6px">
      Leave the splits empty for a simple payment — the whole amount posts as one
      unallocated split. Any amount you don't itemise (the "unallocated" figure) is
      posted the same way, matching the desktop payment window.</div>
  </div>`,
  init(){
    const msg=t=>{$("pay-msg").textContent=t;};
    let types=[],provs=[],procs=[],data=null,editNum=0,loadedFor=0,splits=[];
    $("pay-date").value=new Date().toISOString().slice(0,10);

    function procLabel(procNum){
      const p=procs.find(x=>x.procNum===procNum);
      return p?esc(p.procCode+(p.toothNum?" #"+p.toothNum:"")+" "+(p.procDate||"")):("#"+procNum);
    }

    function renderGrid(){
      const tbody=document.querySelector("#pay-grid tbody");
      const rows=data?data.payments:[];
      //Running balance oldest-first, displayed against the newest-first grid order.
      const running=new Map();
      let sum=0;
      for(let i=rows.length-1;i>=0;i--){sum+=rows[i].amount;running.set(rows[i].payNum,sum);}
      $("pay-grid-title").textContent=data
        ?`Payments — ${esc(data.patient)} · total ${usd(data.total)}`
        :"Payments";
      tbody.innerHTML=rows.map(p=>{
        const splitTxt=p.splits.map(s=>
          (s.procNum?procLabel(s.procNum):"Unallocated")+" "+esc(s.provAbbr||"")+" "+usd(s.amount)).join("; ");
        return `<tr><td class="mono">${esc(p.payDate)}</td>`+
          `<td>${esc(p.typeName)}</td>`+
          `<td>${esc(p.note||"")}</td>`+
          `<td class="sub">${splitTxt}</td>`+
          `<td class="num mono">${usd(p.amount)}</td>`+
          `<td class="num mono">${usd(running.get(p.payNum))}</td>`+
          `<td><button type="button" class="quiet" data-edit="${p.payNum}">Edit</button></td>`+
          `<td><button type="button" class="quiet" data-del="${p.payNum}">Delete</button></td></tr>`;
      }).join("")
        ||`<tr><td colspan="8"><div class="empty"><b>No payments</b>Add the first one with the form below.</div></td></tr>`;
      tbody.querySelectorAll("button[data-edit]").forEach(btn=>btn.addEventListener("click",()=>startEdit(Number(btn.dataset.edit))));
      tbody.querySelectorAll("button[data-del]").forEach(btn=>btn.addEventListener("click",async()=>{
        if(!confirm("This will delete the entire payment and all splits. Continue?"))return;
        try{
          await api(`/api/payments/${btn.dataset.del}`,{method:"DELETE"});
          msg("Payment deleted (security-logged).");
          if(editNum===Number(btn.dataset.del))resetForm();
          load();
        }catch(e){msg("Delete failed: "+e.message);}
      }));
    }

    function procOptions(sel){
      return `<option value="0"${sel?"":" selected"}>Unallocated</option>`+procs.map(p=>
        `<option value="${p.procNum}"${p.procNum===sel?" selected":""}>${esc(p.procCode)}${p.toothNum?" #"+esc(p.toothNum):""} · ${esc(p.procDate||"TP")} · ${usd(p.fee)} (${esc(p.status)})</option>`).join("");
    }
    function provOptions(sel){
      return provs.filter(p=>!p.isHidden).map(p=>
        `<option value="${p.provNum}"${p.provNum===sel?" selected":""}>${esc(p.abbr)}</option>`).join("")
        ||`<option value="0">(no providers)</option>`;
    }

    function renderSplits(){
      const tbody=document.querySelector("#pay-splits tbody");
      tbody.innerHTML=splits.map((s,i)=>
        `<tr><td><select data-proc="${i}" style="max-width:240px">${procOptions(s.procNum)}</select></td>`+
        `<td><select data-prov="${i}">${provOptions(s.provNum)}</select></td>`+
        `<td class="num"><input type="number" step="0.01" data-amt="${i}" value="${Number(s.splitAmt).toFixed(2)}" style="width:100px"></td>`+
        `<td><button type="button" class="quiet" data-rm="${i}">Remove</button></td></tr>`).join("")
        ||`<tr><td colspan="4" class="sub">No splits — whole amount posts as one unallocated split.</td></tr>`;
      tbody.querySelectorAll("select[data-proc]").forEach(el=>el.addEventListener("change",()=>{splits[Number(el.dataset.proc)].procNum=Number(el.value);}));
      tbody.querySelectorAll("select[data-prov]").forEach(el=>el.addEventListener("change",()=>{splits[Number(el.dataset.prov)].provNum=Number(el.value);}));
      tbody.querySelectorAll("input[data-amt]").forEach(el=>el.addEventListener("input",()=>{splits[Number(el.dataset.amt)].splitAmt=Number(el.value);updateUnalloc();}));
      tbody.querySelectorAll("button[data-rm]").forEach(btn=>btn.addEventListener("click",()=>{splits.splice(Number(btn.dataset.rm),1);renderSplits();updateUnalloc();}));
      updateUnalloc();
    }

    function updateUnalloc(){
      const amt=Number($("pay-amt").value)||0;
      const summed=splits.reduce((a,s)=>a+(Number(s.splitAmt)||0),0);
      const un=Math.round((amt-summed)*100)/100;
      $("pay-unalloc").textContent=`Splits ${usd(summed)} · unallocated ${usd(un)}`;
      $("pay-unalloc").style.color=un<0?"#b00":"";
    }

    function defaultProv(){
      const p=provs.find(x=>!x.isHidden);
      return p?p.provNum:0;
    }

    function startEdit(payNum){
      const p=data&&data.payments.find(x=>x.payNum===payNum);
      if(!p)return;
      editNum=payNum;
      $("pay-form-title").textContent="Edit payment "+esc(p.payDate)+" · "+esc(p.typeName);
      $("pay-save").textContent="Save";
      $("pay-cancel").hidden=false;
      $("pay-date").value=p.payDate;
      $("pay-type").value=String(p.payType);
      $("pay-amt").value=Number(p.amount).toFixed(2);
      $("pay-note").value=p.note||"";
      //Load itemised splits (drop the pure-unallocated one — the server re-derives it).
      splits=p.splits.filter(s=>s.procNum!==0).map(s=>({procNum:s.procNum,provNum:s.provNum,splitAmt:s.amount}));
      renderSplits();
    }

    function resetForm(){
      editNum=0;splits=[];
      $("pay-form-title").textContent="New payment";
      $("pay-save").textContent="Create";
      $("pay-cancel").hidden=true;
      $("pay-amt").value="";
      $("pay-note").value="";
      $("pay-date").value=new Date().toISOString().slice(0,10);
      renderSplits();
    }

    async function load(){
      const pat=getActivePatient();
      if(!pat){
        data=null;procs=[];loadedFor=0;
        document.querySelector("#pay-grid tbody").innerHTML=
          `<tr><td colspan="8"><div class="empty"><b>No patient selected</b>Pick a patient in the band above to work their payments.</div></td></tr>`;
        $("pay-grid-title").textContent="Payments";
        msg("");
        return;
      }
      msg("Loading…");
      try{
        const [pays,provList,typeList,procList]=await Promise.all([
          api(`/api/patients/${pat.patNum}/payments`),
          api("/api/setup/providers"),
          api("/api/payment-types"),
          api(`/api/patients/${pat.patNum}/procs`),
        ]);
        data=pays;provs=provList;types=typeList;procs=procList;loadedFor=pat.patNum;
        $("pay-type").innerHTML=types.map(t=>`<option value="${t.defNum}">${esc(t.name)}</option>`).join("")
          +`<option value="0">Income Transfer</option>`;
        renderGrid();
        renderSplits();
        msg(pays.payments.length+" payment"+(pays.payments.length===1?"":"s")+" · total "+usd(pays.total)+".");
      }catch(e){msg("Payments unavailable: "+e.message);}
    }

    $("pay-addsplit").addEventListener("click",()=>{
      splits.push({procNum:0,provNum:defaultProv(),splitAmt:0});
      renderSplits();
    });
    $("pay-amt").addEventListener("input",updateUnalloc);

    $("pay-form").addEventListener("submit",async e=>{
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){msg("Select a patient first.");return;}
      const body=JSON.stringify({
        date:$("pay-date").value,
        payType:Number($("pay-type").value),
        payAmt:Number($("pay-amt").value),
        note:$("pay-note").value,
        splits:splits.filter(s=>Number(s.splitAmt)!==0).map(s=>({
          procNum:Number(s.procNum),provNum:Number(s.provNum),splitAmt:Number(s.splitAmt),
        })),
      });
      msg("Saving…");
      try{
        if(editNum)await api(`/api/payments/${editNum}`,{method:"PUT",headers:{"Content-Type":"application/json"},body});
        else await api(`/api/patients/${pat.patNum}/payments`,{method:"POST",headers:{"Content-Type":"application/json"},body});
        msg(editNum?"Payment updated (security-logged).":"Payment created (security-logged).");
        resetForm();load();
      }catch(e2){msg("Save failed: "+e2.message);}
    });
    $("pay-cancel").addEventListener("click",resetForm);
    $("pay-reload").addEventListener("click",load);
    //Follow the patient band, and refresh when the rail shows this module for a new patient.
    $("band-patient").addEventListener("change",()=>{resetForm();setTimeout(load,0);});
    const railBtn=document.querySelector('nav.rail button[data-mod="billing-payment-entry"]');
    if(railBtn)railBtn.addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor)load();});
    load();
  },
});
})();
