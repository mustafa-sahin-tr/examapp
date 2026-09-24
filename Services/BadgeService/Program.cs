


using BadgeService;
using BadgeService.Commands;
using BadgeService.Consumers;
using BadgeService.Data;
using BadgeService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using BadgeService.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using BadgeService.Security;
using ExamApp.Foundation.Localization;


// Komut modu (issue #225/#243): `dotnet run -- backfill-student-points [--dry-run] [--allow-production] [--confirm]`
// — host kurulur, şema migrate edilir, komut çalışır ve süreç çıkar (Kestrel/MassTransit başlatılmaz).
var isBackfillCommand = StudentPointsBackfillCommand.IsRequested(args);
var backfillArgs = default(StudentPointsBackfillCommand.ParsedArgs);
if (isBackfillCommand)
{
    try
    {
        backfillArgs = StudentPointsBackfillCommand.ParseArgs(args);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return StudentPointsBackfillCommand.ExitUsage;
    }
}

// Komut arg'ları IConfiguration'a sızmasın.
var builder = WebApplication.CreateBuilder(isBackfillCommand ? [] : args);

if (isBackfillCommand && !StudentPointsBackfillCommand.IsAllowedEnvironment(builder.Environment, backfillArgs.AllowProduction))
{
    Console.Error.WriteLine(
        $"{StudentPointsBackfillCommand.CommandName} yalnızca Development/Staging ortamında (veya Production'da --allow-production ile) çalışır; mevcut ortam: {builder.Environment.EnvironmentName}.");
    return StudentPointsBackfillCommand.ExitEnvironmentRefused;
}

var backfillDryRun = isBackfillCommand
    ? StudentPointsBackfillCommand.ResolveEffectiveDryRun(builder.Environment, backfillArgs.DryRun, backfillArgs.Confirm)
    : false;

if (isBackfillCommand && builder.Environment.IsProduction() && backfillDryRun && !backfillArgs.DryRun)
{
    Console.WriteLine(
        $"UYARI: Production'da --confirm verilmedi; {StudentPointsBackfillCommand.CommandName} dry-run olarak çalıştırılıyor. " +
        "Özeti gördükten sonra gerçek yazım için --allow-production --confirm ile tekrar çalıştırın.");
}

builder.AddServiceDefaults();

var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 8006); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

if (builder.Environment.IsDevelopment())
{
    StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);
}

