// Modified for the Apex Dental public source snapshot, 2026-10-25; see NOTICE.
/*
 * Demo web UI over the headless Linux core (OpenDentBusinessCore).
 * ASP.NET Core minimal API + a single self-contained HTML page.
 * All data flows through the real Open Dental business layer:
 * Patients.Insert / Patients.GetPat / Appointments.GetForPeriodList.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DataConnectionBase;
using OpenDentBusiness;
using OpenDentalWebDemo;

//Under Linux the default process culture is InvariantCulture, so ToString("c")
//renders the generic currency glyph (¤) instead of $ on sheet/statement PDFs.
//Keep invariant number formatting (the account grids already format cleanly
//under it) and only override the currency symbol to $, so money shows "$8.00"
//without disturbing the 2-decimal grid amounts. The data layer parses via
//InvariantCulture explicitly, so this only affects human-facing formatting.
var _money=(System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
_money.NumberFormat.CurrencySymbol="$";
System.Globalization.CultureInfo.DefaultThreadCurrentCulture=_money;
System.Threading.Thread.CurrentThread.CurrentCulture=_money;

var builder=WebApplication.CreateBuilder(args);
WebSecurity.Configure(builder);
var app=builder.Build();
WebSecurity.Use(app);
//Workstation UI (wwwroot: index.html/app.css/app.js) is served at "/";
//the original single-string demo page stays at /legacy for comparison.
app.UseDefaultFiles();
//Static assets (SPA JS/CSS) revalidate every load via ETag, so a redeploy is never
//masked by a stale browser cache (the "chart still renders dark" class of bug).
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions{
	OnPrepareResponse = ctx => {
		ctx.Context.Response.Headers["Cache-Control"]="no-cache, must-revalidate";
	}
});
//Auth gate: session -> Security.CurUser, require login on /api/*, enforce role->module access.
OpenDentalWebDemo.Modules.AuthModule.UseAuth(app);

string server=Environment.GetEnvironmentVariable("OD_DB_SERVER")??"127.0.0.1";
DataConnection.DBtype=DatabaseType.MySql;
string database=Environment.GetEnvironmentVariable("OD_DB_NAME")??"opendental";
        string dbUser=Environment.GetEnvironmentVariable("OD_DB_USER")
            ??throw new InvalidOperationException("OD_DB_USER is required.");
        string dbPassword=Environment.GetEnvironmentVariable("OD_DB_PASSWORD");
        if(string.IsNullOrEmpty(dbPassword)) {
            throw new InvalidOperationException("OD_DB_PASSWORD is required.");
        }
        new DataConnection().SetDb(server,database,dbUser,dbPassword,dbUser,dbPassword,DataConnection.DBtype,true);
RemotingClient.MiddleTierRole=MiddleTierRole.ClientDirect;
FontResolverOD.Register();//PDFsharp 6 font embedding on Linux
Cache.Refresh(InvalidType.Prefs,InvalidType.Security,InvalidType.Providers,InvalidType.Defs,InvalidType.ProcCodes);
Security.CurUser=null;
Console.WriteLine($"Connected; schema {PrefC.GetString(PrefName.DataBaseVersion)}");

app.MapGet("/api/patients",() => {
	DataTable table=DataCore.GetTable("SELECT PatNum FROM patient WHERE PatStatus=0 ORDER BY LName,FName LIMIT 200");
	List<object> list=new List<object>();
	foreach(DataRow row in table.Rows) {
		Patient pat=Patients.GetPat(PIn.Long(row["PatNum"].ToString()));
		if(pat==null) {
			continue;
		}
		list.Add(new { patNum=pat.PatNum,name=pat.GetNameLF(),birthdate=pat.Birthdate.ToString("yyyy-MM-dd"),
			provider=Providers.GetAbbr(pat.PriProv) });
	}
	return Results.Json(list);
});

app.MapPost("/api/patients",(NewPatient dto) => {
	Patient pat=new Patient();
	pat.LName=dto.LName;
	pat.FName=dto.FName;
	pat.Birthdate=DateTime.TryParse(dto.Birthdate,out DateTime birthdate)?birthdate:DateTime.MinValue;
	pat.PatStatus=PatientStatus.Patient;
	pat.PriProv=Providers.GetFirst()?.ProvNum??0;
	pat.BillingType=PrefC.GetLong(PrefName.PracticeDefaultBillType);
	Patients.Insert(pat,false);
	Patient patOld=pat.Copy();
	pat.Guarantor=pat.PatNum;
	Patients.Update(pat,patOld);
	return Results.Json(new { patNum=pat.PatNum });
});

app.MapGet("/api/appointments",() => {
	List<Appointment> listAppts=Appointments.GetForPeriodList(DateTime.Today,DateTime.Today.AddDays(14));
	return Results.Json(listAppts.Select(x => new {
		aptNum=x.AptNum,
		dateTime=x.AptDateTime.ToString("yyyy-MM-dd HH:mm"),
		status=x.AptStatus.ToString(),
		patient=Patients.GetPat(x.PatNum)?.GetNameLF()??"?",
		provider=Providers.GetAbbr(x.ProvNum),
	}));
});

//----- Scanning (eSCL first, SANE fallback, upload = TWAIN-agent contract) -----

long GetImageCategory() {
	return Defs.GetDefsForCategory(DefCat.ImageCats,true).FirstOrDefault()?.DefNum??0;
}

Document ImportScan(byte[] bytes,string ext,long patNum) {
	Patient pat=Patients.GetPat(patNum)??throw new Exception("Unknown patient "+patNum);
	string tmpPath=Path.Combine(Path.GetTempPath(),"odimport_"+Guid.NewGuid().ToString("N")+ext);
	try {
		File.WriteAllBytes(tmpPath,bytes);
		return ImageStore.Import(tmpPath,GetImageCategory(),pat);//real business-layer import (audit logged)
	}
	finally {
		try { File.Delete(tmpPath); } catch { }
	}
}

app.MapGet("/api/scan/sources",() => Results.Json(ScanService.GetSources()));

app.MapPost("/api/scan/acquire",async (ScanRequest dto) => {
	try {
		(byte[] bytes,string ext)=await ScanService.Acquire(dto.Source);
		Document doc=ImportScan(bytes,ext,dto.PatNum);
		return Results.Json(new { docNum=doc.DocNum,fileName=doc.FileName,size=bytes.Length });
	}
	catch(Exception ex) {
		return Results.Text("Scanner acquisition failed.",statusCode:500);
	}
});

app.MapPost("/api/scan/upload",async (HttpRequest request) => {
	long patNum=long.Parse(request.Query["patNum"]);
	string ext=Path.GetExtension(request.Query["filename"].ToString());
	if(string.IsNullOrEmpty(ext)) {
		ext=".jpg";
	}
	using MemoryStream stream=new();
	await request.Body.CopyToAsync(stream);
	Document doc=ImportScan(stream.ToArray(),ext,patNum);
	return Results.Json(new { docNum=doc.DocNum,fileName=doc.FileName });
});

app.MapGet("/api/patients/{patNum:long}/documents",(long patNum) => {
	DataTable table=DataCore.GetTable("SELECT DocNum FROM document WHERE PatNum="+patNum+" ORDER BY DateCreated DESC");
	List<object> list=new();
	foreach(DataRow row in table.Rows) {
		Document doc=Documents.GetByNum(PIn.Long(row["DocNum"].ToString()),true);
		if(doc==null) {
			continue;
		}
		list.Add(new { docNum=doc.DocNum,fileName=doc.FileName,imgType=doc.ImgType.ToString(),
			created=doc.DateCreated.ToString("yyyy-MM-dd HH:mm") });
	}
	return Results.Json(list);
});

app.MapGet("/api/documents/{docNum:long}/file",(long docNum) => {
	Document doc=Documents.GetByNum(docNum,true);
	if(doc==null || string.IsNullOrEmpty(doc.RawBase64)) {
		return Results.NotFound();
	}
	string mime=Path.GetExtension(doc.FileName).ToLower() switch {
		".png" => "image/png",
		".pdf" => "application/pdf",
		".dcm" => "application/dicom",
		_ => "image/jpeg",
	};
	return Results.File(Convert.FromBase64String(doc.RawBase64),mime);
});

//----- Signature capture (browser canvas -> OD point-stream -> signed consent PDF) -----

app.MapPost("/api/patients/{patNum:long}/consent",(long patNum,SignatureDto dto) => {
	Patient pat=Patients.GetPat(patNum);
	if(pat==null) {
		return Results.NotFound();
	}
	SheetDef sheetDef=SheetsInternal.GetSheetDef(SheetInternalType.Consent);
	Sheet sheet=SheetUtil.CreateSheet(sheetDef,patNum);
	SheetParameter.SetParameter(sheet,"PatNum",patNum);
	SheetFiller.FillFields(sheet);
	SheetUtil.CalculateHeights(sheet);
	SheetField fieldSig=sheet.SheetFields.FirstOrDefault(x => x.FieldType==SheetFieldType.SigBox);
	if(fieldSig==null) {
		return Results.Text("No SigBox on sheet",statusCode:500);
	}
	//Same key derivation the WinForms client uses: MD5 over the concatenated field values.
	string keyData=Sheets.GetSignatureKey(sheet);
	byte[] key=global::ODCrypt.MD5.Hash(System.Text.Encoding.UTF8.GetBytes(keyData));
	//"0" prefix = not a Topaz-hardware signature.
	fieldSig.FieldValue="0"+OpenDentBusiness.UI.SigBox.EncryptSigString(key,dto.Points);
	fieldSig.DateTimeSig=DateTime.Now;
	long sheetNum=Sheets.SaveNewSheet(sheet);
	return Results.Json(new { sheetNum });
});

app.MapGet("/api/sheets/{sheetNum:long}/pdf",(long sheetNum) => {
	Sheet sheet=Sheets.GetSheet(sheetNum);
	if(sheet==null) {
		return Results.NotFound();
	}
	Sheets.SetSheetFieldsForSheets(new List<Sheet> { sheet });//also computes SigKey for SigBox fields
	Patient pat=Patients.GetPat(sheet.PatNum);
	string pdfPath=Path.Combine(Path.GetTempPath(),$"odsheet_{sheetNum}.pdf");
	SheetPrinting.CreatePdf(sheet,pdfPath,null,null,null,pat);
	return Results.File(File.ReadAllBytes(pdfPath),"application/pdf");
});

//----- Tooth chart (real chart data -> SVG in the browser) -----

app.MapGet("/api/patients/{patNum:long}/toothchart",(long patNum) => {
	List<Procedure> listProcs=Procedures.Refresh(patNum);
	List<ToothInitial> listInitials=ToothInitials.GetPatientData(patNum);
	//Practice-configured chart colors, same defs the desktop legend uses.
	List<Def> listDefs=Defs.GetDefsForCategory(DefCat.ChartGraphicColors,true);
	string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";
	var procsOut=listProcs
		.Where(x => x.ProcStatus!=ProcStat.D && int.TryParse(x.ToothNum,out int toothInt) && toothInt>=1 && toothInt<=32)
		.Select(x => {
			ProcedureCode code=ProcedureCodes.GetProcCode(x.CodeNum);
			return new {
				toothNum=x.ToothNum,
				surf=x.Surf??"",
				status=x.ProcStatus.ToString(),
				code=code.ProcCode,
				descript=code.Descript,
				paintType=code.PaintType.ToString(),
				color=Hex(code.GraphicColor),
			};
		});
	var missing=listInitials.Where(x => x.InitialType==ToothInitialType.Missing).Select(x => x.ToothNum).ToList();
	return Results.Json(new {
		procs=procsOut,
		missing,
		statusColors=new {
			TP=Hex(listDefs[0].ItemColor),
			C=Hex(listDefs[1].ItemColor),
			EC=Hex(listDefs[2].ItemColor),
			EO=Hex(listDefs[3].ItemColor),
			R=Hex(listDefs[4].ItemColor),
		},
	});
});

app.MapPost("/api/patients/{patNum:long}/toothchart/demo",(long patNum) => {
	//Seed demo clinical data through the real business layer.
	Patient pat=Patients.GetPat(patNum);
	if(pat==null) {
		return Results.NotFound();
	}
	//Idempotent: don't re-seed a patient who already has procedures — repeated
	//clicks of "Seed demo clinical data" otherwise pile up duplicate procs/perio.
	if(Procedures.Refresh(patNum).Any(x => x.ProcStatus!=ProcStat.D)) {
		return Results.Json(new { seeded=false,message="Patient already has clinical data — demo seed skipped." });
	}
	void AddProc(string procCode,int toothNum,string surf,ProcStat status) {
		Procedure proc=new Procedure();
		proc.PatNum=patNum;
		proc.ProvNum=pat.PriProv;
		proc.CodeNum=ProcedureCodes.GetCodeNum(procCode);
		proc.ToothNum=toothNum.ToString();//Procedure.ToothNum is a string column
		proc.Surf=surf;
		proc.ProcStatus=status;
		proc.ProcDate=DateTime.Today;
		proc.DateEntryC=DateTime.Today;
		proc.ProcFee=100;
		Procedures.Insert(proc);
	}
	AddProc("D2160",3,"MOD",ProcStat.C);//amalgam 3 surf, tooth 3, complete
	AddProc("D2330",8,"M",ProcStat.EC);//anterior composite, existing
	AddProc("D2740",19,"",ProcStat.TP);//crown, treatment planned
	AddProc("D7140",30,"",ProcStat.C);//extraction, complete
	AddProc("D3330",14,"",ProcStat.C);//root canal (RCT paint), complete
	AddProc("D6010",29,"",ProcStat.C);//implant body, complete
	AddProc("D2790",29,"",ProcStat.C);//crown over the implant
	AddProc("D1351",5,"O",ProcStat.C);//sealant
	AddProc("D2750",12,"",ProcStat.EO);//crown, existing other provider
	ToothInitials.Insert(new ToothInitial() { PatNum=patNum,ToothNum="30",InitialType=ToothInitialType.Missing });
	ToothInitials.Insert(new ToothInitial() { PatNum=patNum,ToothNum="29",InitialType=ToothInitialType.Missing });//implant site
	ToothInitials.Insert(new ToothInitial() { PatNum=patNum,ToothNum="26",InitialType=ToothInitialType.Rotate,Movement=15 });//rotated incisor
	ToothInitials.Insert(new ToothInitial() { PatNum=patNum,ToothNum="7",InitialType=ToothInitialType.Primary });//mixed dentition demo
	//Perio exam with probing depths, gingival margin, bleeding, furcation, mobility.
	PerioExam perioExam=new PerioExam() { PatNum=patNum,ExamDate=DateTime.Today,ProvNum=pat.PriProv };
	PerioExams.Insert(perioExam);
	void AddMeasure(int toothNum,PerioSequenceType seq,int mb,int b,int db,int ml,int l,int dl,int toothValue=-1) {
		PerioMeasures.Insert(new PerioMeasure() { PerioExamNum=perioExam.PerioExamNum,IntTooth=toothNum,SequenceType=seq,
			MBvalue=mb,Bvalue=b,DBvalue=db,MLvalue=ml,Lvalue=l,DLvalue=dl,ToothValue=toothValue });
	}
	foreach(int toothNum in new[]{ 2,3,4,13,14,15,18,19,20,28,31 }) {
		AddMeasure(toothNum,PerioSequenceType.Probing,3,2,3,4,3,4);
	}
	AddMeasure(3,PerioSequenceType.Probing,5,4,6,5,4,5);//pocketing on 3
	AddMeasure(3,PerioSequenceType.GingMargin,2,1,2,-1,-1,-1);
	AddMeasure(14,PerioSequenceType.GingMargin,1,1,1,-1,-1,-1);
	AddMeasure(3,PerioSequenceType.MGJ,4,4,4,-1,-1,-1);
	AddMeasure(3,PerioSequenceType.Furcation,2,0,0,0,0,0);
	AddMeasure(14,PerioSequenceType.Furcation,3,0,0,0,0,0);
	AddMeasure(3,PerioSequenceType.BleedSupPlaqCalc,1,0,1,0,0,0);
	AddMeasure(19,PerioSequenceType.Mobility,0,0,0,0,0,0,2);
	return Results.Ok();
});

//----- Server-rendered tooth chart (real ToothChartHelper via the Sparks3D shim) -----

app.MapGet("/api/patients/{patNum:long}/toothchart.png",(long patNum,bool perio=false,int w=1000,int h=740) => {
	PerioExam perioExamLatest=perio?PerioExams.Refresh(patNum).LastOrDefault():null;
	using System.Drawing.Image image=OpenDentBusiness.SheetFramework.ToothChartHelper.GetImage(patNum,showCompleted:true,
		isForPerio:perio && perioExamLatest!=null,perioExam:perioExamLatest,width:w,height:h);
	using MemoryStream stream=new MemoryStream();
	image.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
	return Results.File(stream.ToArray(),"image/png");
});

app.MapGet("/api/patients/{patNum:long}/toothchart.svg",(long patNum) => {
	using System.Drawing.Image image=OpenDentBusiness.SheetFramework.ToothChartHelper.GetImage(patNum,showCompleted:true);
	return Results.Content(Sparks3D.ToothChart.LastRenderedSvg,"image/svg+xml");
});

//----- Reports (RpProcSheet — real report query class) -----

app.MapGet("/api/reports",() => Results.Json(ReportRegistry.Reports.Select(x => new { id=x.Id,name=x.Name })));

app.MapGet("/api/reports/{id}",(string id,DateTime? from,DateTime? to) => {
	try {
		return Results.Json(ReportRegistry.RunReport(id,from??DateTime.Today.AddDays(-30),to??DateTime.Today.AddDays(1)));
	}
	catch(Exception ex) {
		return Results.Text(ex.Message,statusCode:400);
	}
});

//----- Patient details / edit (FormPatientEdit core fields) -----

app.MapGet("/api/patients/{patNum:long}/details",(long patNum) => {
	Patient pat=Patients.GetPat(patNum);
	if(pat==null) {
		return Results.NotFound();
	}
	return Results.Json(new { patNum=pat.PatNum,lName=pat.LName,fName=pat.FName,preferred=pat.Preferred,
		birthdate=pat.Birthdate.Year>1880?pat.Birthdate.ToString("yyyy-MM-dd"):"",
		gender=pat.Gender.ToString(),status=pat.PatStatus.ToString(),
		hmPhone=pat.HmPhone,wirelessPhone=pat.WirelessPhone,email=pat.Email,
		address=pat.Address,city=pat.City,state=pat.State,zip=pat.Zip,
		priProv=pat.PriProv,provider=Providers.GetAbbr(pat.PriProv) });
});

app.MapPut("/api/patients/{patNum:long}",(long patNum,PatientEditDto dto) => {
	Patient pat=Patients.GetPat(patNum);
	if(pat==null) {
		return Results.NotFound();
	}
	Patient patOld=pat.Copy();
	pat.LName=dto.LName??pat.LName;
	pat.FName=dto.FName??pat.FName;
	pat.Preferred=dto.Preferred??pat.Preferred;
	if(DateTime.TryParse(dto.Birthdate,out DateTime birthdate)) {
		pat.Birthdate=birthdate;
	}
	if(Enum.TryParse(dto.Gender,out PatientGender gender)) {
		pat.Gender=gender;
	}
	pat.HmPhone=dto.HmPhone??pat.HmPhone;
	pat.WirelessPhone=dto.WirelessPhone??pat.WirelessPhone;
	pat.Email=dto.Email??pat.Email;
	pat.Address=dto.Address??pat.Address;
	pat.City=dto.City??pat.City;
	pat.State=dto.State??pat.State;
	pat.Zip=dto.Zip??pat.Zip;
	Patients.Update(pat,patOld);//real business update: audit trail + SecurityHash upkeep
	return Results.Ok();
});

//----- Schedule day view + appointment creation -----

app.MapGet("/api/schedule",(DateTime? date) => {
	DateTime day=date??DateTime.Today;
	List<Appointment> listAppts=Appointments.GetForPeriodList(day,day.AddDays(1).AddSeconds(-1));
	return Results.Json(listAppts.OrderBy(x => x.AptDateTime).Select(x => new {
		aptNum=x.AptNum,
		time=x.AptDateTime.ToString("HH:mm"),
		minutes=x.Pattern.Length*5,
		status=x.AptStatus.ToString(),
		patNum=x.PatNum,
		patient=Patients.GetPat(x.PatNum)?.GetNameLF()??"?",
		provider=Providers.GetAbbr(x.ProvNum),
		notes=x.Note,
	}));
});

app.MapPut("/api/appointments/{aptNum:long}",(long aptNum,EditAppointment dto) => {
	Appointment appt=Appointments.GetOneApt(aptNum);
	if(appt==null) {
		return Results.NotFound();
	}
	Appointment apptOld=appt.Copy();
	if(!string.IsNullOrEmpty(dto.DateTime) && DateTime.TryParse(dto.DateTime,out DateTime when)) {
		appt.AptDateTime=when;
	}
	if(dto.Minutes>0) {
		appt.Pattern=new string('X',Math.Max(1,dto.Minutes/5));
	}
	if(Enum.TryParse(dto.Status,out ApptStatus status)) {
		appt.AptStatus=status;
	}
	Appointments.Update(appt,apptOld);//audit-logged, same call the desktop book makes
	return Results.Ok();
});

app.MapDelete("/api/appointments/{aptNum:long}",(long aptNum) => {
	Appointments.Delete(aptNum,hasSignal:false);
	return Results.Ok();
});

app.MapPost("/api/appointments",(NewAppointment dto) => {
	Patient pat=Patients.GetPat(dto.PatNum);
	if(pat==null) {
		return Results.NotFound();
	}
	Appointment appt=new Appointment();
	appt.PatNum=dto.PatNum;
	appt.AptStatus=ApptStatus.Scheduled;
	appt.AptDateTime=DateTime.Parse(dto.DateTime);
	appt.ProvNum=pat.PriProv;
	appt.Pattern=new string('X',Math.Max(1,dto.Minutes/5)).PadLeft(0);
	appt.Note=dto.Note??"";
	Appointments.Insert(appt);
	return Results.Json(new { aptNum=appt.AptNum });
});

app.MapGet("/api/reports/procs",() => {
	List<Provider> listProvs=Providers.GetDeepCopy(true);
	DataTable table=RpProcSheet.GetIndividualTable(DateTime.Today.AddDays(-30),DateTime.Today.AddDays(1),
		listProvs.Select(x => x.ProvNum).ToList(),new List<long>(),"",false,true,false);
	List<Dictionary<string,object>> rows=new();
	foreach(DataRow row in table.Rows) {
		Dictionary<string,object> dict=new();
		foreach(DataColumn col in table.Columns) {
			dict[col.ColumnName]=row[col].ToString();
		}
		rows.Add(dict);
	}
	return Results.Json(new { columns=table.Columns.Cast<DataColumn>().Select(x => x.ColumnName),rows });
});

app.MapGet("/legacy",() => Results.Content(PAGE,"text/html"));

//Every static class in OpenDentalWebDemo.Modules with a static Map(WebApplication) is
//wired automatically, so adding a module never edits this file.
foreach(Type typeModule in typeof(Program).Assembly.GetTypes()
	.Where(x => x.Namespace=="OpenDentalWebDemo.Modules" && !x.IsNested)
	.OrderBy(x => x.Name)) {
	typeModule.GetMethod("Map",new[]{ typeof(WebApplication) })?.Invoke(null,new object[]{ app });
}

//The front-end loads every wwwroot/modules/*.js as a self-registering UI module.
app.MapGet("/api/ui/modules",() => {
	string dirModules=Path.Combine(app.Environment.WebRootPath??"wwwroot","modules");
	return Results.Json(Directory.Exists(dirModules)
		?Directory.GetFiles(dirModules,"*.js").Select(Path.GetFileName).OrderBy(x => x)
		:Enumerable.Empty<string>());
});

app.Run();

partial class Program {
	record NewPatient(string LName,string FName,string Birthdate);
	record ScanRequest(string Source,long PatNum);
	record SignatureDto(string Points);
	record PatientEditDto(string LName,string FName,string Preferred,string Birthdate,string Gender,
		string HmPhone,string WirelessPhone,string Email,string Address,string City,string State,string Zip);
	record NewAppointment(long PatNum,string DateTime,int Minutes,string Note);
	record EditAppointment(string DateTime,int Minutes,string Status);

	const string PAGE="""
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Apex Dental — Legacy Web Preview</title>
<style>
:root{color-scheme:light dark;font-family:system-ui,sans-serif}
body{margin:0;background:Canvas;color:CanvasText}
header{padding:14px 22px;border-bottom:1px solid color-mix(in srgb,CanvasText 18%,transparent)}
header h1{margin:0;font-size:18px}header p{margin:2px 0 0;font-size:13px;opacity:.7}
main{display:grid;grid-template-columns:1fr 1fr;gap:22px;padding:22px;max-width:1100px}
@media(max-width:800px){main{grid-template-columns:1fr}}
section{border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:8px;padding:16px;overflow-x:auto}
h2{margin:0 0 10px;font-size:15px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{text-align:left;padding:5px 8px;border-bottom:1px solid color-mix(in srgb,CanvasText 12%,transparent)}
th{opacity:.65;font-weight:600}
form{display:flex;gap:8px;flex-wrap:wrap;margin-top:12px}
input,button{padding:6px 9px;border-radius:6px;border:1px solid color-mix(in srgb,CanvasText 30%,transparent);background:Field;color:FieldText;font-size:13px}
button{cursor:pointer;font-weight:600}
.tag{font-size:11px;padding:1px 7px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
nav.mods{display:flex;gap:10px;flex-wrap:wrap;margin-top:8px;font-size:13px}
nav.mods a{color:inherit;text-decoration:none;padding:2px 9px;border-radius:9px;border:1px solid color-mix(in srgb,CanvasText 25%,transparent)}
nav.mods a:hover{background:color-mix(in srgb,CanvasText 10%,transparent)}
</style></head><body>
<header><h1>Apex Dental — Legacy Web Preview</h1>
<p>ASP.NET Core on the Linux VM &middot; real OpenDentBusiness layer &middot; MariaDB schema <span id=ver></span></p>
<nav class=mods><a href="/">Home</a><a href="/module/family">Family</a><a href="/module/account">Account</a><a href="/module/treatplan">Treat Plan</a><a href="/module/claims">Claims</a><a href="/module/schedweek">Week</a><a href="/module/comm">Comm</a><a href="/module/setup">Setup</a></nav></header>
<main>
<section><h2>Patients</h2><table id=pats><thead><tr><th>#</th><th>Name</th><th>Birthdate</th><th>Prov</th></tr></thead><tbody></tbody></table>
<form id=addpat><input name=lname placeholder="Last name" required><input name=fname placeholder="First name" required>
<input name=bd type=date><button>Add patient</button></form></section>
<section><h2>Appointments — next 14 days</h2><table id=appts><thead><tr><th>When</th><th>Patient</th><th>Prov</th><th>Status</th></tr></thead><tbody></tbody></table></section>
<section style="grid-column:1/-1"><h2>Scan &amp; Documents (eSCL → SANE → upload)</h2>
<form id=scanform><select id=scanpat required></select><select id=scansrc required></select>
<button id=scanbtn>Scan</button><input id=upfile type=file style="display:none">
<button type=button id=upbtn>Upload file…</button><span id=scanmsg style="font-size:12px;opacity:.7"></span></form>
<table id=docs><thead><tr><th>#</th><th>File</th><th>Type</th><th>Created</th><th></th></tr></thead><tbody></tbody></table></section>
<section style="grid-column:1/-1"><h2>Consent signature (canvas &rarr; Open Dental point-stream &rarr; signed PDF)</h2>
<form id=sigform><select id=sigpat required></select>
<canvas id=sigpad width=380 height=110 style="border:1px dashed color-mix(in srgb,CanvasText 40%,transparent);border-radius:6px;touch-action:none;background:Field"></canvas>
<button>Sign consent &amp; make PDF</button><button type=button id=sigclear>Clear</button>
<span id=sigmsg style="font-size:12px;opacity:.7"></span></form></section>
<section style="grid-column:1/-1"><h2>Tooth chart (SVG &larr; real procedurelog/toothinitial data)</h2>
<form id=chartform style="margin:0 0 10px"><select id=chartpat required></select>
<button type=button id=chartload>Load chart</button><button type=button id=chartseed>Seed demo clinical data</button>
<span id=chartmsg style="font-size:12px;opacity:.7"></span></form>
<svg id=toothsvg viewBox="0 0 840 260" style="width:100%;max-width:840px"></svg>
<div id=toothdetail style="font-size:12px;margin-top:6px"></div>
<h2 style="margin-top:14px">Server-rendered chart (real ToothChartHelper &rarr; GDI/SVG)</h2>
<img id=serverchart style="max-width:520px;width:100%;border:1px solid color-mix(in srgb,CanvasText 18%,transparent);border-radius:6px" alt="server tooth chart">
<div style="font-size:12px;margin-top:4px"><a id=serversvg target=_blank>open as SVG</a></div></section>
<section style="grid-column:1/-1"><h2>Reports (generic runner over the Rp* report classes)</h2>
<form id=repform style="margin-bottom:8px"><select id=repsel></select>
<input id=repfrom type=date><input id=repto type=date><button>Run</button>
<span id=repmsg style="font-size:12px;opacity:.7"></span></form>
<div style="overflow-x:auto"><table id=report><thead></thead><tbody></tbody></table></div></section>
<section><h2>Patient editor (FormPatientEdit core)</h2>
<form id=patedit style="display:grid;grid-template-columns:1fr 1fr;gap:6px">
<select id=editpat style="grid-column:1/-1"></select>
<input name=lName placeholder="Last"><input name=fName placeholder="First">
<input name=preferred placeholder="Preferred"><input name=birthdate type=date>
<input name=hmPhone placeholder="Home phone"><input name=wirelessPhone placeholder="Cell">
<input name=email placeholder="Email" style="grid-column:1/-1">
<input name=address placeholder="Address" style="grid-column:1/-1">
<input name=city placeholder="City"><input name=state placeholder="ST" maxlength=2>
<input name=zip placeholder="Zip"><button>Save</button>
<span id=editmsg style="font-size:12px;opacity:.7;grid-column:1/-1"></span></form></section>
<section><h2>Schedule — day view</h2>
<form id=schedform style="margin-bottom:8px"><input id=schedday type=date>
<button type=button id=schedload>Load</button></form>
<table id=sched><thead><tr><th>Time</th><th>Len</th><th>Patient</th><th>Prov</th><th>Status</th></tr></thead><tbody></tbody></table>
<form id=apptnew style="margin-top:8px"><select id=apptpat></select><input id=appttime type=time value="09:00">
<input id=apptmin type=number value=30 min=5 step=5 style="width:60px">
<button>Book</button><span id=apptmsg style="font-size:12px;opacity:.7"></span></form></section>
</main>
<script>
async function load(){
 const pats=await (await fetch('/api/patients')).json();
 document.querySelector('#pats tbody').innerHTML=pats.map(p=>`<tr><td>${p.patNum}</td><td>${p.name}</td><td>${p.birthdate}</td><td>${p.provider}</td></tr>`).join('');
 const appts=await (await fetch('/api/appointments')).json();
 document.querySelector('#appts tbody').innerHTML=appts.map(a=>`<tr><td>${a.dateTime}</td><td>${a.patient}</td><td>${a.provider}</td><td><span class=tag>${a.status}</span></td></tr>`).join('');
}
async function loadScan(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('scanpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 const srcs=await (await fetch('/api/scan/sources')).json();
 document.getElementById('scansrc').innerHTML=srcs.filter(s=>s.kind!='upload').map(s=>`<option value="${s.id}">${s.name}</option>`).join('');
 loadDocs();
}
async function loadDocs(){
 const patNum=document.getElementById('scanpat').value;if(!patNum)return;
 const docs=await (await fetch(`/api/patients/${patNum}/documents`)).json();
 document.querySelector('#docs tbody').innerHTML=docs.map(d=>`<tr><td>${d.docNum}</td><td>${d.fileName}</td><td><span class=tag>${d.imgType}</span></td><td>${d.created}</td><td><a href="/api/documents/${d.docNum}/file" target=_blank>view</a></td></tr>`).join('');
}
document.getElementById('scanpat').addEventListener('change',loadDocs);
document.getElementById('scanform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('scanmsg');msg.textContent='Scanning…';
 const r=await fetch('/api/scan/acquire',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({source:document.getElementById('scansrc').value,patNum:+document.getElementById('scanpat').value})});
 msg.textContent=r.ok?'Scanned.':'Scan failed: '+await r.text();loadDocs();
});
document.getElementById('upbtn').addEventListener('click',()=>document.getElementById('upfile').click());
document.getElementById('upfile').addEventListener('change',async e=>{
 const f=e.target.files[0];if(!f)return;const msg=document.getElementById('scanmsg');msg.textContent='Uploading…';
 const patNum=document.getElementById('scanpat').value;
 const r=await fetch(`/api/scan/upload?patNum=${patNum}&filename=${encodeURIComponent(f.name)}`,{method:'POST',body:f});
 msg.textContent=r.ok?'Uploaded.':'Upload failed';e.target.value='';loadDocs();
});
/* Signature pad: record strokes as OD's "x,y;x,y" one-decimal point stream;
   pen lift encoded as a "0,0" point, matching OpenDentBusiness.UI.SignatureBox. */
