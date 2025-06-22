using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
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
        protected readonly AppDbContext _context;
        private readonly KeycloakSettings _keycloakSettings;
        private readonly IKeycloakService _keycloakService;

        public AuthController(AppDbContext context,
             IOptions<KeycloakSettings> options, IHttpClientFactory factory,
             IKeycloakService keycloakService)
            : base()
        {
            _context = context;
            _keycloakSettings = options.Value;
            _keycloakService = keycloakService;
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshProfileInformation()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await GetUserProfile(sub);
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

                keycloakUserId = await _keycloakService.CreateUserAsync(
                    request.Email, request.Password, request.Email,
                    request.FirstName, request.LastName);
                // Keycloak admin access token (önceden alınmalı veya Client Credentials ile otomatik alınabilir)
                await _keycloakService.SetRoleAsync(keycloakUserId, request.Role);
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
                    await _keycloakService.DeleteUserAsync(keycloakUserId);
                }

                return StatusCode(500, "Kullanıcı kaydedilemedi.");
            }


        }







        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            await _keycloakService.LogoutAsync(userId);

            Response.Cookies.Delete("refresh_token");

            return NoContent();
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto request)
        {
            var tokenDto = await _keycloakService.LoginAsync(request.Email, request.Password);
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenDto.AccessToken); // token string’i buraya
            var sub = jwt.Claims.First(c => c.Type == "sub").Value;
            var email = jwt.Claims.First(c => c.Type == "email").Value;

            var realm_access = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
            List<string> roles = new List<string>();
            if (!string.IsNullOrEmpty(realm_access))
            {
                var realmAccess = JsonSerializer.Deserialize<RealmAccess>(realm_access);
                if (realmAccess != null && realmAccess.roles != null)
                {
                    roles = realmAccess.roles.Where(role =>
                        {
                            return !string.IsNullOrEmpty(role) && // Boş isimli rolleri hariç tut
                            (_keycloakSettings.ExcludedRoles == null || !_keycloakSettings.ExcludedRoles.Contains(role)) && // Konfigürasyonda belirtilen rolleri hariç tut
                            !role.StartsWith("default-roles") && // Default role gruplarını hariç tut
                            !role.Contains("uma_"); // UMA authorization rollerini hariç tut
                        })
                        .ToList();
                }
            }
            // return Content(content, "application/json");
            var loginResponseDto = new LoginResponseDto
            {
                Token = tokenDto.AccessToken,
                Roles = roles
            };

            return Ok(loginResponseDto);
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            // 1. Refresh token'ı cookie'den al
            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized("No refresh token provided.");

            // 2. Keycloak token endpoint'ine isteği hazırla
            var tokenData = await _keycloakService.RefreshTokenAsync(refreshToken);
            // 3. Yeni refresh token varsa, cookie’yi güncelle
            if (!string.IsNullOrEmpty(tokenData.RefreshToken))
            {
                Response.Cookies.Append("refresh_token", tokenData.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromSeconds(tokenData.RefreshExpiresIn),
                    Path = "/"
                });
            }
            // 4. Access token'ı UI’a dön
            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn
            });
        }


        [HttpPost("exchange")]
        public async Task<IActionResult> EchangeCode(CodeDto dto)
        {
            var tokenDto = await _keycloakService.ExchangeTokenAsync(dto.Code);
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokenDto.AccessToken); // token string’i buraya
            var sub = jwt.Claims.First(c => c.Type == "sub").Value;
            var email = jwt.Claims.First(c => c.Type == "email").Value;

            var realm_access = jwt.Claims.FirstOrDefault(c => c.Type == "realm_access")?.Value;
            List<string> roles = new List<string>();
            if (!string.IsNullOrEmpty(realm_access))
            {
                var realmAccess = JsonSerializer.Deserialize<RealmAccess>(realm_access);
                if (realmAccess != null && realmAccess.roles != null)
                {
                    roles = realmAccess.roles.Where(role =>
                        {
                            return !string.IsNullOrEmpty(role) && // Boş isimli rolleri hariç tut
                            (_keycloakSettings.ExcludedRoles == null || !_keycloakSettings.ExcludedRoles.Contains(role)) && // Konfigürasyonda belirtilen rolleri hariç tut
                            !role.StartsWith("default-roles") && // Default role gruplarını hariç tut
                            !role.Contains("uma_"); // UMA authorization rollerini hariç tut
                        })
                        .ToList();
                }
            }


            Response.Cookies.Append("refresh_token", tokenDto.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromSeconds(tokenDto.RefreshExpiresIn),
                Path = "/"
            });

            var loginResponseDto = new LoginResponseDto
            {
                Token = tokenDto.AccessToken,
                Roles = roles
            };

            return Ok(loginResponseDto);
        }
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            try
            {
                Console.WriteLine("🔍 GetRoles endpoint called");

                if (_keycloakService == null)
                {
                    Console.WriteLine("❌ KeycloakService is null");
                    return StatusCode(500, "KeycloakService is not properly configured");
                }

                if (_keycloakSettings == null)
                {
                    Console.WriteLine("❌ KeycloakSettings is null");
                    return StatusCode(500, "KeycloakSettings is not properly configured");
                }

                Console.WriteLine($"🔧 Keycloak Host: {_keycloakSettings.Host}");
                Console.WriteLine($"🔧 Realm Roles URL: {_keycloakSettings.RealmRolesUrl}");

                var roles = await _keycloakService.GetRealmRolesAsync();

                Console.WriteLine($"✅ Found {roles?.Count ?? 0} roles");

                return Ok(roles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in GetRoles: {ex.Message}");
                Console.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return StatusCode(500, $"Failed to fetch roles: {ex.Message}");
            }
        }

        private async Task<UserProfileDto> GetUserProfile(string sub)
        {
            var user = await _context.Users
                                .Where(u => !u.IsDeleted)
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
                KeycloakId = sub
            };
        }
    }
}
