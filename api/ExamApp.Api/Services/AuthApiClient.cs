using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

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
}
