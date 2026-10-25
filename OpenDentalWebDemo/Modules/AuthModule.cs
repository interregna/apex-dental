// Modified for the Apex Dental public source snapshot, 2026-10-24; see NOTICE.
/*
 * Server-side authentication + role authorization for the web app.
 *
 * Login validates against the real Open Dental user store (Userods.CheckUserAndPassword
 * -> Authentication.CheckPassword, the same SHA3/hash path the desktop uses). A session
 * cookie maps to a UserNum; middleware resolves it per request, sets Security.CurUser,
 * requires a session for every /api/* call, and enforces role -> module authorization so
 * the six persona workspaces are enforced by the server, not just hidden in the UI.
 *
 * The user's ROLE is derived from their real OD user-group membership (UserGroups.GetForUser);
 * the group->role map below mirrors how a practice names its security groups. Admins map to
 * "full". /api/auth/seed-demo creates the six persona groups+users through the business layer.
 *
 * Demo caveat: OD's Security.CurUser is a process-global static, so this per-request assignment
 * is not safe under heavy concurrency (fine for single-practice demo use). A production port
 * would flow the user via AsyncLocal or refactor the Security surface. Cookie is HttpOnly but
 * not Secure until the demo runs behind HTTPS.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules {

	public static class AuthModule {
		record LoginDto(string UserName,string Password);

		//token -> UserNum
		static readonly ConcurrentDictionary<string,long> _sessions=new ConcurrentDictionary<string,long>();

		//Real OD user-group Description -> web role. Practices name their groups; these are the demo's.
		static readonly Dictionary<string,string> RoleByGroup=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
			{"Front Office","frontdesk"},{"Dentists","dentist"},{"Hygienists","hygienist"},
			{"Assistants","assistant"},{"Billing","billing"},{"Managers","manager"},
			{"Admin Group","full"},
		};

		//Mirror of the client ROLES map (app.js) so the server is authoritative for scoping.
		static readonly Dictionary<string,(string label,string landing,string[] core,string[] mods)> Roles=
			new Dictionary<string,(string,string,string[],string[])>{
			{"full",("Full access","appts",new[]{"appts","family","account","chart","images","reports","sign"},new string[0])},
			{"frontdesk",("Front Desk","appts",new[]{"appts","family","account","sign"},new[]{"schedweek","sched-appt-edit","sched-appt-booking","sched-confirm-list","sched-planned-unsched","sched-recall-asap","sched-slot-search","sched-blockouts","comm","misc-sheets","comm-letters","patfam-referrals","patfam-notes","billing-payment-entry","billing-statements"})},
			{"dentist",("Dentist","chart",new[]{"chart","appts","images"},new[]{"clinical-perio-entry","clinical-proc-entry","treatplan","clinical-rx-entry","clinical-ortho-chart","chart-documents","clinical-lab-cases","patfam-medical","patfam-notes","comm-letters","sched-appt-edit","sched-confirm-list"})},
			{"hygienist",("Hygienist","chart",new[]{"chart","appts"},new[]{"clinical-perio-entry","clinical-proc-entry","chart-documents","patfam-medical","patfam-notes","sched-recall-asap","sched-confirm-list"})},
			{"assistant",("Dental Assistant","chart",new[]{"chart","images","appts"},new[]{"clinical-proc-entry","clinical-lab-cases","chart-documents","clinical-ortho-chart","misc-sheets","patfam-medical","sched-appt-edit","sched-appt-booking"})},
			{"billing",("Billing / Insurance","account",new[]{"account","family"},new[]{"billing-payment-entry","billing-adjustments","billing-claim-builder","billing-payplans","billing-statements","billing-statement-run","billing-deposits","billing-aging-dashboard","ins-coverage-panel","ins-carrier-manager","ins-plan-subscriber-editor","ins-claim-receive","ins-verification","comm"})},
			{"manager",("Office Manager","reports",new[]{"reports","appts","account"},new[]{"comm-tasks-browser","setup-practice-editor","setup-security-view","setup-feescheds","setup-fee-tools","setup-preferences","setup-clinics","setup-employees-timeclock","setup-appttypes-autocodes","billing-statements","billing-statement-run","billing-aging-dashboard","reports-daysheet","ins-verification"})},
		};

		//Path -> the module that owns it, for authorization. Only role-sensitive prefixes are gated;
		//core/shared endpoints (patients, family, account, appointments, reports, toothchart, docs,
		//scan, sheets, ui) are available to any authenticated user. Ordered; first match wins.
		static readonly (Regex rx,string mod)[] Gates=new[]{
			(new Regex(@"^/api/deposits"),"billing-deposits"),
			(new Regex(@"^/api/(payments|payment-types)|^/api/patients/\d+/payments"),"billing-payment-entry"),
			(new Regex(@"^/api/(adjustments|adjustment-types)|^/api/patients/\d+/adjustments"),"billing-adjustments"),
			(new Regex(@"^/api/patients/\d+/unbilled-procs"),"billing-claim-builder"),
			(new Regex(@"^/api/payplans|^/api/patients/\d+/payplans"),"billing-payplans"),
			(new Regex(@"^/api/statements|^/api/patients/\d+/statements"),"billing-statements"),
			(new Regex(@"^/api/insplans|^/api/inssubs|^/api/patients/\d+/(insurance|patplans)|^/api/insurance/(covcats|feescheds)"),"ins-plan-subscriber-editor"),
			(new Regex(@"^/api/carriers"),"ins-carrier-manager"),
			(new Regex(@"^/api/claimpayments|^/api/claims/\d+/receive"),"ins-claim-receive"),
			(new Regex(@"^/api/insverify"),"ins-verification"),
			(new Regex(@"^/api/(perio|patients/\d+/perio)"),"clinical-perio-entry"),
			(new Regex(@"^/api/(ortho|patients/\d+/orthochart|orthochart)"),"clinical-ortho-chart"),
			(new Regex(@"^/api/(laboratories|labcases|patients/\d+/labcases)"),"clinical-lab-cases"),
			(new Regex(@"^/api/(rx|rxdefs|patients/\d+/rx)"),"clinical-rx-entry"),
			(new Regex(@"^/api/(treatplans|patients/\d+/treatplans)"),"treatplan"),
			(new Regex(@"^/api/provider-schedules"),"sched-provider-schedule"),
			(new Regex(@"^/api/slotsearch"),"sched-slot-search"),
			(new Regex(@"^/api/confirmlist|^/api/appointments/\d+/confirm"),"sched-confirm-list"),
			(new Regex(@"^/api/blockouts"),"sched-blockouts"),
			(new Regex(@"^/api/(planned|unsched|patients/\d+/unsched)"),"sched-planned-unsched"),
			(new Regex(@"^/api/setup/security"),"setup-security-view"),
			(new Regex(@"^/api/setup/employees|^/api/setup/clockevents"),"setup-employees-timeclock"),
			(new Regex(@"^/api/setup/(appttypes|autocodes)"),"setup-appttypes-autocodes"),
			(new Regex(@"^/api/setup/(defs|defcats|operatories|providers)"),"setup-practice-editor"),
			(new Regex(@"^/api/prefs"),"setup-preferences"),
			(new Regex(@"^/api/clinics"),"setup-clinics"),
			(new Regex(@"^/api/feescheds"),"setup-feescheds"),
			(new Regex(@"^/api/tasks"),"comm-tasks-browser"),
			(new Regex(@"^/api/billing/"),"billing-statement-run"),
			(new Regex(@"^/api/ar/"),"billing-aging-dashboard"),
			(new Regex(@"^/api/(letter-types|patients/\d+/letters)"),"comm-letters"),
			(new Regex(@"^/api/feetools/"),"setup-fee-tools"),
			(new Regex(@"^/api/daysheet"),"reports-daysheet"),
		};

		//---- TRUE per-user permissions (Open Dental GroupPermission / EnumPermType) ----
		//Each endpoint maps to the OD permission it requires: the module-level permission for
		//reads (GET) and the specific action permission for writes (POST/PUT/DELETE). Enforced
		//per request against the logged-in user's real group permissions via
		//GroupPermissions.HasPermission — so a user with view-but-not-edit rights is stopped at
		//the write, exactly as the desktop enforces it. Ordered; first path match wins.
		static readonly (Regex rx,EnumPermType read,EnumPermType write)[] PermRules=new[]{
			//scheduling
			(new Regex(@"^/api/appointments/\d+/confirm"),EnumPermType.AppointmentsModule,EnumPermType.ApptConfirmStatusEdit),
			(new Regex(@"^/api/confirmlist"),EnumPermType.AppointmentsModule,EnumPermType.ApptConfirmStatusEdit),
			(new Regex(@"^/api/blockouts"),EnumPermType.AppointmentsModule,EnumPermType.Blockouts),
			(new Regex(@"^/api/provider-schedules"),EnumPermType.AppointmentsModule,EnumPermType.Schedules),
			(new Regex(@"^/api/(schedule|schedweek|slotsearch)"),EnumPermType.AppointmentsModule,EnumPermType.AppointmentsModule),
			(new Regex(@"^/api/(planned|unsched|asap)|^/api/patients/\d+/unsched"),EnumPermType.AppointmentsModule,EnumPermType.AppointmentEdit),
			(new Regex(@"^/api/patients/\d+/appointments"),EnumPermType.AppointmentsModule,EnumPermType.AppointmentCreate),
			(new Regex(@"^/api/appointments"),EnumPermType.AppointmentsModule,EnumPermType.AppointmentEdit),
			//recalls
			(new Regex(@"^/api/(recall-types|recalls)|^/api/patients/\d+/recalls"),EnumPermType.AppointmentsModule,EnumPermType.RecallEdit),
			//billing / account
			(new Regex(@"^/api/deposits"),EnumPermType.AccountModule,EnumPermType.DepositSlips),
			(new Regex(@"^/api/(payments|payment-types)|^/api/patients/\d+/payments|^/api/account/\d+/payment"),EnumPermType.AccountModule,EnumPermType.PaymentCreate),
			(new Regex(@"^/api/(adjustments|adjustment-types)|^/api/patients/\d+/adjustments"),EnumPermType.AccountModule,EnumPermType.AdjustmentCreate),
			(new Regex(@"^/api/payplans|^/api/patients/\d+/payplans"),EnumPermType.AccountModule,EnumPermType.PayPlanEdit),
			(new Regex(@"^/api/(statements|billing/)|^/api/patients/\d+/statements"),EnumPermType.AccountModule,EnumPermType.Billing),
			(new Regex(@"^/api/ar/"),EnumPermType.AccountModule,EnumPermType.AccountModule),
			(new Regex(@"^/api/account"),EnumPermType.AccountModule,EnumPermType.PaymentCreate),
			//insurance
			(new Regex(@"^/api/insverify"),EnumPermType.AccountModule,EnumPermType.InsuranceVerification),
			(new Regex(@"^/api/claimpayments|^/api/claims/\d+/receive"),EnumPermType.AccountModule,EnumPermType.InsPayCreate),
			(new Regex(@"^/api/(carriers)"),EnumPermType.AccountModule,EnumPermType.CarrierEdit),
			(new Regex(@"^/api/(insplans|inssubs|claims)|^/api/patients/\d+/(insurance|patplans|claims|unbilled-procs)|^/api/insurance/"),EnumPermType.AccountModule,EnumPermType.InsPlanEdit),
			//chart / clinical
			(new Regex(@"^/api/(perio|patients/\d+/perio)"),EnumPermType.ChartModule,EnumPermType.PerioEdit),
			(new Regex(@"^/api/(ortho|orthochart|patients/\d+/orthochart)"),EnumPermType.ChartModule,EnumPermType.OrthoChartEditFull),
			(new Regex(@"^/api/(rx|rxdefs|patients/\d+/rx)"),EnumPermType.ChartModule,EnumPermType.RxCreate),
			(new Regex(@"^/api/(treatplan|treatplans)|^/api/patients/\d+/treatplans"),EnumPermType.TPModule,EnumPermType.TreatPlanEdit),
			(new Regex(@"^/api/(procs|proccodes)|^/api/patients/\d+/procs"),EnumPermType.ChartModule,EnumPermType.ProcComplEdit),
			(new Regex(@"^/api/(medications|allergies|problems|medhist)|^/api/patients/\d+/(medical|medications|allergies|problems)"),EnumPermType.ChartModule,EnumPermType.ProcComplEdit),
			(new Regex(@"^/api/patients/\d+/(toothchart|perio)"),EnumPermType.ChartModule,EnumPermType.ChartModule),
			(new Regex(@"^/api/dictation"),EnumPermType.ChartModule,EnumPermType.ChartModule),//voice-to-note (clinical roles)
			//imaging / documents / sheets
			(new Regex(@"^/api/(documents|image-categories|scan)|^/api/patients/\d+/(documents|documents-tree)"),EnumPermType.ImagingModule,EnumPermType.ImageEdit),
			(new Regex(@"^/api/(sheets|sheetdefs|letter-types)|^/api/patients/\d+/(sheets|letters|consent)"),EnumPermType.ChartModule,EnumPermType.SheetEdit),
			//family / patient
			(new Regex(@"^/api/family"),EnumPermType.FamilyModule,EnumPermType.PatientEdit),
			(new Regex(@"^/api/patients/\d+/(details|notes|referrals|commlog)"),EnumPermType.FamilyModule,EnumPermType.PatientEdit),
			(new Regex(@"^/api/(referrals|refattaches)"),EnumPermType.FamilyModule,EnumPermType.ReferralEdit),
			//setup / manage
			(new Regex(@"^/api/setup/security"),EnumPermType.ManageModule,EnumPermType.SecurityAdmin),
			(new Regex(@"^/api/(feescheds|feetools)"),EnumPermType.ManageModule,EnumPermType.FeeSchedEdit),
			(new Regex(@"^/api/clinics"),EnumPermType.ManageModule,EnumPermType.ClinicEdit),
			(new Regex(@"^/api/setup"),EnumPermType.ManageModule,EnumPermType.Setup),
			(new Regex(@"^/api/prefs"),EnumPermType.ManageModule,EnumPermType.Setup),
			(new Regex(@"^/api/tasks"),EnumPermType.ManageModule,EnumPermType.TaskEdit),
			//reports
			(new Regex(@"^/api/daysheet"),EnumPermType.Reports,EnumPermType.ReportDaily),
			(new Regex(@"^/api/reports"),EnumPermType.Reports,EnumPermType.Reports),
		};

		//Realistic permission grant per persona user-group. Seeded through GroupPermissions.Insert
		//so enforcement runs against genuine OD data (not a hard-coded list). Admin Group gets
		//SecurityAdmin, which this middleware treats as a super-permission (allow all).
		static readonly Dictionary<string,EnumPermType[]> PermSeeds=new Dictionary<string,EnumPermType[]>{
			{"Admin Group",new[]{EnumPermType.SecurityAdmin}},
			{"Front Office",new[]{EnumPermType.AppointmentsModule,EnumPermType.FamilyModule,EnumPermType.AccountModule,
				EnumPermType.AppointmentCreate,EnumPermType.AppointmentEdit,EnumPermType.AppointmentMove,EnumPermType.Schedules,
				EnumPermType.Blockouts,EnumPermType.ApptConfirmStatusEdit,EnumPermType.RecallEdit,EnumPermType.PatientCreate,
				EnumPermType.PatientEdit,EnumPermType.PaymentCreate,EnumPermType.Billing,EnumPermType.SheetEdit,EnumPermType.ReferralEdit}},
			{"Dentists",new[]{EnumPermType.ChartModule,EnumPermType.TPModule,EnumPermType.ImagingModule,EnumPermType.AppointmentsModule,
				EnumPermType.FamilyModule,EnumPermType.PerioEdit,EnumPermType.ProcComplCreate,EnumPermType.ProcComplEdit,
				EnumPermType.TreatPlanEdit,EnumPermType.RxCreate,EnumPermType.RxEdit,EnumPermType.OrthoChartEditFull,
				EnumPermType.ImageEdit,EnumPermType.SheetEdit,EnumPermType.AppointmentEdit,EnumPermType.ApptConfirmStatusEdit,EnumPermType.PatientEdit}},
			{"Hygienists",new[]{EnumPermType.ChartModule,EnumPermType.AppointmentsModule,EnumPermType.PerioEdit,
				EnumPermType.ProcComplCreate,EnumPermType.ProcComplEdit,EnumPermType.ImageEdit,EnumPermType.RecallEdit,EnumPermType.ApptConfirmStatusEdit}},
			{"Assistants",new[]{EnumPermType.ChartModule,EnumPermType.ImagingModule,EnumPermType.AppointmentsModule,
				EnumPermType.ProcComplCreate,EnumPermType.ProcComplEdit,EnumPermType.ImageEdit,EnumPermType.OrthoChartEditFull,
				EnumPermType.SheetEdit,EnumPermType.AppointmentEdit}},
			{"Billing",new[]{EnumPermType.AccountModule,EnumPermType.FamilyModule,EnumPermType.PaymentCreate,EnumPermType.PaymentEdit,
				EnumPermType.AdjustmentCreate,EnumPermType.AdjustmentEdit,EnumPermType.Billing,EnumPermType.DepositSlips,
				EnumPermType.InsPayCreate,EnumPermType.InsPayEdit,EnumPermType.ClaimEdit,EnumPermType.InsPlanEdit,EnumPermType.InsPlanCreate,
				EnumPermType.CarrierEdit,EnumPermType.CarrierCreate,EnumPermType.InsuranceVerification,EnumPermType.PayPlanEdit}},
			{"Managers",new[]{EnumPermType.ManageModule,EnumPermType.AccountModule,EnumPermType.AppointmentsModule,EnumPermType.Setup,
				EnumPermType.Reports,EnumPermType.ReportDaily,EnumPermType.FeeSchedEdit,EnumPermType.ClinicEdit,EnumPermType.DefEdit,
				EnumPermType.ProviderEdit,EnumPermType.TaskEdit,EnumPermType.Billing,EnumPermType.InsuranceVerification,EnumPermType.PatientEdit}},
		};

		static bool IsWrite(string method) => method=="POST" || method=="PUT" || method=="DELETE" || method=="PATCH";

		///<summary>The permission a request needs, or null if the endpoint isn't permission-gated.</summary>
		static EnumPermType? RequiredPerm(string path,string method){
			foreach(var r in PermRules){
				if(r.rx.IsMatch(path)){
					return IsWrite(method)?r.write:r.read;
				}
			}
			return null;
		}

		static bool UserHasPerm(Userod user,EnumPermType perm){
			if(user==null){ return false; }
			//SecurityAdmin acts as a super-permission for this demo (Admin Group).
			if(GroupPermissions.HasPermission(user,EnumPermType.SecurityAdmin,0)){ return true; }
			return GroupPermissions.HasPermission(user,perm,0);
		}

		static Userod UserFor(long userNum) => Userods.GetUser(userNum)??Userods.GetUserNoCache(userNum);

		static string ResolveRole(long userNum){
			List<UserGroup> groups=UserGroups.GetForUser(userNum,false);
			foreach(UserGroup g in groups){
				if(g.Description!=null && RoleByGroup.TryGetValue(g.Description,out string r)){
					return r;
				}
			}
			if(UserGroups.IsAdminGroup(groups.Select(x => x.UserGroupNum).ToList())){
				return "full";
			}
			return "full";//no mapped group -> full (a real deployment would default to least privilege)
		}

		static bool RoleAllows(string role,string mod){
			if(!Roles.TryGetValue(role,out var r)){
				return true;
			}
			if(role=="full"){
				return true;
			}
			return r.core.Contains(mod) || r.mods.Contains(mod);
		}

		static object SessionInfo(long userNum){
			Userod u=UserFor(userNum);
			string role=ResolveRole(userNum);
			var r=Roles[role];
			List<long> groupNums=UserGroups.GetForUser(userNum,false).Select(x => x.UserGroupNum).ToList();
			bool isAdmin=UserGroups.IsAdminGroup(groupNums);
			//The user's real granted permissions (EnumPermType names) so the client can gate
			//view-vs-edit at the button level, matching what the server will enforce.
			List<string> perms=GroupPermissions.GetForUserGroups(groupNums)
				.Select(x => x.PermType.ToString()).Distinct().OrderBy(x => x).ToList();
			return new {
				authed=true,userNum,userName=u?.UserName??"",role,label=r.label,landing=r.landing,
				core=r.core,mods=r.mods,isAdmin=isAdmin||role=="full",permissions=perms,
			};
		}

		public static void Map(WebApplication app){
			app.MapPost("/api/auth/login",(HttpContext ctx,LoginDto dto) => {
				Userod user;
				try {
					user=Userods.CheckUserAndPassword(dto?.UserName??"",dto?.Password??"",false,false);
				}
				catch {
					user=null;//locked account etc. surface as invalid
				}
				if(user==null){
					return Results.Json(new { error="Invalid username or password." },statusCode:401);
				}
				Security.CurUser=user;
				string token=Guid.NewGuid().ToString("N");
				_sessions[token]=user.UserNum;
				ctx.Response.Cookies.Append("odsess",token,new CookieOptions{
					HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromHours(24),
				});
				SecurityLogs.MakeLogEntry(EnumPermType.UserLogOnOff,0,"Web login: "+user.UserName);
				return Results.Json(SessionInfo(user.UserNum));
			});

			app.MapPost("/api/auth/logout",(HttpContext ctx) => {
				if(ctx.Request.Cookies.TryGetValue("odsess",out string tok)){
					_sessions.TryRemove(tok,out _);
				}
				ctx.Response.Cookies.Delete("odsess");
				return Results.Json(new { authed=false });
			});

			app.MapGet("/api/auth/me",(HttpContext ctx) => {
				if(ctx.Request.Cookies.TryGetValue("odsess",out string tok) && _sessions.TryGetValue(tok,out long userNum)){
					return Results.Json(SessionInfo(userNum));
				}
				return Results.Json(new { authed=false });
			});

			//Optionally create demonstration personas through
			//the real business layer. Restricted to an authenticated Admin Group session.
			app.MapPost("/api/admin/seed-demo",(HttpContext ctx) => {
                if(Environment.GetEnvironmentVariable("OD_ENABLE_DEMO_SEED")!="true") {
                    return Results.NotFound();
                }
                string demoPassword=Environment.GetEnvironmentVariable("OD_DEMO_PASSWORD");
                if(string.IsNullOrWhiteSpace(demoPassword) || demoPassword.Length<12) {
                    return Results.Problem("OD_DEMO_PASSWORD must contain at least 12 characters.");
                }
				if(!ctx.Request.Cookies.TryGetValue("odsess",out string sessionToken)
					|| !_sessions.TryGetValue(sessionToken,out long sessionUserNum)){
					return Results.StatusCode(403);
				}
				Userod sessionUser=UserFor(sessionUserNum);
				List<long> sessionGroups=UserGroups.GetForUser(sessionUserNum,false).Select(x => x.UserGroupNum).ToList();
				if(sessionUser==null || !UserGroups.IsAdminGroup(sessionGroups)){
					return Results.StatusCode(403);
				}
				Userods.RefreshCache();UserGroups.RefreshCache();
				var wanted=new (string grp,string user)[]{
					("Front Office","frontdesk"),("Dentists","dentist"),("Hygienists","hygienist"),
					("Assistants","assistant"),("Billing","billing"),("Managers","manager"),
				};
				var created=new List<string>();
				foreach(var w in wanted){
					UserGroup group=UserGroups.GetList(false).FirstOrDefault(x => x.Description==w.grp);
					if(group==null){
						group=new UserGroup{ Description=w.grp };
						group.UserGroupNum=UserGroups.Insert(group);
						UserGroups.RefreshCache();
					}
					Userod existing=Userods.GetUserByNameNoCache(w.user);
					if(existing==null){
						//Set the password container on the object BEFORE Insert. UpdatePassword()
						//validates via userod.GetGroups() off a cache that is stale immediately after
						//Insert, so the post-insert path throws "must be in at least one user group".
						Userod u=new Userod{ UserName=w.user,PasswordIsStrong=false };
						u.SetPassword(OpenDentBusiness.Authentication.GenerateLoginDetails(demoPassword,HashTypes.SHA3_512));
						Userods.Insert(u,new List<long>{ group.UserGroupNum });
						created.Add(w.user);
					}
				}
				//Grant each group its realistic EnumPermType set through the real GroupPermission
				//table so per-user enforcement runs on genuine OD data. Idempotent.
				UserGroups.RefreshCache();
				int grantsAdded=0;
				foreach(var kv in PermSeeds){
					UserGroup group=UserGroups.GetList(false).FirstOrDefault(x => x.Description==kv.Key);
					if(group==null){ continue; }
					List<GroupPermission> existingPerms=GroupPermissions.GetForUserGroups(new List<long>{ group.UserGroupNum });
					foreach(EnumPermType perm in kv.Value){
						if(existingPerms.Any(x => x.PermType==perm)){ continue; }
						GroupPermissions.Insert(new GroupPermission{ UserGroupNum=group.UserGroupNum,PermType=perm });
						grantsAdded++;
					}
				}
				//Refresh the whole security cache (Userods + UserGroups + UserGroupAttaches +
				//GroupPermissions) so role + permission resolution sees the new rows immediately.
				Cache.Refresh(InvalidType.Security);
				return Results.Json(new { created,grantsAdded,
					logins=wanted.Select(x => new { user=x.user,group=x.grp }) });
			});
		}

		///<summary>Pipeline middleware: resolve the session, set Security.CurUser, require auth on
		////api/* (except /api/auth/*), and enforce role->module authorization. Call from Program.cs
		///after UseStaticFiles so static assets (the SPA + login screen) load unauthenticated.</summary>
		public static void UseAuth(WebApplication app){
			app.Use(async (ctx,next) => {
				string path=ctx.Request.Path.Value??"";
				long userNum=0;
				if(ctx.Request.Cookies.TryGetValue("odsess",out string tok) && _sessions.TryGetValue(tok,out long un)){
					userNum=un;
					Userod u=UserFor(userNum);
					if(u!=null){
						Security.CurUser=u;//per-request user context (see class-header caveat)
					}
				}
				//Non-API and the auth endpoints themselves are always reachable.
				if(!path.StartsWith("/api/") || path.StartsWith("/api/auth/")){
					await next();
					return;
				}
				if(userNum==0){
					ctx.Response.StatusCode=401;
					await ctx.Response.WriteAsync("Not authenticated");
					return;
				}
				//TRUE per-user authorization: enforce the Open Dental permission the endpoint
				//requires against the logged-in user's real GroupPermissions. Reads need the
				//module permission; writes need the specific action permission. This is the
				//server-side security; the role only drives the client's rail presentation.
				Userod user=UserFor(userNum);
				EnumPermType? perm=RequiredPerm(path,ctx.Request.Method);
				if(perm!=null && !UserHasPerm(user,perm.Value)){
					ctx.Response.StatusCode=403;
					await ctx.Response.WriteAsync($"Permission denied: this action requires the '{perm}' permission, which {user?.UserName} does not have.");
					return;
				}
				await next();
			});
		}
	}
}
