using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

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
            // AsEnumerable includes the section root itself with null value; skip it.
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
        // Common for connection strings and redis config strings: key=value;key=value
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
                // keep original (but trim nothing)
                parts[i] = k + "=" + v;
            }
        }

        return string.Join(';', parts);
    }
}

// 📌 Kestrel için port değerini `appsettings.json` veya Environment Variable'dan al
var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 5079); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

var keycloakConfig = builder.Configuration.GetSection("Keycloak");

builder.Services.Configure<KeycloakSettings>(keycloakConfig);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = keycloakConfig["Authority"]; //  "http://localhost:5678/realms/exam-realm"; // Ocelot üzerinden erişilen Keycloak
        options.MetadataAddress = "http://keycloak:8080/realms/exam-realm/.well-known/openid-configuration";
        // options.TokenValidationParameters = new TokenValidationParameters
        // {
        //     ValidateIssuer = true,
        //     ValidIssuer = "http://localhost:5678/realms/exam-realm"
        // };
        options.Audience = "account"; // veya client_id değerin
        options.RequireHttpsMetadata = false;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                Console.WriteLine($"🔹 Raw token: {context.Request.Headers["Authorization"]}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var jwtToken = context.SecurityToken as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
                Console.WriteLine($"✅ Token validated. Subject: {jwtToken?.Subject}");
                Console.WriteLine($"🔐 Issuer: {jwtToken?.Issuer}");
                Console.WriteLine($"🕒 Expiration: {jwtToken?.ValidTo}");
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"❌ JWT ERROR: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var redisConfig = builder.Configuration.GetSection("Redis");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig["Configuration"];
    options.InstanceName = redisConfig["InstanceName"];
});



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IKeycloakService, KeycloakService>();
builder.Services.AddScoped<IClaimsTransformation, KeycloakRoleTransformer>();
builder.Services.AddSingleton<ImageHelper>();

// PostgreSQL & EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// builder.Services.AddHostedService<OutboxPublisher>();
// var rabbitConfig = builder.Configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>();
// builder.Services.AddMassTransit(x =>
// {
//     x.UsingRabbitMq((context, cfg) =>
//     {
//         cfg.Host(rabbitConfig.Host, "/", h =>
//          {
//              h.Username(rabbitConfig.Username);
//              h.Password(rabbitConfig.Password);
//          }); 
//     });
// });



var app = builder.Build();

//Seed Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate(); // Apply any pending migrations
        // Seed TopicSeed data everyitme the application starts       
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

