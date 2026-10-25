/* Billing — payment plans (FormPayPlan / FormPayPlanChargeEdit).
   Lists the active patient's payment plans with the same summary columns as
   ContrAccount's Payment Plans grid, drills in to the charge schedule, and adds
   create / edit / delete for standard patient plans.  The New/Edit form shows a
   live amortization preview computed client-side (mirroring PayPlanEdit.
   CreateScheduleCharges); the server returns the authoritative schedule on save. */
"use strict";
registerODModule({
  id:"billing-payplans",
  label:"Pay Plans",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Payment Plans</h1><span class="sub">plans, schedules and balances</span></div>
  <div class="card" style="margin-bottom:14px">
    <h2>Plans for <span id="pp-who">—</span>
      <button type="button" class="quiet" id="pp-refresh" style="margin-left:8px">Refresh</button>
      <button type="button" class="action" id="pp-new" style="margin-left:4px">New payment plan</button>
      <span class="msg" id="pp-msg"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="pp-plans">
      <thead><tr><th>Date</th><th>Guarantor</th><th>Type</th>
      <th>Total</th><th>Paid</th><th>Due Now</th><th>Balance</th><th></th></tr></thead><tbody></tbody>
    </table></div>
  </div>
  <div class="card" id="pp-editcard" style="margin-bottom:14px;display:none">
    <h2 id="pp-etitle">New payment plan <span class="msg" id="pp-emsg"></span></h2>
    <form class="row" id="pp-form" style="align-items:flex-end">
      <label>Date of agreement<br><input type="date" id="pp-f-date"></label>
      <label>Total amount<br><input type="number" step="0.01" min="0" id="pp-f-total" style="width:110px"></label>
      <label>Down payment<br><input type="number" step="0.01" min="0" id="pp-f-down" style="width:110px" value="0"></label>
      <label>APR %<br><input type="number" step="0.01" min="0" id="pp-f-apr" style="width:80px" value="0"></label>
      <label># Payments (term)<br><input type="number" step="1" min="0" id="pp-f-term" style="width:90px"></label>
      <label>or Payment amt<br><input type="number" step="0.01" min="0" id="pp-f-payamt" style="width:110px"></label>
      <label>Guarantor<br><select id="pp-f-guar" style="min-width:150px"></select></label>
      <label style="flex:1 1 180px">Note<br><input id="pp-f-note" style="width:100%"></label>
      <button type="submit" class="action" id="pp-save">Save plan</button>
      <button type="button" class="quiet" id="pp-cancel">Cancel</button>
    </form>
    <div id="pp-warn" class="msg err" style="margin-top:8px"></div>
    <h2 style="margin-top:14px">Amortization preview <span class="msg" id="pp-psum"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="pp-preview">
      <thead><tr><th>Date</th><th>Type</th><th>Principal</th><th>Interest</th><th>Payment</th></tr></thead><tbody></tbody>
    </table></div>
  </div>
  <div class="card">
    <h2 id="pp-dtitle">Charge schedule</h2>
    <div class="gridwrap scrolly"><table class="grid" id="pp-charges">
      <thead><tr><th>Date</th><th>Prov</th><th>Type</th>
      <th>Principal</th><th>Interest</th><th>Note</th></tr></thead><tbody></tbody>
    </table></div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    const usd=n=>{n=Number(n)||0;return (n<0?"-$":"$")+Math.abs(n).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});};
    const money=v=>(v===""||v==null)?0:Number(v);
    let loadedFor=null;
    let editingNum=null;//null => create mode; otherwise the payPlanNum being edited.

    /* ---------- client-side amortization (mirrors PayPlanEdit.CreateScheduleCharges) ---------- */
    function addMonths(date,n){
      const y=date.getFullYear(),m=date.getMonth()+n,day=date.getDate();
      const last=new Date(y,m+1,0).getDate();//clamp like .NET AddMonths
      return new Date(y,m,Math.min(day,last));
    }
    const fmtD=d=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,"0")}-${String(d.getDate()).padStart(2,"0")}`;
    const round2=x=>Math.round((x+Number.EPSILON)*100)/100;
    function amortize(t){
      const rate=t.apr>0?t.apr/100/12:0;//monthly period rate
      const principal=t.total-t.down;
      let periodPayment;
      if(t.term>0){
        const exact=t.apr>0?principal*rate/(1-Math.pow(1+rate,-t.term)):principal/t.term;
        periodPayment=Math.ceil(exact*100)/100;//ceil to cents, as the form does
      }else{
        periodPayment=t.payAmt;
      }
      const rows=[];
      if(t.down!==0) rows.push({date:fmtD(new Date()),type:"Down payment",principal:t.down,interest:0});
      let remaining=principal,prev=remaining,count=0,valid=true;
      while(remaining>0.00001 && count<2000){
        if(remaining>prev+1e-9){valid=false;break;}//principal not decreasing => never pays off
        prev=remaining;
        const chargeDate=addMonths(t.dateFirst,count);
        const interest=t.apr>0?round2(remaining*rate):0;
        let princ=periodPayment-interest;
        if((t.term>0 && count===t.term-1) || (remaining+interest<=periodPayment)) princ=remaining;
        remaining=round2(remaining-princ);
        rows.push({date:fmtD(chargeDate),type:"Charge",principal:princ,interest});
        count++;
      }
      return {rows:valid?rows:rows.filter(r=>r.type==="Down payment"),periodPayment,valid};
    }

    function readForm(){
      const dateStr=$("pp-f-date").value||fmtD(new Date());
      const parts=dateStr.split("-");
      return {
        planDate:dateStr,
        dateFirst:new Date(+parts[0],(+parts[1]||1)-1,+parts[2]||1),
        total:money($("pp-f-total").value),
        down:money($("pp-f-down").value),
        apr:money($("pp-f-apr").value),
        term:parseInt($("pp-f-term").value||"0",10),
        payAmt:money($("pp-f-payamt").value),
        note:$("pp-f-note").value,
        guarantor:parseInt($("pp-f-guar").value||"0",10),
      };
    }

    function renderPreview(){
      const t=readForm();
      const warn=$("pp-warn");
      if(t.total<=0 || (t.term<=0 && t.payAmt<=0)){
        $("pp-preview").querySelector("tbody").innerHTML=
          `<tr><td colspan="5"><div class="empty">Enter a total and a term or payment amount to preview the schedule.</div></td></tr>`;
        $("pp-psum").textContent="";warn.textContent="";return;
      }
      const res=amortize(t);
      if(!res.valid){
        warn.textContent="This payment plan will never be paid off — the interest per payment exceeds the payment amount. "+
          "Lower the APR or raise the payment amount.";
      }else{warn.textContent="";}
      let totPrinc=0,totInt=0;
      $("pp-preview").querySelector("tbody").innerHTML=res.rows.map(r=>{
        totPrinc+=r.principal;totInt+=r.interest;
        return `<tr><td>${esc(r.date)}</td><td>${esc(r.type)}</td>`+
          `<td class="num">${usd(r.principal)}</td><td class="num">${usd(r.interest)}</td>`+
          `<td class="num">${usd(r.principal+r.interest)}</td></tr>`;
      }).join("")||`<tr><td colspan="5"><div class="empty">No charges.</div></td></tr>`;
      const payCount=res.rows.filter(r=>r.type==="Charge").length;
      $("pp-psum").textContent=res.valid
        ? `${payCount} payment(s) of ~${usd(res.periodPayment)} · principal ${usd(totPrinc)} · interest ${usd(totInt)} · total cost ${usd(totPrinc+totInt)}`
        : "";
    }

    /* ---------- edit-form open/close ---------- */
    function openForm(plan){
      editingNum=plan?plan.payPlanNum:null;
      $("pp-etitle").firstChild.nodeValue=plan?`Edit payment plan ${plan.date} `:"New payment plan ";
      $("pp-emsg").textContent="";$("pp-warn").textContent="";
      $("pp-save").textContent=plan?"Save changes":"Save plan";
      const pat=getActivePatient();
      $("pp-f-date").value=plan?plan.date:fmtD(new Date());
      $("pp-f-total").value=plan?(plan.principal??plan.total):"";
      $("pp-f-down").value=plan?(plan.downPayment??0):0;
      $("pp-f-apr").value=plan?(plan.apr??0):0;
      $("pp-f-term").value=plan?(plan.numberOfPayments||""):"";
      $("pp-f-payamt").value=plan?(plan.numberOfPayments?"":plan.payAmt||""):"";
      $("pp-f-note").value=plan?(plan.note||""):"";
      $("pp-f-guar").value=String(plan?plan.guarantorNum:(pat?pat.patNum:0));
      $("pp-editcard").style.display="";
      renderPreview();
    }
    function closeForm(){editingNum=null;$("pp-editcard").style.display="none";}

    async function loadGuarantors(){
      try{
        const pats=await api("/api/patients");
        $("pp-f-guar").innerHTML=pats.map(p=>`<option value="${p.patNum}">${esc(p.name)}</option>`).join("");
      }catch(err){/* leave empty; guarantor defaults to patient server-side */}
    }

    /* ---------- plans grid ---------- */
    async function loadPlans(){
      const pat=getActivePatient();
      if(!pat){$("pp-msg").textContent="Select a patient first.";return}
      loadedFor=pat.patNum;
      $("pp-who").textContent=pat.name;
      $("pp-msg").textContent="Loading…";
      $("pp-dtitle").textContent="Charge schedule";
      $("pp-charges").querySelector("tbody").innerHTML="";
      try{
        const plans=await api(`/api/patients/${pat.patNum}/payplans`);
        $("pp-plans").querySelector("tbody").innerHTML=plans.map(p=>{
          const editable=!p.isInsurance && !p.isDynamic && !p.isClosed;
          return `<tr data-num="${p.payPlanNum}"><td class="rowsel ppdrill">${esc(p.date)}</td><td>${esc(p.guarantor)}</td>`+
          `<td>${p.isInsurance?"Insurance":p.isDynamic?"Dynamic":"Patient"}${p.isClosed?" — closed":""}</td>`+
          `<td class="num">${usd(p.total)}</td><td class="num">${usd(p.amtPaid)}</td>`+
          `<td class="num">${usd(p.dueNow)}</td><td class="num">${usd(p.balance)}</td>`+
          `<td>${editable?`<button type="button" class="quiet ppedit">Edit</button> `+
            `<button type="button" class="quiet ppdel" style="border-color:var(--alert);color:var(--alert)">Delete</button>`:""}</td></tr>`;
        }).join("")
          ||`<tr><td colspan="8"><div class="empty"><b>No payment plans</b>This patient has no plans as patient or guarantor.</div></td></tr>`;
        $("pp-plans").querySelectorAll("td.ppdrill").forEach(td=>
          td.addEventListener("click",()=>loadCharges(td.parentElement.dataset.num)));
        $("pp-plans").querySelectorAll("button.ppedit").forEach(b=>
          b.addEventListener("click",()=>editPlan(b.closest("tr").dataset.num)));
        $("pp-plans").querySelectorAll("button.ppdel").forEach(b=>
          b.addEventListener("click",()=>delPlan(b.closest("tr").dataset.num)));
        $("pp-msg").textContent="";
      }catch(err){$("pp-msg").textContent="Load failed: "+err.message.slice(0,80)}
    }

    async function loadCharges(payPlanNum){
      try{
        const plan=await api(`/api/payplans/${payPlanNum}`);
        $("pp-dtitle").textContent=
          `Charge schedule — plan ${plan.date} (${esc(plan.guarantor)}) — paid ${usd(plan.amtPaid)}, due now ${usd(plan.dueNow)}, balance ${usd(plan.balance)}`;
        $("pp-charges").querySelector("tbody").innerHTML=plan.charges.map(c=>
          `<tr><td>${esc(c.chargeDate)}</td><td>${esc(c.provider)}</td><td>${esc(c.chargeType)}</td>`+
          `<td class="num">${usd(c.principal)}</td><td class="num">${usd(c.interest)}</td><td>${esc(c.note)}</td></tr>`).join("")
          ||`<tr><td colspan="6"><div class="empty"><b>No charges</b>This plan has no scheduled charges yet.</div></td></tr>`;
      }catch(err){$("pp-msg").textContent="Detail failed: "+err.message.slice(0,80)}
    }

    async function editPlan(payPlanNum){
      try{
        const plan=await api(`/api/payplans/${payPlanNum}`);
        openForm(plan);
      }catch(err){$("pp-msg").textContent="Load failed: "+err.message.slice(0,80)}
    }

    async function delPlan(payPlanNum){
      if(!confirm("Delete this payment plan? All of its debits and credits will also be deleted.")) return;
      try{
        await api(`/api/payplans/${payPlanNum}`,{method:"DELETE"});
        if(editingNum===Number(payPlanNum)) closeForm();
        $("pp-dtitle").textContent="Charge schedule";
        $("pp-charges").querySelector("tbody").innerHTML="";
        await loadPlans();
      }catch(err){$("pp-msg").textContent="Delete failed: "+err.message.slice(0,120)}
    }

    /* ---------- save ---------- */
    async function save(ev){
      ev.preventDefault();
      const pat=getActivePatient();
      if(!pat){$("pp-emsg").textContent="Select a patient first.";return}
      const t=readForm();
      const body={planDate:t.planDate,totalAmount:t.total,downPayment:t.down,apr:t.apr,
        payAmt:t.payAmt,term:t.term,note:t.note,guarantor:t.guarantor};
      $("pp-emsg").textContent="Saving…";
      try{
        let res;
        if(editingNum==null){
          res=await api(`/api/patients/${pat.patNum}/payplans`,{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        }else{
          res=await api(`/api/payplans/${editingNum}`,{method:"PUT",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        }
        $("pp-emsg").textContent=`Saved — ${res.chargeCount} charge(s).`;
        closeForm();
        await loadPlans();
        await loadCharges(res.payPlanNum);//confirm with the server's authoritative schedule
      }catch(err){$("pp-emsg").textContent="Save failed: "+err.message.slice(0,160)}
    }

    /* ---------- wiring ---------- */
    ["pp-f-date","pp-f-total","pp-f-down","pp-f-apr","pp-f-term","pp-f-payamt"].forEach(id=>
      $(id).addEventListener("input",renderPreview));
    $("pp-form").addEventListener("submit",save);
    $("pp-new").addEventListener("click",()=>openForm(null));
    $("pp-cancel").addEventListener("click",closeForm);
    $("pp-refresh").addEventListener("click",loadPlans);
    //Reload when the module is opened for a different patient than last time.
    document.querySelector('nav.rail button[data-mod="billing-payplans"]')
      .addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor){closeForm();loadPlans();}});
    loadGuarantors();
    loadPlans();
  }
});
