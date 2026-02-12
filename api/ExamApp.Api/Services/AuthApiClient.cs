using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services;

public class AuthApiClient : IAuthApiClient
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthApiClient(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }
    public async Task<UserProfileDto> GetUserProfileAsync()
    {
        using var httpClient = new HttpClient();
        var baseUrl = _configuration["AuthApiBaseUrl"]; // Configuration'dan URL oku
        Console.WriteLine($"[AuthApiClient] Base URL: {baseUrl}");

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/user-profile");

        // Mevcut request'ten Authorization header'ını al
        var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader))
        {
            request.Headers.Add("Authorization", authHeader.ToString());
        }

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var userProfile = JsonSerializer.Deserialize<UserProfileDto>(content, options);
        return userProfile;
    }

    public async Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsAsync(IEnumerable<int> userIds)
    {
        if (userIds == null)
        {
            throw new ArgumentNullException(nameof(userIds));
        }

        var distinctIds = userIds.Distinct().ToList();
        if (!distinctIds.Any())
        {
            return Array.Empty<UserLookupResultDto>();
        }

        using var httpClient = new HttpClient();
        var baseUrl = _configuration["AuthApiBaseUrl"];
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/users/lookup")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { UserIds = distinctIds }),
                Encoding.UTF8,
                "application/json")
        };

        var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader))
        {
            request.Headers.Add("Authorization", authHeader.ToString());
        }

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<List<UserLookupResultDto>>(content, options) ?? new List<UserLookupResultDto>();
    }
}
