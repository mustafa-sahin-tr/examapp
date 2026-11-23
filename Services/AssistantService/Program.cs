using AssistantService;
using AssistantService.Data;
using AssistantService.Endpoints;
using AssistantService.Security;
using AssistantService.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
app.MapDefaultEndpoints();

app.Run();

public partial class Program;
