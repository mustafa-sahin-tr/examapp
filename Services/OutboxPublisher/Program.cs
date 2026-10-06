using ExamApp.Foundation.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OutboxPublisherService;
using OutboxPublisherService.Data;
using OutboxPublisherService.Publishers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Issue #371: guest/guest fallback yok; eksik Host/Username/Password açılışta (servis kurulurken) InvalidOperationException.
var rabbit = RabbitMqConnectionSettings.Require(builder.Configuration);
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbit.Host, "/", h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });
    });
});

builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddHostedService<OutboxProcessor>();
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

var host = builder.Build();
host.Run();
