using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Seed;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Video;
using Hangfire;
using Hangfire.PostgreSql;
using MassTransit;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Localization;
using ExamApp.Foundation.Localization;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

// Komut modu (issue #216 seed-schools, #217 seed-teachers): `dotnet run -- <komut> [...]` — host kurulur
// ama Kestrel açılmaz; komut çalışıp süreç çıkar. Hatalı kullanım burada yakalanır ki host hiç kurulmasın.
ISeedCommand? seedCommand = null;
try
{
    seedCommand = SeedCommands.TryParse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return SeedCommands.ExitUsage;
}

// Komut modunda komut arg'ları IConfiguration'a sızmasın (`--limit 5` → config "limit" gibi).
var builder = WebApplication.CreateBuilder(seedCommand is null ? args : []);

if (seedCommand is not null)
{
    // Ortam guard'ı — host BUILD EDİLMEDEN, Migrate()/ReferenceDataSeed çalışmadan, --connection
    // override'ı uygulanmadan. Production'da hedef DB'ye hiç dokunulmaz.
    if (!SeedCommands.IsAllowedEnvironment(builder.Environment))
    {
        Console.Error.WriteLine(SeedCommands.RefusalMessage(seedCommand.CommandName, builder.Environment));
        return SeedCommands.ExitEnvironmentRefused;
    }

    if (seedCommand.ShowHelp)
    {
        Console.WriteLine(seedCommand.UsageText);
        return SeedCommands.ExitOk;
    }

    // --connection: Aspire'ın ürettiği yerel Postgres'e appsettings'teki docker-compose adresiyle
    // ulaşılamadığında bağlantıyı komut satırından geçmek için. Yalnızca bu süreç için geçerli.
    if (seedCommand.ConnectionString is { Length: > 0 } seedConnection)
    {
        builder.Configuration["ConnectionStrings:DefaultConnection"] = seedConnection;
    }
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

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "smart";
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddPolicyScheme("smart", "Smart scheme", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Path.StartsWithSegments("/hangfire")
                ? "HangfireCookie"
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddCookie("HangfireCookie", options =>
    {
        options.Cookie.Name = "examapp_hangfire";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.Path = "/hangfire";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}";
        options.MetadataAddress = $"{builder.Configuration.GetValue<string>("Keycloak:Host")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}/.well-known/openid-configuration";
        // Audience: configurable so it can be tightened to an API-specific value
        // (e.g. "exam-api") once the realm adds a matching audience mapper. Defaults
        // to "account" — Keycloak's built-in audience — so behaviour is unchanged
        // until the config key is set.
        var validAudiences = builder.Configuration.GetSection("Keycloak:ValidAudiences").Get<string[]>()
            ?? new[] { "account" };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}",
            ValidateAudience = true,
            ValidAudiences = validAudiences
        };
        options.RequireHttpsMetadata = false;
        // No custom JwtBearerEvents: the framework's own ILogger already logs
        // token-validation failures at the right level. The previous handlers
        // wrote the token subject/issuer/expiry to stdout on every request.
    });

var serviceClients = builder.Configuration.GetSection("Keycloak:ServiceClients").Get<string[]>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServiceToService", policy =>
        policy.RequireAssertion(context =>
            ExamApp.Foundation.Security.ServicePrincipal.IsService(context.User, serviceClients)));

    options.AddPolicy("TeacherOrService", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole("Teacher") ||
            ExamApp.Foundation.Security.ServicePrincipal.IsService(context.User, serviceClients)));
});

