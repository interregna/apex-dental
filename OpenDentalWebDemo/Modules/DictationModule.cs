// Modified for the Apex Dental public source snapshot, 2026-10-24; see NOTICE.
/*
 * Dictation (voice-to-note) — STAGED / NON-PRODUCTION.
 *
 * Record clinical audio in the browser, transcribe (or translate to English) it with
 * the configured engine, then run a human-in-the-loop review:
 *   transcribe -> server-side DRAFT (never written to the chart) -> edit -> accept.
 * Accept is the only path that touches the record, and it goes through the real
 * business layer (Commlogs.Insert / PatientNotes.Update / Procedures.Update) with
 * the same audit trail the desktop stamps. Discard is explicit and audited.
 *
 * PHI handling notes (why this is "staged"):
 *  - TRANSCRIPTION_ENGINE=local (default): audio is POSTed to a loopback-only Whisper
 *    sidecar (127.0.0.1) that holds the model in memory and deletes each clip immediately.
 *  - TRANSCRIPTION_ENGINE=service: audio is sent to an external transcription API instead.
 *    Its data handling and agreements are the operator's responsibility, and the UI
 *    tells the clinician that audio leaves this host.
 *  - The DRAFT (transcript text) lives in server memory only, keyed to patient+user,
 *    so a page refresh recovers it but a server restart drops it. Production would
 *    persist drafts encrypted-at-rest and add TLS-at-origin + a formal risk review.
 *  - Every step is written to SecurityLogs.
 *
 * Route gating: /api/dictation is permission-gated in AuthModule.PermRules
 * (read = ChartModule, write = ChartModule) so only clinical roles can dictate.
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class DictationModule {
	//Exactly one engine is active, chosen explicitly by TRANSCRIPTION_ENGINE:
	//  local   (default) — the loopback Whisper sidecar; audio stays on this host.
	//  service — an external transcription API; audio leaves this host. Requires
	//            TRANSCRIPTION_SERVICE_URL and TRANSCRIPTION_SERVICE_TOKEN.
	//There is no fallback between them. Ambiguous configuration fails at startup.
	sealed record Engine(string Kind,string BaseUrl,string Token) {
		public bool IsExternal => Kind=="service";
		public string Host => new Uri(BaseUrl).Host;
	}
	//An explicit type initializer runs before Map registers routes, not on first use.
	static DictationModule() { }
	static readonly Engine ActiveEngine=LoadEngine();
	static Engine LoadEngine() {
		string kind=(Environment.GetEnvironmentVariable("TRANSCRIPTION_ENGINE")??"local").Trim().ToLowerInvariant();
		string serviceUrl=Environment.GetEnvironmentVariable("TRANSCRIPTION_SERVICE_URL");
		if(kind=="local") {
			if(!string.IsNullOrWhiteSpace(serviceUrl)) {
				throw new InvalidOperationException("TRANSCRIPTION_SERVICE_URL is set but TRANSCRIPTION_ENGINE is local. "
					+"Set TRANSCRIPTION_ENGINE=service to send audio to that service, or unset the URL.");
			}
			Uri local=ParseEndpoint(Environment.GetEnvironmentVariable("WHISPER_URL")??"http://127.0.0.1:8771","WHISPER_URL");
			if(!local.IsLoopback) {
				throw new InvalidOperationException("WHISPER_URL must be a loopback address; use TRANSCRIPTION_ENGINE=service for an external API.");
			}
			return new Engine("local",local.AbsoluteUri.TrimEnd('/'),null);
		}
		if(kind=="service") {
			if(string.IsNullOrWhiteSpace(serviceUrl)) {
				throw new InvalidOperationException("TRANSCRIPTION_ENGINE=service requires TRANSCRIPTION_SERVICE_URL.");
			}
			Uri service=ParseEndpoint(serviceUrl,"TRANSCRIPTION_SERVICE_URL",requireHttps:true);
			string token=Environment.GetEnvironmentVariable("TRANSCRIPTION_SERVICE_TOKEN");
			if(string.IsNullOrWhiteSpace(token)) {
				throw new InvalidOperationException("TRANSCRIPTION_ENGINE=service requires TRANSCRIPTION_SERVICE_TOKEN.");
			}
			return new Engine("service",service.AbsoluteUri.TrimEnd('/'),token);
		}
		throw new InvalidOperationException("TRANSCRIPTION_ENGINE must be \"local\" or \"service\".");
	}
	static Uri ParseEndpoint(string value,string name,bool requireHttps=false) {
		if(!Uri.TryCreate(value,UriKind.Absolute,out Uri uri)
			|| !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
			|| !string.IsNullOrEmpty(uri.Fragment)
			|| (uri.Scheme!="https" && (requireHttps || !(uri.Scheme=="http" && uri.IsLoopback)))) {
			throw new InvalidOperationException(name+(requireHttps?" must use HTTPS":" must use HTTPS or loopback HTTP")
				+", without credentials, query, or fragment.");
		}
		return uri;
	}
	static HttpRequestMessage ServiceRequest(HttpMethod method,string path) {
		var request=new HttpRequestMessage(method,ActiveEngine.BaseUrl+path);
		//Only the external service receives a credential; the local sidecar never does.
		if(ActiveEngine.IsExternal) {
			request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",ActiveEngine.Token);
		}
		return request;
	}
	static readonly HttpClient Http=new HttpClient(new HttpClientHandler{ AllowAutoRedirect=false }){ Timeout=TimeSpan.FromSeconds(180) };

	//In-memory draft store. Transcript text only — the audio is NOT retained here
	//(the sidecar discards each clip). Keyed by opaque draftId.
	class Draft {
		public string Id="";
		public long PatNum;
		public long UserNum;
		public string Target="commlog";
		public string Task="transcribe";
		public string Transcript="";   //verbatim STT output (kept for compare)
		public string Text="";         //editable proposal (starts == transcript)
		public string Language="";
		public double Duration;
		public DateTime CreatedAt;
		//For the "procedures" target: transcript parsed into structured, confirmable
		//charting entries, parsed locally for clinician review.
		public List<ProcEntry> Entries=new();
	}
	//One structured procedure proposed from the dictation. The clinician edits/confirms
	//these discrete fields before anything is written — that human step is the guarantee.
	class ProcEntry {
		public string ProcCode="";     //CDT code, e.g. D2393
		public string Descript="";     //resolved description for that code
		public string Tooth="";
		public string Surf="";
		public string Confidence="med"; //high | med | low
		public string Raw="";          //the phrase it came from, for reference
	}
	static readonly ConcurrentDictionary<string,Draft> Drafts=new();

	static long CurUser => Security.CurUser?.UserNum??0;

	static object Shape(Draft d) => new {
		draftId=d.Id,patNum=d.PatNum,target=d.Target,task=d.Task,
		transcript=d.Transcript,text=d.Text,language=d.Language,duration=d.Duration,
		createdAt=d.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
		entries=d.Entries.Select(e => new { procCode=e.ProcCode,descript=e.Descript,
			tooth=e.Tooth,surf=e.Surf,confidence=e.Confidence,raw=e.Raw }).ToList(),
	};

	public static void Map(WebApplication app) {

		//Which engine is active, and is it ready? Engine identity comes from local
		//configuration, never from the remote response, so the UI can always say
		//truthfully whether audio leaves this host.
		app.MapGet("/api/dictation/health",async () => {
			object Status(bool ok,string model,string error) => new {
				ok,engine=ActiveEngine.Kind,offHost=ActiveEngine.IsExternal,
				host=ActiveEngine.IsExternal?ActiveEngine.Host:null,model,error };
			try {
				using HttpRequestMessage healthRequest=ServiceRequest(HttpMethod.Get,"/health");
				using HttpResponseMessage r=await Http.SendAsync(healthRequest);
				if(!r.IsSuccessStatusCode) { return Results.Json(Status(false,null,"Transcription engine unavailable")); }
				using JsonDocument doc=JsonDocument.Parse(await r.Content.ReadAsStringAsync());
				JsonElement root=doc.RootElement;
				bool ok=root.TryGetProperty("ok",out var okEl) && okEl.ValueKind==JsonValueKind.True;
				string model=root.TryGetProperty("model",out var m) && m.ValueKind==JsonValueKind.String?m.GetString():null;
				return Results.Json(Status(ok,model,ok?null:"Transcription engine not ready"));
			}
			catch(Exception) {
				//Don't echo exception text: it can carry the service host or network details.
				return Results.Json(Status(false,null,"Transcription engine unavailable"));
			}
		});

		//Recover open drafts for this patient + user (so refresh/nav-away loses nothing).
		app.MapGet("/api/dictation/drafts",(long patNum) => {
			var mine=Drafts.Values
				.Where(d => d.PatNum==patNum && d.UserNum==CurUser)
				.OrderByDescending(d => d.CreatedAt)
				.Select(Shape).ToList();
			return Results.Json(mine);
		});

		//Transcribe/translate an audio clip into a NEW draft. Body = raw audio bytes.
		//Nothing is written to the chart here — this only proposes text.
		app.MapPost("/api/dictation/transcribe",async (HttpRequest request) => {
			if(!long.TryParse(request.Query["patNum"],out long patNum) || Patients.GetPat(patNum)==null) {
				return Results.Text("Unknown or missing patNum",statusCode:400);
			}
			string task=(request.Query["task"].ToString()??"transcribe").ToLowerInvariant();
			if(task!="transcribe" && task!="translate") { task="transcribe"; }
			string target=request.Query["target"].ToString();
			if(string.IsNullOrWhiteSpace(target)) { target="commlog"; }

			using MemoryStream ms=new();
			await request.Body.CopyToAsync(ms);
			byte[] audio=ms.ToArray();
			if(audio.Length<512) {
				return Results.Text("Audio clip is empty or too short.",statusCode:400);
			}

			string text,language=""; double duration=0;
			try {
				using ByteArrayContent content=new(audio);
				content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
				using HttpRequestMessage transcriptionRequest=ServiceRequest(HttpMethod.Post,
                        $"/transcribe?task={task}&format={Uri.EscapeDataString(request.Query["format"].ToString())}");
                    transcriptionRequest.Content=content;
                    using HttpResponseMessage resp=await Http.SendAsync(transcriptionRequest);
				string body=await resp.Content.ReadAsStringAsync();
				using JsonDocument doc=JsonDocument.Parse(body);
				JsonElement root=doc.RootElement;
				if(!resp.IsSuccessStatusCode || !root.GetProperty("ok").GetBoolean()) {
					return Results.Text("Transcription engine rejected the request.",statusCode:502);
				}
				text=root.GetProperty("text").GetString()??"";
				language=root.TryGetProperty("language",out var l)?(l.GetString()??""):"";
				duration=root.TryGetProperty("duration",out var d)?d.GetDouble():0;
			}
			catch(Exception) {
				return Results.Text("Transcription engine unavailable.",statusCode:503);
			}
			//Empty transcript => no draft. Tell the client plainly (it shows a retry hint)
			//rather than proposing a blank note the user can't make sense of.
			if(string.IsNullOrWhiteSpace(text)) {
				SecurityLogs.MakeLogEntry(EnumPermType.ChartModule,patNum,
					$"Dictation produced no speech ({task}, {duration:0.0}s, {audio.Length} bytes)");
				return Results.Json(new { ok=false,reason="no-speech",
					message="No speech was detected. Try again — speak clearly and hold the phone closer." },
					statusCode:422);
			}

			Draft draft=new(){
				Id=Guid.NewGuid().ToString("N"),PatNum=patNum,UserNum=CurUser,
				Target=target,Task=task,Transcript=text,Text=text,
				Language=language,Duration=duration,CreatedAt=DateTime.Now,
			};
			if(target=="procedures") { draft.Entries=ParseProcedures(text); }
			Drafts[draft.Id]=draft;
			SecurityLogs.MakeLogEntry(EnumPermType.ChartModule,patNum,
				$"Dictation transcribed ({task}, {duration:0.0}s, lang={language}, {text.Length} chars) -> draft {draft.Id[..8]}");
			return Results.Json(Shape(draft));
		});

		//Persist edits to the proposed text (so nothing is lost on refresh).
		app.MapPut("/api/dictation/drafts/{id}",async (string id,HttpRequest request) => {
			if(!Drafts.TryGetValue(id,out Draft d) || d.UserNum!=CurUser) {
				return Results.NotFound();
			}
			using StreamReader sr=new(request.Body);
			string raw=await sr.ReadToEndAsync();
			try {
				using JsonDocument doc=JsonDocument.Parse(raw);
				d.Text=doc.RootElement.GetProperty("text").GetString()??d.Text;
			}
			catch { return Results.Text("Bad body",statusCode:400); }
			return Results.Json(Shape(d));
		});

		//ACCEPT — the only write path. Files the (edited) text into the chosen target
		//through the real business layer, audit-logged, then closes the draft.
		app.MapPost("/api/dictation/drafts/{id}/accept",async (string id,HttpRequest request) => {
			if(!Drafts.TryGetValue(id,out Draft d) || d.UserNum!=CurUser) {
				return Results.NotFound();
			}
			//Allow a final edited text + target override + confirmed structured entries
			//to ride along with accept.
			string target=d.Target;
			List<ProcEntry> postedEntries=null;
			try {
				using StreamReader sr=new(request.Body);
				string raw=await sr.ReadToEndAsync();
				if(!string.IsNullOrWhiteSpace(raw)) {
					using JsonDocument doc=JsonDocument.Parse(raw);
					if(doc.RootElement.TryGetProperty("text",out var t)) { d.Text=t.GetString()??d.Text; }
					if(doc.RootElement.TryGetProperty("target",out var tg)) { target=tg.GetString()??target; }
					if(doc.RootElement.TryGetProperty("entries",out var en) && en.ValueKind==JsonValueKind.Array) {
						postedEntries=new();
						foreach(JsonElement it in en.EnumerateArray()) {
							postedEntries.Add(new ProcEntry{ ProcCode=Str(it,"procCode"),Tooth=Str(it,"tooth"),Surf=Str(it,"surf") });
						}
					}
				}
			}
			catch { /* empty/none body is fine */ }

			//Structured procedures: the clinician has confirmed discrete fields; write real
			//Procedure rows through the business layer (treatment-planned), audit-logged.
			if(target=="procedures") {
				IResult err=InsertProcedures(d.PatNum,postedEntries??d.Entries,out string procRef);
				if(err!=null) { return err; }
				SecurityLogs.MakeLogEntry(EnumPermType.ChartModule,d.PatNum,$"Dictation accepted -> {procRef} ({d.Task})");
				Drafts.TryRemove(id,out _);
				return Results.Json(new { ok=true,target,reference=procRef });
			}

			string text=(d.Text??"").Trim();
			if(text.Length==0) {
				return Results.Text("Nothing to accept — the note is empty.",statusCode:400);
			}
			string stamp=$"[Dictation {DateTime.Now:yyyy-MM-dd HH:mm}]";
			string reference;
			try {
				if(target=="commlog") {
					Commlog cl=new(){
						PatNum=d.PatNum,CommDateTime=DateTime.Now,DateTimeEnd=DateTime.Now,
						CommType=Defs.GetDefsForCategory(DefCat.CommLogTypes,true).FirstOrDefault()?.DefNum??0,
						Note=text,Mode_=CommItemMode.None,SentOrReceived=CommSentOrReceived.Neither,
						UserNum=CurUser,CommSource=CommItemSource.User,
					};
					long n=Commlogs.Insert(cl);
					reference=$"commlog #{n}";
				}
				else if(target=="note-medical" || target=="note-treatment") {
					Patient pat=Patients.GetPat(d.PatNum);
					PatientNote note=PatientNotes.Refresh(d.PatNum,pat.Guarantor);
					string add=$"{stamp}\n{text}";
					if(target=="note-medical") {
						note.Medical=Join(note.Medical,add);
					}
					else {
						note.Treatment=Join(note.Treatment,add);
					}
					PatientNotes.Update(note,pat.Guarantor);
					reference=target=="note-medical"?"patient Medical note":"patient Treatment note";
				}
				else if(target.StartsWith("proc:") && long.TryParse(target[5..],out long procNum)) {
					Procedure proc=Procedures.GetOneProc(procNum,true);
					if(proc==null || proc.PatNum!=d.PatNum) {
						return Results.Text("Target procedure not found for this patient.",statusCode:400);
					}
					Procedure procOld=proc.Copy();
					proc.Note=Join(proc.Note,$"{stamp} {text}");
					Procedures.Update(proc,procOld);
					reference=$"procedure #{procNum} note";
				}
				else {
					return Results.Text("Unknown target: "+target,statusCode:400);
				}
			}
			catch(Exception ex) {
				return Results.Text("Could not file the note: "+ex.Message,statusCode:500);
			}

			SecurityLogs.MakeLogEntry(EnumPermType.ChartModule,d.PatNum,
				$"Dictation accepted -> {reference} ({d.Task}, {text.Length} chars)");
			Drafts.TryRemove(id,out _);
			return Results.Json(new { ok=true,target,reference });
		});

		//DISCARD — audited; the UI gates this behind a strong confirmation.
		app.MapDelete("/api/dictation/drafts/{id}",(string id) => {
			if(!Drafts.TryGetValue(id,out Draft d) || d.UserNum!=CurUser) {
				return Results.NotFound();
			}
			Drafts.TryRemove(id,out _);
			SecurityLogs.MakeLogEntry(EnumPermType.ChartModule,d.PatNum,
				$"Dictation draft discarded ({d.Transcript.Length} chars, unsaved)");
			return Results.Json(new { ok=true });
		});
	}

	static string Join(string existing,string add)
		=> string.IsNullOrWhiteSpace(existing)?add:existing.TrimEnd()+"\n\n"+add;

	static string Str(JsonElement el,string prop)
		=> el.TryGetProperty(prop,out var v) && v.ValueKind==JsonValueKind.String ? (v.GetString()??"") : "";

	// ---- Structured dental charting from dictated text (local) ----

	static readonly int[] Anterior={6,7,8,9,10,11,22,23,24,25,26,27};
	static readonly int[] Molar={1,2,3,14,15,16,17,18,19,30,31,32};

	//(regex, family, toothRequired, label) — first match wins; order most-specific first.
	static readonly (Regex rx,string fam,bool tooth,string label)[] ProcRules=new[]{
		(new Regex(@"root canal|\bendo\b|\brct\b",RegexOptions.IgnoreCase),"ENDO",true,"Root canal"),
		(new Regex(@"post ?and ?core|post ?& ?core",RegexOptions.IgnoreCase),"POSTCORE",true,"Post & core"),
		(new Regex(@"build ?up|buildup",RegexOptions.IgnoreCase),"BUILDUP",true,"Core buildup"),
		(new Regex(@"\bcrown\b",RegexOptions.IgnoreCase),"CROWN",true,"Crown"),
		(new Regex(@"extract|\bexo\b|\bext\b",RegexOptions.IgnoreCase),"EXTRACT",true,"Extraction"),
		(new Regex(@"amalgam|silver filling",RegexOptions.IgnoreCase),"AMALGAM",true,"Amalgam"),
		(new Regex(@"composite|\bresin\b|tooth ?colou?red|white filling|filling",RegexOptions.IgnoreCase),"COMPOSITE",true,"Resin composite"),
		(new Regex(@"sealant",RegexOptions.IgnoreCase),"SEALANT",true,"Sealant"),
		(new Regex(@"scaling and root planing|root planing|\bsrp\b|deep clean",RegexOptions.IgnoreCase),"SRP",false,"Scaling & root planing"),
		(new Regex(@"prophylaxis|\bprophy\b|cleaning",RegexOptions.IgnoreCase),"PROPHY",false,"Prophylaxis"),
		(new Regex(@"fluoride",RegexOptions.IgnoreCase),"FLUORIDE",false,"Fluoride"),
		(new Regex(@"comprehensive exam|comprehensive",RegexOptions.IgnoreCase),"EXAMC",false,"Comprehensive exam"),
		(new Regex(@"periodic exam|recall exam|periodic|examination|\bexam\b",RegexOptions.IgnoreCase),"EXAMP",false,"Periodic exam"),
		(new Regex(@"panoramic|\bpano\b",RegexOptions.IgnoreCase),"PANO",false,"Panoramic film"),
		(new Regex(@"full ?mouth|\bfmx\b",RegexOptions.IgnoreCase),"FMX",false,"Full-mouth series"),
		(new Regex(@"bite ?wing",RegexOptions.IgnoreCase),"BW",false,"Bitewings"),
		(new Regex(@"periapical|\bpa film\b",RegexOptions.IgnoreCase),"PA",false,"Periapical film"),
		(new Regex(@"night ?guard|occlusal guard|bite guard",RegexOptions.IgnoreCase),"NIGHTGUARD",false,"Occlusal guard"),
	};

	static List<ProcEntry> ParseProcedures(string transcript){
		List<ProcEntry> list=new();
		if(string.IsNullOrWhiteSpace(transcript)){ return list; }
		string norm=transcript.Replace("\r"," ").Replace("\n"," ");
		//STT punctuates dictation with commas between procedures, so split on commas and
		//sentence terminators — NOT on "and", which would break tooth lists ("1 and 16").
		string[] segs=Regex.Split(norm,@"[.;,]");
		(string fam,string label,bool tooth,string surf)? cur=null;
		foreach(string segRaw in segs){
			string seg=segRaw.Trim();
			if(seg.Length==0){ continue; }
			var rule=ProcRules.FirstOrDefault(r => r.rx.IsMatch(seg));
			if(rule.fam!=null){
				//Surfaces are only meaningful for restorations — gating avoids English words
				//like "Film" being misread as surface letters on an exam/x-ray.
				bool surfRelevant=rule.fam=="COMPOSITE" || rule.fam=="AMALGAM" || rule.fam=="SEALANT";
				string surf=surfRelevant?ExtractSurfaces(seg):"";
				cur=(rule.fam,rule.label,rule.tooth,surf);
				EmitEntries(list,rule.fam,rule.label,rule.tooth,surf,ExtractTeeth(seg),seg);
			}
			else if(cur!=null && Regex.IsMatch(seg,@"^(?:\d{1,2}|and|the|&|\s)+$",RegexOptions.IgnoreCase)){
				//a bare tooth-list continuing the previous procedure ("16", "17 and 32")
				EmitEntries(list,cur.Value.fam,cur.Value.label,cur.Value.tooth,cur.Value.surf,ExtractTeethLoose(seg),seg);
			}
			if(list.Count>=60){ break; }
		}
		return list;
	}

	static void EmitEntries(List<ProcEntry> list,string fam,string label,bool toothReq,string surf,List<string> teeth,string raw){
		IEnumerable<string> toothSet=teeth.Count>0?teeth:new List<string>{""};
		foreach(string tooth in toothSet){
			string code=ResolveCode(fam,surf,tooth);
			string descript=label;
			if(!string.IsNullOrEmpty(code)){
				ProcedureCode pc=ProcedureCodes.GetCodeNum(code)!=0?ProcedureCodes.GetProcCode(code):null;
				if(pc!=null){ descript=pc.Descript; }
			}
			bool haveTooth=!string.IsNullOrEmpty(tooth);
			string conf=string.IsNullOrEmpty(code)?"low":((haveTooth||!toothReq)?"high":"med");
			list.Add(new ProcEntry{ ProcCode=code??"",Descript=descript,Tooth=tooth,Surf=surf,Confidence=conf,Raw=raw });
			if(list.Count>=60){ return; }
		}
	}

	static List<string> ExtractTeethLoose(string seg){
		List<string> teeth=new();
		foreach(Match m in Regex.Matches(seg,@"\b(\d{1,2})\b")){
			int n=int.Parse(m.Groups[1].Value);
			if(n>=1 && n<=32 && !teeth.Contains(n.ToString())){ teeth.Add(n.ToString()); }
		}
		return teeth;
	}

	static List<string> ExtractTeeth(string clause){
		List<string> teeth=new();
		if(!Regex.IsMatch(clause,@"\b(tooth|teeth|number|no\.?)\b|#\s*\d",RegexOptions.IgnoreCase)){ return teeth; }
		foreach(Match m in Regex.Matches(clause,@"#?\b(\d{1,2})\b(?!\s*(?:mm|millimet|%|percent))",RegexOptions.IgnoreCase)){
			int n=int.Parse(m.Groups[1].Value);
			if(n>=1 && n<=32 && !teeth.Contains(n.ToString())){ teeth.Add(n.ToString()); }
		}
		return teeth;
	}

	static string ExtractSurfaces(string clause){
		string c=clause.ToLowerInvariant();
		List<char> set=new();
		void Add(char s){ if(!set.Contains(s)){ set.Add(s); } }
		if(Regex.IsMatch(c,@"\bmesial\b")){ Add('M'); }
		if(Regex.IsMatch(c,@"\bocclusal\b")){ Add('O'); }
		if(Regex.IsMatch(c,@"\bdistal\b")){ Add('D'); }
		if(Regex.IsMatch(c,@"\bbuccal\b")){ Add('B'); }
		if(Regex.IsMatch(c,@"\blingual\b")){ Add('L'); }
		if(Regex.IsMatch(c,@"\bfacial\b")){ Add('F'); }
		if(Regex.IsMatch(c,@"\bincisal\b")){ Add('I'); }
		foreach(Match m in Regex.Matches(clause,@"\b([MODBLIF]{2,5})\b")){ //ALL-CAPS combos: MOD, MODBL
			foreach(char ch in m.Groups[1].Value){ Add(char.ToUpper(ch)); }
		}
		foreach(Match m in Regex.Matches(clause,@"\b([MmOoDdBbLlIiFf]{3,5})\b")){ //any-case len>=3: "Mod" (avoids of/do/on)
			foreach(char ch in m.Groups[1].Value){ Add(char.ToUpper(ch)); }
		}
		string order="MODBLFI";
		return new string(order.Where(o => set.Contains(o)).ToArray());
	}

	static string ResolveCode(string fam,string surf,string tooth){
		int n=surf?.Length??0;
		bool ant=int.TryParse(tooth,out int tn) && Anterior.Contains(tn);
		bool molar=int.TryParse(tooth,out int tn2) && Molar.Contains(tn2);
		switch(fam){
			case "COMPOSITE": return ant ? (n<=1?"D2330":n==2?"D2331":n==3?"D2332":"D2335")
			                              : (n<=1?"D2391":n==2?"D2392":n==3?"D2393":"D2394");
			case "AMALGAM":   return n<=1?"D2140":n==2?"D2150":n==3?"D2160":"D2161";
			case "CROWN":     return "D2740";
			case "EXTRACT":   return "D7140";
			case "ENDO":      return ant?"D3310":(molar?"D3330":"D3320");
			case "BUILDUP":   return "D2950";
			case "POSTCORE":  return "D2954";
			case "SEALANT":   return "D1351";
			case "SRP":       return "D4341";
			case "PROPHY":    return "D1110";
			case "FLUORIDE":  return "D1206";
			case "EXAMC":     return "D0150";
			case "EXAMP":     return "D0120";
			case "PANO":      return "D0330";
			case "FMX":       return "D0210";
			case "BW":        return "D0274";
			case "PA":        return "D0220";
			case "NIGHTGUARD":return "D9944";
			default:          return "";
		}
	}

	static IResult InsertProcedures(long patNum,List<ProcEntry> entries,out string reference){
		reference="";
		Patient pat=Patients.GetPat(patNum);
		if(pat==null){ return Results.Text("Unknown patient.",statusCode:400); }
		if(entries==null || entries.Count==0){ return Results.Text("No procedures to file.",statusCode:400); }
		List<string> skipped=new(); int ok=0;
		foreach(ProcEntry e in entries){
			string code=(e.ProcCode??"").Trim().ToUpperInvariant();
			if(code.Length==0){ continue; }
			long codeNum=ProcedureCodes.GetCodeNum(code);
			if(codeNum==0){ skipped.Add(code); continue; }
			Procedure proc=new Procedure{
				PatNum=patNum,CodeNum=codeNum,ProvNum=pat.PriProv,ClinicNum=pat.ClinicNum,
				ToothNum=(e.Tooth??"").Trim(),Surf=(e.Surf??"").Trim().ToUpperInvariant(),
				ProcStatus=ProcStat.TP,ProcDate=DateTime.Today,DateEntryC=DateTime.Today,
			};
			long feeSched=FeeScheds.GetFeeSched(0,pat.FeeSched,proc.ProvNum);
			proc.ProcFee=Fees.GetAmount0(proc.CodeNum,feeSched,proc.ClinicNum,proc.ProvNum);
			Procedures.Insert(proc);
			ok++;
		}
		if(ok==0){ return Results.Text("No valid procedure codes to file"
			+(skipped.Count>0?" (unrecognized: "+string.Join(", ",skipped)+")":"")+".",statusCode:400); }
		reference=ok+" treatment-planned procedure"+(ok==1?"":"s")
			+(skipped.Count>0?" ("+skipped.Count+" skipped)":"");
		return null;
	}
}
