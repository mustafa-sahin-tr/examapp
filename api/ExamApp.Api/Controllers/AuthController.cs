using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly HttpClient _http;
        protected readonly AppDbContext _context;
        private readonly KeycloakSettings _keycloakSettings;

        private readonly UserProfileCacheService _userProfileCacheService;

        public AuthController(AppDbContext context,
             IOptions<KeycloakSettings> options, IHttpClientFactory factory, UserProfileCacheService userProfileCacheService)
            : base()
        {
            _context = context;
            _keycloakSettings = options.Value;
            _http = factory.CreateClient();
            _userProfileCacheService = userProfileCacheService;
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshProfileInformation()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await GetUserProfile(sub);
            await _userProfileCacheService.SetAsync(sub, profile);
            return Ok(profile);
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto request)
        {
            var keycloakUserId = string.Empty;
            try
            {
                if (_context.Users.Any(u => u.Email == request.Email))
                {
                    return BadRequest("Email already exists.");
                }

                // Keycloak admin access token (önceden alınmalı veya Client Credentials ile otomatik alınabilir)
                var adminToken = await GetKeycloakAdminTokenAsync(); // bunu birazdan gösterebiliriz

                var keycloakUser = new
                {
                    username = request.Email,
                    email = request.Email,
                    enabled = true,
                    firstName = request.FirstName,
                    lastName = request.LastName,
                    credentials = new[]
                    {
                        new {
                            type = "password",
                            value = request.Password,
                            temporary = false
                        }
                    }
                };

                var json = JsonSerializer.Serialize(keycloakUser);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

                var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}", content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return BadRequest($"Keycloak registration failed: {error}");
                }

                // Keycloak yeni kullanıcıya ID dönmez, ama Location header'ı olur
                var locationHeader = response.Headers.Location?.ToString();
                keycloakUserId = locationHeader?.Split("/").Last();

                if (string.IsNullOrEmpty(keycloakUserId))
                    return BadRequest("Keycloak user creation succeeded but no user ID returned.");

                var rolesResponse = await _http.GetAsync($"{_keycloakSettings.Host}/{_keycloakSettings.RealmRolesUrl}");
                var rolesJson = await rolesResponse.Content.ReadAsStringAsync();

                var roles = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(rolesJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });               

                var role = roles.FirstOrDefault(f => f.name.Equals(request.Role.ToString()));  

                var roleAssignJson = JsonSerializer.Serialize(new[] { role });
                var assignContent = new StringContent(roleAssignJson, Encoding.UTF8, "application/json");

                await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm", assignContent);               

                var user = new User
                {
                    FullName = request.FirstName + " " + request.LastName,
                    Email = request.Email,
                    Role = request.Role,
                    KeycloakId = keycloakUserId
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return Ok(user);
            }
            catch (Exception ex)
            {
                // _logger.LogError(ex, "User DB kayıt hatası");

                // Keycloak'taki kullanıcıyı silmeye çalış
                if (!string.IsNullOrEmpty(keycloakUserId))
                {
                    await TryDeleteKeycloakUserAsync(keycloakUserId);
                }

                return StatusCode(500, "Kullanıcı kaydedilemedi.");
            }

            
        }

        private async Task<string> GetKeycloakAdminTokenAsync()
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _keycloakSettings.AdminClientId),
                new KeyValuePair<string, string>("client_secret", _keycloakSettings.AdminClientSecret)
            });

            var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}", content);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }

        private async Task TryDeleteKeycloakUserAsync(string userId)
        {            
            var token = await GetKeycloakAdminTokenAsync();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var result = await _http.DeleteAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{userId}");                
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            // 2) Admin token'ı al
            var adminToken = await GetKeycloakAdminTokenAsync();

            // 3) Admin API ile session'ları sonlandır
           
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", adminToken);

            var logoutUrl = string.Format($"{_keycloakSettings.Host}/{_keycloakSettings.LogoutUrl}", userId);

            var resp = await _http.PostAsync(logoutUrl, null);
            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, "Keycloak oturumu sonlandırılamadı.");

            return NoContent();
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto request)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", _keycloakSettings.GrantType },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "username", request.Email },
                { "password", request.Password }                
            };

            var response = await _http.PostAsync(
                $"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}",
                new FormUrlEncodedContent(body)
            );

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // Keycloak error'ını ayıkla
                using var doc = JsonDocument.Parse(content);
                var error = doc.RootElement.GetProperty("error").GetString();
                var description = doc.RootElement.TryGetProperty("error_description", out var descProp)
                    ? descProp.GetString()
                    : null;
                // _logger.LogWarning("Login failed: {Error} - {Description}", error, description);
                return Unauthorized(new
                {
                    message = description ?? "Login failed."
                });
            }

            var tokenDto = JsonSerializer.Deserialize<TokenResponseDto>(content)!;
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenDto.AccessToken); // token string’i buraya
            var sub = jwt.Claims.First(c => c.Type == "sub").Value;
            var email = jwt.Claims.First(c => c.Type == "email").Value;
            // return Content(content, "application/json");

            var profile = await _userProfileCacheService.GetOrSetAsync(sub, async () =>
            {
                return await GetUserProfile(sub);
            });

            var loginResponseDto = new LoginResponseDto
            {
                Token = tokenDto.AccessToken,
                Profile = profile
            };

            return Ok(loginResponseDto);
        }

        [HttpPost("exchange")]
        public async Task<IActionResult> EchangeCode(CodeDto dto)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "redirect_uri", "http://localhost:5678/callback" },
                { "code", dto.Code }                
            };

            var response = await _http.PostAsync(
                $"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}",
                new FormUrlEncodedContent(body)
            );

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                // Keycloak error'ını ayıkla
                using var doc = JsonDocument.Parse(content);
                var error = doc.RootElement.GetProperty("error").GetString();
                var description = doc.RootElement.TryGetProperty("error_description", out var descProp)
                    ? descProp.GetString()
                    : null;
                // _logger.LogWarning("Login failed: {Error} - {Description}", error, description);
                return Unauthorized(new
                {
                    message = description ?? "Login failed."
                });
            }

            var tokenDto = JsonSerializer.Deserialize<TokenResponseDto>(content)!;
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenDto.AccessToken); // token string’i buraya
            var sub = jwt.Claims.First(c => c.Type == "sub").Value;
            var email = jwt.Claims.First(c => c.Type == "email").Value;
            // return Content(content, "application/json");

            var profile = await _userProfileCacheService.GetOrSetAsync(sub, async () =>
            {
                return await GetUserProfile(sub);
            });

            if((profile == null || profile.Id == 0) && !string.IsNullOrEmpty(sub))
            {
                // new user registration
                var user = new User
                {

                    FullName = jwt.Claims.First(c => c.Type == "given_name").Value + " " +
                               jwt.Claims.First(c => c.Type == "family_name").Value,
                    Email = email,                    
                    KeycloakId = sub
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();                

                profile = await GetUserProfile(sub);
                await _userProfileCacheService.SetAsync(sub, profile);
            }

            var loginResponseDto = new LoginResponseDto
            {
                Token = tokenDto.AccessToken,
                Profile = profile
            };

            return Ok(loginResponseDto);
        }

        private async Task<UserProfileDto> GetUserProfile(string sub)
        {
            var user = await _context.Users
                                .Include(u => u.Student)
                                .Include(u => u.Teacher)
                                .Include(u => u.Parent)
                                .FirstOrDefaultAsync(u => u.KeycloakId == sub);

            if (user == null)
                return null;

            return new UserProfileDto
            {
                Avatar = user.AvatarUrl ?? string.Empty,
                Email = user.Email,
                Role = user.Role.ToString(),
                FullName = user.FullName,
                Id = user.Id,
                KeycloakId = sub,                
                Student = user.Student != null ? new StudentDto
                {
                    Id = user.Student.Id,
                    GradeId = user.Student.GradeId,
                    SchoolName = user.Student.SchoolName,
                    StudentNumber = user.Student.StudentNumber 
                } : null,
                ProfileId = user.Student != null ? user.Student.Id : (user.Teacher != null ? user.Teacher.Id : user.Parent != null ? user.Parent.Id : 0),
            };
        }
    }
}