const pad=document.getElementById('sigpad'),ctx=pad.getContext('2d');
ctx.lineWidth=2;ctx.lineCap='round';ctx.strokeStyle=getComputedStyle(document.body).color;
let sigPts=[],drawing=false,last=null;
const fmt=n=>(Math.round(n*10)/10).toString();
function padPos(e){const r=pad.getBoundingClientRect();return{x:e.clientX-r.left,y:e.clientY-r.top}}
pad.addEventListener('pointerdown',e=>{drawing=true;last=padPos(e);sigPts.push(last);pad.setPointerCapture(e.pointerId)});
pad.addEventListener('pointermove',e=>{if(!drawing)return;const p=padPos(e);
 ctx.beginPath();ctx.moveTo(last.x,last.y);ctx.lineTo(p.x,p.y);ctx.stroke();last=p;sigPts.push(p)});
pad.addEventListener('pointerup',()=>{drawing=false;sigPts.push({x:0,y:0})});
document.getElementById('sigclear').addEventListener('click',()=>{sigPts=[];ctx.clearRect(0,0,pad.width,pad.height)});
document.getElementById('sigform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('sigmsg');
 const pts=sigPts.map(p=>fmt(p.x)+','+fmt(p.y)).join(';');
 if(!pts){msg.textContent='Draw a signature first.';return}
 msg.textContent='Signing…';
 const patNum=document.getElementById('sigpat').value;
 const r=await fetch(`/api/patients/${patNum}/consent`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({points:pts})});
 if(!r.ok){msg.textContent='Failed: '+await r.text();return}
 const {sheetNum}=await r.json();
 msg.innerHTML=`Sheet ${sheetNum} saved — <a href="/api/sheets/${sheetNum}/pdf" target=_blank>signed PDF</a>`;
});
async function loadSigPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('sigpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
}
loadSigPats();
loadScan();
/* Tooth chart: Universal numbering; posterior teeth get a 5-surface cross (B/L/M/D/O),
   colored from the practice's ChartGraphicColors defs or the proc code's GraphicColor. */
