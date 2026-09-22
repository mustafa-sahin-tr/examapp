using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Leaderboards;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #193: okul bazlı / global liderlik tablosu.
/// <c>GET /api/leaderboard?scope=global|school&amp;skip=0&amp;take=20</c> (gateway: <c>/api/exam/leaderboard</c>).
/// <para>
/// Okul kimliği client'tan ALINMAZ — <c>schoolId</c> query parametresi bilinçli olarak bağlanmaz ve yok sayılır;
/// okul her zaman <see cref="BaseController.GetSchoolScopeAsync"/> ile sunucu tarafında (DB kazanır, fail-closed;
/// <see cref="BaseController.GetCurrentSchoolIdAsync"/> ile aynı doğrulama, tek profil çözümü) çözülür. Admin/servis hesabı için bu değer null'dır → <c>scope=school</c> 400 döner (admin'in okulu yoktur);
/// global her rol için çalışır.
/// </para>
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class LeaderboardController : BaseController
{
    private readonly ILeaderboardService _leaderboardService;
    private readonly IStringLocalizer<Messages> _localizer;

    public LeaderboardController(
        ILeaderboardService leaderboardService,
        IStringLocalizer<Messages>? localizer = null)
    {
        _leaderboardService = leaderboardService;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <param name="scope">"global" (varsayılan) veya "school". Büyük/küçük harf duyarsız.</param>
    /// <param name="skip">Sayfalama başlangıcı (>= 0).</param>
    /// <param name="take">Sayfa boyutu (1..<see cref="LeaderboardService.MaxTake"/>).</param>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string? scope,
        [FromQuery] int skip = 0,
        [FromQuery] int take = LeaderboardService.DefaultTake,
        CancellationToken ct = default)
    {
        if (!TryParseScope(scope, out var parsedScope))
        {
            return BadRequest(new { message = _localizer["student.leaderboard.invalidScope"].Value });
        }

        if (skip < 0 || take < 1 || take > LeaderboardService.MaxTake)
        {
            return BadRequest(new { message = _localizer["student.leaderboard.invalidPaging", LeaderboardService.MaxTake].Value });
        }

        // Profil TEK seferde çözülür (GetSchoolScopeAsync = GetCurrentSchoolIdAsync ile aynı #189 yolu:
        // DB kazanır, fail-closed). Admin/servis için SchoolId null → school kapsamı uygulanamaz.
        var schoolScope = await GetSchoolScopeAsync(ct);

        var result = await _leaderboardService.GetLeaderboardAsync(
            new LeaderboardRequest(parsedScope, schoolScope.UserId, schoolScope.SchoolId, skip, take), ct);

        if (!result.Success)
        {
            return BadRequest(new { message = result.Message });
        }

        return Ok(result);
    }

    private static bool TryParseScope(string? scope, out LeaderboardScope parsed)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            parsed = LeaderboardScope.Global;
            return true;
        }

        switch (scope.Trim().ToLowerInvariant())
        {
            case "global":
                parsed = LeaderboardScope.Global;
                return true;
            case "school":
                parsed = LeaderboardScope.School;
                return true;
            default:
                parsed = LeaderboardScope.Global;
                return false;
        }
    }
}
