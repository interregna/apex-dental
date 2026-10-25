/*
 * Setup module: providers list + add-provider (Providers.Insert), operatories list,
 * and a definitions browser over the DefCat enum -> Defs.GetDefsForCategory.
 * Mirrors FormProviderSetup / FormProvEdit (add path) / FormOperatories / FormDefinitions.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupModule {
	public static void Map(WebApplication app) {
		string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";

		app.MapGet("/api/setup/providers",() => {
			List<Provider> listProvs=Providers.GetDeepCopy(false);
			return Results.Json(listProvs.OrderBy(x => x.ItemOrder).Select(x => new {
				provNum=x.ProvNum,
				abbr=x.Abbr,
				lName=x.LName,
				fName=x.FName,
				isHidden=x.IsHidden,
				itemOrder=x.ItemOrder,
				color=Hex(x.ProvColor),
			}));
		});

		app.MapPost("/api/setup/providers",(NewProvider dto) => {
			if(string.IsNullOrWhiteSpace(dto.Abbr) || string.IsNullOrWhiteSpace(dto.LName)) {
				return Results.Text("Abbr and LName are required",statusCode:400);
			}
			List<Provider> listProvs=Providers.GetDeepCopy(false);
			Provider prov=new Provider();
			prov.Abbr=dto.Abbr;
			prov.LName=dto.LName;
			prov.FName=dto.FName??"";
			prov.ItemOrder=listProvs.Count==0?0:listProvs.Max(x => x.ItemOrder)+1;
			//Same default the desktop uses for a brand new provider: inherit the first provider's fee schedule.
			prov.FeeSched=listProvs.FirstOrDefault()?.FeeSched??0;
			Providers.Insert(prov);
			Providers.RefreshCache();
			return Results.Json(new { provNum=prov.ProvNum });
		});

		app.MapGet("/api/setup/operatories",() => {
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			List<Operatory> listOps=Operatories.GetDeepCopy(false);
			return Results.Json(listOps.OrderBy(x => x.ItemOrder).Select(x => new {
				operatoryNum=x.OperatoryNum,
				opName=x.OpName,
				abbrev=x.Abbrev,
				isHidden=x.IsHidden,
				isHygiene=x.IsHygiene,
				provDentist=Providers.GetAbbr(x.ProvDentist),
				provHygienist=Providers.GetAbbr(x.ProvHygienist),
			}));
		});

		app.MapGet("/api/setup/defcats",() => Results.Json(
			Enum.GetValues<DefCat>().Select(x => new { value=(int)x,name=x.ToString() })
		));

		app.MapGet("/api/setup/defs",(string category) => {
			if(!Enum.TryParse(category,out DefCat defCat)) {
				return Results.Text("Unknown DefCat: "+category,statusCode:400);
			}
			List<Def> listDefs=Defs.GetDefsForCategory(defCat,false);
			return Results.Json(listDefs.OrderBy(x => x.ItemOrder).Select(x => new {
				defNum=x.DefNum,
				itemName=x.ItemName,
				itemValue=x.ItemValue,
				color=Hex(x.ItemColor),
				isHidden=x.IsHidden,
			}));
		});

		app.MapGet("/module/setup",() => Results.Content(PAGE,"text/html"));
	}

	record NewProvider(string Abbr,string LName,string FName);

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Setup — Open Dental Web Demo</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr 1fr 1fr;gap:22px;padding:22px;max-width:1300px}
@media(max-width:1000px){main{grid-template-columns:1fr}}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.swatch{display:inline-block;width:14px;height:14px;border-radius:3px;vertical-align:-2px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent)}
</style></head><body>
<header><h1>Setup</h1>
<p>Providers &middot; Operatories &middot; Definitions — real OpenDentBusiness layer</p></header>
<main>
<section><h2>Providers</h2>
<table id=provs><thead><tr><th></th><th>Abbr</th><th>Last</th><th>First</th><th></th></tr></thead><tbody></tbody></table>
<form id=addprov><input name=abbr placeholder="Abbr" required size=6><input name=lname placeholder="Last name" required>
<input name=fname placeholder="First name"><button>Add provider</button>
<span id=provmsg style="font-size:12px;opacity:.7"></span></form></section>
<section><h2>Operatories</h2>
<table id=ops><thead><tr><th>Op</th><th>Abbrev</th><th>Dentist</th><th>Hygienist</th><th></th></tr></thead><tbody></tbody></table></section>
<section><h2>Definitions</h2>
<form style="margin:0 0 10px"><select id=defcat></select></form>
<table id=defs><thead><tr><th>Name</th><th>Value</th><th>Color</th><th></th></tr></thead><tbody></tbody></table></section>
</main>
<script>
const esc=s=>String(s??'').replace(/&/g,'&amp;').replace(/</g,'&lt;');
async function loadProvs(){
 const provs=await (await fetch('/api/setup/providers')).json();
 document.querySelector('#provs tbody').innerHTML=provs.map(p=>
  `<tr><td>${p.color?`<span class=swatch style="background:${p.color}"></span>`:''}</td>
   <td>${esc(p.abbr)}</td><td>${esc(p.lName)}</td><td>${esc(p.fName)}</td>
   <td>${p.isHidden?'<span class=tag>hidden</span>':''}</td></tr>`).join('');
}
document.getElementById('addprov').addEventListener('submit',async e=>{
 e.preventDefault();const f=new FormData(e.target),msg=document.getElementById('provmsg');
 const r=await fetch('/api/setup/providers',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({abbr:f.get('abbr'),lName:f.get('lname'),fName:f.get('fname')})});
 msg.textContent=r.ok?'Added.':'Failed: '+await r.text();
 if(r.ok)e.target.reset();loadProvs();
});
async function loadOps(){
 const ops=await (await fetch('/api/setup/operatories')).json();
 document.querySelector('#ops tbody').innerHTML=ops.length?ops.map(o=>
  `<tr><td>${esc(o.opName)}</td><td>${esc(o.abbrev)}</td><td>${esc(o.provDentist)}</td><td>${esc(o.provHygienist)}</td>
   <td>${o.isHygiene?'<span class=tag>hyg</span>':''}${o.isHidden?' <span class=tag>hidden</span>':''}</td></tr>`).join('')
  :'<tr><td colspan=5 style="opacity:.6">No operatories configured</td></tr>';
}
async function loadDefCats(){
 const cats=await (await fetch('/api/setup/defcats')).json();
 document.getElementById('defcat').innerHTML=cats.map(c=>`<option value="${c.name}">${c.name}</option>`).join('');
 loadDefs();
}
async function loadDefs(){
 const cat=document.getElementById('defcat').value;if(!cat)return;
 const defs=await (await fetch(`/api/setup/defs?category=${encodeURIComponent(cat)}`)).json();
 document.querySelector('#defs tbody').innerHTML=defs.length?defs.map(d=>
  `<tr><td>${esc(d.itemName)}</td><td>${esc(d.itemValue)}</td>
   <td>${d.color?`<span class=swatch style="background:${d.color}"></span> ${d.color}`:''}</td>
   <td>${d.isHidden?'<span class=tag>hidden</span>':''}</td></tr>`).join('')
  :'<tr><td colspan=4 style="opacity:.6">No definitions in this category</td></tr>';
}
document.getElementById('defcat').addEventListener('change',loadDefs);
loadProvs();loadOps();loadDefCats();
</script></body></html>
""";
}