var redisConfig = builder.Configuration.GetSection("Redis");

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig["Configuration"];
    options.InstanceName = redisConfig["InstanceName"];
});
// issue #262: tek Redis multiplexer — IDistributedCache ile admin veri uçlarının dağıtık rate limit sayacı paylaşır.
builder.Services.AddSingleton<ExamApp.Api.Helpers.IRedisConnectionProvider, ExamApp.Api.Helpers.RedisConnectionProvider>();
builder.Services.AddOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>()
    .Configure<ExamApp.Api.Helpers.IRedisConnectionProvider>((options, redis) =>
        options.ConnectionMultiplexerFactory = redis.GetConnectionAsync);



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
// Client'a giden hata/başarı mesajlarının sözlüğü (issue #184).
// Resources/<alan>.<dil>.json dosyalarının tümü açılışta okunup dil başına tek sözlükte
// birleştirilir; mesajlar istek kültürüne (#181, aşağıdaki RequestLocalization) göre seçilir.
// Kullanım: IStringLocalizer<Messages> enjekte edip localizer["questions.notFound"].
// Detay ve anahtar kuralları: api/ExamApp.Api/Resources/README.md
builder.Services.AddJsonLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    })
    // DataAnnotations ([Required] vb.) hata mesajları da aynı JSON sözlüğünden gelir:
    // DTO'da ErrorMessage olarak çeviri ANAHTARI yazılır. Henüz taşınmamış DTO'larda
    // ErrorMessage düz Türkçe metin olduğu için anahtar bulunamaz ve metin olduğu gibi
    // döner — yani faz 2 taşıması bitene kadar davranış değişmez.
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(Messages)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Add IHttpContextAccessor for accessing HTTP context in services
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IKeycloakService, KeycloakService>();
builder.Services.AddScoped<IClaimsTransformation, KeycloakRoleTransformer>();
builder.Services.AddSingleton<IMinIoService, MinIoService>();
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAssignmentService, ExamApp.Api.Services.Worksheets.WorksheetAssignmentService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.ITestSessionService, ExamApp.Api.Services.Worksheets.TestSessionService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAuthoringService, ExamApp.Api.Services.Worksheets.WorksheetAuthoringService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetDetailService, ExamApp.Api.Services.Worksheets.WorksheetDetailService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetReminderService, ExamApp.Api.Services.Worksheets.WorksheetReminderService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetCalendarService, ExamApp.Api.Services.Worksheets.WorksheetCalendarService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetReminderDispatcher, ExamApp.Api.Services.Worksheets.WorksheetReminderDispatcher>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAccessRequestService, ExamApp.Api.Services.Worksheets.WorksheetAccessRequestService>();
builder.Services.AddScoped<ExamApp.Api.Services.Bookings.IBookingService, ExamApp.Api.Services.Bookings.BookingService>();
// Tekrarlayan haftalık müsaitlik kuralları (issue #178) — BookingService top-up için buna bağımlı.
builder.Services.AddScoped<ExamApp.Api.Services.Bookings.IRecurringAvailabilityService, ExamApp.Api.Services.Bookings.RecurringAvailabilityService>();
// Video görüşme (issue #97) — "Video" bölümünü bağlar, IVideoSessionProvider'ı kaydeder.
builder.Services.AddVideoSessions(builder.Configuration);
builder.Services.AddScoped<ExamApp.Api.Services.Practice.IPracticeSessionService, ExamApp.Api.Services.Practice.PracticeSessionService>();
builder.Services.AddScoped<ExamApp.Api.Services.LoginEvents.ILoginEventService, ExamApp.Api.Services.LoginEvents.LoginEventService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<ExamApp.Api.Services.Leaderboards.ILeaderboardService, ExamApp.Api.Services.Leaderboards.LeaderboardService>(); // issue #193
builder.Services.AddScoped<ExamApp.Api.Services.StudentPoints.IStudentPointsSyncService, ExamApp.Api.Services.StudentPoints.StudentPointsSyncService>(); // issue #225
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<ExamApp.Api.Services.Questions.IQuestionClassificationService, ExamApp.Api.Services.Questions.QuestionClassificationService>();
builder.Services.AddScoped<ExamApp.Api.Services.Questions.IQuestionQueryService, ExamApp.Api.Services.Questions.QuestionQueryService>();
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<ITeacherService, TeacherService>();
builder.Services.AddSingleton<ImageHelper>();
builder.Services.AddScoped<UserProfileCacheService>();
builder.Services.AddScoped<ISchoolContextResolver, SchoolContextResolver>(); // issue #189
builder.Services.AddScoped<IUserProfileProvider, UserProfileProvider>(); // issue #189
builder.Services.AddScoped<ISchoolAccessPolicy, SchoolAccessPolicy>(); // issue #190
builder.Services.AddScoped<IProgramService, ProgramService>(); // ProgramService DI
builder.Services.AddScoped<IStudyItemService, StudyItemService>();
builder.Services.AddScoped<ExamApp.Api.Services.Teachers.IApprovedTeacherGuard, ExamApp.Api.Services.Teachers.ApprovedTeacherGuard>(); // issue #61 (#287 tüm öğretmen uçlarında yeniden kullanacak)
builder.Services.AddScoped<ExamApp.Api.Services.StudyLinks.ITopicStudyLinkService, ExamApp.Api.Services.StudyLinks.TopicStudyLinkService>(); // issue #61

// Admin: taxonomy management + question-classifier (Gemini) cache
builder.Services.Configure<ExamApp.Api.Services.Classifier.GeminiCacheOptions>(
    builder.Configuration.GetSection(ExamApp.Api.Services.Classifier.GeminiCacheOptions.SectionName));
