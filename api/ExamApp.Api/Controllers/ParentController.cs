using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParentController : BaseController
    {
        private readonly IParentService _parentService;
        private readonly IKeycloakService _keycloakService;

        // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
        private readonly IStringLocalizer<Messages> _localizer;

        public ParentController(IParentService parentService, IKeycloakService keycloakService, IStringLocalizer<Messages>? localizer = null) : base()
        {
            _parentService = parentService;
            _keycloakService = keycloakService;
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        }

        // Veli kaydı: profil alanı yok — sadece realm rolünü ata ve Parent satırını aç.
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegisterParent()
        {
            var user = await GetAuthenticatedUserAsync();
            if (user == null || user.Id <= 0)
            {
                return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });
            }

            // issue #277 (madde 4): rol değişikliği bağlamı Keycloak çağrısından ÖNCE alınır (aşağıda user.Role üzerine yazılıyor).
            var roleChange = new UserRoleChangeRequest(user.KeycloakId, user.Id, user.Role);

            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Parent);

            // issue #277 (madde 3/4): Parent satırı + rol event'i servis katmanında tek SaveChanges'te, Keycloak rolü ATANDIKTAN
            // sonra (sıra/hata davranışı: ParentService). Satır eskiden token yenilemeden SONRA yazılıyordu; geçersiz refresh
            // token'da rol atanmış ama satır/event yazılmamış kalmasın diye öne alındı.
            var parentId = await _parentService.RegisterAsync(user.Id, roleChange, HttpContext.RequestAborted);

            // Rol Keycloak'ta güncellendi; GetAuthenticatedUserAsync yukarıda profili eski
            // (Role boş) haliyle Redis'e cache'lemiş olabilir. Cache'i güncel rolle tazele
            // ki 1 saat boyunca diğer endpoint'ler eski/boş rolü görmesin.
            user.Role = UserRole.Parent.ToString();
            var userProfileCacheService = HttpContext.RequestServices.GetRequiredService<UserProfileCacheService>();
            await userProfileCacheService.SetAsync(user.KeycloakId, user);

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized(_localizer["auth.noRefreshToken"].Value);

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

            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn,
                profileId = parentId
            });
        }

        [Authorize]
        [HttpGet("check-parent")]
        public async Task<IActionResult> CheckParent()
        {
            var user = await GetAuthenticatedUserAsync();
            var hasRecord = user != null && user.Id > 0
                && await _parentService.HasParentRecordAsync(user.Id, HttpContext.RequestAborted);
            return Ok(new { HasParentRecord = hasRecord });
        }
    }
}
