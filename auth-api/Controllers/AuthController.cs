using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Linq;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Requests;
using ExamApp.Api.Models.Responses;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        /// <summary>
        /// exam API'deki <c>LoginEvent.AttemptedIdentifier</c> kolonu 256 karakterle sınırlı; daha uzun bir
        /// girdi orada 400 alıp consumer'ı dead-letter'a düşürmesin diye kırpılır (issue #100).
        /// </summary>
        private const int AttemptedIdentifierMaxLength = 256;

        protected readonly AppDbContext _context;
        private readonly KeycloakSettings _keycloakSettings;
        private readonly IKeycloakService _keycloakService;
        private readonly ILogger<AuthController> _logger;
        private readonly IStringLocalizer<Messages> _localizer;
        private readonly RegistrationSettings _registrationSettings;

        public AuthController(AppDbContext context,
             IOptions<KeycloakSettings> options, IHttpClientFactory factory,
             IKeycloakService keycloakService,
             ILogger<AuthController> logger,
             IStringLocalizer<Messages>? localizer = null,
             IOptions<RegistrationSettings>? registrationOptions = null)
            : base()
        {
            _registrationSettings = registrationOptions?.Value ?? new RegistrationSettings();
            // Opsiyonel: testler controller'ı `new` ile kurar (bkz. api/ExamApp.Api/Resources/README.md).
            _localizer = localizer ?? FallbackMessageLocalizer.Instance;
            _context = context;
            _keycloakSettings = options.Value;
            _keycloakService = keycloakService;
            _logger = logger;
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

        [Authorize]
        [HttpGet("user-profile")]
        public async Task<IActionResult> UserProfile()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await GetUserProfile(sub);
            return Ok(profile);
        }


        /// <summary>
        /// Anonim kayıt. Issue #240: yanıt e-postanın zaten kayıtlı olup olmadığını ELE VERMEZ.
        /// <list type="bullet">
        /// <item>E-postadan bağımsız doğrulamalar (model, seed alanı, rol, e-posta biçimi) DB/Keycloak'tan ÖNCE → 400.</item>
        /// <item>Yeni kayıt, yerel DB'de kayıtlı e-posta, Keycloak 409, Keycloak 400 (yerel kurallarla Keycloak
        /// politikası arasında kayma) ve eşzamanlı unique ihlali → aynı 200 + <see cref="RegisterResponse"/>.</item>
        /// <item>Kayıtlı e-posta yolu da Keycloak'a gerçek bir istek atar; Keycloak kesintisinde iki yol aynı 500'ü verir.</item>
        /// <item>200 ve 500 yanıtları <see cref="RegistrationSettings.MinimumResponseMilliseconds"/> dolmadan dönmez.</item>
        /// </list>
        /// </summary>
        [HttpPost("register")]
        // Kayıt da kimliksiz ve Keycloak kullanıcı oluşturur — login/exchange ile aynı IP bazlı limit (#231 review).
        [EnableRateLimiting(AuthRateLimiting.AuthAttemptsPolicy)]
        public async Task<IActionResult> Register(RegisterDto request, CancellationToken ct = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // İkinci savunma (seed sahiplik kilidi): seed alanı e-postası yalnızca dev seed ucundan açılabilir;
            // register ile seed desenli bir hesap açılıp sonradan seed aracı tarafından sahiplenilemez/silinemez.
            // Karar e-postanın varlığına değil alan adına bakar — enumeration sızıntısı değil.
            if (ExamApp.Foundation.Security.SeedDataConventions.IsSeedEmail(request.Email?.Trim().ToLowerInvariant()))
            {
                return BadRequest(new { message = _localizer["auth.register.emailDomainNotAllowed"].Value });
            }

            // Anonim uç yalnızca uygulama rollerini atayabilir; aksi halde Keycloak'taki her realm rolü
            // (Admin, exam-service) istek gövdesinden seçilebiliyordu (#240). Karar e-postadan bağımsız.
            var role = AllowedAppRoles.FirstOrDefault(r => r.Equals(request.Role?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                return BadRequest(new { message = _localizer["auth.register.invalidRole"].Value });
            }

            // [EmailAddress] yalnızca '@' arar; Keycloak'a bozuk adres gidip 400 alınması (yalnızca yeni e-posta
            // yolunda olur) bir oracle olurdu. Biçim kararı e-postanın varlığından bağımsız ve DB'den önce.
            if (!IsWellFormedRegistrationEmail(request.Email))
            {
                return BadRequest(new { message = _localizer["auth.register.invalidEmail"].Value });
            }

            try
            {
                if (await _context.Users.AnyAsync(u => u.Email == request.Email, ct))
                {
                    // Yeni e-posta yolu Keycloak'a gidiyor; bu yol da gitmeli ki Keycloak erişilemezken iki yol
                    // aynı 500'ü versin (yalnızca kayıtlı e-postada 200 dönmesi kesintide oracle olurdu).
                    // Sonuç kullanılmaz — tek amaç gerçek bir Keycloak admin round-trip'i.
                    await _keycloakService.FindUserIdByUsernameAsync(request.Email, ct);
                    _logger.LogInformation("Register: e-posta yerel DB'de zaten kayıtlı; genel kabul yanıtı dönülüyor");
                    return await RegisterAcceptedAsync(stopwatch, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Register: kayıt ön kontrolü başarısız");
                return await RegisterFailedAsync(stopwatch, ct);
            }

            var keycloakUserId = string.Empty;
            try
            {
                // CreateUserAsync/SetRoleAsync bilinçli olarak ct ALMAZ: Keycloak kullanıcısı oluştuktan sonra istek
                // iptali yarım iş bırakmamalı (yetim Keycloak kullanıcısı → tekrar kayıt 409 → hesap kalıcı bozuk).
                keycloakUserId = await _keycloakService.CreateUserAsync(
                    request.Email, request.Password, request.Email,
                    request.FirstName, request.LastName);
                await _keycloakService.SetRoleAsync(keycloakUserId, role);
                var user = new User
                {
                    FullName = request.FirstName + " " + request.LastName,
                    Email = request.Email,
                    Role = role,
                    KeycloakId = keycloakUserId
                };

                // Issue #185: BadgeService yeni kullanıcının dil tercihini (henüz hiç
                // değiştirilmemiş, varsayılan) event üzerinden öğrenir — senkron çağrı yok.
                // user.Id identity DB'den üretildiği için outbox satırı, User satırıyla aynı
                // transaction içinde ama İKİNCİ SaveChanges'te yazılır (WorksheetAccessRequestService
                // ile aynı desen); tek transaction olduğu için yine atomik.
                // Geri dönüşsüz nokta: Keycloak kullanıcısı artık var. Yerel yazım istemci iptaliyle
                // (RequestAborted) kesilirse Keycloak'ta yetim kullanıcı kalırdı → CancellationToken.None.
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);

                    _context.Users.Add(user);
                    await _context.SaveChangesAsync(CancellationToken.None);

                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>(),
                        Content = JsonSerializer.Serialize(new UserPreferredLocaleChangedEvent
                        {
                            UserId = user.Id,
                            KeycloakId = keycloakUserId,
                            PreferredLocale = user.PreferredLocale,
                            ChangedAtUtc = DateTime.UtcNow
                        })
                    });
                    await _context.SaveChangesAsync(CancellationToken.None);

                    await tx.CommitAsync(CancellationToken.None);
                });
            }
            catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Conflict)
            {
                // Kullanıcı adı/e-posta Keycloak'ta var ama yerel DB'de yok (büyük/küçük harf farkı, yarım kalmış
                // eski kayıt vb.). Keycloak'ın 409 mesajı istemciye gitmez; yanıt yeni kayıtla aynıdır.
                _logger.LogWarning("Register: Keycloak kullanıcı çakışması (409) — kullanıcı Keycloak'ta var, yerel DB'de yok; genel kabul yanıtı dönülüyor");
                return await RegisterAcceptedAsync(stopwatch, ct);
            }
            catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Validation)
            {
                // Yerel doğrulama Keycloak politikasıyla hizalı olduğu için buraya düşülmemeli. Düşülürse bu bir
                // kayma (realm policy değişti vb.): kayıtlı e-posta yolu Keycloak'a kullanıcı göndermediği için aynı
                // girdide 400 veremez, bu yüzden 400 dönmek e-posta varlığı oracle'ı olurdu → genel kabul yanıtı.
                // Kullanıcı oluşmadı; kayma operasyonel olarak yakalansın diye Warning.
                _logger.LogWarning(ex, "Register: Keycloak kullanıcıyı doğrulama hatasıyla reddetti (yerel kurallar ile realm politikası kaymış olabilir); genel kabul yanıtı dönülüyor");
                return await RegisterAcceptedAsync(stopwatch, ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Eşzamanlı aynı e-posta kaydı (yarış): yerel satır yazılamadı. Bu istekte oluşturulan Keycloak
                // kullanıcısı geri alınır, yanıt yeni kayıtla aynıdır. Diğer DB hataları aşağıdaki 500 yoluna düşer.
                _logger.LogWarning(ex, "Register: yerel kullanıcı yazılamadı (eşzamanlı kayıt?); genel kabul yanıtı dönülüyor");
                await TryDeleteRegisteredKeycloakUserAsync(keycloakUserId);
                return await RegisterAcceptedAsync(stopwatch, ct);
            }
            catch (Exception ex) when (!string.IsNullOrEmpty(keycloakUserId) || ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Keycloak kullanıcısı bu istekte oluşturulduysa iptal dahil her hatada geri alınır (yetim kalmaz).
                _logger.LogError(ex, "Register: kullanıcı kaydı başarısız");
                await TryDeleteRegisteredKeycloakUserAsync(keycloakUserId);
                return await RegisterFailedAsync(stopwatch, ct);
            }

            // Kabul yanıtı try dışında: taban beklemesi iptal edilirse commit edilmiş kayıt geri alınmamalı.
            return await RegisterAcceptedAsync(stopwatch, ct);
        }

        /// <summary>
        /// Eşzamanlı kayıt yarışında unique ihlali mi (#240)? Yalnızca PostgreSQL <c>23505</c>; diğer DB hataları
        /// 500 yoluna gider. Testler (SQLite) ihlali iç <see cref="Npgsql.PostgresException"/> ile temsil eder.
        /// </summary>
        internal static bool IsUniqueViolation(DbUpdateException ex)
            => ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

        /// <summary>
        /// Keycloak'ın bu realm'deki kurallarıyla hizalı e-posta biçimi (#240): realm'de e-posta validator'ı yok,
        /// kullanıcı adı = e-posta. Boşluk yok, tek '@', <see cref="System.Net.Mail.MailAddress"/> ile birebir
        /// ayrışabilen, en fazla 254 karakter (RFC 5321). E-postanın var olup olmadığına bakmaz.
        /// </summary>
        internal static bool IsWellFormedRegistrationEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Any(char.IsWhiteSpace) ||
                email.Count(c => c == '@') != 1)
            {
                return false;
            }

            return System.Net.Mail.MailAddress.TryCreate(email, out var parsed) &&
                   string.Equals(parsed.Address, email, StringComparison.Ordinal) &&
                   parsed.Host.Contains('.') && !parsed.Host.StartsWith('.') && !parsed.Host.EndsWith('.');
        }

        /// <summary>
        /// Register'ın tek kabul yanıtı (#240): her yolda aynı gövde; taban süre dolana kadar beklenir ki
        /// "zaten kayıtlı" kısa yolu yanıt süresinden ayırt edilemesin.
        /// </summary>
        private async Task<IActionResult> RegisterAcceptedAsync(System.Diagnostics.Stopwatch stopwatch, CancellationToken ct)
        {
            await WaitForRegisterFloorAsync(stopwatch, ct);
            return Ok(new RegisterResponse { Message = _localizer["auth.register.accepted"].Value });
        }

        /// <summary>Register'ın tek hata yanıtı; kabul yanıtı gibi taban süreyi bekler (#240).</summary>
        private async Task<IActionResult> RegisterFailedAsync(System.Diagnostics.Stopwatch stopwatch, CancellationToken ct)
        {
            await WaitForRegisterFloorAsync(stopwatch, ct);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = _localizer["auth.register.failed"].Value });
        }

        private async Task WaitForRegisterFloorAsync(System.Diagnostics.Stopwatch stopwatch, CancellationToken ct)
        {
            var floor = TimeSpan.FromMilliseconds(Math.Max(0, _registrationSettings.MinimumResponseMilliseconds));
            var remaining = floor - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, ct);
            }
            else if (floor > TimeSpan.Zero)
            {
                // Taban aşıldı: bu istekte yanıt süresi yolu ele verebilir — taban değeri gözden geçirilmeli.
                _logger.LogWarning(
                    "Register: yanıt süresi tabanı aşıldı ({ElapsedMs} ms > {FloorMs} ms); Registration:MinimumResponseMilliseconds artırılmalı",
                    (long)stopwatch.Elapsed.TotalMilliseconds, (long)floor.TotalMilliseconds);
            }
        }

        private async Task TryDeleteRegisteredKeycloakUserAsync(string keycloakUserId)
        {
            // Yalnızca bu istekte oluşturulduysa geri alınır.
            if (string.IsNullOrEmpty(keycloakUserId))
            {
                return;
            }

            try
            {
                await _keycloakService.DeleteUserAsync(keycloakUserId);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Register: yarım kalan Keycloak kullanıcısı silinemedi ({KeycloakUserId})", keycloakUserId);
            }
        }


        // Issue #219: toplu ad/e-posta/KeycloakId çözümü yalnızca servisler (exam API) içindir;
        // son kullanıcı token'ı ile çağrılamaz. Gateway'de de /api/auth/users/lookup engellidir.
        [Authorize(Policy = "Service")]
        [HttpPost("users/lookup")]
        public async Task<IActionResult> GetUsersByIds([FromBody] BulkUserLookupRequest request)
        {
            if (request == null || request.UserIds == null || request.UserIds.Count == 0)
            {
                return BadRequest("UserIds cannot be empty");
            }

            var distinctIds = request.UserIds.Distinct().ToList();
            if (request.IncludeAccountStatus && distinctIds.Count > MaxAccountStatusLookupIds)
            {
                // Hesap durumu Keycloak'a kullanıcı başı GET demek; sayfalı admin listeleri (≤ 100) dışında izin verme.
                return BadRequest(new { message = _localizer["auth.usersLookup.accountStatusTooManyIds", MaxAccountStatusLookupIds].Value });
            }

            var users = await _context.Users
                .Where(u => !u.IsDeleted && distinctIds.Contains(u.Id))
                .Select(u => new UserLookupResponse
                {
                    Id = u.Id,
                    KeycloakId = u.KeycloakId,
                    FullName = u.FullName,
                    Email = u.Email,
                    Avatar = u.AvatarUrl ?? string.Empty,
                    Role = u.Role.ToString()
                })
                .ToListAsync();

            if (request.IncludeAccountStatus && users.Count > 0)
            {
                await FillAccountStatusAsync(users);
            }

            return Ok(users);
        }

        /// <summary>
        /// Issue #152: Keycloak <c>enabled</c> okumasının üst süre sınırı. Exam API → auth-api çağrısı ServiceDefaults'un
        /// 10 sn attempt timeout'una tabi; bütçe bunun altında kalmalı ki yavaş Keycloak lookup'ın tamamını düşürmesin.
        /// </summary>
        private static readonly TimeSpan AccountStatusBudget = TimeSpan.FromSeconds(5);

        /// <summary>Issue #152 review: <c>IncludeAccountStatus=true</c> iken en fazla bu kadar (tekil) id.</summary>
        internal const int MaxAccountStatusLookupIds = 100;

        /// <summary>
        /// Fail-soft: Keycloak erişilemez/yapılandırılmamış ya da bütçe dolduysa okunamayan kullanıcıların
        /// <c>Enabled</c> alanı null kalır; lookup yine 200 döner (ad/e-posta auth DB'den gelir).
        /// </summary>
        private async Task FillAccountStatusAsync(List<UserLookupResponse> users)
        {
            var requestAborted = HttpContext?.RequestAborted ?? CancellationToken.None;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            budget.CancelAfter(AccountStatusBudget);

            try
            {
                var keycloakIds = users.Select(u => u.KeycloakId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                var statuses = await _keycloakService.GetUsersEnabledAsync(keycloakIds, budget.Token);
                foreach (var user in users)
                {
                    if (!string.IsNullOrWhiteSpace(user.KeycloakId) && statuses.TryGetValue(user.KeycloakId, out var enabled))
                    {
                        user.Enabled = enabled;
                    }
                }

                var unknown = users.Count(u => u.Enabled is null);
                if (unknown > 0)
                {
                    _logger.LogWarning("[users/lookup] {Unknown}/{Total} kullanıcının Keycloak hesap durumu okunamadı; Enabled=null döndü.",
                        unknown, users.Count);
                }
            }
            catch (Exception ex) when (ex is KeycloakException or HttpRequestException or Polly.ExecutionRejectedException
                                       || (ex is OperationCanceledException && !requestAborted.IsCancellationRequested))
            {
                _logger.LogWarning(ex, "[users/lookup] Keycloak hesap durumu okunamadı; {Count} kullanıcı için Enabled=null.", users.Count);
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
        [EnableRateLimiting(AuthRateLimiting.AuthAttemptsPolicy)]
        public async Task<IActionResult> Login(LoginDto request)
        {
            TokenResponseDto tokenDto;
            try
            {
                tokenDto = await _keycloakService.LoginAsync(request.Email, request.Password, HttpContext.RequestAborted);
            }
            catch (KeycloakException ex)
            {
                // Login denemesi Keycloak seviyesinde reddedildi (kötü kimlik bilgisi vb.) — henüz
                // bir sub'a erişimimiz yok. Issue #100: doğrulanmamış e-posta KeycloakUserId'ye
                // YAZILMAZ (başkasının e-postası "kimlik" gibi kalıcılaşmasın); ayrı AttemptedIdentifier
                // alanında taşınır. Şifre/token event'e YAZILMAZ.
                await TryWriteLoginAttemptedEventAsync(
                    keycloakUserId: null,
                    role: "Unknown",
                    success: false,
                    attemptedIdentifier: NormalizeAttemptedIdentifier(request.Email));

                // Issue #231: istemciye yalnızca genel mesaj; Keycloak ayrıntısı log'da kalır.
                // Sınıflandırılmamış hatalar global handler'a (log + gövdesinde stack olmayan 500) bırakılır.
                var failure = KeycloakTokenFailureResult(ex, "login", "auth.login.invalidCredentials");
                if (failure is null)
                {
                    throw;
                }
                return failure;
            }

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

            await TryWriteLoginAttemptedEventAsync(
                keycloakUserId: sub,
                role: roles.FirstOrDefault() ?? "Unknown",
                success: true);

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
                return Unauthorized(new { message = _localizer["auth.refresh.invalidToken"].Value });

            // 2. Keycloak token endpoint'ine isteği hazırla
            TokenResponseDto tokenData;
            try
            {
                tokenData = await _keycloakService.RefreshTokenAsync(refreshToken, HttpContext.RequestAborted);
            }
            catch (KeycloakException ex)
            {
                // Süresi dolmuş/iptal edilmiş refresh token → 401, Keycloak erişilemez → 503 (#231 review).
                var failure = KeycloakTokenFailureResult(ex, "refresh token", "auth.refresh.invalidToken");
                if (failure is null)
                {
                    throw;
                }
                return failure;
            }
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
        [EnableRateLimiting(AuthRateLimiting.AuthAttemptsPolicy)]
        public async Task<IActionResult> EchangeCode(CodeDto dto)
        {
            TokenResponseDto tokenDto;
            try
            {
                tokenDto = await _keycloakService.ExchangeTokenAsync(dto.Code, HttpContext.RequestAborted);
            }
            catch (KeycloakException ex)
            {
                // Authorization code exchange'i başarısız oldu — henüz bir sub'a erişimimiz yok
                // (code tek kullanımlık/kısa ömürlü, kimlik belirleyici olarak taşınmaz).
                await TryWriteLoginAttemptedEventAsync(
                    keycloakUserId: null,
                    role: "Unknown",
                    success: false);

                var failure = KeycloakTokenFailureResult(ex, "code exchange", "auth.exchange.invalidCode");
                if (failure is null)
                {
                    throw;
                }
                return failure;
            }

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

            // Provision our local Users row on first login (Keycloak-native
            // registration no longer hits /api/auth/register). Also keeps the
            // stored role in sync once the user picks one via profile completion.
            await EnsureLocalUserAsync(jwt, roles);

            await TryWriteLoginAttemptedEventAsync(
                keycloakUserId: sub,
                role: roles.FirstOrDefault() ?? "Unknown",
                success: true);

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

        /// <summary>
        /// Ensures a local <c>Users</c> row exists for the authenticated Keycloak
        /// subject and its stored role tracks the realm role once assigned.
        /// </summary>
        private async Task EnsureLocalUserAsync(JwtSecurityToken jwt, List<string> appRoles)
        {
            var sub = jwt.Claims.First(c => c.Type == "sub").Value;
            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? string.Empty;
            var fullName = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                ?? $"{jwt.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value} {jwt.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value}".Trim();
            if (string.IsNullOrWhiteSpace(fullName)) fullName = email;

            // The app role (Student/Teacher/Parent) — empty until profile completion assigns it.
            var appRole = appRoles.FirstOrDefault(r =>
                r.Equals("Student", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Teacher", StringComparison.OrdinalIgnoreCase) ||
                r.Equals("Parent", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.KeycloakId == sub);
            if (user == null)
            {
                _context.Users.Add(new User
                {
                    KeycloakId = sub,
                    Email = email,
                    FullName = fullName,
                    Role = appRole
                });
                await _context.SaveChangesAsync();
                return;
            }

            if (!string.IsNullOrEmpty(appRole) && !string.Equals(user.Role, appRole, StringComparison.OrdinalIgnoreCase))
            {
                user.Role = appRole;
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Allows an already-authenticated (Keycloak SSO) user with no app role yet
        /// to pick one (Student/Teacher/Parent) and have it persisted both locally
        /// and on their Keycloak realm role mapping. Operates only on the caller's
        /// own identity (sub claim) — never accepts a target user id.
        /// </summary>
        [Authorize]
        [HttpPost("complete-profile")]
        public async Task<IActionResult> CompleteProfile([FromBody] CompleteProfileDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Role) ||
                !AllowedAppRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest("Role must be one of: Student, Teacher, Parent.");
            }

            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(sub))
                return Unauthorized();

            // !IsDeleted filter mirrors GetUserProfile's lookup — a deactivated account
            // must not be able to self-grant a Keycloak role while its JWT is still valid.
            var user = await _context.Users.FirstOrDefaultAsync(u => u.KeycloakId == sub && !u.IsDeleted);
            if (user == null)
            {
                // Should have been provisioned at /exchange login time (or the account was
                // soft-deleted). Distinct from "role already set" (409 below) so the
                // frontend/ops can tell a genuinely broken/missing profile apart from an
                // already-completed one instead of both looking like "already done".
                return NotFound("No local user profile found for this account. Please log in again.");
            }

            // Optimization/UX check only — NOT the concurrency safety boundary. Two
            // concurrent requests can both pass this check before either writes; the
            // ExecuteUpdateAsync below is what actually enforces "only once".
            if (!string.IsNullOrEmpty(user.Role))
            {
                return Conflict("Role is already set for this account.");
            }

            // Normalize to the canonical casing used elsewhere (Student/Teacher/Parent).
            var role = AllowedAppRoles.First(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));

            try
            {
                // Keycloak call happens first, deliberately. SetRoleAsync is now exclusive
                // (it removes any existing app-role mapping before adding the new one), so
                // even if two concurrent requests both reach this line — one with "Teacher",
                // one with "Student" — Keycloak can never end up with both roles stacked;
                // whichever call's POST lands last simply leaves the user with exactly one
                // app role. If this call fails, we bail out here without touching the local
                // DB, so we never report 200 while Keycloak didn't actually get the role.
                await _keycloakService.SetRoleAsync(user.KeycloakId, role);
            }
            catch (KeycloakException ex)
            {
                // Keycloak ayrıntısı (hata gövdesi, kullanıcı id) istemciye değil log'a (#231 review).
                _logger.LogWarning(ex, "complete-profile: Keycloak rol ataması başarısız (sub={Sub}, role={Role})", sub, role);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { message = _localizer["auth.completeProfile.roleAssignmentFailed"].Value });
            }

            // This single conditional UPDATE is the real one-time guard for the *local*
            // DB: it only affects a row if Role is still empty at write time, so exactly
            // one concurrent request's local write wins (no read-then-write gap). If we
            // lose this race (0 rows affected), another request already completed the
            // profile locally first; Keycloak still ends up correct (exclusive, single
            // role) either way, so we treat this the same as "already set".
            var affected = await _context.Users
                .Where(u => u.KeycloakId == sub && (u.Role == null || u.Role == ""))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.Role, role)
                    .SetProperty(u => u.UpdateTime, DateTime.UtcNow));

            if (affected == 0)
            {
                return Conflict("Role is already set for this account.");
            }

            var profile = await GetUserProfile(sub);
            return Ok(profile);
        }

        /// <summary>
        /// Oturum sahibinin dil tercihini günceller (issue #181). Sadece çağıranın kendi
        /// kimliği (sub claim) üzerinde çalışır — hedef kullanıcı id'si kabul edilmez.
        /// Değer normalize edilir ("tr-TR" → "tr"); desteklenmeyen dil 400 döner.
        /// </summary>
        [Authorize]
        [HttpPut("me/locale")]
        [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdatePreferredLocale([FromBody] UpdatePreferredLocaleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PreferredLocale))
            {
                return Problem(
                    title: "Invalid locale",
                    detail: "preferredLocale is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!SupportedLocales.TryNormalize(request.PreferredLocale, out var locale))
            {
                return Problem(
                    title: "Unsupported locale",
                    detail: $"preferredLocale must be one of: {string.Join(", ", SupportedLocales.All)}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(sub))
                return Unauthorized();

            // !IsDeleted filtresi GetUserProfile ile aynı — pasifleştirilmiş bir hesap,
            // JWT'si hâlâ geçerliyken bile profilini değiştirememeli.
            var user = await _context.Users.FirstOrDefaultAsync(u => u.KeycloakId == sub && !u.IsDeleted);
            if (user == null)
            {
                return Problem(
                    title: "Profile not found",
                    detail: "No local user profile found for this account.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (!string.Equals(user.PreferredLocale, locale, StringComparison.Ordinal))
            {
                user.PreferredLocale = locale;

                // Issue #185: BadgeService'e senkron çağrı yapılmaz — hedef kullanıcının yeni
                // dili outbox üzerinden taşınır. Değer gerçekten değiştiğinde YAZILIR (no-op'ta
                // gürültü event'i olmasın diye). Aynı SaveChanges ile user satırıyla atomik.
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>(),
                    Content = JsonSerializer.Serialize(new UserPreferredLocaleChangedEvent
                    {
                        UserId = user.Id,
                        KeycloakId = user.KeycloakId,
                        PreferredLocale = locale,
                        ChangedAtUtc = DateTime.UtcNow
                    })
                });

                await _context.SaveChangesAsync();
            }

            var profile = await GetUserProfile(sub);
            return Ok(profile);
        }

        private static readonly string[] AllowedAppRoles = { "Student", "Teacher", "Parent" };

        // Issue #240: realm rol kataloğu (Admin, exam-service dahil) anonim erişime kapalı. Kayıt formu
        // artık bu uca değil sabit uygulama rollerine (Student/Teacher/Parent) dayanıyor; başka çağıran yok.
        [Authorize(Roles = "Admin")]
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            // Hata yolu global handler'a bırakılır (#231 review): exception log'lanır, istemciye
            // Keycloak host/URL/mesaj içermeyen ProblemDetails gider (erişilemezse 503, diğerleri 500).
            _logger.LogDebug("GetRoles: realm rolleri Keycloak'tan okunuyor");
            var roles = await _keycloakService.GetRealmRolesAsync();
            _logger.LogDebug("GetRoles: {Count} rol bulundu", roles?.Count ?? 0);
            return Ok(roles);
        }

        /// <summary>
        /// Login denemesi (başarılı/başarısız) sonucunu outbox'a yazar (issue #84). Response'u
        /// bloklamaz/geciktirmez: aynı DbContext/transaction içinde ekleyip <c>SaveChangesAsync</c>
        /// çağırmak yeterli — asıl RabbitMQ publish'ini ayrı bir process olan
        /// <c>identity-outbox-publisher</c> yapar. Şifre/token gibi hassas veri taşınmaz; sadece
        /// sub/role/zaman/sonuç; başarısız login'de ek olarak doğrulanmamış tanımlayıcı
        /// (<see cref="LoginAttemptedEvent.AttemptedIdentifier"/>, issue #100).
        /// </summary>
        private async Task WriteLoginAttemptedEventAsync(string? keycloakUserId, string role, bool success, string? attemptedIdentifier)
        {
            var outboxId = Guid.NewGuid();
            var @event = new LoginAttemptedEvent
            {
                EventId = outboxId,
                KeycloakUserId = keycloakUserId,
                AttemptedIdentifier = attemptedIdentifier,
                Role = role,
                OccurredAtUtc = DateTime.UtcNow,
                Success = success
            };

            _context.OutboxMessages.Add(new OutboxMessage
            {
                Id = outboxId,
                Type = OutboxEventRegistry.NameFor<LoginAttemptedEvent>(),
                Content = JsonSerializer.Serialize(@event)
            });

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Keycloak token uç noktası hatasını istemciye güvenli bir yanıta eşler (issue #231):
        /// <c>invalid_grant</c> → 401, Keycloak erişilemez → 503; ikisi de yalnızca <c>{ message }</c>
        /// (yerelleştirilmiş, iç ayrıntı yok). Ayrıntı log'a yazılır. Sınıflandırılmamış hata için
        /// <c>null</c> döner — çağıran rethrow eder ve global handler 500 ProblemDetails üretir.
        /// </summary>
        private IActionResult? KeycloakTokenFailureResult(KeycloakException ex, string operation, string invalidGrantMessageKey)
        {
            switch (ex.Kind)
            {
                case KeycloakFailureKind.InvalidGrant:
                    _logger.LogWarning("Keycloak {Operation} rejected: {Reason}", operation, ex.Message);
                    return StatusCode(StatusCodes.Status401Unauthorized,
                        new { message = _localizer[invalidGrantMessageKey].Value });
                case KeycloakFailureKind.ProviderUnavailable:
                    _logger.LogWarning(ex, "Keycloak {Operation} failed: identity provider unavailable", operation);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable,
                        new { message = _localizer["auth.login.providerUnavailable"].Value });
                default:
                    return null;
            }
        }

        /// <summary>
        /// Wraps <see cref="WriteLoginAttemptedEventAsync"/> so a transient outbox-write
        /// failure never breaks the login response: a caller whose Keycloak authentication
        /// already succeeded (or failed) must still get that result, not an unrelated 500
        /// from the audit side-effect.
        /// </summary>
        private async Task TryWriteLoginAttemptedEventAsync(string? keycloakUserId, string role, bool success, string? attemptedIdentifier = null)
        {
            try
            {
                await WriteLoginAttemptedEventAsync(keycloakUserId, role, success, attemptedIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login outbox event yazılamadı (sub={Sub}, success={Success}); login akışı bloklanmadı.", keycloakUserId, success);
            }
        }

        /// <summary>
        /// Başarısız login'de girilen tanımlayıcıyı trim'ler ve <see cref="AttemptedIdentifierMaxLength"/>'e
        /// kırpar. Boş girdi → null.
        /// </summary>
        private static string? NormalizeAttemptedIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            return trimmed.Length <= AttemptedIdentifierMaxLength ? trimmed : trimmed[..AttemptedIdentifierMaxLength];
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
                KeycloakId = sub,
                // Eski satırlarda kolon default'u "tr"; yine de boş/bozuk değeri
                // varsayılana indirgeyerek tüketicilere hep geçerli bir dil kodu veriyoruz.
                PreferredLocale = SupportedLocales.Normalize(user.PreferredLocale)
            };
        }
    }
}
