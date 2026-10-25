/* Week schedule — 7-day appointment grid (rail port of /module/schedweek).
   Same /api/schedweek endpoint: Appointments.GetForPeriodList over the real
   business layer, provider colors from ProvColor. */
"use strict";
(function(){
  const {api,esc,$}=window.ODM;
  const DAYSTART=7*60,DAYEND=19*60,COLH=720,PXPER=COLH/(DAYEND-DAYSTART);
  const line="color-mix(in srgb,CanvasText 12%,transparent)";

  registerODModule({
    id:"schedweek",
    label:"Week",
    group:"Schedule",
    html:`
      <div class="mod-head"><h1>Week schedule</h1>
        <span class="sub">7-day grid · Appointments.GetForPeriodList · provider colors from ProvColor</span></div>
      <form class="row" id="swk-ctl" style="margin-bottom:12px">
        <button type="button" class="quiet" id="swk-prev" aria-label="Previous week">‹</button>
        <input type="date" id="swk-start" aria-label="Week start">
        <button type="button" class="quiet" id="swk-next" aria-label="Next week">›</button>
        <button type="button" class="quiet" id="swk-today">This week</button>
        <span class="msg" id="swk-msg"></span>
      </form>
      <div class="gridwrap" style="overflow-x:auto">
        <div id="swk-grid" style="display:grid;grid-template-columns:48px repeat(7,1fr);gap:2px;min-width:760px"></div>
      </div>
      <div id="swk-legend" style="display:flex;gap:12px;flex-wrap:wrap;font-size:12px;margin-top:10px"></div>`,
    init(){
      $("swk-prev").addEventListener("click",()=>shift(-7));
      $("swk-next").addEventListener("click",()=>shift(7));
      $("swk-today").addEventListener("click",()=>{$("swk-start").value=mondayOf(new Date().toISOString().slice(0,10));load()});
      $("swk-start").addEventListener("change",load);
      $("swk-start").value=mondayOf(new Date().toISOString().slice(0,10));
      load();
    }
  });

  function mondayOf(d){const dt=new Date(d+"T00:00:00");const dow=(dt.getDay()+6)%7;dt.setDate(dt.getDate()-dow);return dt.toISOString().slice(0,10)}
  function shift(days){const dt=new Date($("swk-start").value+"T00:00:00");dt.setDate(dt.getDate()+days);
    $("swk-start").value=dt.toISOString().slice(0,10);load()}

  async function load(){
    const msg=$("swk-msg");msg.textContent="Loading…";
    let d;
    try{d=await api("/api/schedweek?start="+$("swk-start").value)}
    catch(e){msg.textContent="week endpoint unavailable — "+e.message.slice(0,60);return}
    let n=0;
    let s="<div></div>"+d.days.map(day=>
      `<div style="text-align:center;font-size:13px;font-weight:600;padding:4px 0;border-bottom:1px solid ${line}">`+
      `${esc(day.dayName)}<small style="display:block;font-weight:400;opacity:.6">${esc(day.date.slice(5))}</small></div>`).join("");
    s+=`<div style="position:relative;height:${COLH}px">`;
    for(let m=DAYSTART;m<=DAYEND;m+=60)
      s+=`<span style="position:absolute;right:6px;top:${(m-DAYSTART)*PXPER}px;font-size:10px;opacity:.55;transform:translateY(-50%)">${String(m/60).padStart(2,"0")}:00</span>`;
    s+="</div>";
    for(const day of d.days){
      s+=`<div style="position:relative;height:${COLH}px;border:1px solid ${line};border-radius:6px;background:color-mix(in srgb,CanvasText 3%,transparent)">`;
      for(let m=DAYSTART;m<=DAYEND;m+=60)
        s+=`<div style="position:absolute;left:0;right:0;top:${(m-DAYSTART)*PXPER}px;border-top:1px solid color-mix(in srgb,CanvasText 8%,transparent)"></div>`;
      for(const a of day.appts){
        n++;
        const top=Math.max(0,(a.startMinutes-DAYSTART)*PXPER),h=Math.max(16,a.minutes*PXPER);
        const color=a.provColor
          ?`background:color-mix(in srgb,${a.provColor} 35%,Canvas);border-color:${a.provColor};`
          :`background:color-mix(in srgb,Highlight 22%,Canvas);`;
        s+=`<div style="position:absolute;left:3px;right:3px;top:${top}px;height:${h}px;border-radius:5px;padding:2px 5px;`+
          `font-size:11px;line-height:1.25;overflow:hidden;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);${color}" `+
          `title="${esc(a.patient)} ${a.time} (${a.minutes}m) ${esc(a.provider)} ${esc(a.status)}${a.operatory?" — "+esc(a.operatory):""}">`+
          `<b style="display:block;white-space:nowrap;text-overflow:ellipsis;overflow:hidden">${esc(a.time)} ${esc(a.patient)}</b>`+
          `<span style="opacity:.75;white-space:nowrap">${esc(a.provider)}${a.operatory?" · "+esc(a.operatory):""} · ${esc(a.status)}</span></div>`;
      }
      s+="</div>";
    }
    $("swk-grid").innerHTML=s;
    $("swk-legend").innerHTML=(d.columnKind==="provider"?d.columns:[])
      .map(c=>`<span><i style="display:inline-block;width:11px;height:11px;border-radius:3px;margin-right:4px;vertical-align:-1px;`+
        `border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:${c.color||"color-mix(in srgb,Highlight 22%,Canvas)"}"></i>${esc(c.name)}</span>`).join("")+
      (d.columnKind==="operatory"?`<span class="tag">Columns: ${d.columns.map(c=>esc(c.name)).join(", ")}</span>`:"");
    msg.textContent=`${n} appointments, week of ${d.start}`;
  }
})();
