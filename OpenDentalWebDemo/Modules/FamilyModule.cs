/*
 * Family module: web slice of the desktop Family module (ControlFamily).
 * All data flows through the real business layer:
 * Patients.GetFamily / Patients.Insert / Patients.Update — same calls the
 * WinForms client makes when listing a family, moving a patient to another
 * family, or adding a family member.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class FamilyModule {
	public static void Map(WebApplication app) {
		//Family grid: guarantor + every member with position/age/status, same rows ControlFamily shows.
		app.MapGet("/api/family/{patNum:long}",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Family family=Patients.GetFamily(patNum);//real business call; Age is filled by GetFamilies
			Patient guarantor=family.Guarantor;
			return Results.Json(new {
				patNum,
				guarantor=guarantor==null?null:new { patNum=guarantor.PatNum,name=guarantor.GetNameLF() },
				members=family.ListPats.Select(x => new {
					patNum=x.PatNum,
					name=x.GetNameLF(),
					position=x.Position.ToString(),
					age=x.Age,
					gender=x.Gender.ToString(),
					status=x.PatStatus.ToString(),
					birthdate=x.Birthdate.Year>1880?x.Birthdate.ToString("yyyy-MM-dd"):"",
					isGuarantor=x.Guarantor==x.PatNum,
				}),
			});
		});

		//Move a patient into another patient's family, mirroring ControlFamily's
		//"move to an existing family" branch: Guarantor + SuperFamily follow the target, then Patients.Update.
		app.MapPost("/api/family/set-guarantor",(SetGuarantorDto dto) => {
			Patient pat=Patients.GetPat(dto.PatNum);
			Patient patTarget=Patients.GetPat(dto.GuarantorPatNum);
			if(pat==null || patTarget==null) {
				return Results.NotFound();
			}
			if(patTarget.Guarantor==pat.Guarantor) {
				return Results.Text("Patient is already in that family.",statusCode:400);
			}
			Patient patOld=pat.Copy();
			pat.Guarantor=patTarget.Guarantor;
			pat.SuperFamily=patTarget.SuperFamily;
			Patients.Update(pat,patOld);//real business update: audit trail + SecurityHash upkeep
			return Results.Json(new { patNum=pat.PatNum,guarantor=pat.Guarantor });
		});

		//Add a new family member, mirroring Program.cs add-patient plus ControlFamily's
		//add flow: Insert, then Update to point Guarantor at the existing family's guarantor.
		app.MapPost("/api/family/add-member",(NewFamilyMember dto) => {
			Patient patExisting=Patients.GetPat(dto.GuarantorPatNum);
			if(patExisting==null) {
				return Results.NotFound();
			}
			Patient pat=new Patient();
			pat.LName=string.IsNullOrEmpty(dto.LName)?patExisting.LName:dto.LName;
			pat.FName=dto.FName;
			pat.Birthdate=DateTime.TryParse(dto.Birthdate,out DateTime birthdate)?birthdate:DateTime.MinValue;
			if(Enum.TryParse(dto.Position,out PatientPosition position)) {
				pat.Position=position;
			}
			pat.PatStatus=PatientStatus.Patient;
			pat.PriProv=patExisting.PriProv!=0?patExisting.PriProv:(Providers.GetFirst()?.ProvNum??0);
			pat.BillingType=PrefC.GetLong(PrefName.PracticeDefaultBillType);
			pat.Address=patExisting.Address;
			pat.City=patExisting.City;
			pat.State=patExisting.State;
			pat.Zip=patExisting.Zip;
			pat.HmPhone=patExisting.HmPhone;
			Patients.Insert(pat,false);
			Patient patOld=pat.Copy();
			pat.Guarantor=patExisting.Guarantor;//join the existing family
			if(Patients.GetPat(pat.Guarantor)?.SuperFamily!=0) {
				pat.SuperFamily=patExisting.SuperFamily;
			}
			Patients.Update(pat,patOld);
			return Results.Json(new { patNum=pat.PatNum,guarantor=pat.Guarantor });
		});

		app.MapGet("/module/family",() => Results.Content(PAGE,"text/html"));
	}

	record SetGuarantorDto(long PatNum,long GuarantorPatNum);
	record NewFamilyMember(long GuarantorPatNum,string LName,string FName,string Birthdate,string Position);

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Family</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr;gap:22px;padding:22px;max-width:900px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,button,select{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
</style></head><body>
<header><h1>Family</h1>
<p>Real Patients.GetFamily / Patients.Update over the Linux core &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form id=pickform><select id=pickpat></select><button type=button id=pickload>Load family</button>
<span id=pickmsg style="font-size:12px;opacity:.7"></span></form></section>
<section><h2>Family members</h2>
<table id=fam><thead><tr><th>#</th><th>Name</th><th>Position</th><th>Age</th><th>Gender</th><th>Status</th><th>Birthdate</th><th></th></tr></thead><tbody></tbody></table>
<div id=guarline style="font-size:12px;opacity:.7;margin-top:6px"></div></section>
<section><h2>Move patient to another family (set guarantor)</h2>
<form id=guarform><select id=guarpat></select><span style="align-self:center;font-size:12px;opacity:.7">joins family of</span>
<select id=guartarget></select><button>Move</button>
<span id=guarmsg style="font-size:12px;opacity:.7"></span></form></section>
<section><h2>Add family member</h2>
<form id=addform><input name=fname placeholder="First name" required>
<input name=lname placeholder="Last name (blank = family name)">
<input name=bd type=date>
<select name=position><option>Single</option><option>Married</option><option>Child</option><option>Widowed</option><option>Divorced</option></select>
<button>Add to family</button><span id=addmsg style="font-size:12px;opacity:.7"></span></form></section>
</main>
<script>
let curPat=0;
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 const opts=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 document.getElementById('pickpat').innerHTML=opts;
 document.getElementById('guarpat').innerHTML=opts;
 document.getElementById('guartarget').innerHTML=opts;
}
async function loadFam(){
 curPat=+document.getElementById('pickpat').value;if(!curPat)return;
 const r=await fetch(`/api/family/${curPat}`);
 if(!r.ok){document.getElementById('pickmsg').textContent='Not found';return}
 const fam=await r.json();
 document.querySelector('#fam tbody').innerHTML=fam.members.map(m=>
  `<tr><td>${m.patNum}</td><td>${m.name}${m.isGuarantor?' <span class=tag>Guarantor</span>':''}</td>
   <td>${m.position}</td><td>${m.age}</td><td>${m.gender}</td><td><span class=tag>${m.status}</span></td>
   <td>${m.birthdate}</td><td></td></tr>`).join('');
 document.getElementById('guarline').textContent=fam.guarantor?`Guarantor: ${fam.guarantor.name} (#${fam.guarantor.patNum})`:'';
 document.getElementById('pickmsg').textContent=`${fam.members.length} member(s)`;
}
document.getElementById('pickload').addEventListener('click',loadFam);
document.getElementById('pickpat').addEventListener('change',loadFam);
document.getElementById('guarform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('guarmsg');msg.textContent='Moving…';
 const r=await fetch('/api/family/set-guarantor',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({patNum:+document.getElementById('guarpat').value,
   guarantorPatNum:+document.getElementById('guartarget').value})});
 msg.textContent=r.ok?'Moved (audit-logged).':'Failed: '+await r.text();loadFam();
});
document.getElementById('addform').addEventListener('submit',async e=>{
 e.preventDefault();const f=new FormData(e.target);const msg=document.getElementById('addmsg');
 if(!curPat){msg.textContent='Load a family first.';return}
 msg.textContent='Adding…';
 const r=await fetch('/api/family/add-member',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({guarantorPatNum:curPat,fName:f.get('fname'),lName:f.get('lname'),
   birthdate:f.get('bd'),position:f.get('position')})});
 msg.textContent=r.ok?'Added.':'Failed: '+await r.text();e.target.reset();loadFam();
});
loadPats().then(loadFam);
</script></body></html>
""";
}
