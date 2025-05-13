


using BadgeService;
using BadgeService.Consumers;
using BadgeService.Data;
using BadgeService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using BadgeService.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;


var builder = WebApplication.CreateBuilder(args);

var kestrelPort = builder.Configuration.GetValue<int>("Kestrel:Port", 8006); // Varsayılan 5079

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(kestrelPort); // 🟢 Dinamik Port Kullanımı
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// BadgeEvaluator service
builder.Services.AddScoped<BadgeEvaluator>();

// Badge DbContext
builder.Services.AddDbContext<BadgeDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Keycloak:Authority"]; // Keycloak realm URL (Ocelot üzerinden)
        options.Audience = builder.Configuration["Keycloak:Audience"]; // Angular'ın login olduğu clientId
        options.MetadataAddress = "http://keycloak:8080/realms/exam-realm/.well-known/openid-configuration";
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
builder.Services.AddAuthorization();



builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnswerSubmittedConsumer>();

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

app.Run();
