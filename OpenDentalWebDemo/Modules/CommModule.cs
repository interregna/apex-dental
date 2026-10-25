/*
 * Communications module: per-patient commlog (FormCommItem / comm grid equivalent).
 * List via Commlogs.Refresh, add notes via Commlogs.Insert; types come from
 * the practice's DefCat.CommLogTypes definitions.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class CommModule {
	record NewCommlogNote(string Note,string Mode);

	public static void Map(WebApplication app) {
		app.MapGet("/api/patients/{patNum:long}/commlog",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.CommLogTypes,true);
			List<Commlog> listCommlogs=Commlogs.Refresh(patNum);
			return Results.Json(listCommlogs.OrderByDescending(x => x.CommDateTime).Select(x => new {
				commlogNum=x.CommlogNum,
				dateTime=x.CommDateTime.ToString("yyyy-MM-dd HH:mm"),
				commType=Defs.GetName(DefCat.CommLogTypes,x.CommType,listDefs),
				mode=x.Mode_.ToString(),
				sentOrReceived=x.SentOrReceived.ToString(),
				note=x.Note,
			}));
		});

		app.MapPost("/api/patients/{patNum:long}/commlog",(long patNum,NewCommlogNote dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.Note)) {
				return Results.Text("Note is required",statusCode:400);
			}
			Commlog commlog=new Commlog();
			commlog.PatNum=patNum;
			commlog.CommDateTime=DateTime.Now;
			commlog.DateTimeEnd=DateTime.Now;
			//Default type: first (short list=non-hidden) CommLogTypes definition, same as the desktop dialog default.
			commlog.CommType=Defs.GetDefsForCategory(DefCat.CommLogTypes,true).FirstOrDefault()?.DefNum??0;
			commlog.Note=dto.Note;
			commlog.Mode_=Enum.TryParse(dto.Mode,out CommItemMode mode)?mode:CommItemMode.None;
			commlog.SentOrReceived=CommSentOrReceived.Neither;
			commlog.UserNum=Security.CurUser.UserNum;
			commlog.CommSource=CommItemSource.User;
			long commlogNum=Commlogs.Insert(commlog);
			return Results.Json(new { commlogNum });
		});

		app.MapGet("/module/comm",() => Results.Content(PAGE,"text/html"));
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Communications</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr;gap:22px;padding:22px;max-width:900px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent);vertical-align:top}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,select,textarea,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
textarea{flex:1 1 100%;min-height:60px;font-family:inherit}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.note{white-space:pre-wrap;max-width:480px}
</style></head><body>
<header><h1>Communications — Commlog</h1>
<p>Per-patient communication log &middot; real Commlogs business layer &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form id=pickform style="margin-top:0"><select id=commpat required></select></form></section>
<section><h2>Communication log</h2>
<table id=comms><thead><tr><th>When</th><th>Type</th><th>Mode</th><th>Sent/Rcvd</th><th>Note</th></tr></thead><tbody></tbody></table>
<form id=addcomm><select id=commmode>
<option value=None>None</option><option value=Phone>Phone</option><option value=Email>Email</option>
<option value=Mail>Mail</option><option value=InPerson>In Person</option><option value=Text>Text</option><option value=Fax>Fax</option>
</select>
<textarea id=commnote placeholder="Note…" required></textarea>
<button>Add note</button><span id=commmsg style="font-size:12px;opacity:.7"></span></form></section>
</main>
<script>
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('commpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 loadComms();
}
async function loadComms(){
 const patNum=document.getElementById('commpat').value;if(!patNum)return;
 const comms=await (await fetch(`/api/patients/${patNum}/commlog`)).json();
 document.querySelector('#comms tbody').innerHTML=comms.map(c=>
  `<tr><td>${c.dateTime}</td><td>${c.commType||''}</td><td><span class=tag>${c.mode}</span></td><td>${c.sentOrReceived}</td><td class=note>${c.note.replace(/</g,'&lt;')}</td></tr>`).join('')
  ||'<tr><td colspan=5 style="opacity:.6">No communications logged.</td></tr>';
}
document.getElementById('commpat').addEventListener('change',loadComms);
document.getElementById('addcomm').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('commmsg');msg.textContent='Saving…';
 const patNum=document.getElementById('commpat').value;
 const r=await fetch(`/api/patients/${patNum}/commlog`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({note:document.getElementById('commnote').value,mode:document.getElementById('commmode').value})});
 msg.textContent=r.ok?'Saved.':'Failed: '+await r.text();
 if(r.ok)document.getElementById('commnote').value='';
 loadComms();
});
loadPats();
</script></body></html>
""";
}
