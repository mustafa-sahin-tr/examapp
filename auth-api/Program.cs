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

var builder = WebApplication.CreateBuilder(args);

// 📌 Kestrel için port değerini `appsettings.json` veya Environment Variable'dan al
var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 5079); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

StartupConfigDump.Print(builder.Configuration, builder.Environment.EnvironmentName, kestrelPort);

var keycloakConfig = builder.Configuration.GetSection("Keycloak");

builder.Services.Configure<KeycloakSettings>(keycloakConfig);

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
            OnMessageReceived = context =>
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                Console.WriteLine($"[Auth] OnMessageReceived: Path={context.Request.Path}, Method={context.Request.Method}, AuthorizationHeader={authHeader}");
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    var token = authHeader.Substring("Bearer ".Length);
                    Console.WriteLine($"[Auth] JWT Token (truncated): {token.Substring(0, Math.Min(30, token.Length))}...");
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var jwtToken = context.SecurityToken as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
                Console.WriteLine($"[Auth] OnTokenValidated: Path={context.Request.Path}, Method={context.Request.Method}");
                Console.WriteLine($"[Auth] Token validated. Subject: {jwtToken?.Subject}");
                Console.WriteLine($"[Auth] Issuer: {jwtToken?.Issuer}");
                Console.WriteLine($"[Auth] Expiration: {jwtToken?.ValidTo}");
                if (jwtToken != null)
                {
                    foreach (var claim in jwtToken.Claims)
                    {
                        Console.WriteLine($"[Auth] Claim: {claim.Type} = {claim.Value}");
                    }
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"[Auth] OnAuthenticationFailed: Path={context.Request.Path}, Method={context.Request.Method}");
                Console.WriteLine($"[Auth] JWT ERROR: {context.Exception.Message}");
                if (context.Exception != null)
                {
                    Console.WriteLine($"[Auth] Exception: {context.Exception}");
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

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

builder.Services.AddScoped<IKeycloakService, KeycloakService>();
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

//Seed Data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        context.Database.Migrate(); // Apply any pending migrations
        // Seed TopicSeed data everyitme the application starts       
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        Console.WriteLine($"[Auth] An error occurred seeding the DB: {ex}");
    }
}

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
app.MapControllers();

app.Run();

