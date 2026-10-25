/*
 * Account/ledger module: read-only patient ledger (procedures, payments/paysplits,
 * adjustments, balances) plus simple payment entry through the real business layer
 * (Payments.Insert(pay,listPaySplits) -> PaySplits.InsertMany, SecurityHash upkeep).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class AccountModule {

	public static void Map(WebApplication app) {

		app.MapGet("/api/account/{patNum:long}",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<LedgerEntry> entries=new List<LedgerEntry>();
			//Procedures: charges. Same read the Account module grid starts from.
			List<Procedure> listProcs=Procedures.Refresh(patNum);
			foreach(Procedure proc in listProcs.Where(x => x.ProcStatus!=ProcStat.D)) {
				ProcedureCode code=ProcedureCodes.GetProcCode(proc.CodeNum);
				entries.Add(new LedgerEntry(proc.ProcDate,"Procedure",
					code.ProcCode+" "+code.Descript+(string.IsNullOrEmpty(proc.ToothNum)?"":" #"+proc.ToothNum),
					Providers.GetAbbr(proc.ProvNum),proc.ProcStatus.ToString(),proc.ProcFee,0));
			}
			//Payments shown split-by-split, like the desktop ledger.
			List<Payment> listPayments=Payments.Refresh(patNum);
			PaySplit[] arraySplits=PaySplits.Refresh(patNum);
			List<Def> listPayTypeDefs=Defs.GetDefsForCategory(DefCat.PaymentTypes,true);
			foreach(PaySplit split in arraySplits) {
				Payment payment=listPayments.FirstOrDefault(x => x.PayNum==split.PayNum);
				string payType=payment==null?"":Defs.GetName(DefCat.PaymentTypes,payment.PayType,listPayTypeDefs);
				entries.Add(new LedgerEntry(split.DatePay,"Payment",
					("Payment "+payType+" "+(payment?.PayNote??"")).Trim(),
					Providers.GetAbbr(split.ProvNum),"",0,split.SplitAmt));
			}
			//Adjustments: positive amounts are charges, negative are credits.
			Adjustment[] arrayAdjustments=Adjustments.Refresh(patNum);
			List<Def> listAdjTypeDefs=Defs.GetDefsForCategory(DefCat.AdjTypes,true);
			foreach(Adjustment adj in arrayAdjustments) {
				entries.Add(new LedgerEntry(adj.AdjDate,"Adjustment",
					(Defs.GetName(DefCat.AdjTypes,adj.AdjType,listAdjTypeDefs)+" "+adj.AdjNote).Trim(),
					Providers.GetAbbr(adj.ProvNum),"",adj.AdjAmt>0?adj.AdjAmt:0,adj.AdjAmt<0?-adj.AdjAmt:0));
			}
			return Results.Json(new {
				patNum=pat.PatNum,
				patient=pat.GetNameLF(),
				estBalance=pat.EstBalance,
				balTotal=pat.BalTotal,
				insEst=pat.InsEst,
				aging=new { bal_0_30=pat.Bal_0_30,bal_31_60=pat.Bal_31_60,bal_61_90=pat.Bal_61_90,balOver90=pat.BalOver90 },
				entries=entries.OrderByDescending(x => x.SortDate).Select(x => new {
					date=x.SortDate.ToString("yyyy-MM-dd"),
					type=x.Type,
					description=x.Description,
					provider=x.Provider,
					status=x.Status,
					charge=x.Charge,
					credit=x.Credit,
				}).ToList(),
			});
		});

		app.MapPost("/api/account/{patNum:long}/payment",(long patNum,NewPaymentDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(dto.Amount<=0) {
				return Results.BadRequest("Amount must be positive.");
			}
			Payment payment=new Payment() {
				PatNum=patNum,
				PayDate=DateTime.Today,
				DateEntry=DateTime.Today,
				PayAmt=dto.Amount,
				PayType=Defs.GetDefsForCategory(DefCat.PaymentTypes,true).FirstOrDefault()?.DefNum??0,
				PayNote=dto.Note??"",
				IsSplit=false,
				ClinicNum=pat.ClinicNum,
			};
			PaySplit split=new PaySplit() {
				PatNum=patNum,
				SplitAmt=dto.Amount,
				DatePay=DateTime.Today,
				ProcDate=DateTime.Today,
				DateEntry=DateTime.Today,
				ProvNum=pat.PriProv,
				ClinicNum=pat.ClinicNum,
			};
			//Payments.Insert(pay,listPaySplits): inserts the payment, then PaySplits.InsertMany
			//stamps each split with the PayNum and its SecurityHash.
			long payNum=Payments.Insert(payment,new List<PaySplit>() { split });
			if(payNum==0) {
				return Results.Text("Payment insert failed",statusCode:500);
			}
			return Results.Json(new { payNum });
		});

		app.MapGet("/module/account",() => Results.Content(PAGE,"text/html"));
	}

	record NewPaymentDto(double Amount,string Note);
	record LedgerEntry(DateTime SortDate,string Type,string Description,string Provider,string Status,double Charge,double Credit);

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Account</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{padding:22px;max-width:1000px}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto;margin-bottom:22px}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,select,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
#balline{font-size:14px;margin:8px 0 0;font-weight:600}
#aging{font-size:12px;opacity:.75;margin-top:4px}
</style></head><body>
<header><h1>Account — patient ledger</h1>
<p>Procedures, payments &amp; adjustments through the real OpenDentBusiness layer</p></header>
<main>
<section><h2>Patient</h2>
<form onsubmit="return false"><select id=patsel></select><button id=loadbtn>Load ledger</button>
<span id=msg style="font-size:12px;opacity:.7"></span></form>
<div id=balline></div><div id=aging></div></section>
<section><h2>Ledger (newest first)</h2>
<table id=ledger><thead><tr><th>Date</th><th>Type</th><th>Description</th><th>Prov</th><th>Status</th>
<th class=num>Charge</th><th class=num>Credit</th></tr></thead><tbody></tbody></table></section>
<section><h2>Enter payment</h2>
<form id=payform><input id=payamt type=number step=0.01 min=0.01 placeholder="Amount" required>
<input id=paynote placeholder="Note"><button>Post payment</button>
<span id=paymsg style="font-size:12px;opacity:.7"></span></form></section>
</main>
<script>
const usd=n=>Number(n).toLocaleString(undefined,{style:'currency',currency:'USD'});
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('patsel').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
}
async function loadLedger(){
 const patNum=document.getElementById('patsel').value;if(!patNum)return;
 const msg=document.getElementById('msg');msg.textContent='Loading…';
 const r=await fetch(`/api/account/${patNum}`);
 if(!r.ok){msg.textContent='Failed: '+await r.text();return}
 const d=await r.json();msg.textContent=`${d.entries.length} entries`;
 document.querySelector('#ledger tbody').innerHTML=d.entries.map(e=>
  `<tr><td>${e.date}</td><td><span class=tag>${e.type}</span></td><td>${e.description}</td><td>${e.provider}</td><td>${e.status}</td>`+
  `<td class=num>${e.charge?usd(e.charge):''}</td><td class=num>${e.credit?usd(e.credit):''}</td></tr>`).join('');
 document.getElementById('balline').textContent=
  `${d.patient} — estimated balance ${usd(d.estBalance)} · total balance ${usd(d.balTotal)} · insurance estimate ${usd(d.insEst)}`;
 document.getElementById('aging').textContent=
  `Aging: 0-30 ${usd(d.aging.bal_0_30)} · 31-60 ${usd(d.aging.bal_31_60)} · 61-90 ${usd(d.aging.bal_61_90)} · over 90 ${usd(d.aging.balOver90)}`;
}
document.getElementById('loadbtn').addEventListener('click',loadLedger);
document.getElementById('patsel').addEventListener('change',loadLedger);
document.getElementById('payform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('paymsg');
 const patNum=document.getElementById('patsel').value;if(!patNum){msg.textContent='Pick a patient.';return}
 msg.textContent='Posting…';
 const r=await fetch(`/api/account/${patNum}/payment`,{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({amount:+document.getElementById('payamt').value,note:document.getElementById('paynote').value})});
 if(!r.ok){msg.textContent='Failed: '+await r.text();return}
 const {payNum}=await r.json();msg.textContent=`Payment ${payNum} posted.`;
 e.target.reset();loadLedger();
});
loadPats().then(loadLedger);
</script></body></html>
""";
}
