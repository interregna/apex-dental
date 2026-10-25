// Exercises the actual optional service client with a loopback-only synthetic server.
using System.Net;
using System.Net.Sockets;
using OpenDentBusiness;
using Task=System.Threading.Tasks.Task;

static class ServiceChecks {
    public static async Task Run(string scenario) {
        foreach(string capability in new[]{"MAIL","STORAGE","TRANSFER","TAX"}) {
            foreach(string setting in new[]{"ENABLED","URL","TOKEN"}) {
                Environment.SetEnvironmentVariable("APEX_"+capability+"_"+setting,null);
            }
        }
        Environment.SetEnvironmentVariable("APEX_SERVICE_URL",null);
        Environment.SetEnvironmentVariable("APEX_SERVICE_TOKEN",null);
        void Configure(string capability,string url,string? token="synthetic-service-token") {
            Environment.SetEnvironmentVariable("APEX_"+capability+"_ENABLED","true");
            Environment.SetEnvironmentVariable("APEX_"+capability+"_URL",url);
            Environment.SetEnvironmentVariable("APEX_"+capability+"_TOKEN",token);
        }
        if(scenario=="service-unconfigured") {
            Expect<InvalidOperationException>(()=>PracticeServiceClient.ServiceUri("mail/send"));
            return;
        }
        if(scenario=="service-insecure") {
            Configure("MAIL","http://example.invalid");
            Expect<InvalidOperationException>(()=>PracticeServiceClient.ServiceUri("mail/send"));
            return;
        }
        Configure("MAIL","http://127.0.0.1:1","synthetic-mail-token");
        if(scenario=="service-traversal") {
            foreach(string operation in new[]{"../private","mail/../../private","https://example.invalid","mail/send?token=example"}) {
                Expect<ArgumentException>(()=>PracticeServiceClient.ServiceUri(operation));
            }
            return;
        }
        if(scenario=="service-isolation") {
            PracticeServiceClient.ServiceUri("mail/send");
            foreach(string operation in new[]{"storage/exists","tax/estimate","transfer/upload"}) {
                Expect<InvalidOperationException>(()=>PracticeServiceClient.ServiceUri(operation));
            }
            return;
        }
        if(scenario=="service-explicit-enable") {
            Environment.SetEnvironmentVariable("APEX_MAIL_ENABLED",null);
            Expect<InvalidOperationException>(()=>PracticeServiceClient.ServiceUri("mail/send"));
            return;
        }
        if(scenario=="service-legacy-only") {
            Environment.SetEnvironmentVariable("APEX_MAIL_ENABLED",null);
            Environment.SetEnvironmentVariable("APEX_SERVICE_URL","http://127.0.0.1:1");
            Environment.SetEnvironmentVariable("APEX_SERVICE_TOKEN","unused-global-token");
            Expect<InvalidOperationException>(()=>PracticeServiceClient.ServiceUri("mail/send"));
            return;
        }
        if(scenario=="service-no-token") {
            Configure("STORAGE","http://127.0.0.1:1",null);
            Expect<InvalidOperationException>(()=>PracticeServiceClient.Call<bool>("storage/exists",new {path="synthetic"}));
            return;
        }
        var probe=new TcpListener(IPAddress.Loopback,0);
        probe.Start();
        int port=((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var server=new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        Configure("STORAGE",$"http://127.0.0.1:{port}/base");
        var serve=Task.Run(async ()=> {
            var context=await server.GetContextAsync();
            if(context.Request.Url!.AbsolutePath!="/base/v1/storage/exists"
                || context.Request.Headers["Authorization"]!="Bearer synthetic-service-token") {
                throw new Exception("Service routing or authorization changed");
            }
            using var reader=new StreamReader(context.Request.InputStream);
            string body=await reader.ReadToEndAsync();
            if(body!="{\"path\":\"synthetic\"}") { throw new Exception("Unexpected service payload"); }
            context.Response.StatusCode=scenario=="service-error"?502:scenario=="service-redirect"?302:200;
            if(scenario=="service-redirect") { context.Response.RedirectLocation="http://127.0.0.1:1/must-not-follow"; }
            using var writer=new StreamWriter(context.Response.OutputStream);
            await writer.WriteAsync(scenario=="service-success"?"true":"private-service-details");
        });
        if(scenario=="service-success") {
            if(!PracticeServiceClient.Call<bool>("storage/exists",new {path="synthetic"})) { throw new Exception("Service result changed"); }
        }
        else {
            try {
                PracticeServiceClient.Call<bool>("storage/exists",new {path="synthetic"});
                throw new Exception("Unexpected service success");
            }
            catch(HttpRequestException error) {
                if(error.Message.Contains("private-service-details") || error.Message.Contains("synthetic-service-token")) {
                    throw new Exception("Service response or token leaked into local error");
                }
                if(!error.Message.Contains(scenario=="service-error"?"502":"302")) { throw; }
            }
        }
        await serve;
    }
    private static void Expect<T>(Action action) where T:Exception {
        try { action(); }
        catch(T) { return; }
        throw new Exception("Expected "+typeof(T).Name);
    }
}
