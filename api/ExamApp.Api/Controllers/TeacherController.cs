using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;
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

            // Rol Keycloak'ta güncellendi; GetAuthenticatedUserAsync yukarıda profili eski
            // (Role boş) haliyle Redis'e cache'lemiş olabilir. Cache'i güncel rolle tazele
            // ki 1 saat boyunca diğer endpoint'ler eski/boş rolü görmesin.
            user.Role = UserRole.Teacher.ToString();
            await _userProfileCacheService.SetAsync(user.KeycloakId, user);

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

        /// <summary>
        /// Issue #53: öğretmen dashboard özet kartları.
        /// teacherId route/query'den değil, authenticated user'dan alınır — başka öğretmenin verisi sızmaz.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("dashboard-summary")]
        public async Task<ActionResult<TeacherDashboardSummaryDto>> GetDashboardSummary(CancellationToken ct)
        {
            var user = await GetAuthenticatedUserAsync();
            var summary = await _teacherService.GetDashboardSummaryAsync(user.Id, ct);
            return Ok(summary);
        }

        /// <summary>
        /// Issue #54: öğretmen dashboard "Sınavlarım" tablosu.
        /// Sadece authenticated öğretmenin sahip olduğu worksheet'ler döner; boşsa [].
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("worksheets-overview")]
        public async Task<ActionResult<List<TeacherWorksheetOverviewDto>>> GetWorksheetsOverview(CancellationToken ct)
        {
            var user = await GetAuthenticatedUserAsync();
            var overview = await _teacherService.GetWorksheetsOverviewAsync(user.Id, ct);
            return Ok(overview);
        }

        /// <summary>
        /// Issue #55: öğretmen dashboard "Geride Kalan Öğrenciler" listesi.
        /// Sadece authenticated öğretmenin sahip olduğu worksheet'lerdeki, en az bir bayrağı
        /// (IsLowCompletion / IsExpired) true olan öğrenci-worksheet çiftleri döner; boşsa [].
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("lagging-students")]
        public async Task<ActionResult<List<TeacherLaggingStudentDto>>> GetLaggingStudents(CancellationToken ct)
        {
            var user = await GetAuthenticatedUserAsync();
            var lagging = await _teacherService.GetLaggingStudentsAsync(user.Id, ct);
            return Ok(lagging);
        }

        // ------------------------------------------------------------------
        // Issue #95: bağımsız öğretmen tutor profili + öğrenci araması
        // ------------------------------------------------------------------

        /// <summary>
        /// Issue #95: authenticated kullanıcının kendi tutor profili. Teacher kaydı yoksa 404,
        /// bağımsız öğretmen değilse 400. Onay beklerken de döner (ApprovalStatus alanıyla).
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("tutor-profile")]
        public async Task<ActionResult<TutorProfileDto>> GetTutorProfile(CancellationToken ct)
        {
            var user = await GetAuthenticatedUserAsync();
            var result = await _teacherService.GetTutorProfileAsync(user.Id, ct);
            return MapTutorProfileResult(result);
        }

        /// <summary>
        /// Issue #95: sadece kendi IsIndependentTutor=true kaydını günceller. En az 1 ders, en az 1 mod
        /// (online/yüz yüze) ve ücret &gt; 0 zorunlu; aksi halde 400.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpPut("tutor-profile")]
        public async Task<ActionResult<TutorProfileDto>> UpdateTutorProfile([FromBody] UpdateTutorProfileDto request, CancellationToken ct)
        {
            var user = await GetAuthenticatedUserAsync();
            var result = await _teacherService.UpdateTutorProfileAsync(user.Id, request, ct);
            return MapTutorProfileResult(result);
        }

        /// <summary>
        /// Issue #95: öğrenci için branş/ders bazlı bağımsız öğretmen araması.
        /// Sadece IsIndependentTutor=true ve ApprovalStatus=Approved kayıtlar döner; boşsa [].
        /// </summary>
        [Authorize(Roles = "Student")]
        [HttpGet("search")]
        public async Task<ActionResult<List<TeacherSearchResultDto>>> SearchTutors([FromQuery] TeacherSearchFilterDto filter, CancellationToken ct)
        {
            if (filter.MinPrice.HasValue && filter.MaxPrice.HasValue && filter.MinPrice > filter.MaxPrice)
                return BadRequest(new { message = "minPrice, maxPrice değerinden büyük olamaz." });

            var results = await _teacherService.SearchTutorsAsync(filter, ct);
            return Ok(results);
        }

        /// <summary>
        /// Issue #95: tekil öğretmen public profili. Kayıt yoksa / bağımsız değilse / onaylı değilse
        /// hepsi 404 (var/yok ayrımı sızdırılmaz).
        /// </summary>
        [Authorize(Roles = "Student")]
        [HttpGet("{id:int}/public-profile")]
        public async Task<ActionResult<TeacherPublicProfileDto>> GetPublicProfile(int id, CancellationToken ct)
        {
            var profile = await _teacherService.GetPublicProfileAsync(id, ct);
            if (profile == null)
                return NotFound(new { message = "Öğretmen bulunamadı." });

            return Ok(profile);
        }

        private ActionResult<TutorProfileDto> MapTutorProfileResult(TutorProfileResultDto result)
        {
            if (result.Success && result.Profile != null)
                return Ok(result.Profile);

            if (result.NotFound)
                return NotFound(new { message = result.Message });

            // Forbidden bayrağı burada 400'e eşlenir: kayıt kullanıcının kendisine ait, sadece
            // bağımsız öğretmen değil — "yetki" değil "geçersiz istek" durumudur.
            return BadRequest(new { message = result.Message });
        }

    }
}
