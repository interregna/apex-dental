// Exercises startup validation and the real HTTP boundaries without a database.
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using OpenDentalWebDemo.Modules;

string[] scenarios={"security","local","local-ipv6","external","engine-snapshot","http-external","http-loopback-service","http-ipv6-service","whisper-external","embedded-credentials","query","engine-mismatch","engine-no-url","engine-no-token","engine-unknown","redirect","health-identity","health-error","health-invalid-json","health-service-failure","service-unconfigured","service-insecure","service-traversal","service-no-token","service-success","service-error","service-redirect","service-isolation","service-explicit-enable","service-legacy-only"};
string[] rejected={"http-external","http-loopback-service","http-ipv6-service","whisper-external","embedded-credentials","query","engine-mismatch","engine-no-url","engine-no-token","engine-unknown"};
if(args.Length==0) {
    foreach(string scenario in scenarios) {
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(scenario);
        using var process=Process.Start(start)!;
        await process.WaitForExitAsync();
        if(process.ExitCode!=0) { throw new Exception("Failed connector scenario: "+scenario); }
    }
    Console.WriteLine($"Passed {scenarios.Length} connector boundary scenarios.");
    return;
}
string test=args[0];
if(test=="security") { await SecurityChecks.Run(); return; }
if(test.StartsWith("service-")) { await ServiceChecks.Run(test); return; }
Environment.SetEnvironmentVariable("TRANSCRIPTION_ENGINE",null);
Environment.SetEnvironmentVariable("WHISPER_URL",null);
Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_URL",null);
Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_TOKEN","synthetic-test-token");
if(test.StartsWith("health-")) { await TestHealthRoute(test); return; }
static void UseService(string url) {
    Environment.SetEnvironmentVariable("TRANSCRIPTION_ENGINE","service");
    Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_URL",url);
}
switch(test) {
    case "local-ipv6": Environment.SetEnvironmentVariable("WHISPER_URL","http://[::1]:8771"); break;
    case "external": case "engine-snapshot": UseService("https://example.invalid/service"); break;
    case "http-external": UseService("http://example.invalid"); break;
    case "http-loopback-service": UseService("http://127.0.0.1:8771"); break;
    case "http-ipv6-service": UseService("http://[::1]:8771"); break;
    case "whisper-external": Environment.SetEnvironmentVariable("WHISPER_URL","https://example.invalid"); break;
    case "embedded-credentials": UseService("https://user:password@example.invalid"); break;
    case "query": UseService("https://example.invalid?token=example"); break;
    case "engine-mismatch": Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_URL","https://example.invalid"); break;
    case "engine-no-url": Environment.SetEnvironmentVariable("TRANSCRIPTION_ENGINE","service"); break;
    case "engine-no-token":
        UseService("https://example.invalid");
        Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_TOKEN",null);
        break;
    case "engine-unknown": Environment.SetEnvironmentVariable("TRANSCRIPTION_ENGINE","cloud"); break;
}
HttpListener? listener=null;
if(test=="redirect") {
    listener=StartLoopbackListener(out string url);
    //Both engines use the same HTTP transport; exercise redirect handling locally.
    Environment.SetEnvironmentVariable("WHISPER_URL",url);
}
try {
    var type=typeof(DictationModule);
    if(rejected.Contains(test)) {
        //Map must fail before it registers any route or accesses the null app.
        type.GetMethod("Map")!.Invoke(null,new object?[]{null});
        throw new Exception("Unsafe configuration was accepted at startup");
    }
    var factory=type.GetMethod("ServiceRequest",BindingFlags.NonPublic|BindingFlags.Static)!;
    using var request=(HttpRequestMessage)factory.Invoke(null,new object[]{HttpMethod.Get,"/health"})!;
    if((test=="local" || test=="local-ipv6") && (request.Headers.Authorization!=null || !request.RequestUri!.IsLoopback)) {
        throw new Exception("Local engine received service credentials or external URL");
    }
    if(test=="external" || test=="engine-snapshot") {
        if(request.Headers.Authorization?.Parameter!="synthetic-test-token"
            || request.RequestUri!.AbsoluteUri!="https://example.invalid/service/health") {
            throw new Exception("External service contract was not honored");
        }
    }
    if(test=="engine-snapshot") {
        Environment.SetEnvironmentVariable("TRANSCRIPTION_ENGINE","local");
        Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_URL",null);
        Environment.SetEnvironmentVariable("TRANSCRIPTION_SERVICE_TOKEN","changed-token");
        using var later=(HttpRequestMessage)factory.Invoke(null,new object[]{HttpMethod.Get,"/health"})!;
        if(later.RequestUri!=request.RequestUri || later.Headers.Authorization?.Parameter!="synthetic-test-token") {
            throw new Exception("Engine configuration was reloaded during the process lifetime");
        }
    }
    if(test=="redirect") {
        var client=(HttpClient)type.GetField("Http",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var responseTask=client.SendAsync(request);
        var context=await listener!.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if(context.Request.Headers["Authorization"]!=null) { throw new Exception("Local engine received a token"); }
        context.Response.StatusCode=302;
        context.Response.RedirectLocation="http://127.0.0.1:1/must-not-follow";
        context.Response.Close();
        using var response=await responseTask;
        if(response.StatusCode!=HttpStatusCode.Redirect) { throw new Exception("Redirect was followed"); }
    }
}
catch(TargetInvocationException error) when(error.InnerException is TypeInitializationException initialization
        && initialization.InnerException is InvalidOperationException && rejected.Contains(test)) {
    //Invalid configuration fails during route registration, before requests/audio.
}
finally { listener?.Close(); }

static HttpListener StartLoopbackListener(out string url) {
    var probe=new TcpListener(IPAddress.Loopback,0);
    probe.Start();
    int port=((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    url=$"http://127.0.0.1:{port}";
    var listener=new HttpListener();
    listener.Prefixes.Add(url+"/");
    listener.Start();
    return listener;
}

static async Task TestHealthRoute(string scenario) {
    const string privateMarker="synthetic-private-host.invalid";
    HttpListener? engine=null;
    Task? engineResponse=null;
    bool external=scenario=="health-service-failure";
    if(external) {
        //Closed loopback port: no network or external service is involved.
        UseService("https://127.0.0.1:1/private-service-path");
    }
    else {
        engine=StartLoopbackListener(out string url);
        Environment.SetEnvironmentVariable("WHISPER_URL",url);
        engineResponse=Task.Run(async ()=> {
            var context=await engine.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if(context.Request.Headers["Authorization"]!=null) { throw new Exception("Local health check received a token"); }
            context.Response.StatusCode=scenario=="health-error"?502:200;
            using(var writer=new StreamWriter(context.Response.OutputStream)) {
                await writer.WriteAsync(scenario=="health-invalid-json"?privateMarker:
                    JsonSerializer.Serialize(new { ok=true,engine="service",offHost=true,host=privateMarker,error=privateMarker,model="synthetic" }));
            }
            context.Response.Close();
        });
    }
    var builder=WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Logging.ClearProviders();
    await using var app=builder.Build();
    app.Urls.Add("http://127.0.0.1:0");
    DictationModule.Map(app);
    await app.StartAsync();
    try {
        using var browser=new HttpClient { Timeout=TimeSpan.FromSeconds(10) };
        string body=await browser.GetStringAsync(app.Urls.Single()+"/api/dictation/health");
        using var response=JsonDocument.Parse(body);
        var status=response.RootElement;
        if(status.GetProperty("engine").GetString()!=(external?"service":"local")
            || status.GetProperty("offHost").GetBoolean()!=external
            || status.GetProperty("ok").GetBoolean()!=(scenario=="health-identity")) {
            throw new Exception("Remote response overrode configured identity or readiness");
        }
        if(external ? status.GetProperty("host").GetString()!="127.0.0.1" : status.GetProperty("host").ValueKind!=JsonValueKind.Null) {
            throw new Exception("Unexpected engine host disclosure");
        }
        if(body.Contains(privateMarker) || body.Contains("private-service-path")) {
            throw new Exception("Remote error or network details leaked into health response");
        }
        if(engineResponse!=null) { await engineResponse; }
    }
    finally {
        await app.StopAsync();
        engine?.Close();
    }
}
