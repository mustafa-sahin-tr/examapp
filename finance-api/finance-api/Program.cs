using Microsoft.EntityFrameworkCore;
using FinanceApi.Data;
using FinanceApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 📌 Kestrel için port değerini `appsettings.json` veya Environment Variable'dan al
var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 8005); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

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
builder.Services.AddScoped<IRealTimeDataService, YahooFinanceService>();
builder.Services.AddScoped<IWebScrapingService, WebScrapingService>();
// builder.Services.AddScoped<IPortfolioService, PortfolioService>();

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

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

app.UseCors("AllowAll");
app.UseRouting();
app.MapControllers();

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
