using Anthropic;
using AssistantService;
using AssistantService.Chat;
using AssistantService.Consumers;
using AssistantService.Data;
using AssistantService.Endpoints;
using AssistantService.Retention;
using AssistantService.Security;
using AssistantService.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;

// Issue #502: in-app assistant service (epic #495). LLM dependency, cost and long streams stay out of the exam API and
// the service can be switched off on its own (Assistant:Enabled, Assistant:Roles).
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var kestrelPort = builder.Configuration.GetValue("Kestrel:Port", 8010);
builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(kestrelPort));

builder.Services.AddOptions<AssistantOptions>()
    .BindConfiguration(AssistantOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AssistantDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var baseUrl = builder.Configuration["Server:BaseUrl"];
var realm = builder.Configuration["Keycloak:Realm"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{baseUrl}/realms/{realm}";
        options.MetadataAddress = $"{builder.Configuration["Keycloak:Host"]}/realms/{realm}/.well-known/openid-configuration";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{baseUrl}/realms/{realm}",
            ValidateAudience = true,
            ValidAudiences = builder.Configuration.GetSection("Keycloak:ValidAudiences").Get<string[]>() ?? ["account"],
        };
        options.RequireHttpsMetadata = false;
    });
builder.Services.AddScoped<IClaimsTransformation, KeycloakRoleTransformer>();
builder.Services.AddAuthorization();

// #503: tool host. Tools call the exam API on the internal network with the user's own token.
builder.Services.AddSingleton(TimeProvider.System);
var examApiBaseUrl = builder.Configuration["ExamApi:BaseUrl"];
if (string.IsNullOrWhiteSpace(examApiBaseUrl))
    throw new InvalidOperationException("ExamApi:BaseUrl is required (exam API on the internal network).");
builder.Services.AddHttpClient<DownstreamClient>(c =>
{
    c.BaseAddress = new Uri(examApiBaseUrl.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddAssistantTools();

// #504: chat. Model client via the Anthropic SDK's Microsoft.Extensions.AI bridge (honours WithCacheControl); gen_ai.* spans
// and logs as in BadgeService. No key → no client (the gate answers 503 before any chat call).
builder.Services.AddSingleton(sp =>
{
    var apiKey = builder.Configuration["Anthropic:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
        return new ChatModel(null);
    var client = new AnthropicClient { ApiKey = apiKey.Trim() }
        .AsIChatClient()
        .AsBuilder()
        .UseLogging(sp.GetRequiredService<ILoggerFactory>())
        .UseOpenTelemetry(sourceName: builder.Environment.ApplicationName)
        .Build(sp);
    return new ChatModel(client);
});
builder.Services.AddSingleton<InMemoryCounterStore>();
var redisConfiguration = builder.Configuration["Redis:Configuration"];
if (string.IsNullOrWhiteSpace(redisConfiguration))
    builder.Services.AddSingleton<ICounterStore>(sp => sp.GetRequiredService<InMemoryCounterStore>());
else
    builder.Services.AddSingleton<ICounterStore>(sp => new RedisCounterStore(redisConfiguration,
        sp.GetRequiredService<InMemoryCounterStore>(), sp.GetRequiredService<ILogger<RedisCounterStore>>()));
builder.Services.AddSingleton<UsageLimiter>();
builder.Services.AddScoped<ChatTurnRunner>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AssistantService.Security.ParentControl>(); // #523

// #505: retention purge (Assistant:RetentionDays, every Assistant:PurgeIntervalMinutes) and the student reset consumer.
builder.Services.AddScoped<AssistantDataCleaner>();
builder.Services.AddHostedService<RetentionPurgeService>();

// Queue "assistant-service" (dead-letter assistant-service_error), own least-privilege RabbitMQ user (#279): it can read
// StudentProgressResetEvent and nothing else, and publish nothing. Without RabbitMQ:Host the bus is not registered (tests,
// local runs); in Production it is required — a silent no-consumer start would keep reset students' conversations.
var rabbitMqHost = builder.Configuration["RabbitMQ:Host"];
if (string.IsNullOrWhiteSpace(rabbitMqHost))
{
    if (builder.Environment.IsProduction() && !EF.IsDesignTime)
        throw new InvalidOperationException("RabbitMQ:Host is required in Production (StudentProgressResetEvent consumer, #505).");
}
else
{
    var rabbit = ExamApp.Foundation.Messaging.RabbitMqConnectionSettings.Require(builder.Configuration);
    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<StudentProgressResetConsumer, StudentProgressResetConsumerDefinition>();
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbit.Host, "/", h =>
            {
                h.Username(rabbit.Username);
                h.Password(rabbit.Password);
            });
            cfg.ReceiveEndpoint("assistant-service", e =>
            {
                // Least privilege (#279): faults go to this endpoint's own _error queue, not to a fault exchange.
                e.PublishFaults = false;
                e.ConfigureConsumer<StudentProgressResetConsumer>(context);
            });
        });
    });
}

var app = builder.Build();

app.UseAuthentication();
app.UseAssistantGate();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AssistantDbContext>().Database.MigrateAsync();
}

app.MapAssistantEndpoints();
app.MapToolEndpoints();
app.MapChatEndpoints();
app.MapConversationEndpoints();
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
