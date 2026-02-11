using Microsoft.EntityFrameworkCore;
using FinanceApi.Data;
using FinanceApi.Services;
using FinanceApi.Hubs;
using FinanceApi.Options;

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
        PrintSection(config, "EmailSettings");
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

// 📌 Kestrel için port değerini `appsettings.json` veya Environment Variable'dan al
var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 8005); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Finance API",
        Version = "v1",
        Description = "Finance management API for tracking assets, transactions, and portfolios"
    });
});

// Database
builder.Services.AddDbContext<FinanceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
builder.Services.AddScoped<IPortfolioPriceUpdateService, PortfolioPriceUpdateService>();
builder.Services.AddScoped<IRealTimeDataService, YahooFinanceService>();
builder.Services.AddScoped<IWebScrapingService, WebScrapingService>();
builder.Services.AddScoped<IAllowedCryptoService, AllowedCryptoService>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

// HttpClient for Yahoo Finance API
builder.Services.AddHttpClient<YahooFinanceService>(client =>
{
    client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
    client.DefaultRequestHeaders.Add("Connection", "keep-alive");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// HttpClient for Web Scraping
builder.Services.AddHttpClient<WebScrapingService>();

// HttpClient for Exchange Rate Service
builder.Services.AddHttpClient<ExchangeRateService>();

// SignalR
builder.Services.AddSignalR();

// Background Services
builder.Services.AddHostedService<PriceUpdateBackgroundService>();
builder.Services.AddHostedService<ProfitLossHistoryService>();

// CORS - SignalR için güncellendi
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("SignalRCors", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:SignalR:AllowedOrigins").Get<string[]>();
        var allowedOrigins = (configuredOrigins != null && configuredOrigins.Length > 0)
            ? configuredOrigins
            : new[] { "http://localhost:4200", "http://localhost:5678", "http://localhost:3000" };

        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Finance API V1");
        c.RoutePrefix = string.Empty; // Swagger UI at root
    });
}

app.UseCors("SignalRCors");
app.UseRouting();
app.MapControllers();
app.MapHub<PriceUpdateHub>("/priceUpdateHub");

// Database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    try
    {
        await context.Database.MigrateAsync();
        Console.WriteLine("✅ Database migrated successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database migration failed: {ex.Message}");
    }
}

app.Run();
