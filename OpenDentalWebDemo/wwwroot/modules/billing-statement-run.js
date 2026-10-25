/* Billing — batch statement run (FormBilling / FormBillingOptions "Create statements").
   No patient context: pick filters (minimum balance, age of account), preview the guarantor
   accounts a run would pick up (real Patients.GetAgingList behind /api/billing/aging-accounts),
   then generate a Statement for every matched account in one POST. Each created statement gets
   an inline "view PDF" link rendered by the existing sheet PDF path — never a new tab. */
registerODModule({
  id:"billing-statement-run",
  label:"Billing Run",
  group:"Billing",
  html:`
  <div class="mod-head"><h1>Batch Statement Run</h1><span class="sub">generate statements for every account with a balance</span></div>
  <div class="card" style="margin-bottom:14px">
    <h2>Filters <span class="msg" id="br-msg"></span></h2>
    <form id="br-form" class="formgrid" autocomplete="off">
      <label>Minimum balance <input type="number" id="br-minbal" step="0.01" value="0.01" style="width:110px"></label>
      <label>Age of account
        <select id="br-age">
          <option value="0">Any balance</option>
          <option value="30">Over 30 days</option>
          <option value="60">Over 60 days</option>
          <option value="90">Over 90 days</option>
        </select>
      </label>
      <label>Delivery
        <select id="br-mode">
          <option value="inperson">In person</option>
          <option value="mail">Mail</option>
          <option value="email">Email</option>
          <option value="electronic">Electronic</option>
        </select>
      </label>
      <button type="button" id="br-preview">Preview eligible accounts</button>
      <button type="submit" id="br-run">Generate statements</button>
    </form>
    <p class="hint">Preview lists guarantor accounts (whole-family balances) matching the filters. Generating creates and persists one statement per account.</p>
  </div>
  <div class="card" style="margin-bottom:14px">
    <h2>Eligible accounts <span class="sub" id="br-elig-sum"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="br-elig">
      <thead><tr><th>Pat#</th><th>Guarantor</th><th>0-30</th><th>31-60</th><th>61-90</th><th>90+</th>
      <th>Ins Est</th><th>Balance</th><th>Last Stmt</th></tr></thead><tbody></tbody>
    </table></div>
  </div>
  <div class="card">
    <h2>Run result <span class="sub" id="br-run-sum"></span></h2>
    <div class="gridwrap scrolly"><table class="grid" id="br-result">
      <thead><tr><th>Stmt#</th><th>Pat#</th><th>Guarantor</th><th>Ins Est</th><th>Balance</th><th>PDF</th></tr></thead><tbody></tbody>
    </table></div>
  </div>`,
  init(){
    const {api,esc,$}=window.ODM;
    const money=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
    function filters(){
      return {
        minBalance:parseFloat($("br-minbal").value||"0")||0,
        daysOld:parseInt($("br-age").value||"0",10)||0,
        mode:$("br-mode").value,
      };
    }
    function eligRows(list){
      return list.map(a=>{
        const b=a.agingBuckets||{};
        return `<tr><td class="num">${a.patNum}</td><td>${esc(a.guarantor)}</td>`+
          `<td class="num">${money(b.d0_30)}</td><td class="num">${money(b.d31_60)}</td>`+
          `<td class="num">${money(b.d61_90)}</td><td class="num">${money(b.d90plus)}</td>`+
          `<td class="num">${money(a.insEst)}</td><td class="num">${money(a.balTotal)}</td>`+
          `<td>${esc(a.dateLastStatement)}</td></tr>`;
      }).join("")
      ||`<tr><td colspan="9"><div class="empty"><b>No eligible accounts</b>No guarantor accounts match these filters.</div></td></tr>`;
    }
    function resultRows(list){
      return list.map(p=>
        `<tr><td class="num">${p.statementNum}</td><td class="num">${p.patNum}</td>`+
        `<td>${esc(p.guarantor)}</td><td class="num">${money(p.insEst)}</td>`+
        `<td class="num">${money(p.balanceTotal)}</td>`+
        `<td><a data-view="Statement ${p.statementNum}" href="/api/statements/${p.statementNum}/pdf">view</a></td></tr>`).join("")
        ||`<tr><td colspan="6"><div class="empty"><b>No statements generated</b>Nothing matched the filters.</div></td></tr>`;
    }
    async function preview(){
      const f=filters();
      $("br-msg").textContent="Loading eligible accounts…";
      try{
        const data=await api(`/api/billing/aging-accounts?minBalance=${f.minBalance}&daysOld=${f.daysOld}`);
        $("br-elig").querySelector("tbody").innerHTML=eligRows(data.accounts);
        $("br-elig-sum").textContent=`${data.count} account(s)`+(data.capped?` shown of ${data.totalEligible}`:"")+` — total ${money(data.grandTotal)}`;
        $("br-msg").textContent="";
      }catch(err){$("br-msg").textContent="Preview failed: "+err.message.slice(0,80)}
    }
    async function run(e){
      e.preventDefault();
      const f=filters();
      if(!confirm(`Generate statements for every account with a balance ≥ ${money(f.minBalance)}?`)){return}
      $("br-msg").textContent="Generating statements…";
      try{
        const res=await api(`/api/billing/statement-run`,
          {method:"POST",headers:{"Content-Type":"application/json"},
           body:JSON.stringify({minBalance:f.minBalance,daysOld:f.daysOld,mode:f.mode})});
        $("br-result").querySelector("tbody").innerHTML=resultRows(res.patients);
        $("br-run-sum").textContent=`${res.generated} statement(s) generated (${esc(res.mode)})`;
        $("br-msg").textContent=`Run complete — ${res.generated} statement(s).`;
      }catch(err){$("br-msg").textContent="Run failed: "+err.message.slice(0,80)}
    }
    $("br-preview").addEventListener("click",preview);
    $("br-form").addEventListener("submit",run);
    preview();
  }
});
