using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExamApp.Api.Services.Video;

/// <summary>Video görüşme (issue #97) servislerinin DI kaydı.</summary>
public static class VideoServiceCollectionExtensions
{
    /// <summary>
    /// "Video" bölümünü bağlar ve seçili sağlayıcıyı kaydeder. Şimdilik yalnızca "Jitsi"
    /// desteklenir; başka bir değer verilirse startup'ta sessizce yanlış davranmak yerine
    /// açık biçimde patlar.
    /// </summary>
    public static IServiceCollection AddVideoSessions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(VideoOptions.SectionName);

        var provider = section[nameof(VideoOptions.Provider)];
        if (!string.IsNullOrWhiteSpace(provider)
            && !string.Equals(provider, VideoOptions.JitsiProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Desteklenmeyen video sağlayıcısı: '{provider}'. Şu an yalnızca '{VideoOptions.JitsiProviderName}' destekleniyor " +
                $"({VideoOptions.SectionName}:{nameof(VideoOptions.Provider)}).");
        }

        // ValidateDataAnnotations iç içe nesnelere inmez; Jitsi alanlarını elle doğruluyoruz.
        services.AddOptions<VideoOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.Jitsi.PublicBaseUrl)
                    && Uri.TryCreate(o.Jitsi.PublicBaseUrl, UriKind.Absolute, out _),
                $"{VideoOptions.SectionName}:Jitsi:PublicBaseUrl mutlak bir URL olmalıdır.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Jitsi.AppId),
                $"{VideoOptions.SectionName}:Jitsi:AppId zorunludur.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Jitsi.XmppDomain),
                $"{VideoOptions.SectionName}:Jitsi:XmppDomain zorunludur (prosody VirtualHost, ör. meet.jitsi).")
            // AppSecret/RoomSecret bilerek startup'ta zorunlu tutulmuyor: appsettings.json'da
            // saklanmazlar (yalnızca Video__Jitsi__* env / user-secrets ile gelirler) ve Jitsi
            // kurulu olmayan bir ortamda API'nin hiç açılmamasını istemiyoruz. Eksiklik,
            // JitsiVideoSessionProvider ilk çağrıldığında açık mesajla patlar.
            .Validate(o => o.Jitsi.TokenLifetimeMinutes is > 0 and <= 24 * 60,
                $"{VideoOptions.SectionName}:Jitsi:TokenLifetimeMinutes 1-1440 aralığında olmalıdır.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IVideoSessionProvider, JitsiVideoSessionProvider>();

        return services;
    }
}
