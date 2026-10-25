using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OpenDentalWebDemo;

public static class WebSecurity {
    public const long MaxBodyBytes=16L*1024*1024;
    public static void Configure(WebApplicationBuilder builder) {
        builder.WebHost.ConfigureKestrel(server => {
            server.AddServerHeader=false;
            server.Limits.MaxRequestBodySize=MaxBodyBytes;
            server.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout=TimeSpan.FromSeconds(30);
            server.Limits.MaxConcurrentConnections=100;
        });
        builder.Services.AddRateLimiter(options => {
            options.RejectionStatusCode=StatusCodes.Status429TooManyRequests;
            // A single bounded bucket avoids keeping attacker-controlled IP keys in memory.
            options.AddFixedWindowLimiter("login",limiter => {
                limiter.PermitLimit=10;
                limiter.Window=TimeSpan.FromMinutes(1);
                limiter.QueueLimit=0;
                limiter.AutoReplenishment=true;
            });
        });
    }
    public static void Use(WebApplication app) {
        app.Use(async (context,next) => {
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            context.Response.Headers["X-Frame-Options"]="DENY";
            context.Response.Headers["Referrer-Policy"]="no-referrer";
            if(context.Request.Path.StartsWithSegments("/api")) {
                context.Response.Headers["Cache-Control"]="no-store";
                if(context.Request.ContentLength>MaxBodyBytes) { context.Response.StatusCode=413; return; }
                if(context.Request.Method!="GET" && context.Request.Method!="HEAD"
                    && context.Request.Headers["Sec-Fetch-Site"]=="cross-site") {
                    context.Response.StatusCode=403; return;
                }
            }
            await next();
        });
        app.UseRouting();
        app.UseRateLimiter();
    }
}