// Issue #238: appsettings(.Development).json'daki Keycloak ClientSecret/
// AdminClientSecret artık gerçek değer taşımıyor ("" placeholder) — Development
// dışında ServiceTokenProvider'ın ilk token isteğinde patlamasını beklemek yerine
// açılışta açıkça patlat. Development'ta docker-compose/.env veya Aspire AppHost
// parametreleri değeri zaten dolduruyor.
if (!builder.Environment.IsDevelopment())
{
    var badgeKeycloakConfig = builder.Configuration.GetSection("Keycloak");

    // "devOnly" öneki .env.example/AppHost'taki dev-only Keycloak secret'larının
    // ortak deseni (bkz. .env.example) — bunlar yanlışlıkla prod/staging'e
    // taşınmışsa da boş secret'la aynı şekilde reddedilir.
    static bool IsMissingOrDevOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("devOnly", StringComparison.OrdinalIgnoreCase);

    if (IsMissingOrDevOnly(badgeKeycloakConfig["ClientSecret"]) ||
        IsMissingOrDevOnly(badgeKeycloakConfig["AdminClientSecret"]))
    {
        throw new InvalidOperationException(
            "Keycloak:ClientSecret ve Keycloak:AdminClientSecret ortam değişkeninden (Keycloak__ClientSecret / Keycloak__AdminClientSecret) set edilmeli; Development dışında boş veya dev-only değer bırakılamaz.");
    }
}

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// Rapor uçlarındaki IDOR koruması (issue #165): Keycloak sub -> auth-api sayısal user id çözümü.
// Singleton: durumu yok, IHttpClientFactory + IMemoryCache zaten singleton-safe;
// HttpContext metoda parametre olarak geldiği için accessor'a gerek yok.
builder.Services.AddHttpClient(
    AuthApiCallerIdentityResolver.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<ICallerIdentityResolver, AuthApiCallerIdentityResolver>();

// Badge services
builder.Services.AddScoped<AnswerSubmissionAggregationService>();
builder.Services.AddScoped<BadgeEvaluator>();
builder.Services.AddScoped<StudentReportService>();
builder.Services.AddScoped<UserResetService>();
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddScoped<IQuestionClassifier, GeminiQuestionClassifier>();

// issue #279 item 6: QuestionPoint üst sınırı (consumer tarafında sahte event ile puan şişirmeye karşı).
builder.Services.AddOptions<AnswerPointOptions>()
    .BindConfiguration(AnswerPointOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// issue #279 item 3: ProcessedAnswerSubmissions saklama süresi + periyodik temizleme (BadgeService'te
// Hangfire yok — hafif BackgroundService, bkz. ProcessedAnswerSubmissionRetentionService XML doc).
builder.Services.AddOptions<ProcessedAnswerSubmissionRetentionOptions>()
    .BindConfiguration(ProcessedAnswerSubmissionRetentionOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<IProcessedAnswerSubmissionRetentionJob, ProcessedAnswerSubmissionRetentionJob>();
builder.Services.AddHostedService<ProcessedAnswerSubmissionRetentionService>();

// Bildirim lokalizasyonu (issue #185): notifications.<dil>.json altında toplanan metinler +
// hedef kullanıcının UserLocalePreference'tan çözülen dili. IStringLocalizer değil doğrudan
// JsonResourceStore kullanılır — consumer'larda istek bağlamı (CurrentUICulture) yok, bkz.
// NotificationTextFactory XML yorumu.
builder.Services.AddJsonLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddScoped<IUserLocaleResolver, UserLocaleResolver>();
builder.Services.AddSingleton<INotificationTextFactory, NotificationTextFactory>();

// Badge DbContext
builder.Services.AddDbContext<BadgeDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});



builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {

        options.Authority = $"{builder.Configuration.GetValue<string>("Server:BaseUrl")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}";
        options.MetadataAddress = $"{builder.Configuration.GetValue<string>("Keycloak:Host")}/realms/{builder.Configuration.GetValue<string>("Keycloak:Realm")}/.well-known/openid-configuration";
        // Audience: configurable (Keycloak:ValidAudiences), defaults to "account"
        // so behaviour is unchanged until tightened to an API-specific value.
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
// Realm rollerini ClaimTypes.Role'e projekte eder — BadgeNotificationHub'ın
// Context.User.IsInRole("Admin") kontrolü (issue #94) bunsuz çalışmaz.
// ExamApp.Api.Helpers.KeycloakRoleTransformer ile aynı mantık, bilinçli olarak
// BadgeService.Security altında ayrıca tutuluyor (bkz. dosyadaki yorum) —
// Foundation'a FrameworkReference eklemeyi engellemek için.
builder.Services.AddScoped<IClaimsTransformation, KeycloakRoleTransformer>();

var serviceClients = builder.Configuration.GetSection("Keycloak:ServiceClients").Get<string[]>();
builder.Services.AddAuthorization(options =>
{
    // Service-to-service only (e.g. the exam API's student-reset job).
    options.AddPolicy("Service", policy =>
        policy.RequireAssertion(context =>
            ExamApp.Foundation.Security.ServicePrincipal.IsService(context.User, serviceClients)));
});



builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnswerSubmittedConsumer, AnswerSubmittedConsumerDefinition>();
    x.AddConsumer<QuestionCreatedConsumer>();
    x.AddConsumer<WorksheetReminderDueConsumer, WorksheetReminderDueConsumerDefinition>();
    x.AddConsumer<WorksheetAccessRequestedConsumer, WorksheetAccessRequestedConsumerDefinition>();
    x.AddConsumer<WorksheetAccessDecisionConsumer, WorksheetAccessDecisionConsumerDefinition>();
    x.AddConsumer<LoginAttemptedConsumer, LoginAttemptedConsumerDefinition>();
    x.AddConsumer<TeacherApplicationSubmittedConsumer, TeacherApplicationSubmittedConsumerDefinition>();
    x.AddConsumer<TeacherApplicationDecisionConsumer, TeacherApplicationDecisionConsumerDefinition>();
    x.AddConsumer<TeacherSchoolRequestSubmittedConsumer, TeacherSchoolRequestSubmittedConsumerDefinition>();
    x.AddConsumer<IndependentTeacherRegisteredConsumer, IndependentTeacherRegisteredConsumerDefinition>();
    x.AddConsumer<BookingRequestCreatedConsumer, BookingRequestCreatedConsumerDefinition>();
    x.AddConsumer<BookingDecisionConsumer, BookingDecisionConsumerDefinition>();
    x.AddConsumer<UserPreferredLocaleChangedConsumer, UserPreferredLocaleChangedConsumerDefinition>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("badge-service", e =>
        {
            // issue #279 review (SHOULD-FIX, least privilege): consumer hataları RabbitMQ fault mesajı
            // olarak AYRICA yayınlanmasın (varsayılan MassTransit davranışı publish izni gerektirir) —
            // hatalar zaten bu endpoint'in kendi `_error` (dead-letter) kuyruğuna gider, bu yeterli.
            e.PublishFaults = false;

            // Kullanıcı bazlı sıralı işleme (partitioner) AnswerSubmittedConsumerDefinition'da (issue #225).
            e.ConfigureConsumer<AnswerSubmittedConsumer>(context);
            e.ConfigureConsumer<QuestionCreatedConsumer>(context);
            // WorksheetReminderDueConsumer'ın retry'ı WorksheetReminderDueConsumerDefinition'da
            // scope'lu; diğer iki consumer'ın davranışı değişmez.
            e.ConfigureConsumer<WorksheetReminderDueConsumer>(context);
            e.ConfigureConsumer<WorksheetAccessRequestedConsumer>(context);
            e.ConfigureConsumer<WorksheetAccessDecisionConsumer>(context);
            e.ConfigureConsumer<LoginAttemptedConsumer>(context);
            e.ConfigureConsumer<TeacherApplicationSubmittedConsumer>(context);
            e.ConfigureConsumer<TeacherApplicationDecisionConsumer>(context);
            e.ConfigureConsumer<TeacherSchoolRequestSubmittedConsumer>(context);
            e.ConfigureConsumer<IndependentTeacherRegisteredConsumer>(context);
            e.ConfigureConsumer<BookingRequestCreatedConsumer>(context);
            e.ConfigureConsumer<BookingDecisionConsumer>(context);
            e.ConfigureConsumer<UserPreferredLocaleChangedConsumer>(context);
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

if (isBackfillCommand)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StudentPointsBackfill");
    try
    {
        await StudentPointsBackfillCommand.RunAsync(
            scope.ServiceProvider.GetRequiredService<BadgeDbContext>(), logger, backfillDryRun);
        return StudentPointsBackfillCommand.ExitOk;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "{Command} başarısız.", StudentPointsBackfillCommand.CommandName);
        return StudentPointsBackfillCommand.ExitFailed;
    }
}

app.MapHub<BadgeNotificationHub>("/hub/badges");
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
return 0;
