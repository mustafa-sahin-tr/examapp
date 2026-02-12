using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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
        options.Authority = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}";
        options.Audience = "account";
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://staging.hedefokul.com/realms/exam-realm"
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                Console.WriteLine($"[JWT] OnMessageReceived: Path={context.Request.Path}, Method={context.Request.Method}, AuthorizationHeader={authHeader}");
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    var token = authHeader.Substring("Bearer ".Length);
                    Console.WriteLine($"[JWT] Token (truncated): {token.Substring(0, Math.Min(30, token.Length))}...");
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var jwtToken = context.SecurityToken as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
                Console.WriteLine($"[JWT] OnTokenValidated: Path={context.Request.Path}, Method={context.Request.Method}");
                Console.WriteLine($"[JWT] Token validated. Subject: {jwtToken?.Subject}");
                Console.WriteLine($"[JWT] Issuer: {jwtToken?.Issuer}");
                Console.WriteLine($"[JWT] Audience: {string.Join(",", jwtToken?.Audiences ?? new string[0])}");
                Console.WriteLine($"[JWT] Expiration: {jwtToken?.ValidTo}");
                if (jwtToken != null)
                {
                    foreach (var claim in jwtToken.Claims)
                    {
                        if (claim.Type == "exp" || claim.Type == "iss" || claim.Type == "aud")
                            Console.WriteLine($"[JWT] Claim: {claim.Type} = {claim.Value}");
                    }
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"[JWT] OnAuthenticationFailed: Path={context.Request.Path}, Method={context.Request.Method}");
                Console.WriteLine($"[JWT] JWT ERROR: {context.Exception.Message}");
                if (context.Exception != null)
                {
                    Console.WriteLine($"[JWT] Exception Type: {context.Exception.GetType().FullName}");
                    Console.WriteLine($"[JWT] Exception: {context.Exception}");
                    if (context.Exception.InnerException != null)
                        Console.WriteLine($"[JWT] InnerException: {context.Exception.InnerException.Message}");
                    Console.WriteLine($"[JWT] StackTrace: {context.Exception.StackTrace}");
                }
                return Task.CompletedTask;
            }
        };
    });


// builder.Services
//     .AddAuthentication("Bearer")
//     .AddJwtBearer("Bearer", options =>
//     {
//         options.Authority = "https://staging.hedefokul.com/realms/exam-realm";
//         options.RequireHttpsMetadata = true;
//         options.Audience = "account";
//     });

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