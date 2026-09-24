using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
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

        private readonly IUserProfileProvider _userProfileProvider;

        private readonly IKeycloakService _keycloakService;

        // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
        private readonly IStringLocalizer<Messages> _localizer;

        public AuthController(AppDbContext context,
             IOptions<KeycloakSettings> options, IHttpClientFactory factory, UserProfileCacheService userProfileCacheService,
             IUserProfileProvider userProfileProvider,
             IKeycloakService keycloakService,
             IStringLocalizer<Messages>? localizer = null)
            : base()
        {
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
            _context = context;
            _keycloakSettings = options.Value;
            _userProfileCacheService = userProfileCacheService;
            _userProfileProvider = userProfileProvider;
            _keycloakService = keycloakService;
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshProfileInformation()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Bu endpoint'in tek amacı önbelleği tazelemek: önce mevcut kaydı düşür, sonra profili
            // yeniden yükle. (GetOrSet cache hit'te eski profili döndürürdü, önbellek hiç tazelenmezdi.)
            // issue #189: yükleme IUserProfileProvider'dan geçer — auth-api'den taze profil çeker,
            // SchoolId'yi DB'den doldurur ve cache'e yazar (BaseController ile aynı yol).
            await _userProfileCacheService.RemoveAsync(sub);
            var profile = await _userProfileProvider.GetAsync(sub);

            if (profile != null)
            {
                if (profile.Role == "Student")
                {
                    // issue #243 review: XP aynı sorguda (ek round-trip yok) toplanır; Level okuma anında XP'den
                    // hesaplanır (StudentService.GetStudentProfile ile aynı kural, minimum 1). Eskiden ikisi de 0 dönüyordu.
                    var row = await _context.Students
                    .Where(s => s.UserId == profile.Id)
                    .Select(s => new { Student = s, XP = s.StudentPoints.Sum(sp => sp.XP) })
                    .FirstOrDefaultAsync();
                    if (row == null)
                        return Ok(profile);
                    var student = row.Student;
                    profile.Student = new StudentDto
                    {
                        Id = student.Id,
                        XP = row.XP,
                        Level = StudentLevel.FromXp(row.XP),
                        GradeId = student.GradeId,
                        SchoolName = student.SchoolName,
                        SchoolId = student.SchoolId,
                        AvatarUrl = profile.Avatar,
                        FullName = profile.FullName,
                        ThemePreset = student.ThemePreset,
                        ThemeCustomConfig = student.ThemeCustomConfig
                    };
                }
                else if (profile.Role == "Teacher")
                {
                    // Teacher için ek bilgiler eklenebilir
                    var teacher = await _context.Teachers
                    .OrderBy(t => t.Id) // UserId unique değil — Save/SchoolContextResolver ile aynı deterministik seçim
                    .FirstOrDefaultAsync(t => t.UserId == profile.Id);
                    if (teacher == null)
                        return Ok(profile);
                    var approval = TeacherApprovalState.From(teacher);
                    profile.Teacher = new TeacherDto
                    {
                        Id = teacher.Id,
                        AvatarUrl = profile.Avatar,
                        FullName = profile.FullName,
                        SchoolName = teacher.SchoolName,
                        SchoolId = teacher.SchoolId,
                        ThemePreset = teacher.ThemePreset,
                        ThemeCustomConfig = teacher.ThemeCustomConfig,
                        // issue #287: UI onaysız öğretmene öğretmen menülerini kapatıp "onay bekleniyor" gösterir.
                        TeacherAccountApproved = approval.TeacherAccountApproved,
                        TeacherApplicationStatus = approval.TeacherApplicationStatus,
                        RejectionReason = approval.RejectionReason
                    };
                }
            }

            return Ok(profile);
        }










        /// <summary>
        /// Tanı endpoint'i (issue #181): bu istek için çözümlenmiş kültürü ve onu hangi
        /// provider'ın belirlediğini döner. #184'teki mesaj sözlüğü ve ui-tester doğrulaması
        /// bunun üzerine kurulacak.
        /// Kaynak (source) değerleri sabit bir sözleşmedir, iç tip adı sızdırılmaz:
        /// "header" (Accept-Language), "profile" (kullanıcının kayıtlı tercihi),
        /// "default" (hiçbiri eşleşmedi → tr-TR).
        /// </summary>
        [Authorize]
        [HttpGet("culture")]
        public IActionResult GetCurrentCulture()
        {
            var feature = HttpContext.Features.Get<IRequestCultureFeature>();
            var requestCulture = feature?.RequestCulture
                ?? new RequestCulture(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);

            return Ok(new
            {
                culture = requestCulture.Culture.Name,
                uiCulture = requestCulture.UICulture.Name,
                source = MapCultureSource(feature?.Provider)
            });
        }

        /// <summary>
        /// Provider tipini istemciye açık sözleşme değerine eşler ("header" | "profile" | "default").
        /// </summary>
        private static string MapCultureSource(IRequestCultureProvider? provider) => provider switch
        {
            NormalizedAcceptLanguageCultureProvider => "header",
            UserPreferredLocaleCultureProvider => "profile",
            _ => "default"
        };

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
                return Unauthorized(_localizer["auth.noRefreshToken"].Value);

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
