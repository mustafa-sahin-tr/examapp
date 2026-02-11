using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

static class StartupConfigDump
{
    public static void Print(IConfiguration config, string environmentName, int? kestrelPort = null)
    {
        Console.WriteLine("========================================");
        Console.WriteLine($"🚀 Effective configuration ({environmentName})");
        if (kestrelPort.HasValue)
        {
            Console.WriteLine($"Kestrel:Port = {kestrelPort.Value}");
        }

        PrintSection(config, "Keycloak");
        PrintSection(config, "ConnectionStrings");
        PrintSection(config, "Redis");
        PrintSection(config, "RabbitMQ");
        PrintSection(config, "MinioConfig");
        PrintSection(config, "Server");
        PrintSection(config, "Cors");

        Console.WriteLine("========================================");
    }

    private static void PrintSection(IConfiguration config, string rootKey)
    {
        var section = config.GetSection(rootKey);
        if (!section.GetChildren().Any())
        {
            return;
        }

        foreach (var pair in section.AsEnumerable(makePathsRelative: true))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            var fullKey = $"{rootKey}:{pair.Key}";
            Console.WriteLine($"{fullKey} = {Sanitize(fullKey, pair.Value)}");
        }
    }

    private static string Sanitize(string key, string value)
    {
        var lowerKey = key.ToLowerInvariant();
        if (lowerKey.Contains("password") ||
            lowerKey.Contains("secret") ||
            lowerKey.EndsWith(":key") ||
            lowerKey.Contains("token") ||
            lowerKey.Contains("apikey"))
        {
            return "***";
        }

        if (lowerKey.StartsWith("connectionstrings:"))
        {
            return RedactSemicolonKvp(value, new[] { "password", "pwd" });
        }

        if (lowerKey.Contains("redis:configuration"))
        {
            return RedactSemicolonKvp(value, new[] { "password" });
        }

        if (value.Length > 400)
        {
            return value.Substring(0, 400) + "…";
        }

        return value;
    }

    private static string RedactSemicolonKvp(string input, string[] sensitiveKeys)
    {
        var parts = input.Split(';', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var k = part.Substring(0, eq).Trim();
            var v = part.Substring(eq + 1);
            if (sensitiveKeys.Any(sk => string.Equals(sk, k, StringComparison.OrdinalIgnoreCase)))
            {
                parts[i] = k + "=***";
            }
            else
            {
                parts[i] = k + "=" + v;
            }
        }

        return string.Join(';', parts);
    }
}

var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 5678); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});


builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

builder.Services.AddAuthentication()
    .AddJwtBearer("TestKey", options =>
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