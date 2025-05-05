using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 5678); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});


builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddAuthentication()
    .AddJwtBearer("TestKey", options =>
    {
        options.Authority = "http://localhost:5678/realms/exam-realm"; // Keycloak
        options.Audience = "account";
        options.RequireHttpsMetadata = false;
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"JWT ERROR: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddOcelot();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Request.Headers["X-Forwarded-For"] = context.Connection.RemoteIpAddress?.ToString();
    context.Request.Headers["X-Forwarded-Proto"] = context.Request.Scheme;
    context.Request.Headers["X-Forwarded-Port"] = context.Request.Host.Port?.ToString() ?? "80";
    context.Request.Headers["X-Forwarded-Host"] = context.Request.Host.Host;

    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/oidc-login")
    {
        context.Response.Redirect("http://localhost:5678/auth/realms/exam-realm/protocol/openid-connect/auth?client_id=exam-client&redirect_uri=http://localhost:5678/callback&response_type=code&scope=openid");
        return;
    }

    await next();
});


await app.UseOcelot();

app.Run();