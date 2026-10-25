/*
 * Medical module: web slice of FormMedical and its child dialogs
 * (FormMedPat, FormMedications, FormMedicationEdit, FormAllergyEdit).
 * Medications flow through MedicationPats.Refresh/Insert/Update joined to the
 * Medications cache, allergies through Allergies.GetAll/Insert/Update against
 * AllergyDefs.GetAll, and problems through Diseases.Refresh/Insert/Update
 * against the DiseaseDefs cache — the same business calls the WinForms
 * dialogs make, so every write is audit-visible in the desktop client.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class PatfamMedicalModule {
	record MedicationPatDto(long MedicationNum,string NewMedName,string PatNote,string DateStart,string DateStop);
	record MedicationPatUpdateDto(string PatNote,string DateStart,string DateStop);
	record AllergyDto(long AllergyDefNum,string Reaction,string DateAdverseReaction,bool StatusIsActive);
	record ProblemDto(long DiseaseDefNum,string PatNote,string ProbStatus,string DateStart,string DateStop);

	static string FmtDate(DateTime date) {
		return date.Year<1880?"":date.ToString("yyyy-MM-dd");
	}

	static DateTime ParseDate(string str) {
		return DateTime.TryParse(str,out DateTime date)?date:DateTime.MinValue;
	}

	public static void Map(WebApplication app) {
		//Medications and DiseaseDefs are cached tables; Program.cs only warms
		//Prefs/Security/Providers/Defs/ProcCodes, so warm the two we join against.
		Cache.Refresh(InvalidType.Medications,InvalidType.Diseases);

		//FormMedical's three tabs in one payload: medications (active or all),
		//allergies, problems, plus the def lists the add/edit pickers need.
		app.MapGet("/api/patients/{patNum:long}/medical",(long patNum,string includeInactive) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			bool showInactive=includeInactive=="true";//maps to includeDiscontinued/showInactive flags below
			List<AllergyDef> listAllergyDefsAll=AllergyDefs.GetAll(true);//hidden included so existing rows always resolve
			List<MedicationPat> listMedicationPats=MedicationPats.Refresh(patNum,showInactive);
			List<Allergy> listAllergies=Allergies.GetAll(patNum,showInactive);
			List<Disease> listDiseases=showInactive?Diseases.Refresh(patNum):Diseases.Refresh(patNum,true);
			return Results.Json(new {
				patNum,
				showInactive,
				medications=listMedicationPats.Select(x => {
					Medication medication=x.MedicationNum==0?null:Medications.GetMedication(x.MedicationNum);//free-text meds have MedicationNum 0; GetMedication returns null instead of throwing
					return new {
						medicationPatNum=x.MedicationPatNum,
						medicationNum=x.MedicationNum,
						name=medication?.MedName??x.MedDescript??"",
						generic=(medication==null || medication.GenericNum==medication.MedicationNum)?"":Medications.GetGenericName(medication.GenericNum),
						notes=medication?.Notes??"",
						patNote=x.PatNote??"",
						dateStart=FmtDate(x.DateStart),
						dateStop=FmtDate(x.DateStop),
						isActive=MedicationPats.IsMedActive(x),
					};
				}).OrderByDescending(x => x.isActive).ThenBy(x => x.name),
				allergies=listAllergies.Select(x => new {
					allergyNum=x.AllergyNum,
					allergyDefNum=x.AllergyDefNum,
					description=listAllergyDefsAll.FirstOrDefault(y => y.AllergyDefNum==x.AllergyDefNum)?.Description??"",
					reaction=x.Reaction??"",
					dateAdverseReaction=FmtDate(x.DateAdverseReaction),
					isActive=x.StatusIsActive,
				}).OrderByDescending(x => x.isActive).ThenBy(x => x.description),
				problems=listDiseases.Select(x => new {
					diseaseNum=x.DiseaseNum,
					diseaseDefNum=x.DiseaseDefNum,
					name=DiseaseDefs.GetName(x.DiseaseDefNum),
					patNote=x.PatNote??"",
					status=x.ProbStatus.ToString(),
					dateStart=FmtDate(x.DateStart),
					dateStop=FmtDate(x.DateStop),
				}).OrderBy(x => x.status).ThenBy(x => x.name),
				pickers=new {
					medications=Medications.GetList("").OrderBy(x => x.MedName)
						.Select(x => new { medicationNum=x.MedicationNum,name=x.MedName }),
					allergyDefs=AllergyDefs.GetAll(false)
						.Select(x => new { allergyDefNum=x.AllergyDefNum,description=x.Description }),
					diseaseDefs=DiseaseDefs.GetDeepCopy(true).OrderBy(x => x.DiseaseName)
						.Select(x => new { diseaseDefNum=x.DiseaseDefNum,name=x.DiseaseName }),
				},
			});
		});

		//POST /api/patients/{patNum}/medications is owned by ClinicalMedHistoryModule
		//(single registration; it supports MedicationNum, NewMedName, and MedDescript).

		//FormMedPat "edit" flow: note and start/stop dates.  Setting a stop date in
		//the past is exactly how the desktop dialog discontinues a medication.
		app.MapPut("/api/medications/pat/{medicationPatNum:long}",(long medicationPatNum,MedicationPatUpdateDto dto) => {
			MedicationPat medicationPat=MedicationPats.GetOne(medicationPatNum);
			if(medicationPat==null) {
				return Results.NotFound();
			}
			medicationPat.PatNote=dto.PatNote??"";
			medicationPat.DateStart=ParseDate(dto.DateStart);
			medicationPat.DateStop=ParseDate(dto.DateStop);
			MedicationPats.Update(medicationPat);
			return Results.Json(new { medicationPatNum,isActive=MedicationPats.IsMedActive(medicationPat) });
		});

		//Allergy and problem POST/PUT routes are owned by ClinicalMedHistoryModule
		//(single registration serving both the med-history and patfam-medical UIs).

		app.MapGet("/module/medical",() => Results.Content(PAGE,"text/html"));
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Medical</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr;gap:22px;padding:22px;max-width:1000px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent);vertical-align:top}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.off{opacity:.55}
.msg{font-size:12px;opacity:.7;align-self:center}
</style></head><body>
<header><h1>Medical — Medications · Allergies · Problems</h1>
<p>Real MedicationPats / Allergies / Diseases business layer &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form style="margin-top:0"><select id=medpat required></select>
<label style="align-self:center;font-size:13px"><input type=checkbox id=showinactive> Show discontinued / inactive</label></form></section>
<section><h2>Medications</h2>
<table id=meds><thead><tr><th>Medication</th><th>Generic</th><th>Pat note</th><th>Start</th><th>Stop</th><th>Status</th><th></th></tr></thead><tbody></tbody></table>
<form id=addmed><select id=medpick><option value=0>— pick medication —</option></select>
<input id=mednew placeholder="…or new medication name">
<input id=mednote placeholder="Patient note (dosage etc.)">
<button>Add medication</button><span class=msg id=medmsg></span></form></section>
<section><h2>Allergies</h2>
<table id=algs><thead><tr><th>Allergy</th><th>Reaction</th><th>Reaction date</th><th>Status</th><th></th></tr></thead><tbody></tbody></table>
<form id=addalg><select id=algpick><option value=0>— pick allergy —</option></select>
<input id=algreact placeholder="Reaction">
<button>Add allergy</button><span class=msg id=algmsg></span></form></section>
<section><h2>Problems</h2>
<table id=probs><thead><tr><th>Problem</th><th>Note</th><th>Start</th><th>Stop</th><th>Status</th><th></th></tr></thead><tbody></tbody></table>
<form id=addprob><select id=probpick><option value=0>— pick problem —</option></select>
<input id=probnote placeholder="Note">
<button>Add problem</button><span class=msg id=probmsg></span></form></section>
</main>
<script>
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let data=null;
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('medpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${esc(p.name)}</option>`).join('');
 loadMedical();
}
async function loadMedical(){
 const patNum=document.getElementById('medpat').value;if(!patNum)return;
 const inc=document.getElementById('showinactive').checked;
 data=await (await fetch(`/api/patients/${patNum}/medical?includeInactive=${inc}`)).json();
 document.querySelector('#meds tbody').innerHTML=data.medications.map(m=>
  `<tr class="${m.isActive?'':'off'}"><td>${esc(m.name)}</td><td>${esc(m.generic)}</td><td>${esc(m.patNote)}</td>
   <td>${m.dateStart}</td><td>${m.dateStop}</td><td><span class=tag>${m.isActive?'Active':'Stopped'}</span></td>
   <td><button data-med=${m.medicationPatNum} data-active=${m.isActive}>${m.isActive?'Discontinue':'Resume'}</button></td></tr>`).join('')
  ||'<tr><td colspan=7 style="opacity:.6">No medications.</td></tr>';
 document.querySelector('#algs tbody').innerHTML=data.allergies.map(a=>
  `<tr class="${a.isActive?'':'off'}"><td>${esc(a.description)}</td><td>${esc(a.reaction)}</td><td>${a.dateAdverseReaction}</td>
   <td><span class=tag>${a.isActive?'Active':'Inactive'}</span></td>
   <td><button data-alg=${a.allergyNum}>${a.isActive?'Set inactive':'Set active'}</button></td></tr>`).join('')
  ||'<tr><td colspan=5 style="opacity:.6">No allergies.</td></tr>';
 document.querySelector('#probs tbody').innerHTML=data.problems.map(p=>
  `<tr class="${p.status==='Active'?'':'off'}"><td>${esc(p.name)}</td><td>${esc(p.patNote)}</td><td>${p.dateStart}</td><td>${p.dateStop}</td>
   <td><select data-prob=${p.diseaseNum}>${['Active','Resolved','Inactive'].map(s=>`<option${s===p.status?' selected':''}>${s}</option>`).join('')}</select></td>
   <td></td></tr>`).join('')
  ||'<tr><td colspan=6 style="opacity:.6">No problems.</td></tr>';
 document.getElementById('medpick').innerHTML='<option value=0>— pick medication —</option>'+
  data.pickers.medications.map(m=>`<option value=${m.medicationNum}>${esc(m.name)}</option>`).join('');
 document.getElementById('algpick').innerHTML='<option value=0>— pick allergy —</option>'+
  data.pickers.allergyDefs.map(d=>`<option value=${d.allergyDefNum}>${esc(d.description)}</option>`).join('');
 document.getElementById('probpick').innerHTML='<option value=0>— pick problem —</option>'+
  data.pickers.diseaseDefs.map(d=>`<option value=${d.diseaseDefNum}>${esc(d.name)}</option>`).join('');
 wire();
}
function wire(){
 document.querySelectorAll('[data-med]').forEach(b=>b.addEventListener('click',async e=>{
  e.preventDefault();
  const m=data.medications.find(x=>x.medicationPatNum==b.dataset.med);
  await fetch(`/api/medications/pat/${b.dataset.med}`,{method:'PUT',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({patNote:m.patNote,dateStart:m.dateStart,
    dateStop:b.dataset.active==='true'?new Date().toISOString().slice(0,10):''})});
  loadMedical();
 }));
 document.querySelectorAll('[data-alg]').forEach(b=>b.addEventListener('click',async e=>{
  e.preventDefault();
  const a=data.allergies.find(x=>x.allergyNum==b.dataset.alg);
  await fetch(`/api/allergies/${b.dataset.alg}`,{method:'PUT',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({allergyDefNum:a.allergyDefNum,reaction:a.reaction,
    dateAdverseReaction:a.dateAdverseReaction,statusIsActive:!a.isActive})});
  loadMedical();
 }));
 document.querySelectorAll('select[data-prob]').forEach(sel=>sel.addEventListener('change',async()=>{
  const p=data.problems.find(x=>x.diseaseNum==sel.dataset.prob);
  await fetch(`/api/problems/${sel.dataset.prob}`,{method:'PUT',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({diseaseDefNum:p.diseaseDefNum,patNote:p.patNote,probStatus:sel.value,
    dateStart:p.dateStart,dateStop:sel.value==='Active'?'':(p.dateStop||new Date().toISOString().slice(0,10))})});
  loadMedical();
 }));
}
document.getElementById('medpat').addEventListener('change',loadMedical);
document.getElementById('showinactive').addEventListener('change',loadMedical);
document.getElementById('addmed').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('medmsg');msg.textContent='Saving…';
 const patNum=document.getElementById('medpat').value;
 const r=await fetch(`/api/patients/${patNum}/medications`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({medicationNum:+document.getElementById('medpick').value,
   newMedName:document.getElementById('mednew').value,patNote:document.getElementById('mednote').value,
   dateStart:new Date().toISOString().slice(0,10),dateStop:''})});
 msg.textContent=r.ok?'Added.':'Failed: '+await r.text();
 if(r.ok){document.getElementById('mednew').value='';document.getElementById('mednote').value='';}
 loadMedical();
});
document.getElementById('addalg').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('algmsg');msg.textContent='Saving…';
 const patNum=document.getElementById('medpat').value;
 const r=await fetch(`/api/patients/${patNum}/allergies`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({allergyDefNum:+document.getElementById('algpick').value,
   reaction:document.getElementById('algreact').value,dateAdverseReaction:'',statusIsActive:true})});
 msg.textContent=r.ok?'Added.':'Failed: '+await r.text();
 if(r.ok)document.getElementById('algreact').value='';
 loadMedical();
});
document.getElementById('addprob').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('probmsg');msg.textContent='Saving…';
 const patNum=document.getElementById('medpat').value;
 const r=await fetch(`/api/patients/${patNum}/problems`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({diseaseDefNum:+document.getElementById('probpick').value,
   patNote:document.getElementById('probnote').value,probStatus:'Active',
   dateStart:new Date().toISOString().slice(0,10),dateStop:''})});
 msg.textContent=r.ok?'Added.':'Failed: '+await r.text();
 if(r.ok)document.getElementById('probnote').value='';
 loadMedical();
});
loadPats();
</script></body></html>
""";
}
