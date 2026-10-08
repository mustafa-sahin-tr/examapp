using ExamApp.Api.Services.Teachers.Authorization;
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
using ExamApp.Api.Services.Whiteboard;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

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
// Issue #372: ServiceClientSecret (exam-service, servisler arası token) da aynı kurala tabi.
ExamApp.Foundation.Security.KeycloakSecretGuard.EnsureConfigured(
    builder.Environment.IsDevelopment(),
    ("Keycloak:ClientSecret", keycloakConfig["ClientSecret"]),
    ("Keycloak:AdminClientSecret", keycloakConfig["AdminClientSecret"]),
    ("Keycloak:ServiceClientSecret", keycloakConfig["ServiceClientSecret"]));

// Issue #402 (O1): görsel URL'leri root ile değil ayrı GetObject-only MinIO hesabıyla imzalanır — Development dışında
// MinioConfig:PresignAccessKey/PresignSecretKey eksik, dev-only ya da root ile aynıysa açılışta patla.
ExamApp.Api.Services.Storage.MinioPresignCredentialGuard.EnsureConfigured(
    builder.Environment.IsDevelopment(), builder.Configuration.GetSection("MinioConfig"));

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
        // Doğrulanan token AuthenticationProperties'te saklanır (varsayılan zaten true; açıkça yazıldı): AuthApiClient
        // kullanıcı adına auth-api'ye giderken header'sız SignalR WebSocket isteğinde token'ı buradan alır (CallerAccessToken).
        options.SaveToken = true;
        // Only OnMessageReceived (no logging handlers): the framework's own ILogger already logs
        // token-validation failures at the right level. The previous handlers
        // wrote the token subject/issuer/expiry to stdout on every request.
        options.Events = new JwtBearerEvents
        {
            // issue #98: SignalR WebSocket upgrade Authorization header taşıyamaz; istemci token'ı ?access_token= ile
            // gönderir. Yalnızca whiteboard hub yolunda ve Authorization header'ı yokken kabul edilir (security review O1;
            // BadgeService /hub/badges ile aynı desen).
            OnMessageReceived = context =>
            {
                var queryToken = ExamApp.Api.Helpers.SignalRQueryToken.Resolve(context.Request, ExamApp.Api.Hubs.WhiteboardHub.Path);
                if (queryToken is not null)
                    context.Token = queryToken;

                return Task.CompletedTask;
            }
        };
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

    // issue #287 (security review H1): soru bankası uçları — öğretmen/admin ya da servis hesabı (BadgeService
    // sınıflandırıcısı). Öğretmen ayrıca ApprovedTeacher policy'sinden geçer (servis/admin muaf).
    ExamApp.Api.Services.Questions.QuestionAccessPolicies.AddTo(options, serviceClients);
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
// issue #365 (S1): bilinen bucket'ları oluşturur + prefix bazlı geçici anonim okuma politikasını uygular/düzeltir.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddHostedService<ExamApp.Api.Services.Storage.MinioBucketBootstrapper>();
// issue #365 (S2): [StorageUrl] alanları yalnız MVC JSON çıktısında kısa ömürlü imzalı /img URL'sine çevrilir
// (SignalR/Redis/outbox serileştirmesi etkilenmez); istemciden gelen görsel adresleri StorageAreaPolicy ile
// normalize edilir. İmzanın host'u gateway'in MinIO downstream adresi: MinioConfig:PresignEndpoint (yoksa Endpoint).
builder.Services.AddSingleton<ExamApp.Api.Services.Storage.StorageAreaPolicy>();
builder.Services.AddSingleton<ExamApp.Api.Services.Storage.IStorageUrlSigner, ExamApp.Api.Services.Storage.MinioStorageUrlSigner>();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>,
    ExamApp.Api.Services.Storage.StorageUrlJsonOptionsSetup>();
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAssignmentService, ExamApp.Api.Services.Worksheets.WorksheetAssignmentService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.ITestSessionService, ExamApp.Api.Services.Worksheets.TestSessionService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAuthoringService, ExamApp.Api.Services.Worksheets.WorksheetAuthoringService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetDetailService, ExamApp.Api.Services.Worksheets.WorksheetDetailService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetReminderService, ExamApp.Api.Services.Worksheets.WorksheetReminderService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetCalendarService, ExamApp.Api.Services.Worksheets.WorksheetCalendarService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetReminderDispatcher, ExamApp.Api.Services.Worksheets.WorksheetReminderDispatcher>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAccessRequestService, ExamApp.Api.Services.Worksheets.WorksheetAccessRequestService>();
// issue #105: yorum-soru thread'leri + ilgili öğretmen tespiti (dilim 2 bildirim hedefi de bunu kullanır).
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetResponsibleTeacherResolver, ExamApp.Api.Services.Worksheets.WorksheetResponsibleTeacherResolver>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetCommentService, ExamApp.Api.Services.Worksheets.WorksheetCommentService>();
// issue #106: öğrenci ↔ öğretmen doğrudan mesajlaşma — CanMessage kuralı (tek nokta) + konuşma/mesaj/engel/şikayet servisi.
builder.Services.AddOptions<ExamApp.Api.Models.Dtos.DirectMessages.DirectMessagingOptions>()
    .BindConfiguration(ExamApp.Api.Models.Dtos.DirectMessages.DirectMessagingOptions.SectionName); // #361: B yolu bayrağı
