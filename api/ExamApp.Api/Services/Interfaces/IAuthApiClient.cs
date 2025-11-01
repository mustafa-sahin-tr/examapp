using System.Collections.Generic;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IAuthApiClient
{
    Task<UserProfileDto> GetUserProfileAsync();
    Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsAsync(IEnumerable<int> userIds);
}
