/* Provider day sheet — web slice of FormDaily / the Daily (Production & Income) report.
   A date picker (default today) drives GET /api/daysheet, which returns per-provider
   production / adjustments / writeoffs / patient + insurance payments plus a totals row,
   all from the real RpProdInc.GetDailyProdIncDataSet report query. Day-total stat tiles
   (Production, Collections, Adjustments, Net) sit above the per-provider table; clicking a
   provider row loads that provider's line items from /api/daysheet/detail. No patient context. */
"use strict";
(function(){
  const {api,esc,$,provColor}=window.ODM;
  const money=v=>"$"+Number(v||0).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  const today=()=>new Date().toISOString().slice(0,10);

  registerODModule({
    id:"reports-daysheet",
    label:"Day Sheet",
    group:"Practice",
    html:`
      <div class="mod-head"><h1>Day Sheet</h1>
        <span class="sub">daily production &amp; income by provider · RpProdInc Daily report</span></div>
      <div class="row" style="margin-bottom:12px;align-items:flex-end;gap:10px;flex-wrap:wrap">
        <label style="display:flex;flex-direction:column;gap:2px;font-size:12px;opacity:.75">Date
          <input type="date" id="ds-date"></label>
        <button type="button" id="ds-reload">Refresh</button>
        <span class="msg" id="ds-msg"></span>
      </div>
      <div class="stats" id="ds-tiles" style="margin-bottom:14px"></div>
      <div class="card" style="margin-bottom:14px"><h2>By provider</h2>
        <div class="gridwrap scrolly"><table class="grid" id="ds-provs">
          <thead><tr><th>Provider</th><th class="num">Production</th><th class="num">Adjustments</th>
            <th class="num">Writeoffs</th><th class="num">Pat Pay</th><th class="num">Ins Pay</th>
            <th class="num">Net Prod</th></tr></thead>
          <tbody></tbody></table></div>
      </div>
      <div class="card" id="ds-detail-card" hidden><h2 id="ds-detail-title">Detail</h2>
        <div class="gridwrap scrolly"><table class="grid" id="ds-detail">
          <thead><tr><th>Date</th><th>Patient</th><th>Description</th><th>Type</th><th class="num">Amount</th></tr></thead>
          <tbody></tbody></table></div>
      </div>`,
    init(){
      $("ds-date").value=today();
      $("ds-reload").addEventListener("click",load);
      $("ds-date").addEventListener("change",load);
      load();
    }
  });

  async function load(){
    const date=$("ds-date").value||today();
    $("ds-msg").textContent="Loading…";
    $("ds-detail-card").hidden=true;
    try{
      const d=await api(`/api/daysheet?date=${encodeURIComponent(date)}`);
      renderTiles(d.totals);
      renderProvs(d.rows,date);
      $("ds-msg").textContent=d.rows.length?`${d.rows.length} provider(s) with activity`:"";
    }catch(e){$("ds-msg").textContent="day sheet unavailable — "+e.message.slice(0,80);}
  }

  function renderTiles(t){
    t=t||{};
    $("ds-tiles").innerHTML=[
      ["Production",t.production],
      ["Collections",t.collections],
      ["Adjustments",t.adjustments],
      ["Net Prod",t.net],
    ].map(([k,v])=>`<div class="stat"><div class="k">${esc(k)}</div><div class="v">${money(v)}</div></div>`).join("");
  }

  function renderProvs(rows,date){
    const body=$("ds-provs").querySelector("tbody");
    body.innerHTML=(rows||[]).map(r=>
      `<tr class="sel" data-prov="${r.provNum}" data-abbr="${esc(r.provAbbr)}" style="cursor:pointer">
        <td><span style="display:inline-block;width:8px;height:8px;border-radius:50%;margin-right:6px;background:${esc(provColor(r.provAbbr))}"></span>${esc(r.provAbbr)}</td>
        <td class="num">${money(r.production)}</td><td class="num">${money(r.adjustments)}</td>
        <td class="num">${money(r.writeoffs)}</td><td class="num">${money(r.patientPayments)}</td>
        <td class="num">${money(r.insPayments)}</td><td class="num"><b>${money(r.net)}</b></td></tr>`).join("")
      ||'<tr><td colspan="7"><div class="empty"><b>No activity</b>No completed production, payments, or adjustments on this date.</div></td></tr>';
    body.querySelectorAll("tr.sel").forEach(tr=>tr.addEventListener("click",()=>
      loadDetail(date,tr.dataset.prov,tr.dataset.abbr)));
  }

  async function loadDetail(date,provNum,abbr){
    $("ds-detail-card").hidden=false;
    $("ds-detail-title").textContent=`Detail — ${abbr} · ${date}`;
    const body=$("ds-detail").querySelector("tbody");
    body.innerHTML='<tr><td colspan="5" style="opacity:.6">Loading…</td></tr>';
    try{
      const d=await api(`/api/daysheet/detail?date=${encodeURIComponent(date)}&provNum=${encodeURIComponent(provNum)}`);
      body.innerHTML=(d.items||[]).map(it=>
        `<tr><td>${esc(it.date)}</td><td>${esc(it.patient)}</td><td>${esc(it.description)}</td>
          <td>${esc(it.category)}</td><td class="num">${money(it.amount)}</td></tr>`).join("")
        ||'<tr><td colspan="5"><div class="empty"><b>No line items</b>Nothing recorded for this provider on this date.</div></td></tr>';
    }catch(e){body.innerHTML=`<tr><td colspan="5" style="opacity:.6">detail unavailable — ${esc(e.message.slice(0,80))}</td></tr>`;}
  }
})();