builder.Services.AddScoped<ExamApp.Api.Services.DirectMessages.IDirectMessagePolicy, ExamApp.Api.Services.DirectMessages.DirectMessagePolicy>();
builder.Services.AddScoped<ExamApp.Api.Services.DirectMessages.IDirectMessageService, ExamApp.Api.Services.DirectMessages.DirectMessageService>();
builder.Services.AddScoped<ExamApp.Api.Services.Bookings.IBookingService, ExamApp.Api.Services.Bookings.BookingService>();
// Tekrarlayan haftalık müsaitlik kuralları (issue #178) — BookingService top-up için buna bağımlı.
builder.Services.AddScoped<ExamApp.Api.Services.Bookings.IRecurringAvailabilityService, ExamApp.Api.Services.Bookings.RecurringAvailabilityService>();
// Video görüşme (issue #97) — "Video" bölümünü bağlar, IVideoSessionProvider'ı kaydeder.
builder.Services.AddVideoSessions(builder.Configuration);
// Ortak çizim tahtası (issue #98) — SignalR hub /hub/whiteboard, bellek içi durum + dakikalık temizlik servisi.
builder.Services.AddWhiteboard();
builder.Services.AddScoped<ExamApp.Api.Services.Practice.IPracticeSessionService, ExamApp.Api.Services.Practice.PracticeSessionService>();
// issue #99: "Günün soruları" — N ve son-X-gün dışlaması config'ten (DailyQuestions), açılışta doğrulanır.
builder.Services.AddOptions<ExamApp.Api.Services.Practice.DailyQuestionsOptions>()
    .BindConfiguration(ExamApp.Api.Services.Practice.DailyQuestionsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.Practice.IDailyQuestionSetService, ExamApp.Api.Services.Practice.DailyQuestionSetService>();
builder.Services.AddScoped<ExamApp.Api.Services.LoginEvents.ILoginEventService, ExamApp.Api.Services.LoginEvents.LoginEventService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<ExamApp.Api.Services.Leaderboards.ILeaderboardService, ExamApp.Api.Services.Leaderboards.LeaderboardService>(); // issue #193
builder.Services.AddScoped<ExamApp.Api.Services.StudentPoints.IStudentPointsSyncService, ExamApp.Api.Services.StudentPoints.StudentPointsSyncService>(); // issue #225
builder.Services.AddScoped<ExamApp.Api.Services.Badges.IStudentBadgeProjectionService, ExamApp.Api.Services.Badges.StudentBadgeProjectionService>(); // issue #422
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<ExamApp.Api.Services.Questions.IQuestionClassificationService, ExamApp.Api.Services.Questions.QuestionClassificationService>();
builder.Services.AddScoped<ExamApp.Api.Services.Questions.IQuestionQueryService, ExamApp.Api.Services.Questions.QuestionQueryService>();
builder.Services.AddScoped<ExamApp.Api.Services.Questions.IQuestionOwnershipGuard, ExamApp.Api.Services.Questions.QuestionOwnershipGuard>(); // issue #287 H1
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
// issue #265: dashboard gün sınırı yerel takvim (Dashboard:TimeZone, varsayılan Europe/Istanbul; geçersiz kimlik başlangıçta
// düşer) + iki öğretmen aktivite ucunun ortak toplamasını paylaşan kısa ömürlü süreç içi önbellek.
builder.Services.AddOptions<ExamApp.Api.Services.Dashboard.DashboardOptions>()
    .BindConfiguration(ExamApp.Api.Services.Dashboard.DashboardOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => ExamApp.Api.Services.Dashboard.LocalDayCalendar.IsKnownTimeZone(o.TimeZone),
        "Dashboard:TimeZone geçerli bir IANA saat dilimi kimliği olmalı (örn. Europe/Istanbul).")
    .ValidateOnStart();
builder.Services.AddSingleton<ExamApp.Api.Services.Dashboard.ILocalDayCalendar>(sp =>
    new ExamApp.Api.Services.Dashboard.LocalDayCalendar(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Dashboard.DashboardOptions>>(),
        sp.GetService<TimeProvider>() ?? TimeProvider.System));
builder.Services.AddSingleton<ExamApp.Api.Services.Teachers.ITeacherActivityCache>(sp =>
    new ExamApp.Api.Services.Teachers.TeacherActivityCache(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Dashboard.DashboardOptions>>()));
builder.Services.AddScoped<ITeacherService, TeacherService>();
// issue #277 (madde 3/4): veli kaydı servisi + register sonrası UserRoleChangedEvent yazıcısı.
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentService, ExamApp.Api.Services.Parents.ParentService>();
// issue #419: veli–öğrenci bağlantısı + davet kodu (HMAC pepper: ParentLinks__InviteCodePepper; Development'ta dev-only yedek).
// Development dışında eksik/kısa/devOnly pepper açılışta fail-fast (ParentLinkOptionsValidator).
builder.Services.AddOptions<ExamApp.Api.Services.Parents.ParentLinkOptions>()
    .BindConfiguration(ExamApp.Api.Services.Parents.ParentLinkOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ExamApp.Api.Services.Parents.ParentLinkOptions>,
    ExamApp.Api.Services.Parents.ParentLinkOptionsValidator>();
// review: başarısız redeem — hesap başına günde 20 + platform devre kesicisi (IFixedWindowCounterStore; Redis varsa dağıtık).
builder.Services.AddOptions<ExamApp.Api.Services.Parents.ParentRedeemGuardOptions>()
    .BindConfiguration(ExamApp.Api.Services.Parents.ParentRedeemGuardOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ExamApp.Api.Services.Parents.IParentRedeemAttemptGuard, ExamApp.Api.Services.Parents.ParentRedeemAttemptGuard>();
builder.Services.AddSingleton<ExamApp.Api.Services.Parents.IParentInviteCodeHasher, ExamApp.Api.Services.Parents.ParentInviteCodeHasher>();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentLinkService, ExamApp.Api.Services.Parents.ParentLinkService>();
// issue #420: veli paneli — tek yetki kapısı (Active bağlantı, aksi 404) + erişim kaydı + özet okuma modeli.
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentChildAccess, ExamApp.Api.Services.Parents.ParentChildAccess>();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentAccessAuditLog, ExamApp.Api.Services.Parents.ParentAccessAuditLog>();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentDashboardService, ExamApp.Api.Services.Parents.ParentDashboardService>();
// issue #421 (veli V3): ödev/test listesi + test sonuç özeti (salt okunur; kapı → audit → veri).
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentAssignmentService, ExamApp.Api.Services.Parents.ParentAssignmentService>();
// issue #421 review: test sonucu 404 taraması uyarısı (süreç içi sayaç, veli başına 10 dk'da 20'yi aşınca Warning).
builder.Services.AddSingleton<ExamApp.Api.Services.Parents.IParentTestResultProbeMonitor, ExamApp.Api.Services.Parents.ParentTestResultProbeMonitor>();
// issue #422 (veli V4): puan/rozet/sıra + program (salt okunur; kapı → audit → veri). Rozetler ve haftalık puan exam DB
// projeksiyonlarından (StudentBadgeProjections / StudentDailyXps; BadgeService event'leriyle beslenir) — servisler arası HTTP yok.
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentProgressService, ExamApp.Api.Services.Parents.ParentProgressService>();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentScheduleService, ExamApp.Api.Services.Parents.ParentScheduleService>();
builder.Services.AddScoped<ExamApp.Api.Services.UserRoles.IUserRoleChangeRecorder, ExamApp.Api.Services.UserRoles.UserRoleChangeRecorder>();
// issue #419: veli ↔ öğrenci/öğretmen rol dışlaması (student/teacher/parent register).
builder.Services.AddScoped<ExamApp.Api.Services.UserRoles.IUserRoleExclusivity, ExamApp.Api.Services.UserRoles.UserRoleExclusivity>();
builder.Services.AddSingleton<ImageHelper>();
builder.Services.AddScoped<UserProfileCacheService>();
builder.Services.AddScoped<ISchoolContextResolver, SchoolContextResolver>(); // issue #189
builder.Services.AddScoped<IUserProfileProvider, UserProfileProvider>(); // issue #189
builder.Services.AddScoped<ISchoolAccessPolicy, SchoolAccessPolicy>(); // issue #190
builder.Services.AddScoped<IProgramService, ProgramService>(); // ProgramService DI
builder.Services.AddScoped<IStudyItemService, StudyItemService>();
builder.Services.AddScoped<ExamApp.Api.Services.Teachers.IApprovedTeacherGuard, ExamApp.Api.Services.Teachers.ApprovedTeacherGuard>(); // issue #61, #287
// issue #287: onaysız öğretmen kapısı — "ApprovedTeacher" / "ApprovedTeacherOrStudent" policy'leri + TeacherNotApproved 403 gövdesi.
builder.Services.AddApprovedTeacherAuthorization();
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
// issue #277 (madde 2): retten sonra yeni okul talebi bekleme süresi — TeacherApprovals:SchoolRequestCooldownHours (varsayılan 24).
builder.Services.AddOptions<ExamApp.Api.Services.TeacherApprovals.TeacherSchoolRequestOptions>()
    .BindConfiguration(ExamApp.Api.Services.TeacherApprovals.TeacherSchoolRequestOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
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
// issue #365 (S3): kitap sayfası sorgusu (POST study-items/book-pages/lookup) kullanıcı başına rate limit.
builder.Services.AddStudyBookPageLookupRateLimiting();
// issue #277 (madde 8): admin öğrenci okul değişikliği — audit AdminUserActionLogs'a, ayrı rate limit kovası.
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminStudentSchoolService, ExamApp.Api.Services.AdminUsers.AdminStudentSchoolService>();
builder.Services.AddAdminStudentSchoolRateLimiting();
// issue #361: öğrenci okul üyeliği onayı (platform admin + aynı okulun onaylı öğretmeni); karar uçları yukarıdaki kovayı kullanır.
builder.Services.AddScoped<ExamApp.Api.Services.StudentSchoolMemberships.IStudentSchoolMembershipService, ExamApp.Api.Services.StudentSchoolMemberships.StudentSchoolMembershipService>();
builder.Services.AddStudentSchoolRequestListRateLimiting();
// issue #313: admin öğretmen okul bağlama/değiştirme — öğrenci okul ucuyla aynı audit ve rate limit kovası.
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminTeacherSchoolService, ExamApp.Api.Services.AdminUsers.AdminTeacherSchoolService>();
// issue #289: öğretmen hesap onayını askıya alma / geri açma — audit AdminUserActionLogs'a; hesap durumu (#155) rate limit kovası.
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminTeacherSuspensionService, ExamApp.Api.Services.AdminUsers.AdminTeacherSuspensionService>();
// issue #331: askıdaki öğretmende kalmış Pending talepleri kapatan güvenlik ağı (Hangfire, varsayılan 5 dk).
builder.Services.AddOptions<ExamApp.Api.Services.Bookings.SuspendedTeacherBookingSweepOptions>()
    .BindConfiguration(ExamApp.Api.Services.Bookings.SuspendedTeacherBookingSweepOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.Bookings.ISuspendedTeacherBookingSweepJob, ExamApp.Api.Services.Bookings.SuspendedTeacherBookingSweepJob>();
// issue #396: süresi dolmuş açık test oturumlarını Expired'a çeken güvenlik ağı (Hangfire, varsayılan 5 dk).
builder.Services.AddOptions<ExamApp.Api.Services.Worksheets.ExpiredTestInstanceSweepOptions>()
    .BindConfiguration(ExamApp.Api.Services.Worksheets.ExpiredTestInstanceSweepOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IExpiredTestInstanceSweepJob, ExamApp.Api.Services.Worksheets.ExpiredTestInstanceSweepJob>();
// issue #423: veli bildirimi — süresi geçen, tamamlanmamış ödevler (Hangfire, varsayılan 15 dk).
builder.Services.AddOptions<ExamApp.Api.Services.Parents.ParentHomeworkOverdueSweepOptions>()
    .BindConfiguration(ExamApp.Api.Services.Parents.ParentHomeworkOverdueSweepOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentHomeworkOverdueSweepJob, ExamApp.Api.Services.Parents.ParentHomeworkOverdueSweepJob>();
// issue #424: veli erişim kaydı saklama süresi (KVKK) — ParentAccessAudit:RetentionDays (varsayılan 180), günlük Hangfire temizliği;
// admin okuma yüzeyi GET api/admin/parent-access-audits.
builder.Services.AddOptions<ExamApp.Api.Services.Parents.ParentAccessAuditOptions>()
    .BindConfiguration(ExamApp.Api.Services.Parents.ParentAccessAuditOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<ExamApp.Api.Services.Parents.IParentAccessAuditRetentionJob, ExamApp.Api.Services.Parents.ParentAccessAuditRetentionJob>();
builder.Services.AddScoped<ExamApp.Api.Services.AdminUsers.IAdminParentAccessAuditService, ExamApp.Api.Services.AdminUsers.AdminParentAccessAuditService>();

// Student activity reset
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.AddScoped<IBadgeResetApiClient, BadgeResetApiClient>();
builder.Services.AddScoped<StudentResetJob>();
// issue #243: self-reset tekilleştirme (bekleyen iş varsa yenisi açılmaz) + sub başına rate limit.
builder.Services.AddScoped<IStudentResetScheduler, StudentResetScheduler>();
builder.Services.AddStudentSelfResetRateLimiting();
// issue #61: çalışma linki yazma uçları (POST/PUT/DELETE/reorder) için sub başına bellek içi sabit pencere.
builder.Services.AddStudyLinkWriteRateLimiting();
// issue #265: öğretmen aktivite uçları — öğretmen (sub) başına dağıtık sabit pencere (#262 sayaç altyapısı).
builder.Services.AddTeacherActivityRateLimiting();
// issue #105: yorum yazma — kullanıcı (sub) başına dağıtık sabit pencere (varsayılan dakikada 10).
builder.Services.AddWorksheetCommentWriteRateLimiting();
builder.Services.AddWorksheetCommentReadRateLimiting(); // okuma: dakikada 60 (review O4)
// issue #106: doğrudan mesaj — gönderme (dakikada 10), şikayet (saatte 20), okuma (dakikada 60); sub başına dağıtık.
builder.Services.AddDirectMessageRateLimiting();
builder.Services.AddDailyQuestionsRateLimiting(); // issue #99 security D4: günün soruları, öğrenci başına dakikada 30
builder.Services.AddParentLinkRateLimiting(); // issue #419: veli kod denemesi dakikada 5, öğrenci kod üretimi saatte 10 (sub başına)

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
// Consumer'lar: BadgeService outbox'ından gelen StudentPointsChangedEvent → StudentPoints (+ günlük puan defteri, #422)
// ve StudentBadgeEarnedEvent → StudentBadgeProjections (#422).
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
    // Issue #371: ortak fail-fast doğrulama (Host burada zaten dolu); eksik Username/Password InvalidOperationException.
    var rabbitMqSettings = ExamApp.Foundation.Messaging.RabbitMqConnectionSettings.Require(builder.Configuration);
    var rabbitMqUsername = rabbitMqSettings.Username;
    var rabbitMqPassword = rabbitMqSettings.Password;

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<ExamApp.Api.Consumers.StudentPointsChangedConsumer, ExamApp.Api.Consumers.StudentPointsChangedConsumerDefinition>();
        // issue #422: BadgeService rozet event'i → StudentBadgeProjections (veli paneli).
        x.AddConsumer<ExamApp.Api.Consumers.StudentBadgeEarnedConsumer, ExamApp.Api.Consumers.StudentBadgeEarnedConsumerDefinition>();

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbitMqHost, "/", h =>
            {
                h.Username(rabbitMqUsername);
                h.Password(rabbitMqPassword);
            });

            cfg.ReceiveEndpoint("exam-api", e =>
            {
                // issue #279 review (SHOULD-FIX, least privilege): fault mesajları ayrıca publish
                // edilmesin — hatalar zaten bu endpoint'in kendi `_error` (dead-letter) kuyruğuna gider.
                e.PublishFaults = false;

                e.ConfigureConsumer<ExamApp.Api.Consumers.StudentPointsChangedConsumer>(context);
                e.ConfigureConsumer<ExamApp.Api.Consumers.StudentBadgeEarnedConsumer>(context);
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

// issue #287 (security review L3): production filtresi async — öğretmenin HESAP onayını da doğrular.
app.UseHangfireDashboard("/hangfire", app.Environment.IsDevelopment()
    ? new DashboardOptions { Authorization = new[] { new HangfireDashboardDevAuthFilter() } }
    : new DashboardOptions
    {
        Authorization = Array.Empty<Hangfire.Dashboard.IDashboardAuthorizationFilter>(),
        AsyncAuthorization = new[] { new HangfireDashboardAuthFilter() }
    });

app.MapControllers();
app.MapWhiteboardHub(); // issue #98
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

// issue #331: askı/talep yarışı sonrası askıdaki öğretmende kalan Pending talepleri #298 yoluyla kapatır.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Bookings.ISuspendedTeacherBookingSweepJob>(
    ExamApp.Api.Services.Bookings.SuspendedTeacherBookingSweepJob.RecurringJobId,
    j => j.SweepAsync(CancellationToken.None),
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Bookings.SuspendedTeacherBookingSweepOptions>>().Value.Cron);

// issue #396: öğrenci dönmeyince istek anında kapanamayan, süresi dolmuş Started test oturumlarını Expired'a çeker.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Worksheets.IExpiredTestInstanceSweepJob>(
    ExamApp.Api.Services.Worksheets.ExpiredTestInstanceSweepJob.RecurringJobId,
    j => j.SweepAsync(CancellationToken.None),
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Worksheets.ExpiredTestInstanceSweepOptions>>().Value.Cron);

// issue #423: süresi geçen, tamamlanmamış ödevler için Active velilere bildirim event'i yazar (atama+öğrenci başına bir kez).
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Parents.IParentHomeworkOverdueSweepJob>(
    ExamApp.Api.Services.Parents.ParentHomeworkOverdueSweepJob.RecurringJobId,
    j => j.SweepAsync(CancellationToken.None),
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Parents.ParentHomeworkOverdueSweepOptions>>().Value.Cron);

// issue #424: ParentAccessAudits saklama süresi (KVKK) — süresi dolan satırları günlük, parti parti siler.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Parents.IParentAccessAuditRetentionJob>(
    ExamApp.Api.Services.Parents.ParentAccessAuditRetentionJob.RecurringJobId,
    j => j.PurgeExpiredAsync(CancellationToken.None),
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExamApp.Api.Services.Parents.ParentAccessAuditOptions>>().Value.Cron);

app.Run();
return 0;

