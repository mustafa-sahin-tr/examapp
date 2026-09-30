using System;
using ExamApp.Api.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Whiteboard;

public static class WhiteboardServiceCollectionExtensions
{
    /// <summary>
    /// issue #98: ortak çizim tahtası — SignalR, hub'a özel mesaj boyutu sınırı, bellek içi durum deposu, erişim servisi,
    /// kapatıcı ve temizlik servisi. Katılım penceresi <c>Video</c> bölümünden okunur (<c>AddVideoSessions</c> gerekli).
    /// </summary>
    public static IServiceCollection AddWhiteboard(this IServiceCollection services)
    {
        services.AddOptions<WhiteboardOptions>()
            .BindConfiguration(WhiteboardOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.AllowedTransports != HttpTransportType.None, "Whiteboard:AllowedTransports boş olamaz.")
            .ValidateOnStart();

        // Global SignalR varsayılanları (32 KB) korunur; yalnızca bu hub'a 128 KB (config) verilir.
        // DİKKAT: AddHubOptions<THub> ŞART — HubOptionsSetup<THub>'ı kaydeder (global değerleri kopyalar ve dahili
        // UserHasSetValues=true yapar). O olmadan HubConnectionHandler hub'a özel ayarları yok sayıp global 32 KB'ı
        // kullanır. Aşağıdaki Configure, setup'tan sonra kaydedildiği için ondan sonra çalışır.
        services.AddSignalR().AddHubOptions<WhiteboardHub>(_ => { });
        services.AddOptions<HubOptions<WhiteboardHub>>()
            .Configure<IOptions<WhiteboardOptions>>((hub, wb) =>
            {
                hub.MaximumReceiveMessageSize = wb.Value.MaxReceiveMessageBytes;
                hub.EnableDetailedErrors = false; // HubException dışındaki hata metinleri istemciye gitmez.
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IWhiteboardStore, WhiteboardStore>();
        services.AddSingleton<IWhiteboardSessionCloser, WhiteboardSessionCloser>();
        services.AddScoped<IWhiteboardAccessService, WhiteboardAccessService>();
        services.AddHostedService<WhiteboardCleanupService>();
        return services;
    }

    /// <summary>
    /// Hub'ı <see cref="WhiteboardHub.Path"/> yoluna bağlar. Transport varsayılanı YALNIZCA WebSockets
    /// (<see cref="WhiteboardOptions.AllowedTransports"/>); tarayıcı istemcisi BadgeService hub'ı gibi
    /// skipNegotiation + WebSockets kullanır (gateway route'u /negotiate'i eşlemez).
    /// <c>CloseOnAuthenticationExpiration</c>: JWT süresi dolunca sunucu bağlantıyı kapatır — istemci token'ı yenileyip
    /// yeniden bağlanır ve <c>JoinBoard</c> ile sahneyi geri alır.
    /// </summary>
    public static IEndpointConventionBuilder MapWhiteboardHub(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<WhiteboardOptions>>().Value;
        return endpoints.MapHub<WhiteboardHub>(WhiteboardHub.Path, o =>
        {
            o.Transports = options.AllowedTransports;
            o.CloseOnAuthenticationExpiration = true;
        });
    }
}
