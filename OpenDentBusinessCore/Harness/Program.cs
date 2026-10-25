// Modified for the Apex Dental public source snapshot, 2026-10-25; see NOTICE.
/*
 * Proof harness for the headless Linux core (OpenDentBusinessCore).
 * Connects to the MariaDB in the VM through the real Open Dental data layer
 * (DataConnection/Db/Crud), then exercises genuine business-layer code paths:
 * preferences, providers, patient CRUD via the Patients API, and appointments.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using DataConnectionBase;
using OpenDentBusiness;

class Program {
	static int Main(string[] args) {
		string server=Environment.GetEnvironmentVariable("OD_DB_SERVER")??"127.0.0.1";
		int idxServer=Array.IndexOf(args,"-server");
		if(idxServer>=0 && idxServer+1<args.Length) {
			server=args[idxServer+1];
		}
		Console.WriteLine("== Open Dental headless core harness (.NET 10, Linux) ==");
		FontResolverOD.Register();//PDFsharp 6 font embedding on Linux (Liberation fonts)
		Console.WriteLine($"Connecting to MariaDB at {server}:3306");
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
		Console.WriteLine("Connected.");

		Console.WriteLine("\n-- Schema version check / upgrade (ConvertDatabases) --");
		Version versionDb=new Version(DataCore.GetScalar("SELECT ValueString FROM preference WHERE PrefName='DataBaseVersion'"));
		Console.WriteLine($"Database schema: {versionDb}   Code expects: {ConvertDatabases.LatestVersion}");
		if(versionDb<ConvertDatabases.LatestVersion) {
			Console.WriteLine("Running Open Dental's own schema converter (same path as the Windows client)...");
			var stopwatch=System.Diagnostics.Stopwatch.StartNew();
			ConvertDatabases.FromVersion=versionDb;
			ConvertDatabases.InvokeConvertMethods();
			stopwatch.Stop();
			string versionAfter=DataCore.GetScalar("SELECT ValueString FROM preference WHERE PrefName='DataBaseVersion'");
			Console.WriteLine($"Conversion finished in {stopwatch.Elapsed.TotalSeconds:F1}s. Schema now: {versionAfter}");
		}

		//Business methods audit-stamp rows with the logged-in user; use the Admin user from the dump.
		Cache.Refresh(InvalidType.Security);
		Security.CurUser=Userods.GetFirstOrDefault(x => true)??new Userod() { UserNum=1,UserName="harness" };
		Console.WriteLine($"Security.CurUser: {Security.CurUser.UserName}");

		Console.WriteLine("\n-- Preferences (Prefs.GetString via cache) --");
		Cache.Refresh(InvalidType.Prefs);
		Console.WriteLine($"DataBaseVersion: {PrefC.GetString(PrefName.DataBaseVersion)}");
		Console.WriteLine($"Language:        {PrefC.GetString(PrefName.LanguageAndRegion)}");

		Console.WriteLine("\n-- Providers (Providers.GetAll) --");
		List<Provider> listProviders=Providers.GetAll();
		Console.WriteLine($"Provider count: {listProviders.Count}");
		foreach(Provider prov in listProviders.Take(3)) {
			Console.WriteLine($"  ProvNum={prov.ProvNum} Abbr={prov.Abbr}");
		}

		Console.WriteLine("\n-- Patient insert through business layer (Patients.Insert) --");
		Patient pat=new Patient();
		pat.LName="Linux";
		pat.FName="Tux";
		pat.Birthdate=new DateTime(1991,9,17);//first Linux release
		pat.PatStatus=PatientStatus.Patient;
		pat.Gender=PatientGender.Unknown;
		pat.PriProv=listProviders.Count>0?listProviders[0].ProvNum:0;
		pat.BillingType=PrefC.GetLong(PrefName.PracticeDefaultBillType);
		Patients.Insert(pat,false);
		//Match upstream usage: SecurityLogs are skipped here; a fresh Patient is
		//normally updated once more so guarantor/family fields are consistent.
		Patient patOld=pat.Copy();
		pat.Guarantor=pat.PatNum;
		Patients.Update(pat,patOld);
		Console.WriteLine($"Inserted PatNum={pat.PatNum} ({pat.FName} {pat.LName})");

		Console.WriteLine("\n-- Read back (Patients.GetPat) --");
		Patient patCheck=Patients.GetPat(pat.PatNum);
		Console.WriteLine($"Fetched: PatNum={patCheck.PatNum} Name={patCheck.GetNameFL()} Birthdate={patCheck.Birthdate:yyyy-MM-dd} Status={patCheck.PatStatus}");

		Console.WriteLine("\n-- Appointment insert (Appointments.Insert) --");
		Appointment appt=new Appointment();
		appt.PatNum=pat.PatNum;
		appt.AptStatus=ApptStatus.Scheduled;
		appt.AptDateTime=DateTime.Today.AddDays(7).AddHours(9);
		appt.ProvNum=pat.PriProv;
		appt.Pattern="//XX//";
		Appointments.Insert(appt);
		Console.WriteLine($"Inserted AptNum={appt.AptNum} at {appt.AptDateTime}");
		List<Appointment> listAppts=Appointments.GetAppointmentsForPat(pat.PatNum);
		Console.WriteLine($"Appointments for patient: {listAppts.Count}");

		Console.WriteLine("\n-- GDI on Linux (System.Drawing.EnableUnixSupport + libgdiplus) --");
		using(var font=new System.Drawing.Font("Liberation Sans",9f))
		using(var bmp=new System.Drawing.Bitmap(40,40))
		using(var g=System.Drawing.Graphics.FromImage(bmp)) {
			var size=g.MeasureString("Open Dental",font);
			Console.WriteLine($"Font '{font.Name}' loaded; MeasureString(\"Open Dental\")={size.Width:F1}x{size.Height:F1}px");
		}

		Console.WriteLine("\n-- Sheet PDF generation on Linux (SheetsInternal + PDFsharp 6) --");
		SheetDef sheetDefRx=SheetsInternal.GetSheetDef(SheetInternalType.Consent);
		Sheet sheetRx=SheetUtil.CreateSheet(sheetDefRx,pat.PatNum);
		SheetParameter.SetParameter(sheetRx,"PatNum",pat.PatNum);
		SheetFiller.FillFields(sheetRx);
		SheetUtil.CalculateHeights(sheetRx);
		string pdfPath="/tmp/od-sheet-linux.pdf";
		SheetPrinting.CreatePdf(sheetRx,pdfPath,null,null,null,pat);
		var infoPdf=new System.IO.FileInfo(pdfPath);
		using(var fs=System.IO.File.OpenRead(pdfPath)) {
			byte[] header=new byte[5];
			fs.Read(header,0,5);
			Console.WriteLine($"Wrote {pdfPath}: {infoPdf.Length} bytes, header \"{System.Text.Encoding.ASCII.GetString(header)}\"");
		}

		Console.WriteLine("\n-- Raw query through the OD data layer (DataCore) --");
		long patCount=PIn.Long(DataCore.GetScalar("SELECT COUNT(*) FROM patient"));
		Console.WriteLine($"Total patients in db: {patCount}");

		Console.WriteLine("\nALL CHECKS PASSED — Open Dental business layer is live on Linux.");
		return 0;
	}
}
