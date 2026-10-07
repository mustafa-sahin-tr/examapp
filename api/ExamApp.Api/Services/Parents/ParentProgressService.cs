using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Leaderboards;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli "Puan ve rozetler" okuma modeli (issue #422, epic #407 V4) — salt okunur. Sıra: <b>kapı → audit → veri</b>
/// (<see cref="IParentChildAccess"/> → <see cref="IParentAccessAuditLog"/> → sorgular).
/// </summary>
public interface IParentProgressService
{
    /// <summary>Çocuğun puan/seviye/rozet/sıra özeti. Erişim yoksa null → 404.</summary>
    Task<ParentChildProgressDto?> GetProgressAsync(int parentUserId, int studentId, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentProgressService"/>
/// <remarks>
/// Tüm veri exam DB'den okunur — servisler arası HTTP yok (architecture.md, #225 deseni):
/// <list type="bullet">
/// <item>Toplam puan: <c>StudentPoints.XP</c> (profil ve V2 özetiyle aynı). Seviye: <see cref="StudentLevel.FromXp"/> (#243).</item>
/// <item>Bu hafta: <c>StudentDailyXps</c> — <c>StudentPointsChangedEvent</c> senkronunun yerel (Europe/Istanbul) gün defteri;
/// hafta yerel Pazartesi–bugün.</item>
/// <item>Rozetler: <c>StudentBadgeProjections</c> — <c>StudentBadgeEarnedEvent</c> projeksiyonu. Geriye dönük doldurma yok:
/// projeksiyondan önce kazanılmış rozetler yeniden kazanılana kadar görünmez.</item>
/// <item>Sıra: <see cref="ILeaderboardService.GetRankForXpAsync"/> (#193 kuralı) — yalnızca çocuğun sırası ve kapsam büyüklüğü.</item>
/// </list>
/// </remarks>
public sealed class ParentProgressService : IParentProgressService
{
    /// <summary>Dönülen en fazla rozet (güvenlik tavanı; katalog bundan çok küçük).</summary>
    internal const int MaxBadges = 100;

    private readonly AppDbContext _context;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly ILeaderboardService _leaderboard;
    private readonly ILocalDayCalendar _calendar;

    public ParentProgressService(
        AppDbContext context,
        IParentChildAccess access,
        IParentAccessAuditLog audit,
        ILeaderboardService leaderboard,
        ILocalDayCalendar? calendar = null)
    {
        _context = context;
        _access = access;
        _audit = audit;
        _leaderboard = leaderboard;
        _calendar = calendar ?? LocalDayCalendar.Default;
    }

    public async Task<ParentChildProgressDto?> GetProgressAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        // 1) Kapı: Active bağlantı değilse hiçbir veri sorgusu yok, audit yok.
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;

        // 2) Audit (veri okunmadan önce).
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildProgress, ct: ct);

        // 3) Veri (AppDbContext thread-safe değil: sorgular art arda).
        var today = _calendar.Today;
        var weekStart = ParentDashboardService.StartOfWeek(today);

        var xp = await _context.StudentPoints.AsNoTracking()
            .Where(sp => sp.StudentId == grant.StudentId)
            .SumAsync(sp => (int?)sp.XP, ct) ?? 0;

        var weekly = await _context.StudentDailyXps.AsNoTracking()
            .Where(d => d.StudentId == grant.StudentId && d.Day >= weekStart && d.Day <= today)
            .SumAsync(d => (int?)d.Xp, ct) ?? 0;

        var badges = await _context.StudentBadgeProjections.AsNoTracking()
            .Where(b => b.StudentId == grant.StudentId)
            .OrderByDescending(b => b.EarnedAtUtc)
            .ThenBy(b => b.Name)
            .Take(MaxBadges)
            .Select(b => new { b.Name, b.Icon, b.EarnedAtUtc })
            .ToListAsync(ct);

        var ranks = new List<ParentChildRankDto>();
        var global = await _leaderboard.GetRankForXpAsync(grant.StudentId, xp, LeaderboardScope.Global, null, ct);
        ranks.Add(new ParentChildRankDto { Scope = ParentRankScopes.Global, Rank = global.Rank, TotalCount = global.TotalCount });
        if (grant.VerifiedSchoolId is { } schoolId)
        {
            var school = await _leaderboard.GetRankForXpAsync(grant.StudentId, xp, LeaderboardScope.School, schoolId, ct);
            ranks.Add(new ParentChildRankDto { Scope = ParentRankScopes.School, Rank = school.Rank, TotalCount = school.TotalCount });
        }

        return new ParentChildProgressDto
        {
            StudentId = grant.StudentId,
            TotalXp = xp,
            Level = StudentLevel.FromXp(xp),
            WeekStart = weekStart,
            // Net puan; negatif gösterilmez.
            WeeklyXp = Math.Max(0, weekly),
            Badges = badges.Select(b => new ParentBadgeDto
            {
                Name = b.Name,
                Icon = b.Icon,
                EarnedOn = ToLocalDay(b.EarnedAtUtc, _calendar.TimeZone)
            }).ToList(),
            Ranks = ranks
        };
    }

    /// <summary>UTC an → YEREL takvim günü (saat bilgisi atılır).</summary>
    internal static DateOnly ToLocalDay(DateTime utc, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone));
}
