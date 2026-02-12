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

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

builder.Services.AddAuthentication()
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}";//  "http://localhost:5678/realms/exam-realm"; // Keycloak
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


builder.Services
    .AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = "https://staging.hedefokul.com/realms/exam-realm";
        options.RequireHttpsMetadata = true;
        options.Audience = "account";
    });

// CORS desteği SignalR için
builder.Services.AddCors(options =>
{
    options.AddPolicy("SignalRCors", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var allowedOrigins = (configuredOrigins != null && configuredOrigins.Length > 0)
            ? configuredOrigins
            : new[] { "http://localhost:4200", "http://localhost:4201", "http://localhost:3000" };

        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddOcelot();

var app = builder.Build();

app.UseCors("SignalRCors");
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
        var host = builder.Configuration.GetValue<string>("Server:BaseUrl");
        var realm = builder.Configuration.GetValue<string>("Keycloak:Realm");
        var ClientCallbackUrl = builder.Configuration.GetValue<string>("Keycloak:ClientCallbackUrl");
        context.Response.Redirect($"{host}/auth/realms/{realm}/protocol/openid-connect/auth?client_id=exam-client&redirect_uri={ClientCallbackUrl}");
        return;
    }

    await next();
});

app.UseWebSockets();    // WebSocket desteği SignalR için gerekli
await app.UseOcelot();

app.Run();