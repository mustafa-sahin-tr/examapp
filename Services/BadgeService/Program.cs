


using BadgeService;
using BadgeService.Consumers;
using BadgeService.Data;
using BadgeService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using BadgeService.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;


var builder = WebApplication.CreateBuilder(args);

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

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Badge services
builder.Services.AddScoped<AnswerSubmissionAggregationService>();
builder.Services.AddScoped<BadgeEvaluator>();
builder.Services.AddScoped<StudentReportService>();
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddScoped<IQuestionClassifier, GeminiQuestionClassifier>();

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
    x.AddConsumer<AnswerSubmittedConsumer>();
    x.AddConsumer<QuestionCreatedConsumer>();
    x.AddConsumer<WorksheetReminderDueConsumer, WorksheetReminderDueConsumerDefinition>();
    x.AddConsumer<WorksheetAccessRequestedConsumer, WorksheetAccessRequestedConsumerDefinition>();
    x.AddConsumer<WorksheetAccessDecisionConsumer, WorksheetAccessDecisionConsumerDefinition>();

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
            e.ConfigureConsumer<QuestionCreatedConsumer>(context);
            // WorksheetReminderDueConsumer'ın retry'ı WorksheetReminderDueConsumerDefinition'da
            // scope'lu; diğer iki consumer'ın davranışı değişmez.
            e.ConfigureConsumer<WorksheetReminderDueConsumer>(context);
            e.ConfigureConsumer<WorksheetAccessRequestedConsumer>(context);
            e.ConfigureConsumer<WorksheetAccessDecisionConsumer>(context);
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
app.MapDefaultEndpoints();

app.Run();
