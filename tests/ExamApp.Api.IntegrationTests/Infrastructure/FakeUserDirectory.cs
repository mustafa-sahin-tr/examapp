using System.Collections.Concurrent;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Testlerin auth-api yerine "çözülmüş" kullanıcı kaydedebildiği bellek içi rehber (issue #246 — liste yanıtında
/// e-postanın maskeli döndüğünü uçtan uca görmek için). Boşken hiçbir davranışı değiştirmez.
/// Respawn DB'yi sıfırlar ama bunu değil: çakışmasın diye testler benzersiz (büyük) UserId kullanmalı.
/// </summary>
public sealed class FakeUserDirectory
{
    private readonly ConcurrentDictionary<int, UserLookupResultDto> _users = new();

    public void Add(UserLookupResultDto user) => _users[user.Id] = user;

    public bool TryResolveAll(IEnumerable<int> ids, out IReadOnlyList<UserLookupResultDto> users)
    {
        var list = new List<UserLookupResultDto>();
        foreach (var id in ids)
        {
            if (!_users.TryGetValue(id, out var user))
            {
                users = [];
                return false;
            }
            list.Add(user);
        }
        users = list;
        return list.Count > 0;
    }
}

/// <summary>
/// İstenen id'lerin TAMAMI <see cref="FakeUserDirectory"/>'de kayıtlıysa oradan döner; aksi halde gerçek
/// <c>AuthApiClient</c>'a devreder (test ortamında erişilemez → mevcut fail-soft davranış).
/// </summary>
public sealed class FakeUserDirectoryAuthApiClient(IAuthApiClient inner, FakeUserDirectory directory) : IAuthApiClient
{
    public Task<UserProfileDto> GetUserProfileAsync() => inner.GetUserProfileAsync();

    public Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        return directory.TryResolveAll(ids, out var users) ? Task.FromResult(users) : inner.GetUsersByIdsAsync(ids, ct);
    }

    public Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsOrThrowAsync(IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        return directory.TryResolveAll(ids, out var users) ? Task.FromResult(users) : inner.GetUsersByIdsOrThrowAsync(ids, ct);
    }

    public Task<IReadOnlyList<UserLookupResultDto>> GetUsersWithAccountStatusByIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        return directory.TryResolveAll(ids, out var users) ? Task.FromResult(users) : inner.GetUsersWithAccountStatusByIdsAsync(ids, ct);
    }
}