builder.Services.AddScoped<ExamApp.Api.Services.Taxonomy.ITaxonomyService, ExamApp.Api.Services.Taxonomy.TaxonomyService>();
builder.Services.AddScoped<ExamApp.Api.Services.Schools.ISchoolService, ExamApp.Api.Services.Schools.SchoolService>();
// Test verisi: MEB türevi okul listesi içe aktarma (issue #216). Yalnızca Development/Staging'de kayıt olur.
builder.Services.AddSchoolSeed(builder.Environment);
// Test verisi: okula bağlı öğretmen hesapları (issue #217). Yalnızca Development/Staging'de kayıt olur.
builder.Services.AddTeacherSeed(builder.Environment);
builder.Services.AddScoped<ExamApp.Api.Services.Locations.ILocationService, ExamApp.Api.Services.Locations.LocationService>();
builder.Services.AddScoped<ExamApp.Api.Services.Classifier.IClassifierCacheService, ExamApp.Api.Services.Classifier.ClassifierCacheService>();
builder.Services.AddScoped<ExamApp.Api.Services.Dashboard.IDashboardService, ExamApp.Api.Services.Dashboard.DashboardService>();
builder.Services.AddScoped<ExamApp.Api.Services.TeacherApprovals.ITeacherApprovalService, ExamApp.Api.Services.TeacherApprovals.TeacherApprovalService>();
// Admin kullanıcı listeleri (issue #152 öğretmen; #153 öğrenci aynı IAdminUserDirectory'yi kullanır)
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminUserDirectory, ExamApp.Api.Services.AdminUsers.AdminUserDirectory>();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminTeacherService, ExamApp.Api.Services.AdminUsers.AdminTeacherService>();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminStudentService, ExamApp.Api.Services.AdminUsers.AdminStudentService>();
// issue #246: admin kişisel veri listeleri — erişim audit'i (DB) + kullanıcı (sub) başına rate limit.
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminDataAccessAuditService, ExamApp.Api.Services.AdminUsers.AdminDataAccessAuditService>();
// issue #262: rate limit sayacı Redis'te (dağıtık, fail-open); 429'lar da audit tablosuna yazılır.
builder.Services.AddAdminUserListRateLimiting();
// issue #262: audit saklama süresi (KVKK) — AdminDataAccessLog:RetentionDays (varsayılan 180), günlük Hangfire temizliği.
builder.Services.AddOptions<ExamApp.Api.Services.AdminUsers.AdminDataAccessLogOptions>()
    .BindConfiguration(ExamApp.Api.Services.AdminUsers.AdminDataAccessLogOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminDataAccessLogRetentionJob, ExamApp.Api.Services.AdminUsers.AdminDataAccessLogRetentionJob>();
// issue #156: admin şifre sıfırlama (geçici şifre) — audit AdminUserActionLogs'a, ayrı rate limit kovası.
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminAccountTargetResolver, ExamApp.Api.Services.AdminUsers.AdminAccountTargetResolver>();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminUserActionAuditService, ExamApp.Api.Services.AdminUsers.AdminUserActionAuditService>();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminPasswordResetService, ExamApp.Api.Services.AdminUsers.AdminPasswordResetService>();
builder.Services.AddAdminPasswordResetRateLimiting();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminAccountStatusService, ExamApp.Api.Services.AdminUsers.AdminAccountStatusService>();
builder.Services.AddAdminAccountStatusRateLimiting();

// Student activity reset
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.AddScoped<IBadgeResetApiClient, BadgeResetApiClient>();
builder.Services.AddScoped<StudentResetJob>();
// issue #243: self-reset tekilleştirme (bekleyen iş varsa yenisi açılmaz) + sub başına rate limit.
builder.Services.AddScoped<IStudentResetScheduler, StudentResetScheduler>();
builder.Services.AddStudentSelfResetRateLimiting();
// issue #61: çalışma linki yazma uçları (POST/PUT/DELETE/reorder) için sub başına bellek içi sabit pencere.
builder.Services.AddStudyLinkWriteRateLimiting();

// PostgreSQL & EF Core (Aspire client integration — reads ConnectionStrings:DefaultConnection,
// same key as before, so standalone `dotnet run` against appsettings.json is unaffected).
// Retry-on-failure is enabled by default here (a real resiliency win for
// transient network blips, especially relevant to a containerized setup) —
// QuestionService.cs and QuestionTransferJobRunner.cs's manual
// Database.BeginTransaction() calls were updated to run inside
// Database.CreateExecutionStrategy().Execute(...) instead of being disabled,
// per EF Core's documented pattern for combining retries with transactions.
builder.AddNpgsqlDbContext<AppDbContext>("DefaultConnection");

// Hangfire (PostgreSQL)
var hangfireConn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(hangfireConn, new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire"
        });
});

