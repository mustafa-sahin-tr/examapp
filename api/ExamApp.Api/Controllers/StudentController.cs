using ExamApp.Api.Services.UserRoles;
using ExamApp.Api.Services.Teachers.Authorization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : BaseController
    {
        private readonly IMinIoService _minioService;

        private readonly IStudentService _studentService;

        private readonly IKeycloakService _keycloakService;


        private readonly KeycloakSettings _keycloakSettings;

        private readonly UserProfileCacheService _userProfileCacheService;

        private readonly IStudentResetScheduler _studentResetScheduler;
        private readonly ILoginEventService _loginEventService;
        private readonly ILogger<StudentController> _logger;

        // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
        private readonly IStringLocalizer<Messages> _localizer;


        public StudentController(
            IMinIoService minioService,
            IStudentService studentService,
            UserProfileCacheService userProfileCacheService,
            IOptions<KeycloakSettings> options,
            IKeycloakService keycloakService,
            IStudentResetScheduler studentResetScheduler,
            ILoginEventService loginEventService,
            ILogger<StudentController> logger,
            IStringLocalizer<Messages>? localizer = null)
            : base()
        {
            _minioService = minioService;
            _studentService = studentService;
            _userProfileCacheService = userProfileCacheService;
            _keycloakService = keycloakService;
            _keycloakSettings = options.Value;
            _studentResetScheduler = studentResetScheduler;
            _loginEventService = loginEventService;
            _logger = logger;
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        }

        /// <summary>
        /// Öğrencinin mevcut oturumu hariç bir önceki başarılı giriş zamanı (issue #125).
        /// Önceki giriş yoksa 200 + { lastLoginAtUtc: null }.
        /// </summary>
        [Authorize(Roles = "Student")]
        [HttpGet("me/last-login")]
        public async Task<IActionResult> GetMyLastLogin(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(KeyCloakId))
            {
                return Unauthorized();
            }

            var previousLogin = await _loginEventService.GetPreviousSuccessfulLoginAsync(KeyCloakId, ct);
            return Ok(new LastLoginDto { LastLoginAtUtc = previousLogin?.OccurredAtUtc });
        }

        [Authorize(Roles = "Student")]
        [HttpPost("me/reset")]
        [EnableRateLimiting(StudentSelfResetRateLimiting.Policy)]
        public async Task<IActionResult> ResetMyStudentData(CancellationToken cancellationToken)
        {
            // issue #255: yalnız cache'e bakmak (TTL dolunca null) geçerli oturumu 401 ile düşürüyordu;
            // profil provider üzerinden çözülür, çözülemezse 404 (sub yoksa 401).
            var user = await GetAuthenticatedUserAsync(cancellationToken);
            if (user == null)
            {
                return UserNotResolved(_localizer["auth.authenticationFailed"].Value);
            }

            var student = await _studentService.GetStudentProfile(user.Id);
            if (student == null)
            {
                return NotFound(new { message = _localizer["student.notFound"].Value });
            }

            // Hangfire işi (JWT saklanmaz). issue #243: aynı kullanıcı için bekleyen iş varsa yenisi açılmaz,
            // mevcut işin id'si döner (aynı 202 sözleşmesi, farklı mesaj).
            var result = _studentResetScheduler.Enqueue(user.Id, student.Id, KeyCloakId);
            var messageKey = result.AlreadyPending ? "student.reset.alreadyPending" : "student.reset.queued";

            return Accepted(new { jobId = result.JobId, message = _localizer[messageKey].Value });
        }

        // Handy for manual browser testing; the actual reset must be triggered via POST.
        [Authorize(Roles = "Student")]
        [HttpGet("me/reset")]
        public IActionResult ResetMyStudentDataHelp()
        {
            return Ok(new
            {
                message = _localizer["student.reset.usePost"].Value
            });
        }

        [HttpPost("update-grade")]
        public async Task<IActionResult> UpdateStudentGrade([FromBody] int newGradeId)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _studentService.UpdateStudentGrade(user.Id, newGradeId);
            if (response == null)
            {
                return BadRequest(new { message = _localizer["student.registrationFailed"].Value });
            }
            if (response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }
            return await GetStudentProfile();
        }

        [HttpPost("update-avatar")]
        public async Task<IActionResult> UpdateAvatar(IFormFile avatar)
        {
            var user = await GetAuthenticatedUserAsync();

            if (avatar == null || avatar.Length == 0)
                return BadRequest(new { message = _localizer["student.invalidFile"].Value });

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(avatar.FileName)}";
            var filePath = $"avatars/{fileName}";

            using (var stream = avatar.OpenReadStream())
            {
                await _minioService.UploadFileAsync(stream, filePath, "student-avatars");
            }
            return await GetStudentProfile();
        }



        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentDto request)
        {
            // 🔹 Token’dan UserId'yi al // token var valid ama user
            var user = await GetAuthenticatedUserAsync();

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized(_localizer["auth.noRefreshToken"].Value);

            // 🔹 Öğrenci zaten var mı?
            // issue #277 (madde 4): rol değişikliği bağlamı, user.Role aşağıda üzerine yazılmadan ÖNCE alınır.
            var roleChange = new UserRoleChangeRequest(user.KeycloakId, user.Id, user.Role);
            var response = await _studentService.Save(user.Id, request);

            if (response == null)
            {
                return BadRequest(new { message = _localizer["student.registrationFailed"].Value });
            }

            if (response.Success == false)
            {
                // issue #234: öğretmen kaydı olan kullanıcı öğrenci olarak kaydolamaz → 409.
                return response.Conflict
                    ? Conflict(new { message = response.Message })
                    : BadRequest(new { message = response.Message });
            }

            // issue #234: Keycloak rolü yalnızca doğrulama/çakışma kontrolleri geçtikten SONRA verilir — reddedilen
            // bir kayıt denemesi kullanıcıya Student rolü eklememeli.
            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Student);

            // issue #277 (madde 4): Keycloak rolü BAŞARIYLA atandıktan sonra, rol gerçekten değiştiyse UserRoleChangedEvent
            // (auth-api Users.Role senkronu). Sıra/hata davranışı: UserRoleChangeRecorder.
            await HttpContext.RequestServices.GetRequiredService<IUserRoleChangeRecorder>()
                .RecordIfChangedAsync(roleChange, UserRole.Student, HttpContext.RequestAborted);

            // issue #234 (security re-review): önbelleğe İSTEK verisi yazılmaz; okul DB'den ISchoolContextResolver ile
            // çözülür (öğretmen kaydı varsa Teachers.SchoolId esas — eşzamanlı teacher/student register yarışında iki
            // satır oluşsa bile istekteki okul kapsama taşınmaz). Rol ise bilinçli olarak az önce Keycloak'ta atanan
            // rolle yazılır: yalnızca RemoveAsync yapılsaydı bir sonraki istek profili auth-api'den yükler ve auth-api
            // Users.Role'ü yalnızca login/exchange'te senkronladığı için rol boş/eski gelip 1 saat cache'lenirdi
            // (ParentController'daki "rol cache'ini tazele" kaygısıyla aynı).
            // RefreshTokenAsync'ten ÖNCE: refresh token geçersizse fırlatır, ama DB zaten güncellendiği
            // için cache eski Role/SchoolId ile kalmamalı.
            user.Role = UserRole.Student.ToString();
            user.SchoolId = await HttpContext.RequestServices.GetRequiredService<ISchoolContextResolver>()
                .ResolveSchoolIdAsync(user, HttpContext.RequestAborted);

            // issue #189: Student.SchoolId değişmiş olabilir — Keycloak "school_id" attribute'unu
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
            // 4. Access token'ı UI’a dön
            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn,
                profileId = user.Id
            });

        }

        // [Authorize]
        // [HttpGet("activity-heatmap")]
        // public async Task<IActionResult> GetActivityHeatmap()
        // {
        //     var user = await _userProfileCacheService.GetAsync(KeyCloakId);
        //     if (user == null)
        //     {
        //         return NotFound(new { message = _localizer["student.userNotFound"].Value });
        //     }
        //     var student = await _studentService.GetStudentProfile(user.Id);
        //     if (student == null)
        //     {
        //         return NotFound(new { message = _localizer["student.notFound"].Value });
        //     }
        //     var activityData = await _studentService.GetStudentActivityHeatmap(student.Id);
        //     return Ok(activityData);
        // }


        [Authorize]
        [HttpGet("check-student")]
        public async Task<IActionResult> CheckStudent()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return NotFound(new { message = _localizer["student.userNotFound"].Value });
            }
            var student = await _studentService.GetStudentProfile(user.Id);
            if (student != null)
            {
                return Ok(new { HasStudentRecord = true, Student = student });
            }

            return Ok(new { HasStudentRecord = false });
        }


        [HttpGet("grades")]
        public async Task<IActionResult> GetGradesAsync(CancellationToken ct)
        {
            var grades = await _studentService.GetGradesAsync(ct);
            return Ok(grades);
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetStudentProfile()
        {
            var user = await GetAuthenticatedUserAsync();
            var student = await _studentService.GetStudentProfile(user.Id);
            if (student == null)
            {
                return NotFound(new { message = _localizer["student.notFound"].Value });
            }
            return Ok(student);
        }

        /// <summary>
        /// issue #190: liste istek sahibinin okuluyla sınırlıdır — okul kimliği sunucu tarafında çözülür
        /// (GetSchoolScopeAsync), client parametresi yoktur. Admin/servis tüm okulları görür.
        /// </summary>
        [Authorize(Roles = "Teacher")]
        [HttpGet("lookup")]
        [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
        public async Task<IActionResult> GetStudentLookup(CancellationToken ct)
        {
            var scope = await GetSchoolScopeAsync(ct);
            var students = await _studentService.GetStudentLookupsAsync(scope, ct);
            return Ok(students);
        }

        [Authorize]
        [HttpPost("update-theme")]
        public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeDto request)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _studentService.UpdateStudentTheme(user.Id, request.ThemePreset, request.ThemeCustomConfig);

            if (response == null || !response.Success)
            {
                return BadRequest(new { message = response?.Message ?? _localizer["student.theme.updateFailed"].Value });
            }

            return Ok(response);
        }


    }
}
