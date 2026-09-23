using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

public class AdminUserDirectory : IAdminUserDirectory
{
    private static readonly IReadOnlyDictionary<int, UserLookupResultDto> Empty = new Dictionary<int, UserLookupResultDto>();

    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<AdminUserDirectory> _logger;

    public AdminUserDirectory(IAuthApiClient authApiClient, ILogger<AdminUserDirectory>? logger = null)
    {
        _authApiClient = authApiClient;
        _logger = logger ?? NullLogger<AdminUserDirectory>.Instance;
    }

    public async Task<IReadOnlyDictionary<int, UserLookupResultDto>> ResolveWithAccountStatusAsync(
        IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
            return Empty;

        try
        {
            var users = await _authApiClient.GetUsersWithAccountStatusByIdsAsync(userIds, ct);
            return users
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or Polly.ExecutionRejectedException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "[AdminUserDirectory] auth-api lookup başarısız; {Count} kullanıcı ad/e-posta/hesap durumu olmadan listelenecek.",
                userIds.Count);
            return Empty;
        }
    }
}