builder.Services.AddHangfireServer(options =>
{
    options.Queues = new[] { "default", "question-transfer" };
});

// RabbitMQ consumer'ları (issue #225): exam API'nin sahibi olduğu veriye yazan event'ler burada tüketilir.
// İlk (ve şimdilik tek) consumer: BadgeService outbox'ından gelen StudentPointsChangedEvent → StudentPoints.
// Kendi kuyruğu "exam-api" (BadgeService'in "badge-service" kuyruğundan bağımsız; dead-letter: exam-api_error).
// RabbitMQ:Host yoksa bus kurulmaz (entegrasyon testleri / RabbitMQ'suz lokal çalıştırma) — başlangıçta uyarı loglanır.
// Production'da RabbitMQ:Host zorunlu (consumer'sız sessizce açılmak liderliği fark edilmeden dondurur);
// Host tanımlıysa Username/Password da zorunlu, "guest" fallback'i yok (fail-fast). EF design-time
// ("dotnet ef", ortam varsayılanı Production) bu kontrolden muaf.
var rabbitMqHost = builder.Configuration["RabbitMQ:Host"];
var rabbitMqEnabled = !string.IsNullOrWhiteSpace(rabbitMqHost);
if (!rabbitMqEnabled && builder.Environment.IsProduction() && !EF.IsDesignTime)
{
    throw new InvalidOperationException(
        "RabbitMQ:Host tanımlı değil. Production'da exam API consumer'ları (StudentPointsChangedEvent, issue #225) zorunlu.");
}
builder.Services.AddOptions<ExamApp.Api.Services.StudentPoints.StudentPointsSyncOptions>()
    .Bind(builder.Configuration.GetSection(ExamApp.Api.Services.StudentPoints.StudentPointsSyncOptions.SectionName))
    .Validate(o => o.MaxTotalPoints > 0, "StudentPoints:Sync:MaxTotalPoints pozitif olmalı.")
    .ValidateOnStart();
if (rabbitMqEnabled)
{
    var rabbitMqUsername = builder.Configuration["RabbitMQ:Username"];
    var rabbitMqPassword = builder.Configuration["RabbitMQ:Password"];
    if (string.IsNullOrWhiteSpace(rabbitMqUsername) || string.IsNullOrWhiteSpace(rabbitMqPassword))
    {
        throw new InvalidOperationException("RabbitMQ:Host tanımlı ama RabbitMQ:Username/RabbitMQ:Password eksik.");
    }

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<ExamApp.Api.Consumers.StudentPointsChangedConsumer, ExamApp.Api.Consumers.StudentPointsChangedConsumerDefinition>();

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbitMqHost, "/", h =>
            {
                h.Username(rabbitMqUsername);
                h.Password(rabbitMqPassword);
            });

            cfg.ReceiveEndpoint("exam-api", e =>
            {
                e.ConfigureConsumer<ExamApp.Api.Consumers.StudentPointsChangedConsumer>(context);
            });
        });
    });
}

// Question export/import
builder.Services.AddScoped<IQuestionTransferService, QuestionTransferService>();
builder.Services.AddScoped<QuestionTransferJobRunner>();

// İstek kültürü çözümlemesi (issue #181). Desteklenen diller tek kaynaktan gelir:
// ExamApp.Foundation.Localization.SupportedLocales (tr → tr-TR, en → en-US).
// Provider sırası bilinçli:
//   1) Accept-Language — UI aktif dili her istekte gönderir; kullanıcının ANLIK seçimi,
//      profilinde kayıtlı tercihten daha günceldir.
//   2) PreferredLocale — header yoksa/desteklenmeyen dil istiyorsa kullanıcının kayıtlı tercihi.
//   3) DefaultRequestCulture (tr-TR).
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = SupportedLocales.AllCultureNames
        .Select(name => new CultureInfo(name))
        .ToList();

    options.DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.ApplyCurrentCultureToResponseHeaders = true;

    // Varsayılan provider'lar (query string / cookie / ham Accept-Language) temizlenir;
    // yerine normalize eden kendi ikilimiz konur.
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new NormalizedAcceptLanguageCultureProvider());
    options.RequestCultureProviders.Add(new UserPreferredLocaleCultureProvider());
});



var app = builder.Build();

