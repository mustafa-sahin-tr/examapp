using ExamApp.Api.Commands;
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

// Komut modu (issue #267): `dotnet run -- audit-privileged-users [--format table|csv|json]` — salt okunur yetkili
// hesap denetimi. Web host KURULMAZ (Kestrel/Redis/migration yok); yalnızca yapılandırma okunur, komut çalışır, çıkılır.
// Hiçbir şey yazmadığı için seed komutlarının aksine ortam guard'ı yoktur (Production'da da çalışır).
PrivilegedUserAuditCommand.Options? auditOptions = null;
if (PrivilegedUserAuditCommand.IsRequested(args))
{
    try
    {
        auditOptions = PrivilegedUserAuditCommand.Parse(args);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return PrivilegedUserAuditCommand.ExitUsage;
    }
    if (auditOptions.ShowHelp)
    {
        Console.WriteLine(PrivilegedUserAuditCommand.Usage);
        return PrivilegedUserAuditCommand.ExitOk;
    }
}

// Komut arg'ları IConfiguration'a sızmasın.
var builder = WebApplication.CreateBuilder(auditOptions is null ? args : []);

if (auditOptions is not null)
{
    return await PrivilegedUserAuditCommand.ExecuteAsync(builder.Configuration, auditOptions);
}

builder.AddServiceDefaults();

// 📌 Kestrel için port değerini `appsettings.json` veya Environment Variable'dan al
var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 5079); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

if (builder.Environment.IsDevelopment())
{
    StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);
}

var keycloakConfig = builder.Configuration.GetSection("Keycloak");

builder.Services.Configure<KeycloakSettings>(keycloakConfig);

// Issue #238: appsettings.json'daki Keycloak ClientSecret/AdminClientSecret artık
// gerçek değer taşımıyor ("" placeholder) — Development dışında boş secret ile
// sessizce 401/invalid_client alınmasın diye açılışta açıkça patlat. Development'ta
// docker-compose/.env veya Aspire AppHost parametreleri değeri zaten dolduruyor.
if (!builder.Environment.IsDevelopment())
{
    // "devOnly" öneki .env.example/AppHost'taki dev-only Keycloak secret'larının
    // ortak deseni (bkz. .env.example) — bunlar yanlışlıkla prod/staging'e
    // taşınmışsa da boş secret'la aynı şekilde reddedilir.
    static bool IsMissingOrDevOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("devOnly", StringComparison.OrdinalIgnoreCase);

    if (IsMissingOrDevOnly(keycloakConfig["ClientSecret"]) ||
        IsMissingOrDevOnly(keycloakConfig["AdminClientSecret"]))
    {
        throw new InvalidOperationException(
            "Keycloak:ClientSecret ve Keycloak:AdminClientSecret ortam değişkeninden (Keycloak__ClientSecret / Keycloak__AdminClientSecret) set edilmeli; Development dışında boş veya dev-only değer bırakılamaz.");
    }
}

// Register kabul yanıtı taban süresi (#240: kayıtlı/yeni e-posta yanıt süresinden ayırt edilemesin).
builder.Services.Configure<RegistrationSettings>(builder.Configuration.GetSection(RegistrationSettings.SectionName));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}";
        options.MetadataAddress = $"{builder.Configuration.GetValue<string>("Keycloak:Host")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}/.well-known/openid-configuration";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}"
        };
        options.Audience = "account"; // veya client_id değerin
        options.RequireHttpsMetadata = false;
        options.Events = new JwtBearerEvents
        {
            // Log only the failure reason, at Warning. Never the token or the
            // Authorization header.
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth.Jwt")
                    .LogWarning("JWT rejected on {Method} {Path}: {Reason}",
                        context.Request.Method, context.Request.Path, context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

var serviceClients = builder.Configuration.GetSection("Keycloak:ServiceClients").Get<string[]>();
builder.Services.AddAuthorization(options =>
{
    // Service-to-service only (issue #217: exam API'nin seed-teachers komutu) — BadgeService/exam API
    // ile aynı ortak karar noktası (ExamApp.Foundation.Security.ServicePrincipal).
    options.AddPolicy("Service", policy =>
        policy.RequireAssertion(context =>
            ExamApp.Foundation.Security.ServicePrincipal.IsService(context.User, serviceClients)));
});

// Login/exchange brute-force koruması (bkz. Helpers/AuthRateLimiting.cs). Forwarded-headers
// kaydı da burada: gateway arkasında limiter anahtarı gerçek istemci IP'si olmalı.
builder.Services.AddAuthForwardedHeaders(builder.Configuration);
builder.Services.AddAuthRateLimiting(builder.Configuration);

var redisConfig = builder.Configuration.GetSection("Redis");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig["Configuration"];
    options.InstanceName = redisConfig["InstanceName"];
});



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
// İstemciye giden login/exchange hata mesajları (issue #231): Resources/<alan>.<dil>.json + Accept-Language.
builder.Services.AddAuthLocalization();
// İşlenmemiş exception → log + genel ProblemDetails (Development dahil stack trace yok; bkz. Helpers/AuthErrorHandling.cs).
builder.Services.AddAuthErrorHandling();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
// Keycloak admin toplu işlemleri: standart resilience handler'dan muaf named client (gerekçe extension'da).
builder.Services.AddKeycloakAdminHttpClient();

builder.Services.AddSingleton<KeycloakAdminTokenCache>(); // admin token istekler arası paylaşılır (issue #152 review)
builder.Services.AddScoped<IKeycloakService, KeycloakService>();
// Dev-only toplu kullanıcı oluşturma (issue #217). YALNIZCA Development/Staging'de kayıt olur;
// Production'da DevSeedController servisi null çözümler ve 404 döner (ortam guard'ının ilk katmanı).
if (DevUserSeedService.IsAllowedEnvironment(builder.Environment))
{
    builder.Services.AddScoped<IDevUserSeedService, DevUserSeedService>();
}
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

// Database migration — fail fast: don't serve requests against a wrong schema.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        services.GetRequiredService<AppDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        services.GetRequiredService<ILogger<Program>>()
            .LogCritical(ex, "Database migration failed — aborting startup.");
        throw;
    }
}

// En dışta: sonraki her middleware/controller exception'ı burada log'lanır ve gövdesiz-stack ProblemDetails'e çevrilir.
// Development'ta örtük eklenen DeveloperExceptionPage'den daha içte olduğu için o sayfa artık tetiklenmez (#231).
app.UseAuthErrorHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// X-Forwarded-For'u RemoteIpAddress'e uygulayan middleware, adresi okuyan her şeyden önce gelmeli.
app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();
// Accept-Language → CurrentUICulture (IStringLocalizer<Messages> mesajları için).
app.UseRequestLocalization();
app.UseHttpsRedirection();
// Routing'den sonra (WebApplication UseRouting'i pipeline başına örtük ekler), auth'tan önce:
// limit aşan istek JWT doğrulaması yapılmadan 429 ile reddedilir.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
return 0;
