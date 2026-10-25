// Optional HTTP service boundary. No provider SDK or service implementation lives here.
// Modified 2026-10-25; see NOTICE.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

namespace OpenDentBusiness {
    public static class PracticeServiceClient {
        private static readonly HttpClient Http=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) {
            Timeout=TimeSpan.FromSeconds(180)
        };
        private static readonly Dictionary<string,string> Capabilities=new Dictionary<string,string> {
            {"mail","APEX_MAIL"}, {"storage","APEX_STORAGE"},
            {"transfer","APEX_TRANSFER"}, {"tax","APEX_TAX"}
        };
        private sealed record Connection(Uri Uri,string Token);

        private static Connection Configure(string operation) {
            if(!System.Text.RegularExpressions.Regex.IsMatch(operation??"",@"\A[a-z][a-z0-9-]*/[a-z][a-z0-9-]*\z")) {
                throw new ArgumentException("Invalid service operation.",nameof(operation));
            }
            string capability=operation.Split('/')[0];
            if(!Capabilities.TryGetValue(capability,out string prefix)) {
                throw new ArgumentException("Unknown service capability.",nameof(operation));
            }
            if(Environment.GetEnvironmentVariable(prefix+"_ENABLED")!="true") {
                throw new InvalidOperationException("This optional capability is disabled; set "+prefix+"_ENABLED=true to enable it explicitly.");
            }
            string configured=Environment.GetEnvironmentVariable(prefix+"_URL");
            if(!Uri.TryCreate(configured,UriKind.Absolute,out Uri uri)
                || (uri.Scheme!="https" && !(uri.Scheme=="http" && uri.IsLoopback))
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)) {
                throw new InvalidOperationException(prefix+"_URL requires HTTPS or loopback HTTP without credentials, query, or fragment.");
            }
            string token=Environment.GetEnvironmentVariable(prefix+"_TOKEN");
            if(string.IsNullOrWhiteSpace(token)) {
                throw new InvalidOperationException(prefix+"_TOKEN is required for this capability.");
            }
            return new Connection(new Uri(configured.TrimEnd('/')+"/v1/"+operation),token);
        }

        public static Uri ServiceUri(string operation) => Configure(operation).Uri;

        public static T Call<T>(string operation,object payload) {
            Connection connection=Configure(operation);
            using var request=new HttpRequestMessage(HttpMethod.Post,connection.Uri);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",connection.Token);
            request.Content=new StringContent(JsonConvert.SerializeObject(payload),Encoding.UTF8,"application/json");
            using var response=Http.SendAsync(request).GetAwaiter().GetResult();
            if(!response.IsSuccessStatusCode) {
                throw new HttpRequestException($"Optional service returned HTTP {(int)response.StatusCode}.");
            }
            string body=response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonConvert.DeserializeObject<T>(body);
        }
    }
}
