using System.Collections.Concurrent;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Issue #438: auth-api çocuk hesabı uçlarının bellek içi sahtesi (test ortamında auth-api/Keycloak yok). Kullanıcı adı tekilliğini
/// (409), açılan/geri alınan hesapları ve şifre sıfırlamaları kaydeder. Respawn bunu sıfırlamaz: testler benzersiz kullanıcı adı
/// kullanmalı. <see cref="NextUserIdOverride"/> bir sonraki hesaba verilecek id'yi zorlar (ör. DB'de zaten öğrencisi olan id →
/// yerel yazım unique ihlaliyle düşer, kompanzasyon gözlenir).
/// </summary>
public sealed class FakeChildAccountDirectory : IChildAccountDirectory
{
    private int _nextUserId = 7_400_000;

    public ConcurrentDictionary<string, int> Accounts { get; } = new(StringComparer.Ordinal);
    public ConcurrentBag<int> Deleted { get; } = new();
    public ConcurrentQueue<(int UserId, string Password)> Resets { get; } = new();
    public ConcurrentQueue<CreateChildAccountRequest> CreateRequests { get; } = new();
    public ConcurrentDictionary<Guid, int> ByRequestId { get; } = new();
    public ConcurrentBag<Guid> DeletedByRequest { get; } = new();

    public int? NextUserIdOverride { get; set; }

    public Task<ChildAccountCallResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default)
    {
        CreateRequests.Enqueue(request);
        // auth-api ile aynı: aynı requestId yeni hesap açmaz, mevcut hesabı döner.
        if (ByRequestId.TryGetValue(request.RequestId, out var existing))
            return Task.FromResult(new ChildAccountCallResult(ChildAccountCallStatus.Success,
                Created: new CreateChildAccountResponse { UserId = existing, KeycloakId = $"kc-child-{existing}" }));
        var userId = NextUserIdOverride ?? Interlocked.Increment(ref _nextUserId);
        NextUserIdOverride = null;
        if (!Accounts.TryAdd(request.Username, userId))
            return Task.FromResult(new ChildAccountCallResult(ChildAccountCallStatus.Conflict, ChildAccountErrorCodes.UsernameTaken));
        ByRequestId[request.RequestId] = userId;
        return Task.FromResult(new ChildAccountCallResult(ChildAccountCallStatus.Success,
            Created: new CreateChildAccountResponse { UserId = userId, KeycloakId = $"kc-child-{userId}" }));
    }

    public Task<ChildAccountCallResult> DeleteAsync(int userId, CancellationToken ct = default)
    {
        Deleted.Add(userId);
        foreach (var kv in Accounts.Where(kv => kv.Value == userId).ToList())
            Accounts.TryRemove(kv.Key, out _);
        return Task.FromResult(new ChildAccountCallResult(ChildAccountCallStatus.Success));
    }

    public Task<ChildAccountCallResult> DeleteByRequestIdAsync(Guid requestId, CancellationToken ct = default)
    {
        DeletedByRequest.Add(requestId);
        if (!ByRequestId.TryRemove(requestId, out var userId))
            return Task.FromResult(new ChildAccountCallResult(ChildAccountCallStatus.NotFound, ChildAccountErrorCodes.NotFound));
        return DeleteAsync(userId, ct);
    }

    public Task<ChildAccountCallResult> ResetPasswordAsync(int userId, string newPw, CancellationToken ct = default)
    {
        Resets.Enqueue((userId, newPw));
        return Task.FromResult(Accounts.Values.Contains(userId)
            ? new ChildAccountCallResult(ChildAccountCallStatus.Success)
            : new ChildAccountCallResult(ChildAccountCallStatus.NotFound, ChildAccountErrorCodes.NotFound));
    }
}