const POSTERIOR=n=>(n>=1&&n<=5)||(n>=12&&n<=21&&n!=22)||(n>=28&&n<=32)||(n>=12&&n<=16);
let chartData=null;
function surfKeys(surf){return surf.toUpperCase().replace(/F|V/g,'B').replace(/I/g,'O').split('').filter(c=>'MODBL'.includes(c))}
function toothSvg(n,x,y){
 const d=chartData,w=44,h=100;
 const procs=d.procs.filter(p=>p.toothNum==n);
 const isMissing=d.missing.includes(String(n))||d.missing.includes(n);
 const fillFor=p=>p.color||d.statusColors[p.status.toLowerCase()]||'#888';//JSON camelCases the key names
 // surface rects: B top, O middle, L bottom, M left, D right
 const sw=12,cx=x+w/2-sw/2,cy=y+40;
 const surfaces={B:[cx,cy-14],O:[cx,cy],L:[cx,cy+14],M:[cx-14,cy],D:[cx+14,cy]};
 let paint={};procs.forEach(p=>{const ks=surfKeys(p.surf);(ks.length?ks:['O']).forEach(k=>paint[k]=fillFor(p))});
 let s=`<g data-tooth="${n}" style="cursor:pointer">`;
 s+=`<rect x=${x+4} y=${y+22} width=${w-8} height=56 rx=9 fill="none" stroke="${isMissing?'color-mix(in srgb,CanvasText 25%,transparent)':'CanvasText'}" stroke-width=1.2 />`;
 for(const k in surfaces){const [sx,sy]=surfaces[k];
  s+=`<rect x=${sx} y=${sy} width=${sw} height=12 fill="${paint[k]||'transparent'}" stroke="color-mix(in srgb,CanvasText 30%,transparent)" stroke-width=.5 />`}
 if(isMissing)s+=`<line x1=${x+6} y1=${y+26} x2=${x+w-6} y2=${y+74} stroke=CanvasText stroke-width=2 /><line x1=${x+w-6} y1=${y+26} x2=${x+6} y2=${y+74} stroke=CanvasText stroke-width=2 />`;
 s+=`<text x=${x+w/2} y=${y+94} text-anchor=middle font-size=11 fill=CanvasText>${n}</text></g>`;
 return s}
