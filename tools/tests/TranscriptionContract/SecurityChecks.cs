using Microsoft.AspNetCore.Http;
using OpenDentBusiness;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using OpenDentalWebDemo;
using Task=System.Threading.Tasks.Task;
using OpenDentalWebDemo.Modules;

static class SecurityChecks {
    public static async Task Run() {
        Security.CurUser=null;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int waiting=0;
        var requests=Enumerable.Range(1,32).Select(id => Task.Run(async () => {
            Security.CurUser=new Userod { UserNum=id };
            if(Interlocked.Increment(ref waiting)==32) { ready.SetResult(); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            for(int i=0;i<10;i++) {
                await Task.Yield();
                if(Security.CurUser?.UserNum!=id) { throw new Exception("User identity crossed request contexts"); }
            }
            Security.CurUser=null;
        })).ToArray();
        await Task.WhenAll(requests);
        if(Security.CurUser!=null) { throw new Exception("User identity escaped to parent context"); }
        Plugins.LoadAllPlugins();
        if(Plugins.PluginsAreLoaded || Plugins.GetAssembly("synthetic")!=null || Plugins.HookMethod(null!,"synthetic")) {
            throw new Exception("Legacy plug-in loader is active");
        }
        bool rejected=false;
        try { Plugins.ListPlugins=new List<PluginContainer> { new PluginContainer() }; }
        catch(NotSupportedException) { rejected=true; }
        if(!rejected) { throw new Exception("Legacy plug-in injection was accepted"); }
        AuthModule.Sessions["expired"]=new AuthModule.Session(42,DateTimeOffset.UtcNow.AddSeconds(-1));
        if(AuthModule.TrySession("expired",out _) || AuthModule.Sessions.ContainsKey("expired")) {
            throw new Exception("Expired session remained valid");
        }
        AuthModule.Sessions["active"]=new AuthModule.Session(43,DateTimeOffset.UtcNow.AddMinutes(1));
        if(!AuthModule.TrySession("active",out long userNum) || userNum!=43) { throw new Exception("Valid session rejected"); }
        AuthModule.Sessions.TryRemove("active",out _);
        if(AuthModule.TrySession("active",out _) || AuthModule.TrySession(null!,out _)) { throw new Exception("Revoked or missing session accepted"); }
        var https=AuthModule.SessionCookie(true);
        var local=AuthModule.SessionCookie(false);
        if(!https.HttpOnly || !https.Secure || https.SameSite!=SameSiteMode.Strict || https.MaxAge!=TimeSpan.FromHours(8)
            || local.Secure || !local.HttpOnly || local.SameSite!=SameSiteMode.Strict) {
            throw new Exception("Session cookie protections regressed");
        }
        if(AuthModule.RequiredPerm("/API/PATIENTS/123","PUT")!=EnumPermType.PatientEdit
            || AuthModule.RequiredPerm("/api/patients","POST")!=EnumPermType.PatientCreate
            || AuthModule.RequiredPerm("/api/unmapped-sensitive-action","POST")!=EnumPermType.SecurityAdmin) {
            throw new Exception("Patient edits or unmapped APIs are under-protected");
        }
        string directory=Path.Combine(Path.GetTempPath(),"apex-zip-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            string archive=Path.Combine(directory,"report.zip");
            using(var zip=new CodeBase.ZipArchiveBuilder()) { zip.AddEntry("report.xml","synthetic"); zip.Save(archive); }
            using(var zip=new CodeBase.ZipArchiveBuilder(archive)) { zip.ExtractAll(Path.Combine(directory,"safe")); }
            if(File.ReadAllText(Path.Combine(directory,"safe/report.xml"))!="synthetic") { throw new Exception("ZIP round trip failed"); }
            string malicious=Path.Combine(directory,"malicious.zip");
            using(var stream=File.Create(malicious))
            using(var zip=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Create)) {
                using var writer=new StreamWriter(zip.CreateEntry("../escaped.txt").Open());
                writer.Write("synthetic");
            }
            bool traversalRejected=false;
            try { using var zip=new CodeBase.ZipArchiveBuilder(malicious); zip.ExtractAll(Path.Combine(directory,"unsafe")); }
            catch(IOException) { traversalRejected=true; }
            if(!traversalRejected || File.Exists(Path.Combine(directory,"escaped.txt"))) { throw new Exception("ZIP traversal accepted"); }
            if(OpenDentBusiness.Eclaims.DentiCal.Launch(null!,0) || OpenDentBusiness.Eclaims.MercuryDE.Launch(null!,0)) {
                throw new Exception("Retired SFTP transports are active");
            }
        }
        finally { Directory.Delete(directory,true); }
        foreach(string source in new[]{"escl|http://127.0.0.1:1/unconfigured","escl|http://example.invalid/unconfigured","malformed"}) {
            bool rejectedSource=false;
            try { await ScanService.Acquire(source); }
            catch(ArgumentException) { rejectedSource=true; }
            if(!rejectedSource) { throw new Exception("Unconfigured scanner destination accepted"); }
        }
        foreach(string job in new[]{"https://example.invalid/eSCL/ScanJobs/1","http://127.0.0.1:1/private","http://127.0.0.1:1/eSCL/ScanJobs/%2e%2e%2fprivate"}) {
            bool rejectedJob=false;
            try { ScanService.ValidateJob("http://127.0.0.1:1/eSCL",new Uri(job)); }
            catch(InvalidOperationException) { rejectedJob=true; }
            if(!rejectedJob) { throw new Exception("Unsafe scanner job destination accepted"); }
        }
        if(ScanService.ValidateJob("http://127.0.0.1:1/eSCL",new Uri("/eSCL/ScanJobs/123",UriKind.Relative)).Port!=1) {
            throw new Exception("Configured scanner job rejected");
        }
        var builder=WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        WebSecurity.Configure(builder);
        await using var app=builder.Build();
        WebSecurity.Use(app);
        AuthModule.UseAuth(app);
        app.MapPost("/api/auth/limit-test",() => "synthetic").RequireRateLimiting("login");
        app.MapGet("/api/auth/context-test",() => Security.CurUser==null?"isolated":"leaked");
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try {
            using var client=new HttpClient { BaseAddress=new Uri(app.Urls.Single()) };
            for(int i=0;i<11;i++) {
                using var response=await client.PostAsync("/api/auth/limit-test",null);
                int expected=i<10?200:429;
                if((int)response.StatusCode!=expected) { throw new Exception("Login throttling failed"); }
            }
            using var crossSite=new HttpRequestMessage(HttpMethod.Post,"/api/auth/limit-test");
            crossSite.Headers.Add("Sec-Fetch-Site","cross-site");
            using var blocked=await client.SendAsync(crossSite);
            if((int)blocked.StatusCode!=403) { throw new Exception("Cross-site write accepted"); }
            using var anonymous=await client.GetAsync("/api/synthetic-private-endpoint");
            if((int)anonymous.StatusCode!=401 || anonymous.Headers.CacheControl?.NoStore!=true
                || !anonymous.Headers.Contains("X-Content-Type-Options") || anonymous.Headers.Contains("Server")) {
                throw new Exception("Anonymous request or response protection failed");
            }
            using var upperCase=await client.GetAsync("/API/synthetic-private-endpoint");
            if((int)upperCase.StatusCode!=401) { throw new Exception("Case-varied API route bypassed authentication"); }
            if(await client.GetStringAsync("/api/auth/context-test")!="isolated") { throw new Exception("Anonymous identity inherited"); }
        }
        finally { await app.StopAsync(); }
        Console.WriteLine("Passed concurrent identity isolation, plug-in rejection, session expiration/revocation and cookie checks.");
    }
}
