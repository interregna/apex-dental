/*
 * Week schedule module: 7-day appointment grid over the real business layer.
 * Appointments.GetForPeriodList + Operatories.GetDeepCopy + Providers.GetDeepCopy (ProvColor).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ScheduleWeekModule {
	public static void Map(WebApplication app) {
		app.MapGet("/api/schedweek",(DateTime? start) => {
			DateTime dateStart=(start??DateTime.Today).Date;
			DateTime dateEnd=dateStart.AddDays(7).AddSeconds(-1);
			List<Appointment> listAppts=Appointments.GetForPeriodList(dateStart,dateEnd);
			Operatories.RefreshCache();//Program.cs startup cache refresh does not include operatories
			List<Operatory> listOps=Operatories.GetDeepCopy(true);//isShort=true excludes hidden
			List<Provider> listProvs=Providers.GetDeepCopy(true);
			string Hex(System.Drawing.Color c) => (c.ToArgb()==0 || (c.R==0 && c.G==0 && c.B==0))?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";
			//Column set: operatory names if any operatories are configured, else provider abbrs.
			List<object> columns;
			if(listOps.Count>0) {
				columns=listOps.Select(x => (object)new { id=x.OperatoryNum,name=string.IsNullOrEmpty(x.Abbrev)?x.OpName:x.Abbrev }).ToList();
			}
			else {
				columns=listProvs.Select(x => (object)new { id=x.ProvNum,name=x.Abbr,color=Hex(x.ProvColor) }).ToList();
			}
			var days=Enumerable.Range(0,7).Select(i => {
				DateTime day=dateStart.AddDays(i);
				var appts=listAppts
					.Where(x => x.AptDateTime.Date==day)
					.OrderBy(x => x.AptDateTime)
					.Select(x => {
						Provider prov=Providers.GetProv(x.ProvNum);
						Operatory op=listOps.FirstOrDefault(y => y.OperatoryNum==x.Op);
						return new {
							aptNum=x.AptNum,
							time=x.AptDateTime.ToString("HH:mm"),
							startMinutes=(int)x.AptDateTime.TimeOfDay.TotalMinutes,
							minutes=Math.Max(5,(x.Pattern??"").Length*5),
							patNum=x.PatNum,
							patient=Patients.GetPat(x.PatNum)?.GetNameLF()??"?",
							provider=Providers.GetAbbr(x.ProvNum),
							provColor=prov==null?"":Hex(prov.ProvColor),
							status=x.AptStatus.ToString(),
							operatory=op==null?"":(string.IsNullOrEmpty(op.Abbrev)?op.OpName:op.Abbrev),
							column=listOps.Count>0?x.Op:x.ProvNum,
						};
					}).ToList();
				return new { date=day.ToString("yyyy-MM-dd"),dayName=day.ToString("ddd"),appts };
			}).ToList();
			return Results.Json(new {
				start=dateStart.ToString("yyyy-MM-dd"),
				end=dateStart.AddDays(6).ToString("yyyy-MM-dd"),
				columnKind=listOps.Count>0?"operatory":"provider",
				columns,
				days,
			});
		});
		app.MapGet("/module/schedweek",() => Results.Content(PAGE,"text/html"));
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Week Schedule</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{padding:22px}
form{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin-bottom:14px}
input,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
#grid{display:grid;grid-template-columns:48px repeat(7,1fr);gap:2px;overflow-x:auto;min-width:760px}
.dayhead{text-align:center;font-size:13px;font-weight:600;padding:4px 0;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
.dayhead small{display:block;font-weight:400;opacity:.6}
.timecol,.daycol{position:relative;height:720px}
.daycol{border:1px solid color-mix(in srgb,CanvasText 12%,transparent);border-radius:6px;background:color-mix(in srgb,CanvasText 3%,transparent)}
.hourline{position:absolute;left:0;right:0;border-top:1px solid color-mix(in srgb,CanvasText 8%,transparent)}
.timelabel{position:absolute;right:6px;font-size:10px;opacity:.55;transform:translateY(-50%)}
.appt{position:absolute;left:3px;right:3px;border-radius:5px;padding:2px 5px;font-size:11px;line-height:1.25;overflow:hidden;
 border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:color-mix(in srgb,Highlight 22%,Canvas);cursor:default}
.appt b{display:block;white-space:nowrap;text-overflow:ellipsis;overflow:hidden}
.appt .meta{opacity:.75;white-space:nowrap;text-overflow:ellipsis;overflow:hidden}
#legend{display:flex;gap:12px;flex-wrap:wrap;font-size:12px;margin-top:10px}
#legend span i{display:inline-block;width:11px;height:11px;border-radius:3px;margin-right:4px;vertical-align:-1px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent)}
</style></head><body>
<header><h1>Week Schedule</h1><p>Appointments.GetForPeriodList &middot; 7-day grid &middot; provider colors from ProvColor</p></header>
<main>
<form id=ctl><button type=button id=prev>&larr;</button><input id=start type=date><button type=button id=next>&rarr;</button>
<button type=button id=today>This week</button><span id=msg style="font-size:12px;opacity:.7"></span></form>
<div id=grid></div>
<div id=legend></div>
<script>
const DAYSTART=7*60,DAYEND=19*60,PXPER=720/(DAYEND-DAYSTART);
function mondayOf(d){const dt=new Date(d+'T00:00:00');const dow=(dt.getDay()+6)%7;dt.setDate(dt.getDate()-dow);return dt.toISOString().slice(0,10)}
function esc(s){return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;')}
async function load(){
 const start=document.getElementById('start').value;
 const msg=document.getElementById('msg');msg.textContent='Loading…';
 const r=await fetch('/api/schedweek?start='+start);
 if(!r.ok){msg.textContent='Failed: '+await r.text();return}
 const d=await r.json();
 let n=0;
 let s='<div></div>'+d.days.map(day=>`<div class=dayhead>${day.dayName}<small>${day.date.slice(5)}</small></div>`).join('');
 s+='<div class=timecol>';
 for(let m=DAYSTART;m<=DAYEND;m+=60)s+=`<span class=timelabel style="top:${(m-DAYSTART)*PXPER}px">${String(m/60).padStart(2,'0')}:00</span>`;
 s+='</div>';
 for(const day of d.days){
  s+='<div class=daycol>';
  for(let m=DAYSTART;m<=DAYEND;m+=60)s+=`<div class=hourline style="top:${(m-DAYSTART)*PXPER}px"></div>`;
  for(const a of day.appts){
   n++;
   const top=Math.max(0,(a.startMinutes-DAYSTART)*PXPER),h=Math.max(16,a.minutes*PXPER);
   const style=`top:${top}px;height:${h}px;`+(a.provColor?`background:color-mix(in srgb,${a.provColor} 35%,Canvas);border-color:${a.provColor};`:'');
   s+=`<div class=appt style="${style}" title="${esc(a.patient)} ${a.time} (${a.minutes}m) ${esc(a.provider)} ${esc(a.status)}${a.operatory?' — '+esc(a.operatory):''}">`+
    `<b>${a.time} ${esc(a.patient)}</b><span class=meta>${esc(a.provider)}${a.operatory?' &middot; '+esc(a.operatory):''} &middot; ${esc(a.status)}</span></div>`;
  }
  s+='</div>';
 }
 document.getElementById('grid').innerHTML=s;
 document.getElementById('legend').innerHTML=(d.columnKind=='provider'?d.columns:[])
  .map(c=>`<span><i style="background:${c.color||'color-mix(in srgb,Highlight 22%,Canvas)'}"></i>${esc(c.name)}</span>`).join('')+
  (d.columnKind=='operatory'?`<span class=tag>Columns: ${d.columns.map(c=>esc(c.name)).join(', ')}</span>`:'');
 msg.textContent=`${n} appointments, week of ${d.start}`;
}
function shift(days){const dt=new Date(document.getElementById('start').value+'T00:00:00');dt.setDate(dt.getDate()+days);
 document.getElementById('start').value=dt.toISOString().slice(0,10);load()}
document.getElementById('prev').addEventListener('click',()=>shift(-7));
document.getElementById('next').addEventListener('click',()=>shift(7));
document.getElementById('today').addEventListener('click',()=>{document.getElementById('start').value=mondayOf(new Date().toISOString().slice(0,10));load()});
document.getElementById('start').addEventListener('change',load);
document.getElementById('start').value=mondayOf(new Date().toISOString().slice(0,10));
load();
</script></body></html>
""";
}
