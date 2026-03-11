using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeacherController : BaseController
    {

        private readonly ITeacherService _teacherService;

        private readonly UserProfileCacheService _userProfileCacheService;
        private readonly IKeycloakService _keycloakService;

        public TeacherController(ITeacherService teacherService, UserProfileCacheService userProfileCacheService,
            IKeycloakService keycloakService
        )
            : base()
        {
            _userProfileCacheService = userProfileCacheService;
            _teacherService = teacherService;
            _keycloakService = keycloakService;
        }

        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterTeacher(RegisterTeacherDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var user = await GetAuthenticatedUserAsync();

            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Teacher);

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized("No refresh token provided.");

            // 2. Keycloak token endpoint'ine isteği hazırla
            var tokenData = await _keycloakService.RefreshTokenAsync(refreshToken);

            // 🔹 Öğrenci zaten var mı?
            var response = await _teacherService.Save(user.Id, request);
            if (response == null)
            {
                return BadRequest(new { message = "Öğretmen kaydı başarısız." });
            }

            if (response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }

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

            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn,
                profileId = user.Id
            });
        }


        [Authorize]
        [HttpGet("check-teacher")]
        public async Task<IActionResult> CheckTeacher()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            var teacher = await _teacherService.GetTeacher(user.Id);

            if (teacher != null)
            {
                return Ok(new { HasTeacherRecord = true, Teacher = teacher });
            }

            return Ok(new { HasTeacherRecord = false });
        }

        [Authorize]
        [HttpPost("update-theme")]
        public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeDto request)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _teacherService.UpdateTeacherTheme(user.Id, request.ThemePreset, request.ThemeCustomConfig);

            if (response == null || !response.Success)
            {
                return BadRequest(new { message = response?.Message ?? "Theme güncellenirken hata oluştu." });
            }

            return Ok(response);
        }

    }
}
