using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.StudentReset;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

// Add IHttpContextAccessor for accessing HTTP context in services
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IKeycloakService, KeycloakService>();
builder.Services.AddScoped<IClaimsTransformation, KeycloakRoleTransformer>();
builder.Services.AddSingleton<IMinIoService, MinIoService>();
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<ExamApp.Api.Services.Worksheets.IWorksheetAssignmentService, ExamApp.Api.Services.Worksheets.WorksheetAssignmentService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<ITeacherService, TeacherService>();
builder.Services.AddSingleton<ImageHelper>();
builder.Services.AddScoped<UserProfileCacheService>();
builder.Services.AddScoped<IProgramService, ProgramService>(); // ProgramService DI
builder.Services.AddScoped<IStudyPageService, StudyPageService>();

// Admin: taxonomy management + question-classifier (Gemini) cache
builder.Services.Configure<ExamApp.Api.Services.Classifier.GeminiCacheOptions>(
    builder.Configuration.GetSection(ExamApp.Api.Services.Classifier.GeminiCacheOptions.SectionName));
builder.Services.AddScoped<ExamApp.Api.Services.Taxonomy.ITaxonomyService, ExamApp.Api.Services.Taxonomy.TaxonomyService>();
builder.Services.AddScoped<ExamApp.Api.Services.Classifier.IClassifierCacheService, ExamApp.Api.Services.Classifier.ClassifierCacheService>();

// Student activity reset
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.AddScoped<IBadgeResetApiClient, BadgeResetApiClient>();
builder.Services.AddScoped<StudentResetJob>();

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

// Question export/import
builder.Services.AddScoped<IQuestionTransferService, QuestionTransferService>();
builder.Services.AddScoped<QuestionTransferJobRunner>();



var app = builder.Build();

// Database migration (prod-safe default for single-instance deployments).
// Fail fast: a failed migration means the schema is wrong — the app must not
// start and serve requests against it. EF's EnableRetryOnFailure already
// covers transient "DB not ready yet" blips.
using (var scope = app.Services.CreateScope())
{
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

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = app.Environment.IsDevelopment()
        ? new[] { new HangfireDashboardDevAuthFilter() }
        : new[] { new HangfireDashboardAuthFilter() }
});

app.MapControllers();
app.MapDefaultEndpoints();

// Safety net: hourly reconcile in case a per-change job was lost. No-ops
// unless the classifier cache is actually stale vs. the live taxonomy.
RecurringJob.AddOrUpdate<ExamApp.Api.Services.Classifier.IClassifierCacheService>(
    "classifier-cache-reconcile",
    s => s.RefreshIfStaleAsync(0),
    app.Configuration.GetValue<string>("Classifier:ReconcileCron") ?? "0 * * * *");

app.Run();

