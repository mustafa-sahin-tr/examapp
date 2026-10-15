using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #440: YALNIZ Development — yerelde SMTP olmadığından öğrenci ekranı "dev: aktivasyon bağlantısı"nı gösterebilsin diye son
/// üretilen aktivasyon bağlantısı süreç belleğinde (davet id'si anahtarıyla, token geçerliliği kadar) tutulur. DB'de yalnız hash olduğu
/// için başka yoldan gösterilemez. Development dışında hiçbir şey yazmaz/okumaz.
/// </summary>
public interface IParentActivationDevLinkStore
{
    void Remember(Guid invitationId, string activationUrl, DateTime expiresAt);

    string? Get(Guid invitationId);
}

/// <inheritdoc cref="IParentActivationDevLinkStore"/>
public sealed class ParentActivationDevLinkStore(IMemoryCache cache, IHostEnvironment environment) : IParentActivationDevLinkStore
{
    private static string Key(Guid id) => "parent-activation-dev-link:" + id.ToString("N");

    public void Remember(Guid invitationId, string activationUrl, DateTime expiresAt)
    {
        if (!environment.IsDevelopment())
            return;
        cache.Set(Key(invitationId), activationUrl, new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)));
    }

    public string? Get(Guid invitationId)
        => environment.IsDevelopment() && cache.TryGetValue(Key(invitationId), out string? url) ? url : null;
}
