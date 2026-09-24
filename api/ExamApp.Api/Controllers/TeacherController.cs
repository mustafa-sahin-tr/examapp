using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeacherController : BaseController
    {

        private readonly ITeacherService _teacherService;

        private readonly UserProfileCacheService _userProfileCacheService;
        private readonly IKeycloakService _keycloakService;
        private readonly ILogger<TeacherController> _logger;

        // Client'a donen tum metinler mesaj sozlugunden gelir (issue #184).
        private readonly IStringLocalizer<Messages> _localizer;

        public TeacherController(ITeacherService teacherService, UserProfileCacheService userProfileCacheService,
            IKeycloakService keycloakService,
            ILogger<TeacherController> logger,
            IStringLocalizer<Messages>? localizer = null
        )
            : base()
        {
            _userProfileCacheService = userProfileCacheService;
            _teacherService = teacherService;
            _keycloakService = keycloakService;
            _logger = logger;
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        }

        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterTeacher(RegisterTeacherDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var user = await GetAuthenticatedUserAsync();

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized(_localizer["teacher.refreshTokenMissing"].Value);

            // 🔹 Öğretmen zaten var mı?
            var response = await _teacherService.Save(user.Id, request);
            if (response == null)
            {
                return BadRequest(new { message = _localizer["teacher.registerFailed"].Value });
            }

            if (response.Success == false)
            {
                // issue #234: mevcut kaydın okul/bağımsızlık bilgisini değiştirme denemesi → 409.
                return response.Conflict
                    ? Conflict(new { message = response.Message })
                    : BadRequest(new { message = response.Message });
            }

            // issue #234: Keycloak rolü yalnızca doğrulama/çakışma kontrolleri geçtikten SONRA verilir — reddedilen
            // bir kayıt denemesi (ör. öğrenci kaydı olan kullanıcı) kullanıcıya Teacher rolü eklememeli.
            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Teacher);

            // issue #234 (security re-review): önbelleğe İSTEK verisi yazılmaz; okul DB'den ISchoolContextResolver ile
            // çözülür (öğretmen kaydı varsa Teachers.SchoolId esas — eşzamanlı teacher/student register yarışında iki
            // satır oluşsa bile istekteki okul kapsama taşınmaz). Rol ise bilinçli olarak az önce Keycloak'ta atanan
            // rolle yazılır: yalnızca RemoveAsync yapılsaydı bir sonraki istek profili auth-api'den yükler ve auth-api
            // Users.Role'ü yalnızca login/exchange'te senkronladığı için rol boş/eski gelip 1 saat cache'lenirdi
            // (ParentController'daki "rol cache'ini tazele" kaygısıyla aynı).
            // RefreshTokenAsync'ten ÖNCE: refresh token geçersizse fırlatır, ama DB zaten güncellendiği
            // için cache eski Role/SchoolId ile kalmamalı.
            // Okul talebi admin onayı bekliyorsa Teachers.SchoolId null'dır — onaysız okul hiçbir yere taşınmaz.
            user.Role = UserRole.Teacher.ToString();
            user.SchoolId = await HttpContext.RequestServices.GetRequiredService<ISchoolContextResolver>()
                .ResolveSchoolIdAsync(user, HttpContext.RequestAborted);

            // issue #189: Teacher.SchoolId değişmiş olabilir — Keycloak "school_id" attribute'unu
            // (JWT'ye taşınan ipucu) RefreshTokenAsync'ten ÖNCE güncelle ki hemen aşağıda alınan
            // yeni token bu claim'i güncel haliyle içersin. Keycloak hatası kayıt akışını kırmamalı.
            try
            {
                await _keycloakService.SetSchoolIdAttributeAsync(user.KeycloakId, user.SchoolId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Keycloak school_id attribute update failed for {KeycloakId}", user.KeycloakId);
            }

            // Rol Keycloak'ta güncellendi; GetAuthenticatedUserAsync yukarıda profili eski (Role boş, SchoolId eski)
            // haliyle Redis'e cache'lemiş olabilir — yukarıda çözülen Role + DB SchoolId ile tek seferde üzerine yaz.
            await _userProfileCacheService.SetAsync(user.KeycloakId, user);

            // 2. Keycloak token endpoint'ine isteği hazırla
            var tokenData = await _keycloakService.RefreshTokenAsync(refreshToken);

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

            // issue #234: schoolId / requestedSchoolId / approvalStatus / schoolApprovalPending eklemeli alanlardır;
            // mevcut istemciler (ui register-wizard, auth-ui complete-profile) yalnızca ilk üç alanı okur.
            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn,
                profileId = user.Id,
                schoolId = response.SchoolId,
                requestedSchoolId = response.RequestedSchoolId,
                approvalStatus = response.ApprovalStatus,
                schoolApprovalPending = response.SchoolApprovalPending,
                // issue #287: yeni kayıt her zaman onay bekler — UI "onay bekleniyor" ekranına geçer.
                teacherAccountApproved = response.AccountApproved,
                // issue #287 (review): sunucu metni (ör. "hesabınız yönetici onayı bekliyor") UI'da gösterilir.
                message = response.Message
            });
        }


        [Authorize]
        [HttpGet("check-teacher")]
        public async Task<IActionResult> CheckTeacher()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return NotFound(new { message = _localizer["teacher.userNotFound"].Value });
            }

            var teacher = await _teacherService.GetTeacher(user.Id);

            if (teacher != null)
            {
                // issue #287: onay durumu üst seviyede (teacherAccountApproved / teacherApplicationStatus / rejectionReason).
                var approval = TeacherApprovalState.From(teacher);
                return Ok(new
                {
                    HasTeacherRecord = true,
                    Teacher = teacher,
                    approval.TeacherAccountApproved,
                    approval.TeacherApplicationStatus,
                    approval.RejectionReason
                });
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
                return BadRequest(new { message = response?.Message ?? _localizer["teacher.themeUpdateFailed"].Value });
            }

            return Ok(response);
        }

        /// <summary>
        /// Issue #53: öğretmen dashboard özet kartları.
        /// teacherId route/query'den değil, authenticated user'dan alınır — başka öğretmenin verisi sızmaz.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("dashboard-summary")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<ActionResult<TeacherDashboardSummaryDto>> GetDashboardSummary(CancellationToken ct)
        {
            // issue #222: bağımsız öğretmen kapsamı için tenant bağlamı #190 yolundan (fail-closed).
            var scope = await GetSchoolScopeAsync(ct);
            var summary = await _teacherService.GetDashboardSummaryAsync(scope, ct);
            return Ok(summary);
        }

        /// <summary>
        /// Issue #54: öğretmen dashboard "Sınavlarım" tablosu.
        /// Sadece authenticated öğretmenin sahip olduğu worksheet'ler döner; boşsa [].
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("worksheets-overview")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<ActionResult<List<TeacherWorksheetOverviewDto>>> GetWorksheetsOverview(CancellationToken ct)
        {
            var scope = await GetSchoolScopeAsync(ct);
            var overview = await _teacherService.GetWorksheetsOverviewAsync(scope, ct);
            return Ok(overview);
        }

        /// <summary>
        /// Issue #55: öğretmen dashboard "Geride Kalan Öğrenciler" listesi.
        /// Sadece authenticated öğretmenin sahip olduğu worksheet'lerdeki, en az bir bayrağı
        /// (IsLowCompletion / IsExpired) true olan öğrenci-worksheet çiftleri döner; boşsa [].
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("lagging-students")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<ActionResult<List<TeacherLaggingStudentDto>>> GetLaggingStudents(CancellationToken ct)
        {
            var scope = await GetSchoolScopeAsync(ct);
            var lagging = await _teacherService.GetLaggingStudentsAsync(scope, ct);
            return Ok(lagging);
        }

        /// <summary>
        /// Issue #56: öğretmen dashboard "Benim Aktivitem" kartı. teacherId path'te yok — authenticated user'dan
        /// alınır (IDOR yüzeyi yok). <paramref name="days"/> 1..90, varsayılan 7; aralık dışı 400.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("own-activity-summary")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<ActionResult<TeacherOwnActivitySummaryDto>> GetOwnActivitySummary(
            [FromQuery] int days = 7, CancellationToken ct = default)
        {
            if (!IsValidActivityDays(days))
                return BadRequest(new { message = _localizer["teacher.activity.invalidDays"].Value });

            var scope = await GetSchoolScopeAsync(ct);
            return Ok(await _teacherService.GetOwnActivitySummaryAsync(scope, days, ct));
        }

        /// <summary>
        /// Issue #56: öğretmen dashboard "Öğrenci Aktivitesi" kartı + "En Aktif Öğrenciler" tablosu.
        /// Yalnızca öğretmenin kendi worksheet'lerine atanan öğrenciler; <paramref name="days"/> 1..90, varsayılan 7.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("students-activity-summary")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<ActionResult<TeacherStudentsActivitySummaryDto>> GetStudentsActivitySummary(
            [FromQuery] int days = 7, CancellationToken ct = default)
        {
            if (!IsValidActivityDays(days))
                return BadRequest(new { message = _localizer["teacher.activity.invalidDays"].Value });

            var scope = await GetSchoolScopeAsync(ct);
            return Ok(await _teacherService.GetStudentsActivitySummaryAsync(scope, days, ct));
        }

        private static bool IsValidActivityDays(int days)
            => days >= TeacherService.ActivityMinDays && days <= TeacherService.ActivityMaxDays;

        // ------------------------------------------------------------------
        // Issue #95: bağımsız öğretmen tutor profili + öğrenci araması
        // ------------------------------------------------------------------

        /// <summary>
        /// Issue #95: authenticated kullanıcının kendi tutor profili. Teacher kaydı yoksa 404,
        /// bağımsız öğretmen değilse 400. Onay beklerken de döner (ApprovalStatus alanıyla).
        /// </summary>
        // issue #287 (security review L2): ApprovedTeacher policy BİLEREK YOK — bağımsız öğretmen başvurusunun formu
        // bu profildir; hesabı onay bekleyen öğretmen başvurusunu doldurup görebilmeli. Arama/public profil zaten
        // yalnızca Approved tutor'ları döner; randevu uçları ayrıca kapılı.
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
        // issue #287 (security review L2): bilerek onaysız öğretmene açık — başvuru formu (bkz. GET tutor-profile).
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
                return BadRequest(new { message = _localizer["teacher.search.priceRangeInvalid"].Value });

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
                return NotFound(new { message = _localizer["teacher.notFound"].Value });

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
