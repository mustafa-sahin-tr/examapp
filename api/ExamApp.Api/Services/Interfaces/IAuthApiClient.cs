using System.Collections.Generic;
using System.Threading;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IAuthApiClient
{
    Task<UserProfileDto> GetUserProfileAsync();
    Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    /// <summary>
    /// <see cref="GetUsersByIdsAsync"/> + Keycloak hesap durumu (<see cref="UserLookupResultDto.Enabled"/>, issue #152).
    /// auth-api Keycloak'tan kullanıcı başı okur — yalnızca sayfalı admin listeleri için kullan, geniş toplu çözümlerde değil.
    /// </summary>
    Task<IReadOnlyList<UserLookupResultDto>> GetUsersWithAccountStatusByIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default);
}
