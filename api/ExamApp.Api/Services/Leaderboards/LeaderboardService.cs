using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Leaderboards;

/// <summary>
/// issue #193. Sorgu ailesi: <c>Students</c> (soft-delete global filter) + <c>StudentPoints</c> alt sorgusu
/// (<c>StudentId</c> UNIQUE → öğrenci başına en fazla bir satır; alt sorgu <c>FirstOrDefault</c>, toplama yok).
/// Okul filtresi (<c>SchoolId = @p</c>) projeksiyon/sıralama/Skip/Take'ten ÖNCE SQL düzeyinde uygulanır;
/// mevcut <c>IX_Students_SchoolId</c> (FK index) ve <c>IX_StudentPoints_StudentId</c> (unique) bu sorguyu karşılar.
/// İstek sahibinin satırı + önündeki öğrenci sayısı TEK sorguda (korelasyonlu COUNT) alınır.
/// İsim/avatar auth-api'den toplu çözülür (N+1 yok); auth-api erişilemezse liste temel alanlarla döner.
/// Skip/Take doğrulaması controller'dadır (tek yer); servis değerleri olduğu gibi kullanır.
/// </summary>
public sealed class LeaderboardService : ILeaderboardService
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<LeaderboardService> _logger;
    private readonly IStringLocalizer<Messages> _localizer;

    public LeaderboardService(
        AppDbContext context,
        IAuthApiClient authApiClient,
        ILogger<LeaderboardService> logger,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<LeaderboardDto> GetLeaderboardAsync(LeaderboardRequest request, CancellationToken ct = default)
    {
        var schoolScopeAvailable = request.CurrentSchoolId.HasValue;

        if (request.Scope == LeaderboardScope.School && !schoolScopeAvailable)
        {
            // Okulsuz öğrenci / okulsuz öğretmen / admin: okul kapsamı uygulanamaz.
            return new LeaderboardDto
            {
                Success = false,
                Message = _localizer["student.leaderboard.schoolScopeUnavailable"],
                Scope = ScopeName(request.Scope),
                SchoolScopeAvailable = false
            };
        }

        var scoped = BuildScopedQuery(request.Scope, request.CurrentSchoolId);

        var totalCount = await scoped.CountAsync(ct);

        var page = await Rank(scoped)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(ct);

        // İstek sahibinin kendi satırı + önündeki öğrenci sayısı tek sorguda. Kapsam içinde arandığından
        // farklı okuldaki öğrenci için null olur. Aynı sıralama kuralı: XP büyük olanlar + eşit XP'de Id küçük olanlar.
        var me = await scoped
            .Where(s => s.UserId == request.RequesterUserId)
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                Xp = s.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0,
                Ahead = scoped.Count(o =>
                    o.Id != s.Id
                    && ((o.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0)
                            > (s.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0)
                        || ((o.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0)
                                == (s.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0)
                            && o.Id < s.Id)))
            })
            .FirstOrDefaultAsync(ct);

        var entries = page.Select((row, i) => new LeaderboardEntryDto
        {
            Rank = request.Skip + i + 1,
            Xp = row.Xp,
            Level = StudentLevel.FromXp(row.Xp),
            IsMe = row.UserId == request.RequesterUserId
        }).ToList();

        await FillNamesAsync(entries, page, ct);

        return new LeaderboardDto
        {
            Success = true,
            Scope = ScopeName(request.Scope),
            SchoolId = request.Scope == LeaderboardScope.School ? request.CurrentSchoolId : null,
            SchoolScopeAvailable = schoolScopeAvailable,
            TotalCount = totalCount,
            Skip = request.Skip,
            Take = request.Take,
            Entries = entries,
            MyRank = me == null ? null : me.Ahead + 1,
            MyXp = me?.Xp
        };
    }

    /// <summary>
    /// Kapsam filtresi: School → <c>SchoolId = @schoolId</c> (null olamaz, çağıran garanti eder); Global → filtre yok.
    /// Test tarafında <c>ToQueryString</c> ile SchoolId filtresinin Skip/Take'ten önce geldiği doğrulanır.
    /// </summary>
    public IQueryable<Student> BuildScopedQuery(LeaderboardScope scope, int? schoolId)
    {
        var query = _context.Students.AsNoTracking();

        if (scope == LeaderboardScope.School)
        {
            var id = schoolId ?? throw new ArgumentNullException(nameof(schoolId), "School scope requires a resolved schoolId.");
            query = query.Where(s => s.SchoolId == id);
        }

        return query;
    }

    /// <summary>
    /// XP: öğrencinin tek StudentPoints satırından (yoksa 0). Profil ekranıyla aynı değer
    /// (StudentService.GetStudentProfile Sum kullanır; UNIQUE index sayesinde sonuç aynıdır).
    /// Level SQL'den okunmaz; issue #243: <see cref="StudentLevel.FromXp"/> ile XP'den bellekte hesaplanır.
    /// </summary>
    public static IQueryable<RankedRow> Rank(IQueryable<Student> students)
    {
        return students
            .Select(s => new RankedRow
            {
                Id = s.Id,
                UserId = s.UserId,
                Xp = s.StudentPoints.Select(sp => (int?)sp.XP).FirstOrDefault() ?? 0
            })
            .OrderByDescending(r => r.Xp)
            .ThenBy(r => r.Id);
    }

    private async Task FillNamesAsync(List<LeaderboardEntryDto> entries, List<RankedRow> rows, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            var lookup = users.ToDictionary(u => u.Id);

            for (var i = 0; i < entries.Count; i++)
            {
                if (lookup.TryGetValue(rows[i].UserId, out var user))
                {
                    entries[i].FullName = user.FullName ?? string.Empty;
                    entries[i].AvatarUrl = user.Avatar ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            // Auth API erişilemezse liste temel bilgilerle döner (lookup ucuyla aynı karar) — ama sessiz değil.
            _logger.LogWarning(ex, "[Leaderboard] auth-api kullanıcı çözümü başarısız; {Count} satır isimsiz döndü.", entries.Count);
        }
    }

    private static string ScopeName(LeaderboardScope scope) =>
        scope == LeaderboardScope.School ? "school" : "global";

    /// <summary>Sorgu projeksiyonu — DTO değil, dışarı dönmez. UserId yalnızca isim çözümü için.</summary>
    public sealed class RankedRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int Xp { get; set; }
    }
}
