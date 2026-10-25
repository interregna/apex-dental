/*
 * Referrals module: patient referrals (from/to) and referral sources.
 * Retires FrmReferralsPatient / FrmRefAttachEdit / FrmReferralEdit /
 * FormReferralProcTrack. All data flows through the real business layer:
 * RefAttaches.Refresh/Insert/Update/Delete for the per-patient attachments and
 * Referrals.GetAllReferrals/Insert for the practice-wide referral sources.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class PatfamReferralsModule {
	record NewRefAttach(long ReferralNum,string RefType,string Status,string Note,string RefDate);
	record UpdateRefAttachDto(string Status,string Note,string RefDate);
	record NewReferral(string LName,string FName,string Title,string Phone,bool IsDoctor,bool NotPerson,string BusinessName,string Note);

	public static void Map(WebApplication app) {
		//Warm the referral cache once; readers below use GetDeepCopy instead of
		//Referrals.GetAllReferrals, whose raw SQL says "FROM Referral" and breaks on
		//case-sensitive Linux MariaDB (table name is lowercase `referral`).
		Referrals.RefreshCache();

		//Per-patient referral grid: the same rows FrmReferralsPatient shows, via RefAttaches.Refresh.
		app.MapGet("/api/patients/{patNum:long}/referrals",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<RefAttach> listRefAttaches=RefAttaches.Refresh(patNum);
			List<Referral> listReferrals=Referrals.GetDeepCopy();
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.ProviderSpecialties,false);
			return Results.Json(listRefAttaches.Select(x => {
				Referral referral=listReferrals.FirstOrDefault(y => y.ReferralNum==x.ReferralNum);
				return new {
					refAttachNum=x.RefAttachNum,
					referralNum=x.ReferralNum,
					name=referral?.GetNameFL()??"(missing referral "+x.ReferralNum+")",
					business=referral?.BusinessName??"",
					isDoctor=referral?.IsDoctor??false,
					phone=referral?.Telephone??"",
					specialty=referral==null||referral.Specialty==0?"":Defs.GetName(DefCat.ProviderSpecialties,referral.Specialty,listDefs),
					refType=x.RefType.ToString(),//RefTo / RefFrom / RefCustom
					status=x.RefToStatus.ToString(),
					refDate=x.RefDate.Year>1880?x.RefDate.ToString("yyyy-MM-dd"):"",
					note=x.Note,
					itemOrder=x.ItemOrder,
				};
			}));
		});

		//Attach a referral source to the patient, mirroring FrmRefAttachEdit defaults:
		//outgoing referrals are transitions of care and track the referring provider.
		app.MapPost("/api/patients/{patNum:long}/referrals",(long patNum,NewRefAttach dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Referral> listReferrals=Referrals.GetDeepCopy();
			Referral referral=listReferrals.FirstOrDefault(x => x.ReferralNum==dto.ReferralNum);
			if(referral==null) {
				return Results.Text("Referral source not found",statusCode:400);
			}
			if(!Enum.TryParse(dto.RefType,out ReferralType refType)) {
				return Results.Text("RefType must be RefTo, RefFrom, or RefCustom",statusCode:400);
			}
			RefAttach refAttach=new RefAttach();
			refAttach.ReferralNum=dto.ReferralNum;
			refAttach.PatNum=patNum;
			refAttach.ItemOrder=RefAttaches.Refresh(patNum).Count+1;//one-based display order
			refAttach.RefDate=DateTime.TryParse(dto.RefDate,out DateTime refDate)?refDate:DateTime.Today;
			refAttach.RefType=refType;
			refAttach.RefToStatus=Enum.TryParse(dto.Status,out ReferralToStatus status)?status:ReferralToStatus.None;
			refAttach.Note=dto.Note??"";
			refAttach.IsTransitionOfCare=(refType==ReferralType.RefTo);//all outgoing referrals default to true
			refAttach.ProvNum=refType==ReferralType.RefTo?pat.PriProv:-1;//-1 when RefType is not RefTo
			long refAttachNum=RefAttaches.Insert(refAttach);
			return Results.Json(new { refAttachNum });
		});

		//Edit status/date/note on an existing attachment through the audited two-object Update.
		app.MapPut("/api/refattaches/{refAttachNum:long}",(long refAttachNum,UpdateRefAttachDto dto) => {
			RefAttach refAttach=RefAttaches.GetOne(refAttachNum);
			if(refAttach==null) {
				return Results.NotFound();
			}
			RefAttach refAttachOld=refAttach.Copy();
			if(dto.Status!=null && Enum.TryParse(dto.Status,out ReferralToStatus status)) {
				refAttach.RefToStatus=status;
			}
			if(dto.Note!=null) {
				refAttach.Note=dto.Note;
			}
			if(DateTime.TryParse(dto.RefDate,out DateTime refDate)) {
				refAttach.RefDate=refDate;
			}
			RefAttaches.Update(refAttach,refAttachOld);
			return Results.Json(new { refAttachNum });
		});

		//Detach a referral from the patient; business Delete also compacts ItemOrder.
		app.MapDelete("/api/refattaches/{refAttachNum:long}",(long refAttachNum) => {
			RefAttach refAttach=RefAttaches.GetOne(refAttachNum);
			if(refAttach==null) {
				return Results.NotFound();
			}
			RefAttaches.Delete(refAttach);
			return Results.Ok();
		});

		//Practice-wide referral source list (picker for the attach form).
		app.MapGet("/api/referrals",() => {
			List<Referral> listReferrals=Referrals.GetDeepCopy();
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.ProviderSpecialties,false);
			return Results.Json(listReferrals
				.Where(x => !x.IsHidden)
				.OrderBy(x => x.LName).ThenBy(x => x.FName)
				.Select(x => new {
					referralNum=x.ReferralNum,
					name=x.GetNameFL(),
					lName=x.LName,
					fName=x.FName,
					title=x.Title,
					business=x.BusinessName,
					phone=x.Telephone!=""?x.Telephone:x.Phone2,
					email=x.EMail,
					specialty=x.Specialty==0?"":Defs.GetName(DefCat.ProviderSpecialties,x.Specialty,listDefs),
					isDoctor=x.IsDoctor,
					notPerson=x.NotPerson,
					note=x.Note,
				}));
		});

		//Quick-add of a new referral source (the FrmReferralEdit essentials).
		app.MapPost("/api/referrals",(NewReferral dto) => {
			if(string.IsNullOrWhiteSpace(dto.LName)) {
				return Results.Text("Last name (or source name) is required",statusCode:400);
			}
			Referral referral=new Referral();
			referral.LName=dto.LName.Trim();
			referral.FName=dto.FName?.Trim()??"";
			referral.MName="";
			referral.SSN="";
			referral.ST="";
			//Telephone column is restrictive: exactly 10 digits, numbers only. Anything else goes in Phone2.
			string digits=new string((dto.Phone??"").Where(char.IsDigit).ToArray());
			referral.Telephone=digits.Length==10?digits:"";
			referral.Phone2=digits.Length==10?"":(dto.Phone??"");
			referral.Address="";
			referral.Address2="";
			referral.City="";
			referral.Zip="";
			referral.Note=dto.Note??"";
			referral.Title=dto.Title?.Trim()??"";
			referral.EMail="";
			referral.NationalProvID="";
			referral.BusinessName=dto.BusinessName?.Trim()??"";
			referral.DisplayNote="";
			referral.IsDoctor=dto.IsDoctor && !dto.NotPerson;//a doctor cannot be a non-person source
			referral.NotPerson=dto.NotPerson;
			long referralNum=Referrals.Insert(referral);
			Referrals.RefreshCache();//keep the referral cache in step for cache-based readers
			return Results.Json(new { referralNum });
		});

		app.MapGet("/module/referrals",() => Results.Content(PAGE,"text/html"));
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Referrals</title>
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
label{display:flex;align-items:center;gap:4px;font-size:13px}
</style></head><body>
<header><h1>Referrals</h1>
<p>Patient referrals from/to &middot; real RefAttaches + Referrals business layer &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2>
<form style="margin-top:0"><select id=refpat required></select></form></section>
<section><h2>Patient referrals</h2>
<table id=attaches><thead><tr><th>Type</th><th>Referral</th><th>Specialty</th><th>Date</th><th>Status</th><th>Note</th><th></th></tr></thead><tbody></tbody></table>
<form id=attachform>
<select id=atttype><option value=RefFrom>Referred From</option><option value=RefTo>Referred To</option></select>
<select id=attref></select>
<input id=attnote placeholder="Note">
<button>Attach</button><span id=attmsg style="font-size:12px;opacity:.7"></span></form></section>
<section><h2>Referral sources</h2>
<table id=sources><thead><tr><th>Name</th><th>Business</th><th>Specialty</th><th>Phone</th><th>Type</th></tr></thead><tbody></tbody></table>
<form id=srcform>
<input id=srclname placeholder="Last name / source" required><input id=srcfname placeholder="First name">
<input id=srctitle placeholder="Title (DDS)" style="width:80px"><input id=srcphone placeholder="Phone">
<input id=srcbiz placeholder="Business name">
<label><input type=checkbox id=srcdoc checked>Doctor</label>
<label><input type=checkbox id=srcnp>Not a person</label>
<button>Add source</button><span id=srcmsg style="font-size:12px;opacity:.7"></span></form></section>
</main>
<script>
const STATUSES=["None","Declined","Scheduled","Consulted","InTreatment","Complete"];
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('refpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 loadAttaches();
}
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
async function loadSources(){
 const srcs=await (await fetch('/api/referrals')).json();
 document.getElementById('attref').innerHTML=srcs.map(s=>`<option value=${s.referralNum}>${esc(s.name)}</option>`).join('');
 document.querySelector('#sources tbody').innerHTML=srcs.map(s=>
  `<tr><td>${esc(s.name)}</td><td>${esc(s.business)}</td><td>${esc(s.specialty)}</td><td>${esc(s.phone)}</td>
   <td><span class=tag>${s.isDoctor?'Doctor':s.notPerson?'Source':'Person'}</span></td></tr>`).join('')
  ||'<tr><td colspan=5 style="opacity:.6">No referral sources yet.</td></tr>';
}
async function loadAttaches(){
 const patNum=document.getElementById('refpat').value;if(!patNum)return;
 const rows=await (await fetch(`/api/patients/${patNum}/referrals`)).json();
 document.querySelector('#attaches tbody').innerHTML=rows.map(r=>
  `<tr><td><span class=tag>${r.refType==='RefFrom'?'From':r.refType==='RefTo'?'To':'Custom'}</span></td>
   <td>${esc(r.name)}</td><td>${esc(r.specialty)}</td><td>${r.refDate}</td>
   <td><select data-status=${r.refAttachNum}>${STATUSES.map(s=>`<option${s===r.status?' selected':''}>${s}</option>`).join('')}</select></td>
   <td>${esc(r.note)}</td>
   <td><button data-del=${r.refAttachNum} type=button>Detach</button></td></tr>`).join('')
  ||'<tr><td colspan=7 style="opacity:.6">No referrals for this patient.</td></tr>';
 document.querySelectorAll('#attaches [data-status]').forEach(sel=>sel.addEventListener('change',async()=>{
  await fetch(`/api/refattaches/${sel.dataset.status}`,{method:'PUT',headers:{'Content-Type':'application/json'},
   body:JSON.stringify({status:sel.value})});
 }));
 document.querySelectorAll('#attaches [data-del]').forEach(btn=>btn.addEventListener('click',async()=>{
  await fetch(`/api/refattaches/${btn.dataset.del}`,{method:'DELETE'});loadAttaches();
 }));
}
document.getElementById('refpat').addEventListener('change',loadAttaches);
document.getElementById('attachform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('attmsg');msg.textContent='Attaching…';
 const patNum=document.getElementById('refpat').value;
 const r=await fetch(`/api/patients/${patNum}/referrals`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({referralNum:+document.getElementById('attref').value,
   refType:document.getElementById('atttype').value,status:'None',
   note:document.getElementById('attnote').value,refDate:''})});
 msg.textContent=r.ok?'Attached.':'Failed: '+await r.text();
 if(r.ok)document.getElementById('attnote').value='';
 loadAttaches();
});
document.getElementById('srcform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('srcmsg');msg.textContent='Saving…';
 const r=await fetch('/api/referrals',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({lName:document.getElementById('srclname').value,
   fName:document.getElementById('srcfname').value,title:document.getElementById('srctitle').value,
   phone:document.getElementById('srcphone').value,businessName:document.getElementById('srcbiz').value,
   isDoctor:document.getElementById('srcdoc').checked,notPerson:document.getElementById('srcnp').checked,note:''})});
 msg.textContent=r.ok?'Saved.':'Failed: '+await r.text();
 if(r.ok)e.target.reset();
 loadSources();
});
loadPats();loadSources();
</script></body></html>
""";
}