// Komut modunda --no-migrate: bekleyen migration'lar ve il/ilçe referans seed'i atlanır.
var runStartupDatabaseSteps = seedCommand is null || !seedCommand.NoMigrate;

// Database migration (prod-safe default for single-instance deployments).
// Fail fast: a failed migration means the schema is wrong — the app must not
// start and serve requests against it. EF's EnableRetryOnFailure already
// covers transient "DB not ready yet" blips.
if (runStartupDatabaseSteps)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        services.GetRequiredService<ILogger<Program>>()
            .LogCritical(ex, "Database migration failed — aborting startup.");
        throw;
    }
}

// İl / ilçe referans verisi (issue #91). İdempotent: tablolar doluysa atlar.
// Seed başarısızlığı uygulamayı durdurmaz — okul adres formu il listesi boş kalır, diğer akışlar çalışır.
if (runStartupDatabaseSteps)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        ReferenceDataSeed.Initialize(services);
    }
    catch (Exception ex)
    {
        services.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Province/District reference data seed failed.");
    }
}

// Komut modu (issue #216/#217): host kuruldu, şema ve il/ilçe referansı hazır — Kestrel açılmadan çalış ve çık.
if (seedCommand is not null)
{
    await using (app)
    {
        return await seedCommand.RunAsync(app.Services, app.Environment);
    }
}

//Seed Data
// using (var scope = app.Services.CreateScope())
// {
//     var services = scope.ServiceProvider;
//     try
//     {
//         var context = services.GetRequiredService<AppDbContext>();
//         // context.Database.Migrate(); // Apply any pending migrations
//         // Seed TopicSeed data everyitme the application starts       
//         TopicSeed.InitializeSeed(context);
//     }
//     catch (Exception ex)
//     {
//         var logger = services.GetRequiredService<ILogger<Program>>();
//         logger.LogError(ex, "An error occurred seeding the DB.");
//     }
// }

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

// DİKKAT: RequestLocalization normalde pipeline'ın başında (auth'tan önce) yer alır.
// Burada bilinçli olarak UseAuthentication/UseAuthorization'dan SONRA çağrılıyor: providers
// arasındaki UserPreferredLocaleCultureProvider, kullanıcının kayıtlı dil tercihini okumak
// için HttpContext.User claim'lerine ihtiyaç duyuyor; auth'tan önce çalışsaydı her istekte
// kullanıcı anonim görünür ve tercih hiç uygulanmazdı. Bu noktadan sonrasında (controller'lar,
// endpoint'ler) CurrentCulture/CurrentUICulture doğru şekilde ayarlı olur — auth öncesindeki
// middleware'ler (exception handler, HTTPS redirect) kültüre bağımlı çıktı üretmiyor.
app.UseRequestLocalization(); // Ayarlar yukarıdaki Configure<RequestLocalizationOptions>'tan gelir.

// issue #246: yalnızca [EnableRateLimiting] taşıyan uçlar (admin kullanıcı listeleri). Auth'tan SONRA: partition
// anahtarı kullanıcının sub'ıdır ve 401/403 alan istekler kovayı tüketmez; localization'dan sonra: 429 metni çevrilir.
app.UseRateLimiter();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = app.Environment.IsDevelopment()
        ? new[] { new HangfireDashboardDevAuthFilter() }
        : new[] { new HangfireDashboardAuthFilter() }
});

app.MapControllers();
app.MapDefaultEndpoints();

if (!rabbitMqEnabled)
{
    app.Logger.LogWarning(
        "RabbitMQ:Host tanımlı değil — exam API consumer'ları (StudentPointsChangedEvent, issue #225) çalışmıyor; liderlik puanı senkronlanmaz.");
}

// Safety net: hourly reconcile in case a per-change job was lost. No-ops
// unless the classifier cache is actually stale vs. the live taxonomy.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Classifier.IClassifierCacheService>(
    "classifier-cache-reconcile",
    s => s.RefreshIfStaleAsync(0),
    app.Configuration.GetValue<string>("Classifier:ReconcileCron") ?? "0 * * * *");

// issue #262: AdminDataAccessLogs saklama süresi (KVKK) — süresi dolan satırları günlük, parti parti siler.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.AdminUsers.IAdminDataAccessLogRetentionJob>(
    ExamApp.Api.Services.AdminUsers.AdminDataAccessLogRetentionJob.RecurringJobId,
    j => j.PurgeExpiredAsync(CancellationToken.None),
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.AdminUsers.AdminDataAccessLogOptions>>().Value.Cron);

app.Run();
return 0;

