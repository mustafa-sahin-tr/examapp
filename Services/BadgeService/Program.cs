


using BadgeService;
using BadgeService.Consumers;
using BadgeService.Data;
using BadgeService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using BadgeService.Hubs;
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

var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 8006); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

// Badge services
builder.Services.AddScoped<AnswerSubmissionAggregationService>();
builder.Services.AddScoped<BadgeEvaluator>();
builder.Services.AddScoped<StudentReportService>();

// Badge DbContext
builder.Services.AddDbContext<BadgeDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"]; // Keycloak realm URL (Ocelot üzerinden)
        options.Audience = builder.Configuration["Keycloak:Audience"]; // Angular'ın login olduğu clientId
        options.MetadataAddress = "http://keycloak:8080/realms/exam-realm/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false;

        // SignalR bağlantısı için token'ı query string'den çek
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                // Bu istek SignalR Hub ise token'ı burada yakala
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/badges"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();



builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnswerSubmittedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("badge-service", e =>
        {
            e.ConfigureConsumer<AnswerSubmittedConsumer>(context);
        });
    });
});



builder.Services.AddSignalR();




var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BadgeDbContext>();
    await dbContext.Database.MigrateAsync();
    await BadgeSeeder.SeedAsync(dbContext);
}

app.MapHub<BadgeNotificationHub>("/hub/badges");
app.MapControllers();

app.Run();
