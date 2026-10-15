using System.Collections.Concurrent;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Issue #440: auth-api veli hesabı uçlarının bellek içi sahtesi (test ortamında auth-api/Keycloak yok). Lookup salt okunur: kayıtsız e-posta
/// None, <see cref="SeedExistingParent"/> ExistingParent, <see cref="SeedNonParent"/> NotParentAccount. Hesap yalnız <see cref="CreateAsync"/>
/// (aktivasyon) ile açılır; kayıtlı e-postada 409 EmailAlreadyRegistered. Respawn bunu sıfırlamaz: testler benzersiz e-posta kullanmalı.
/// Parola değerleri saklanmaz, yalnız açılan hesap id'leri.
/// </summary>
public sealed class FakeParentAccountDirectory : IParentAccountDirectory
{
    private int _nextUserId = 7_500_000;

    private readonly ConcurrentDictionary<string, int> _parents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _nonParents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _unverifiedParents = new(StringComparer.Ordinal);

    /// <summary>identity userId → hesabın sunucudaki e-postası (confirm-email için; veli hesapları).</summary>
    private readonly ConcurrentDictionary<int, string> _accountEmails = new();

    public ConcurrentQueue<int> Verified { get; } = new();

    public ConcurrentQueue<int> Created { get; } = new();
    public ConcurrentQueue<string> LookedUp { get; } = new();

    public void SeedExistingParent(string email, int userId)
    {
        _parents[email] = userId;
        _accountEmails[userId] = email;
    }

    /// <summary>Security C1: e-postası DOĞRULANMAMIŞ saf veli hesabı (lookup UnverifiedParent).</summary>
    public void SeedUnverifiedParent(string email, int userId)
    {
        _unverifiedParents[email] = userId;
        _accountEmails[userId] = email;
    }

    /// <summary>Oturum açabilen başka bir veli hesabı (e-postası verilen adres).</summary>
    public void SeedParentAccountEmail(int userId, string email) => _accountEmails[userId] = email;

    public void SeedNonParent(string email) => _nonParents[email] = 0;

    public bool HasAccount(string email) => _parents.ContainsKey(email) || _nonParents.ContainsKey(email) || _unverifiedParents.ContainsKey(email);

    public Task<ParentAccountCallResult> LookupAsync(string email, CancellationToken ct = default)
    {
        LookedUp.Enqueue(email);
        var lookup = _nonParents.ContainsKey(email)
            ? new ParentAccountLookupResponse { Outcome = ParentAccountOutcomes.NotParentAccount }
            : _parents.TryGetValue(email, out var id)
                ? new ParentAccountLookupResponse { Outcome = ParentAccountOutcomes.ExistingParent, UserId = id, KeycloakId = $"kc-{id}" }
                : _unverifiedParents.ContainsKey(email)
                    ? new ParentAccountLookupResponse { Outcome = ParentAccountOutcomes.UnverifiedParent }
                    : new ParentAccountLookupResponse { Outcome = ParentAccountOutcomes.None };
        return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Success, Lookup: lookup));
    }

    public Task<ParentAccountCallResult> ConfirmEmailAsync(int parentUserId, string email, CancellationToken ct = default)
    {
        if (!_accountEmails.TryGetValue(parentUserId, out var accountEmail))
            return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, ParentAccountErrorCodes.NotParentAccount));
        if (!string.Equals(accountEmail, email, StringComparison.Ordinal))
            return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, ParentAccountErrorCodes.EmailMismatch));
        Verified.Enqueue(parentUserId);
        return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Success));
    }

    public Task<ParentAccountCallResult> CreateAsync(string email, string fullName, string pw, CancellationToken ct = default)
    {
        if (_nonParents.ContainsKey(email) || _unverifiedParents.ContainsKey(email))
            return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, ParentAccountErrorCodes.EmailAlreadyRegistered));
        var id = Interlocked.Increment(ref _nextUserId);
        if (!_parents.TryAdd(email, id))
            return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Conflict, ParentAccountErrorCodes.EmailAlreadyRegistered));
        Created.Enqueue(id);
        _accountEmails[id] = email;
        return Task.FromResult(new ParentAccountCallResult(ParentAccountCallStatus.Success,
            Created: new CreateParentAccountResponse { UserId = id, KeycloakId = $"kc-{id}" }));
    }
}
