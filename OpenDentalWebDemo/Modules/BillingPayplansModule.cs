/*
 * Billing pay plans module: read-only web slice of FormPayPlan / FormPayPlanChargeEdit.
 * All data flows through the real business layer:
 * PayPlans.GetForPatNum / PayPlans.GetOne list the plans, PayPlanCharges.GetForPayPlan
 * loads the charge/credit schedule, and PayPlans.GetAmtPaid / GetDueNow / GetBalance
 * are the same calculations ContrAccount and FormPayPlan run for their summary rows.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingPayplansModule {
	//Terms for a static patient payment plan create/edit (FormPayPlan's GetTermsFromUI inputs).
	//Term is the number of payments (PayPlanTerms.PayCount); when 0 the PayAmt drives the schedule.
	record NewPayPlanDto(string PlanDate,double TotalAmount,double DownPayment,double Apr,double PayAmt,int Term,string Note,long Guarantor);

	public static void Map(WebApplication app) {
		//Per-patient payment plan grid: same columns as the Payment Plans grid in ContrAccount
		//(guarantor, total, amount paid, due now, balance), for plans where the patient is
		//either the PatNum or the Guarantor (PayPlans.GetForPatNum semantics).
		app.MapGet("/api/patients/{patNum:long}/payplans",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<PayPlan> listPayPlans=PayPlans.GetForPatNum(patNum);
			return Results.Json(listPayPlans.OrderByDescending(x => x.PayPlanDate).Select(x => {
				List<PayPlanCharge> listCharges=PayPlanCharges.GetForPayPlan(x.PayPlanNum);
				return new {
					payPlanNum=x.PayPlanNum,
					date=x.PayPlanDate.ToString("yyyy-MM-dd"),
					patNum=x.PatNum,
					patient=Patients.GetPat(x.PatNum)?.GetNameLF()??"",
					guarantorNum=x.Guarantor,
					guarantor=Patients.GetPat(x.Guarantor)?.GetNameLF()??"",
					isInsurance=x.PlanNum!=0,
					isDynamic=x.IsDynamic,
					isClosed=x.IsClosed,
					apr=x.APR,
					//Total = every debit's principal plus interest, the full scheduled cost of the plan.
					total=listCharges.Where(y => y.ChargeType==PayPlanChargeType.Debit).Sum(y => y.Principal+y.Interest),
					amtPaid=PayPlans.GetAmtPaid(x),
					dueNow=PayPlans.GetDueNow(x.PayPlanNum,listCharges),
					balance=PayPlans.GetBalance(x.PayPlanNum,listCharges),
				};
			}));
		});

		//Drill-in: one plan's header plus its full charge/credit schedule
		//(FormPayPlan's grid; each row is what FormPayPlanChargeEdit displayed).
		app.MapGet("/api/payplans/{payPlanNum:long}",(long payPlanNum) => {
			PayPlan payPlan=PayPlans.GetOne(payPlanNum);
			if(payPlan==null) {
				return Results.NotFound();
			}
			List<PayPlanCharge> listCharges=PayPlanCharges.GetForPayPlan(payPlanNum);
			return Results.Json(new {
				payPlanNum=payPlan.PayPlanNum,
				date=payPlan.PayPlanDate.ToString("yyyy-MM-dd"),
				patNum=payPlan.PatNum,
				patient=Patients.GetPat(payPlan.PatNum)?.GetNameLF()??"",
				guarantorNum=payPlan.Guarantor,
				guarantor=Patients.GetPat(payPlan.Guarantor)?.GetNameLF()??"",
				isInsurance=payPlan.PlanNum!=0,
				isDynamic=payPlan.IsDynamic,
				isClosed=payPlan.IsClosed,
				apr=payPlan.APR,
				note=payPlan.Note,
				payAmt=payPlan.PayAmt,
				downPayment=payPlan.DownPayment,
				numberOfPayments=payPlan.NumberOfPayments,
				completedAmt=payPlan.CompletedAmt,
				//Loan principal (sum of debit principal, including any down payment) — this is the
				//"Total amount" the create form takes, distinct from total below which adds interest.
				principal=listCharges.Where(x => x.ChargeType==PayPlanChargeType.Debit).Sum(x => x.Principal),
				total=listCharges.Where(x => x.ChargeType==PayPlanChargeType.Debit).Sum(x => x.Principal+x.Interest),
				amtPaid=PayPlans.GetAmtPaid(payPlan),
				dueNow=PayPlans.GetDueNow(payPlanNum,listCharges),
				balance=PayPlans.GetBalance(payPlanNum,listCharges),
				charges=listCharges.OrderBy(x => x.ChargeDate).ThenBy(x => x.PayPlanChargeNum).Select(x => new {
					payPlanChargeNum=x.PayPlanChargeNum,
					chargeDate=x.ChargeDate.ToString("yyyy-MM-dd"),
					chargeType=x.ChargeType.ToString(),//Debit or Credit
					provider=Providers.GetAbbr(x.ProvNum,true),
					principal=x.Principal,
					interest=x.Interest,
					note=x.Note,
					isDue=x.ChargeType==PayPlanChargeType.Debit && x.ChargeDate<=DateTime.Today,
				}),
			});
		});

		//Create a static patient payment plan (FormPayPlan in IsNew mode): insert the plan,
		//then build the amortization schedule exactly as PayPlanEdit.CreateScheduleCharges does
		//and Sync the resulting PayPlanCharges — the same two calls FormPayPlan.SaveData makes.
		app.MapPost("/api/patients/{patNum:long}/payplans",(long patNum,NewPayPlanDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			string err=ValidateTerms(dto);
			if(err!="") {
				return Results.Text(err,statusCode:400);
			}
			Family fam=Patients.GetFamily(patNum);
			long provNum=pat.PriProv!=0?pat.PriProv:(Providers.GetFirst()?.ProvNum??0);
			PayPlan payPlan=new PayPlan();
			payPlan.PatNum=patNum;
			payPlan.Guarantor=dto.Guarantor>0?dto.Guarantor:patNum;
			payPlan.PlanNum=0;//Standard patient payment plan (not insurance, not dynamic).
			payPlan.IsDynamic=false;
			payPlan.PayPlanDate=ParseDate(dto.PlanDate);
			payPlan.CompletedAmt=dto.TotalAmount;
			payPlan.Note=dto.Note??"";
			//Insert first so the plan has a PayPlanNum for its charges (ContrAccount inserts before
			//opening FormPayPlan; the form then only Updates).
			payPlan.PayPlanNum=PayPlans.Insert(payPlan);
			(List<PayPlanCharge> listCharges,bool areTermsValid)=BuildSchedule(payPlan,fam,provNum,pat.ClinicNum,dto);
			if(!areTermsValid) {
				//Same condition FormPayPlan.AreTermsValid warns on: interest per period exceeds the payment.
				PayPlans.Delete(payPlan);
				return Results.Text("This payment plan will never be paid off. The interest charged on each payment is greater than "
					+"the payment amount. Choose a lower interest rate or a higher payment amount.",statusCode:400);
			}
			PayPlans.Update(payPlan);//Persist the terms set on the plan by BuildSchedule.
			PayPlanCharges.Sync(listCharges,payPlan.PayPlanNum);
			SecurityLogs.MakeLogEntry(EnumPermType.PayPlanEdit,patNum,"Patient Payment Plan created from the web payment plan editor.",
				payPlan.PayPlanNum,DateTime.MinValue);
			if(listCharges.Count>0) {
				SecurityLogs.MakeLogEntry(EnumPermType.PayPlanChargeEdit,patNum,"Payment plan charges created from the web payment plan editor.");
			}
			Signalods.SetInvalid(InvalidType.BillingList);
			Signalods.SetInvalid(InvalidType.AccModule,KeyType.PatNum,patNum);
			return Results.Json(new { payPlanNum=payPlan.PayPlanNum,chargeCount=listCharges.Count });
		});

		//Edit terms and re-amortize (FormPayPlan's Recalculate/Save on an unpaid plan): refuse if the
		//plan is closed or any payments are attached (the desktop blocks clearing the schedule then),
		//otherwise delete the existing charges and regenerate the whole schedule from the new terms.
		app.MapPut("/api/payplans/{payPlanNum:long}",(long payPlanNum,NewPayPlanDto dto) => {
			PayPlan payPlan=PayPlans.GetOne(payPlanNum);
			if(payPlan==null) {
				return Results.NotFound();
			}
			if(payPlan.PlanNum!=0 || payPlan.IsDynamic) {
				return Results.Text("Only standard patient payment plans can be edited here.",statusCode:409);
			}
			if(payPlan.IsClosed) {
				return Results.Text("Closed payment plans cannot be edited.",statusCode:409);
			}
			if(PayPlans.GetAmtPaid(payPlan)!=0) {
				return Results.Text("Cannot change the terms of a payment plan with payments attached.  Unattach the payments first.",statusCode:409);
			}
			string err=ValidateTerms(dto);
			if(err!="") {
				return Results.Text(err,statusCode:400);
			}
			Family fam=Patients.GetFamily(payPlan.PatNum);
			Patient pat=Patients.GetPat(payPlan.PatNum);
			long provNum=pat.PriProv!=0?pat.PriProv:(Providers.GetFirst()?.ProvNum??0);
			payPlan.Guarantor=dto.Guarantor>0?dto.Guarantor:payPlan.Guarantor;
			payPlan.PayPlanDate=ParseDate(dto.PlanDate);
			payPlan.CompletedAmt=dto.TotalAmount;
			payPlan.Note=dto.Note??"";
			(List<PayPlanCharge> listCharges,bool areTermsValid)=BuildSchedule(payPlan,fam,provNum,pat.ClinicNum,dto);
			if(!areTermsValid) {
				return Results.Text("This payment plan will never be paid off. The interest charged on each payment is greater than "
					+"the payment amount. Choose a lower interest rate or a higher payment amount.",statusCode:400);
			}
			PayPlans.Update(payPlan);
			//Sync against a fresh schedule: the existing (unpaid) charges are not in the new list, so
			//Sync deletes them and inserts the regenerated rows.
			PayPlanCharges.Sync(listCharges,payPlan.PayPlanNum);
			SecurityLogs.MakeLogEntry(EnumPermType.PayPlanEdit,payPlan.PatNum,"Payment plan terms edited from the web payment plan editor.",
				payPlan.PayPlanNum,DateTime.MinValue);
			SecurityLogs.MakeLogEntry(EnumPermType.PayPlanChargeEdit,payPlan.PatNum,"Payment plan schedule recalculated from the web payment plan editor.");
			Signalods.SetInvalid(InvalidType.BillingList);
			Signalods.SetInvalid(InvalidType.AccModule,KeyType.PatNum,payPlan.PatNum);
			return Results.Json(new { payPlanNum=payPlan.PayPlanNum,chargeCount=listCharges.Count });
		});

		//Delete a payment plan (FormPayPlan.butDelete): PayPlans.Delete removes the plan and all its
		//charges, but throws when patient/insurance payments are attached — surfaced as 409 with the
		//business layer's own message.
		app.MapDelete("/api/payplans/{payPlanNum:long}",(long payPlanNum) => {
			PayPlan payPlan=PayPlans.GetOne(payPlanNum);
			if(payPlan==null) {
				return Results.NotFound();
			}
			try {
				PayPlans.Delete(payPlan);
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.PayPlanEdit,payPlan.PatNum,
				(payPlan.PlanNum==0?"Patient":"Insurance")+" Payment Plan deleted from the web payment plan editor.",
				payPlan.PayPlanNum,DateTime.MinValue);
			Signalods.SetInvalid(InvalidType.BillingList);
			Signalods.SetInvalid(InvalidType.AccModule,KeyType.PatNum,payPlan.PatNum);
			return Results.Ok();
		});

		app.MapGet("/module/payplans",() => Results.Content(PAGE,"text/html"));
	}

	//Shared validation for create/edit, mirroring FormPayPlan.HasErrors' numeric checks.
	static string ValidateTerms(NewPayPlanDto dto) {
		if(dto.TotalAmount<=0) {
			return "Total amount must be greater than zero.";
		}
		if(dto.DownPayment<0 || dto.DownPayment>dto.TotalAmount) {
			return "Down payment must be between zero and the total amount.";
		}
		if(dto.Apr<0) {
			return "APR cannot be negative.";
		}
		if(dto.Term<=0 && dto.PayAmt<=0) {
			return "Enter either a number of payments (term) or a payment amount.";
		}
		return "";
	}

	//Builds the amortization schedule with the real business logic and stamps the derived terms
	//(PaySchedule, NumberOfPayments, PayAmt, DownPayment, APR, DateInterestStart) onto the plan the
	//way FormPayPlan.GetTermsFromUI does.  Monthly frequency, matching a standard patient plan.
	static (List<PayPlanCharge>,bool) BuildSchedule(PayPlan payPlan,Family fam,long provNum,long clinicNum,NewPayPlanDto dto) {
		DateTime dateFirst=ParseDate(dto.PlanDate);
		PayPlanTerms terms=new PayPlanTerms();
		terms.APR=dto.Apr;
		terms.DateFirstPayment=dateFirst;
		terms.DateInterestStart=dateFirst;
		terms.Frequency=PayPlanFrequency.Monthly;
		terms.PayCount=dto.Term>0?dto.Term:0;
		terms.PrincipalAmount=dto.TotalAmount;
		terms.RoundDec=2;
		terms.DateAgreement=dateFirst;
		terms.DownPayment=dto.DownPayment;
		terms.PaySchedule=PayPlanEdit.GetPayScheduleFromFrequency(terms.Frequency);
		terms.PeriodPayment=PayPlanEdit.CalculatePeriodPayment(terms.APR,terms.Frequency,(decimal)dto.PayAmt,terms.PayCount,
			terms.RoundDec,terms.PrincipalAmount,terms.DownPayment);
		//Stamp the archived terms onto the plan, same fields FormPayPlan sets in GetTermsFromUI.
		payPlan.APR=terms.APR;
		payPlan.DownPayment=terms.DownPayment;
		payPlan.PaySchedule=terms.PaySchedule;
		payPlan.NumberOfPayments=terms.PayCount;
		payPlan.PayAmt=terms.PayCount==0?(double)terms.PeriodPayment:0;
		payPlan.DateInterestStart=terms.DateInterestStart;
		List<PayPlanCharge> listCharges=new List<PayPlanCharge>();
		PayPlanEdit.CreateScheduleCharges(terms,payPlan,fam,provNum,clinicNum,listCharges);
		return (listCharges,terms.AreTermsValid);
	}

	static DateTime ParseDate(string s) {
		return DateTime.TryParse(s,out DateTime d)?d:DateTime.Today;
	}

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Open Dental — Payment Plans</title>
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
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}
select{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
tr.sel{cursor:pointer}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
.due{color:color-mix(in srgb,red 70%,CanvasText)}
</style></head><body>
<header><h1>Billing — Payment Plans</h1>
<p>Per-patient payment plans &middot; real PayPlans business layer &middot; <a href="/">back to workstation</a></p></header>
<main>
<section><h2>Patient</h2><select id=pppat></select></section>
<section><h2>Payment plans</h2>
<table id=plans><thead><tr><th>Date</th><th>Guarantor</th><th>Type</th>
<th class=num>Total</th><th class=num>Paid</th><th class=num>Due Now</th><th class=num>Balance</th></tr></thead><tbody></tbody></table></section>
<section><h2 id=dtitle>Charge schedule</h2>
<table id=charges><thead><tr><th>Date</th><th>Prov</th><th>Type</th>
<th class=num>Principal</th><th class=num>Interest</th><th>Note</th></tr></thead><tbody></tbody></table></section>
</main>
<script>
const usd=n=>(n<0?'-$':'$')+Math.abs(n).toFixed(2);
const esc=s=>String(s??'').replace(/</g,'&lt;');
async function loadPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('pppat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${esc(p.name)}</option>`).join('');
 loadPlans();
}
async function loadPlans(){
 const patNum=document.getElementById('pppat').value;if(!patNum)return;
 const plans=await (await fetch(`/api/patients/${patNum}/payplans`)).json();
 document.querySelector('#plans tbody').innerHTML=plans.map(p=>
  `<tr class=sel data-num=${p.payPlanNum}><td>${p.date}</td><td>${esc(p.guarantor)}</td>
   <td><span class=tag>${p.isInsurance?'Insurance':p.isDynamic?'Dynamic':'Patient'}${p.isClosed?' — closed':''}</span></td>
   <td class=num>${usd(p.total)}</td><td class=num>${usd(p.amtPaid)}</td>
   <td class="num${p.dueNow>0?' due':''}">${usd(p.dueNow)}</td><td class=num>${usd(p.balance)}</td></tr>`).join('')
  ||'<tr><td colspan=7 style="opacity:.6">No payment plans for this patient.</td></tr>';
 document.querySelectorAll('#plans tr.sel').forEach(tr=>tr.addEventListener('click',()=>loadCharges(tr.dataset.num)));
 document.querySelector('#charges tbody').innerHTML='';
 document.getElementById('dtitle').textContent='Charge schedule';
}
async function loadCharges(payPlanNum){
 const plan=await (await fetch(`/api/payplans/${payPlanNum}`)).json();
 document.getElementById('dtitle').textContent=
  `Charge schedule — plan ${plan.date} (${esc(plan.guarantor)}) — paid ${usd(plan.amtPaid)}, balance ${usd(plan.balance)}`;
 document.querySelector('#charges tbody').innerHTML=plan.charges.map(c=>
  `<tr><td${c.isDue?' class=due':''}>${c.chargeDate}</td><td>${esc(c.provider)}</td><td>${c.chargeType}</td>
   <td class=num>${usd(c.principal)}</td><td class=num>${usd(c.interest)}</td><td>${esc(c.note)}</td></tr>`).join('')
  ||'<tr><td colspan=6 style="opacity:.6">No charges on this plan.</td></tr>';
}
document.getElementById('pppat').addEventListener('change',loadPlans);
loadPats();
</script></body></html>
""";
}
