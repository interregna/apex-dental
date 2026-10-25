/* Billing — account statements (FormStatementOptions, single-account path).
   Generates a Statement for the active patient's account (assemble + persist via the
   real GenerateStatement/Insert business calls), lists prior statements with balances,
   and views each one as a PDF rendered by the sheet framework — inline, never a new tab. */
registerODModule({
  id:"billing-statements",
  label:"Statements",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Account Statements</h1><span class="sub">generate, list and view account statements</span></div>
  <div class="card" style="margin-bottom:14px">
    <h2>Generate for <span id="st-who">—</span> <span class="msg" id="st-msg"></span></h2>
    <form id="st-form" class="formgrid" autocomplete="off">
      <label>From <input type="date" id="st-from"></label>
      <label>To <input type="date" id="st-to"></label>
      <label class="chk"><input type="checkbox" id="st-single"> Single patient (not whole family)</label>
      <label class="chk"><input type="checkbox" id="st-inter"> Intermingle family members</label>
      <button type="submit">Generate statement</button>
    </form>
    <p class="hint">Leave the dates blank to use the practice's default range.</p>
  </div>
  <div class="card">
    <h2>Prior statements
      <button type="button" class="quiet" id="st-refresh" style="margin-left:8px">Refresh</button></h2>
    <div class="gridwrap scrolly"><table class="grid" id="st-list">
      <thead><tr><th>#</th><th>Date Sent</th><th>Mode</th>
      <th>Balance</th><th>Ins Est</th><th>Sent</th><th>PDF</th></tr></thead><tbody></tbody>
    </table></div>
  </div>`,
  init(){
    const {api,esc,$,getActivePatient}=window.ODM;
    const money=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
    let loadedFor=null;
    function rowsHtml(list){
      return list.map(s=>
        `<tr><td class="num">${s.statementNum}</td><td>${esc(s.dateSent)}</td>`+
        `<td>${esc(s.mode)}${s.isReceipt?" (receipt)":""}${s.isInvoice?" (invoice)":""}</td>`+
        `<td class="num">${money(s.balanceTotal)}</td><td class="num">${money(s.insEst)}</td>`+
        `<td>${s.isSent?"yes":"no"}</td>`+
        `<td><a data-view="Statement ${s.statementNum}" href="/api/statements/${s.statementNum}/pdf">view</a></td></tr>`).join("")
        ||`<tr><td colspan="7"><div class="empty"><b>No statements</b>Generate the first statement for this account above.</div></td></tr>`;
    }
    async function load(){
      const pat=getActivePatient();
      if(!pat){$("st-msg").textContent="Select a patient first.";$("st-who").textContent="—";
        $("st-list").querySelector("tbody").innerHTML="";return}
      loadedFor=pat.patNum;
      $("st-who").textContent=pat.name;
      $("st-msg").textContent="Loading…";
      try{
        const data=await api(`/api/patients/${pat.patNum}/statements`);
        $("st-list").querySelector("tbody").innerHTML=rowsHtml(data.statements);
        $("st-msg").textContent="";
      }catch(err){$("st-msg").textContent="Load failed: "+err.message.slice(0,80)}
    }
    async function generate(e){
      e.preventDefault();
      const pat=getActivePatient();
      if(!pat){$("st-msg").textContent="Select a patient first.";return}
      $("st-msg").textContent="Generating…";
      const body={
        dateFrom:$("st-from").value||null,
        dateTo:$("st-to").value||null,
        singlePatient:$("st-single").checked,
        intermingled:$("st-inter").checked,
      };
      try{
        const res=await api(`/api/patients/${pat.patNum}/statements`,
          {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        $("st-msg").textContent=`Statement ${res.statementNum} created — balance ${money(res.balanceTotal)}.`;
        await load();
      }catch(err){$("st-msg").textContent="Generate failed: "+err.message.slice(0,80)}
    }
    $("st-form").addEventListener("submit",generate);
    $("st-refresh").addEventListener("click",load);
    //Reload when opened for a different patient than last time.
    const railBtn=document.querySelector('nav.rail button[data-mod="billing-statements"]');
    if(railBtn){railBtn.addEventListener("click",()=>{const p=getActivePatient();if(!p||p.patNum!==loadedFor)load()})}
    load();
  }
});
