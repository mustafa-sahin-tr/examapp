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

        private readonly UserProfileCacheService _userProfileCacheService;

        private readonly IKeycloakService _keycloakService;

        public AuthController(AppDbContext context,
             IOptions<KeycloakSettings> options, IHttpClientFactory factory, UserProfileCacheService userProfileCacheService,
             IKeycloakService keycloakService)
            : base()
        {
            _context = context;
            _keycloakSettings = options.Value;
            _userProfileCacheService = userProfileCacheService;
            _keycloakService = keycloakService;
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshProfileInformation()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var profile = await _userProfileCacheService.GetOrSetAsync(sub, async () =>
            {
                var authApiClient = HttpContext.RequestServices.GetRequiredService<IAuthApiClient>();
                return await authApiClient.GetUserProfileAsync();
            });
            await _userProfileCacheService.SetAsync(sub, profile);

            if (profile != null)
            {
                if (profile.Role == "Student")
                {
                    var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.UserId == profile.Id);

                    profile.Student = new StudentDto
                    {
                        Id = student.Id,
                        GradeId = student.GradeId,
                        SchoolName = student.SchoolName,
                        AvatarUrl = profile.Avatar,
                        FullName = profile.FullName
                    };
                }
            }

            return Ok(profile);
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



    }
}
