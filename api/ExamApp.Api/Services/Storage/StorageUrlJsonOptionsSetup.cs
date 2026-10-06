using System;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S2): <see cref="StorageUrlAttribute"/> taşıyan string alanları MVC JSON çıktısı yazılırken imzalar.
/// <para>
/// Yalnız <c>AddControllers().AddJsonOptions</c> (<see cref="MvcJsonOptions"/>) seçeneklerine eklenir. SignalR
/// (<c>JsonHubProtocolOptions</c>), Redis önbelleği (<c>UserProfileCacheService</c> → varsayılan
/// <c>JsonSerializer</c>), outbox/event serileştirmesi ve minimal API <c>Http.Json.JsonOptions</c> ayrı seçenek
/// nesneleri kullandığı için hiçbir yerde imzalı URL saklanmaz/önbelleğe girmez — imza yalnız HTTP yanıtına yazılır.
/// Okuma (deserialize) yolu değişmez; istemcinin geri gönderdiği imzalı URL saklanmadan önce
/// <see cref="StorageAreaPolicy.TryNormalizeClientUrl"/> ile ayıklanır.
/// </para>
/// </summary>
public sealed class StorageUrlJsonOptionsSetup(IStorageUrlSigner signer) : IConfigureOptions<MvcJsonOptions>
{
    public void Configure(MvcJsonOptions options)
    {
        var json = options.JsonSerializerOptions;
        json.TypeInfoResolver = (json.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(CreateModifier(signer));
    }

    /// <summary>Test edilebilir modifier: <see cref="StorageUrlAttribute"/>'lu string özelliğin getter'ını sarar.</summary>
    public static Action<JsonTypeInfo> CreateModifier(IStorageUrlSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        return typeInfo =>
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return;

            foreach (var property in typeInfo.Properties)
            {
                if (property.PropertyType != typeof(string) || property.Get is not { } getter)
                    continue;

                // ICustomAttributeProvider.GetCustomAttributes(inherit: true) PropertyInfo'da kalıtımı yok sayar;
                // Attribute.GetCustomAttributes override edilmiş özellikte taban sınıftaki işareti de bulur.
                var attribute = property.AttributeProvider is MemberInfo member
                    ? Attribute.GetCustomAttributes(member, typeof(StorageUrlAttribute), inherit: true)
                        .OfType<StorageUrlAttribute>()
                        .FirstOrDefault()
                    : null;
                if (attribute is null)
                    continue;

                var areas = attribute.Areas;
                property.Get = obj => signer.SignForBrowser((string?)getter(obj), areas);
            }
        };
    }
}