function renderChart(){
 if(!chartData)return;let s='';
 for(let i=0;i<16;i++)s+=toothSvg(i+1,10+i*51,0);          // upper 1..16
 for(let i=0;i<16;i++)s+=toothSvg(32-i,10+i*51,130);       // lower 32..17
 document.getElementById('toothsvg').innerHTML=s;
 document.querySelectorAll('#toothsvg g').forEach(g=>g.addEventListener('click',()=>{
  const n=g.dataset.tooth,procs=chartData.procs.filter(p=>p.toothNum==n);
  document.getElementById('toothdetail').innerHTML=`<b>Tooth ${n}</b>: `+
   (procs.length?procs.map(p=>`${p.code} ${p.surf||''} <span class=tag>${p.status}</span> ${p.descript}`).join(' &middot; '):'no procedures');
 }));
}
async function loadChartPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('chartpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
}
document.getElementById('chartload').addEventListener('click',async()=>{
 const patNum=document.getElementById('chartpat').value;
 chartData=await (await fetch(`/api/patients/${patNum}/toothchart`)).json();renderChart();
 document.getElementById('chartmsg').textContent=`${chartData.procs.length} procedures, ${chartData.missing.length} missing teeth`;
 document.getElementById('serverchart').src=`/api/patients/${patNum}/toothchart.png?t=${Date.now()}`;
 document.getElementById('serversvg').href=`/api/patients/${patNum}/toothchart.svg`;
});
document.getElementById('chartseed').addEventListener('click',async()=>{
 const patNum=document.getElementById('chartpat').value;
 await fetch(`/api/patients/${patNum}/toothchart/demo`,{method:'POST'});
 document.getElementById('chartload').click();
});
async function loadReports(){
 const reps=await (await fetch('/api/reports')).json();
 document.getElementById('repsel').innerHTML=reps.map(r=>`<option value=${r.id}>${r.name}</option>`).join('');
 const d=new Date();document.getElementById('repto').valueAsDate=d;
 const f=new Date();f.setDate(f.getDate()-30);document.getElementById('repfrom').valueAsDate=f;
}
document.getElementById('repform').addEventListener('submit',async e=>{
 e.preventDefault();const msg=document.getElementById('repmsg');msg.textContent='Running…';
 const id=document.getElementById('repsel').value;
 const r=await fetch(`/api/reports/${id}?from=${document.getElementById('repfrom').value}&to=${document.getElementById('repto').value}`);
 if(!r.ok){msg.textContent='Failed: '+await r.text();return}
 const rep=await r.json();msg.textContent=`${rep.rows.length} rows`;
 document.querySelector('#report thead').innerHTML='<tr>'+rep.columns.map(c=>`<th>${c}</th>`).join('')+'</tr>';
 document.querySelector('#report tbody').innerHTML=rep.rows.map(r=>'<tr>'+r.map(v=>`<td>${v}</td>`).join('')+'</tr>').join('');
});
async function loadEditPats(){
 const pats=await (await fetch('/api/patients')).json();
 document.getElementById('editpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 document.getElementById('apptpat').innerHTML=pats.map(p=>`<option value=${p.patNum}>${p.name}</option>`).join('');
 fillEditForm();
}
async function fillEditForm(){
 const patNum=document.getElementById('editpat').value;if(!patNum)return;
 const d=await (await fetch(`/api/patients/${patNum}/details`)).json();
 const f=document.getElementById('patedit');
 for(const k of ['lName','fName','preferred','birthdate','hmPhone','wirelessPhone','email','address','city','state','zip'])
  if(f.elements[k])f.elements[k].value=d[k]||'';
}
document.getElementById('editpat').addEventListener('change',fillEditForm);
document.getElementById('patedit').addEventListener('submit',async e=>{
 e.preventDefault();const f=e.target,patNum=document.getElementById('editpat').value;
 const body={};for(const k of ['lName','fName','preferred','birthdate','hmPhone','wirelessPhone','email','address','city','state','zip'])
  body[k]=f.elements[k].value;
 const r=await fetch(`/api/patients/${patNum}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 document.getElementById('editmsg').textContent=r.ok?'Saved (audit-logged).':'Save failed';load();
});
async function loadSched(){
 const day=document.getElementById('schedday').value||new Date().toISOString().slice(0,10);
 const appts=await (await fetch(`/api/schedule?date=${day}`)).json();
 document.querySelector('#sched tbody').innerHTML=appts.map(a=>
  `<tr><td>${a.time}</td><td>${a.minutes}m</td><td>${a.patient}</td><td>${a.provider}</td><td><span class=tag>${a.status}</span></td></tr>`).join('');
}
document.getElementById('schedload').addEventListener('click',loadSched);
document.getElementById('apptnew').addEventListener('submit',async e=>{
 e.preventDefault();const day=document.getElementById('schedday').value||new Date().toISOString().slice(0,10);
 const r=await fetch('/api/appointments',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({patNum:+document.getElementById('apptpat').value,
   dateTime:day+'T'+document.getElementById('appttime').value,minutes:+document.getElementById('apptmin').value,note:''})});
 document.getElementById('apptmsg').textContent=r.ok?'Booked.':'Failed';loadSched();
});
document.getElementById('schedday').valueAsDate=new Date();
loadReports();loadEditPats();loadSched();
loadChartPats();
document.getElementById('addpat').addEventListener('submit',async e=>{
 e.preventDefault();const f=new FormData(e.target);
 await fetch('/api/patients',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({lName:f.get('lname'),fName:f.get('fname'),birthdate:f.get('bd')})});
 e.target.reset();load();
});
load();
</script></body></html>
""";
}
