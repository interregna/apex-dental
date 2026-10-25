/* Accounts-receivable aging dashboard — read-only web slice of FormRpAging
   ("Aging of A/R"). No patient context: this is a whole-practice financial
   overview. Reads the aggregated stored aging figures via /api/ar/aging-summary
   (patient.BalTotal / Bal_0_30 / Bal_31_60 / Bal_61_90 / BalOver90 / InsEst,
   populated by Ledgers.ComputeAging) and the largest outstanding guarantor
   balances via /api/ar/top-accounts. Stat tiles for the headline figures, a
   CSS-width bar breakdown of the four aging buckets (older = redder), and a
   top-accounts table. If the stored columns were stale the summary endpoint
   recomputes aging and reports it via `recomputed`. */
"use strict";
(function(){
  const {api,esc,$}=window.ODM;
  const money=v=>(v==null)?"—":"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
  //Bucket definitions drive both the breakdown bars and the top-accounts filter.
  //Severity climbs 0..3 so older buckets render redder.
  const BUCKETS=[
    {key:"d0_30",label:"0–30 days",sev:0},
    {key:"d31_60",label:"31–60 days",sev:1},
    {key:"d61_90",label:"61–90 days",sev:2},
    {key:"d90plus",label:"Over 90 days",sev:3},
  ];
  const SEV_COLOR=[
    "color-mix(in srgb, seagreen 70%, CanvasText)",
    "color-mix(in srgb, goldenrod 75%, CanvasText)",
    "color-mix(in srgb, darkorange 80%, CanvasText)",
    "color-mix(in srgb, red 75%, CanvasText)",
  ];
  let curBucket="";//active top-accounts bucket filter ("" = all)

  registerODModule({
    id:"billing-aging-dashboard",
    label:"A/R Aging",
    group:"Billing",
    html:`
      <div class="mod-head"><h1>A/R Aging</h1>
        <span class="sub">accounts-receivable overview · whole practice · FormRpAging figures</span></div>
      <div class="row" style="margin-bottom:12px;align-items:center;gap:10px">
        <button type="button" class="quiet" id="ar-reload">Refresh</button>
        <button type="button" class="quiet" id="ar-recompute" title="Re-run Ledgers.ComputeAging for all families">Recompute aging</button>
        <span class="msg" id="ar-msg"></span>
      </div>
      <div class="stats" id="ar-stats"></div>
      <div class="card" style="margin-bottom:14px"><h2>Aging buckets</h2>
        <div id="ar-buckets"></div>
      </div>
      <div class="card"><h2>Top outstanding accounts</h2>
        <div class="row" style="margin-bottom:10px;gap:6px;flex-wrap:wrap" id="ar-bucket-tabs"></div>
        <div class="gridwrap scrolly"><table class="grid" id="ar-top">
          <thead><tr><th>#</th><th>Guarantor</th><th style="text-align:right">Balance</th>
            <th style="text-align:right">Over 90</th><th style="text-align:right">Ins est</th></tr></thead>
          <tbody></tbody>
        </table></div>
      </div>`,
    init(){
      $("ar-reload").addEventListener("click",()=>load(false));
      $("ar-recompute").addEventListener("click",()=>load(true));
      renderBucketTabs();
      load(false);
    }
  });

  async function load(recompute){
    $("ar-msg").textContent=recompute?"Recomputing aging…":"Loading…";
    try{
      const d=await api("/api/ar/aging-summary"+(recompute?"?recompute=true":""));
      renderStats(d);
      renderBuckets(d);
      $("ar-msg").textContent=(d.recomputed?"Aging recomputed. ":"")+"As of "+esc(d.asOf);
    }catch(e){
      $("ar-msg").textContent="aging summary unavailable — "+e.message.slice(0,60);
      $("ar-stats").innerHTML="";$("ar-buckets").innerHTML="";
    }
    loadTop();
  }

  function renderStats(d){
    $("ar-stats").innerHTML=`
      <div class="stat"><div class="k">Total A/R</div><div class="v">${money(d.totalAR)}</div></div>
      <div class="stat"><div class="k">Insurance est</div><div class="v">${money(d.insEst)}</div></div>
      <div class="stat"><div class="k">Patient portion</div><div class="v">${money(d.patPortion)}</div></div>
      <div class="stat"><div class="k"># Accounts</div><div class="v">${esc(d.accountCount)}</div></div>`;
  }

  function renderBuckets(d){
    const vals=BUCKETS.map(b=>Math.max(0,Number(d.buckets[b.key])||0));
    const max=Math.max(...vals,0.01);//avoid divide-by-zero on an empty practice
    $("ar-buckets").innerHTML=BUCKETS.map((b,i)=>{
      const v=vals[i];
      const pct=Math.round(v/max*100);
      return `<div style="display:flex;align-items:center;gap:10px;margin:6px 0">
        <div style="flex:0 0 96px;font:600 11px var(--font-cond);letter-spacing:.04em;color:var(--ink-2)">${esc(b.label)}</div>
        <div style="flex:1;background:color-mix(in srgb,CanvasText 8%,transparent);border-radius:6px;overflow:hidden;height:20px">
          <div style="height:100%;width:${pct}%;min-width:${v>0.005?2:0}px;background:${SEV_COLOR[b.sev]}"></div>
        </div>
        <div style="flex:0 0 120px;text-align:right;font:500 13px var(--font-mono)">${money(v)}</div>
      </div>`;
    }).join("");
  }

  function renderBucketTabs(){
    const tabs=[{key:"",label:"All"}].concat(BUCKETS.map(b=>({key:b.key,label:b.label})));
    $("ar-bucket-tabs").innerHTML=tabs.map(t=>
      `<button type="button" class="quiet" data-bucket="${esc(t.key)}"
        style="${t.key===curBucket?'font-weight:600;text-decoration:underline':''}">${esc(t.label)}</button>`).join("");
    $("ar-bucket-tabs").querySelectorAll("[data-bucket]").forEach(btn=>btn.addEventListener("click",()=>{
      curBucket=btn.dataset.bucket;
      renderBucketTabs();
      loadTop();
    }));
  }

  async function loadTop(){
    try{
      const rows=await api("/api/ar/top-accounts?limit=25"+(curBucket?"&bucket="+encodeURIComponent(curBucket):""));
      $("ar-top").querySelector("tbody").innerHTML=rows.map((r,i)=>
        `<tr><td class="mono">${i+1}</td><td>${esc(r.name)}</td>`+
        `<td style="text-align:right" class="mono">${money(r.balTotal)}</td>`+
        `<td style="text-align:right" class="mono">${money(r.over90)}</td>`+
        `<td style="text-align:right" class="mono">${money(r.insEst)}</td></tr>`).join("")
        ||'<tr><td colspan="5"><div class="empty"><b>No outstanding accounts</b>No guarantors carry a balance in this bucket.</div></td></tr>';
    }catch(e){
      $("ar-top").querySelector("tbody").innerHTML=
        `<tr><td colspan="5"><div class="empty"><b>Unavailable</b>${esc(e.message.slice(0,80))}</div></td></tr>`;
    }
  }
})();
