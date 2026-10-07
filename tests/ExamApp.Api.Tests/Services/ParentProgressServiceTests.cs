using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Leaderboards;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #422 (epic #407 V4): veli "Puan ve rozetler". Kapı → audit → veri sırası (kapıdan geçmeyen istek hiçbir veri sorgusu
/// yapmaz); IDOR / Active olmayan bağlantı → null (404); seviye öğrenci profiliyle aynı formül; haftalık puan exam DB'deki
/// günlük defterden (yerel hafta); rozetler projeksiyondan, tarih yerel GÜNE kesilir; sıralamada yalnız çocuğun sırası
/// (başka öğrencinin adı/puanı yok, eşitlikte küçük id önde); DTO alan listesi kilitli. Servisler arası HTTP yok.
/// </summary>
public class ParentProgressServiceTests : IDisposable
{
    private const int ParentUser = 45001;
    private const int OtherParentUser = 45002;
    private const int PendingParentUser = 45003;
    private const int StudentUser = 45101;
    private const int RivalUser = 45102;
    private const int ClassmateUser = 45103;
    private const int UnverifiedUser = 45104;
    private const int OtherSchoolUser = 45105;

    // Çarşamba 2026-10-07 12:00 Istanbul → hafta Pazartesi 2026-10-05.
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private int _studentId, _rivalId, _parentId, _schoolId;

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var otherSchool = new School { Name = "Cumhuriyet Ortaokulu" };
        ctx.AddRange(school, otherSchool);
        await ctx.SaveChangesAsync();

        var verified = Now.AddDays(-30);
        var student = new Student { UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = verified };
        var rival = new Student { UserId = RivalUser, StudentNumber = "s2", SchoolId = otherSchool.Id, SchoolVerifiedAt = verified };
        var classmate = new Student { UserId = ClassmateUser, StudentNumber = "s3", SchoolId = school.Id, SchoolVerifiedAt = verified };
        // Okulu doğrulanmamış öğrenci okul sıralamasına girmez (#361).
        var unverified = new Student { UserId = UnverifiedUser, StudentNumber = "s4", SchoolId = school.Id };
        var otherSchoolStudent = new Student { UserId = OtherSchoolUser, StudentNumber = "s5", SchoolId = otherSchool.Id, SchoolVerifiedAt = verified };
        var parent = new Parent { UserId = ParentUser };
        var otherParent = new Parent { UserId = OtherParentUser };
        var pendingParent = new Parent { UserId = PendingParentUser };
        ctx.AddRange(student, rival, classmate, unverified, otherSchoolStudent, parent, otherParent, pendingParent);
        await ctx.SaveChangesAsync();

        ctx.StudentPoints.AddRange(
            new StudentPoint { StudentId = student.Id, XP = 2500 },
            new StudentPoint { StudentId = rival.Id, XP = 98765 },
            new StudentPoint { StudentId = classmate.Id, XP = 3333 },
            new StudentPoint { StudentId = unverified.Id, XP = 7777 },
            new StudentPoint { StudentId = otherSchoolStudent.Id, XP = 11 });

        // Günlük defter: Pazar (önceki hafta) hariç, Pazartesi + bugün dahil, yarın (ileri tarihli) hariç; başka öğrenci hariç.
        ctx.StudentDailyXps.AddRange(
            new StudentDailyXp { StudentId = student.Id, Day = new DateOnly(2026, 10, 4), Xp = 500 },
            new StudentDailyXp { StudentId = student.Id, Day = new DateOnly(2026, 10, 5), Xp = 100 },
            new StudentDailyXp { StudentId = student.Id, Day = new DateOnly(2026, 10, 7), Xp = 40 },
            new StudentDailyXp { StudentId = student.Id, Day = new DateOnly(2026, 10, 8), Xp = 9000 },
            new StudentDailyXp { StudentId = rival.Id, Day = new DateOnly(2026, 10, 6), Xp = 4444 });

        ctx.StudentBadgeProjections.AddRange(
            Badge(student.Id, "İlk Adım", "rocket_launch", new DateTime(2026, 9, 1, 8, 15, 0, DateTimeKind.Utc)),
            // 22:30Z = Istanbul 01:30 ertesi gün → kazanıldığı YEREL gün 6 Ekim.
            Badge(student.Id, "Seri Ustası", null, new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc)),
            Badge(rival.Id, "Rakip Rozeti", "star", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)));

        ctx.ParentStudentLinks.AddRange(
            new ParentStudentLink
            {
                ParentId = parent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            new ParentStudentLink
            {
                ParentId = otherParent.Id, StudentId = rival.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            new ParentStudentLink
            {
                ParentId = pendingParent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Pending, CreatedAt = Now.AddHours(-2)
            });
        await ctx.SaveChangesAsync();

        (_studentId, _rivalId, _parentId, _schoolId) = (student.Id, rival.Id, parent.Id, school.Id);
    }

    private static StudentBadgeProjection Badge(int studentId, string name, string? icon, DateTime earnedAt) => new()
    {
        StudentId = studentId, BadgeDefinitionId = Guid.NewGuid(), Name = name, Icon = icon, EarnedAtUtc = earnedAt, ReceivedAtUtc = earnedAt
    };

    private ParentProgressService Service(AppDbContext ctx, IParentChildAccess? access = null, IParentAccessAuditLog? audit = null)
        => new(ctx, access ?? new ParentChildAccess(ctx), audit ?? new ParentAccessAuditLog(ctx, _time),
            new LeaderboardService(ctx, _authApi, NullLogger<LeaderboardService>.Instance),
            new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _time));

    private async Task<ParentChildProgressDto?> ProgressAsync(int parentUser = ParentUser)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).GetProgressAsync(parentUser, _studentId);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildProgress);
    }

    [Fact]
    public async Task Returns_level_weekly_points_badges_and_own_ranks()
    {
        await SeedAsync();

        var dto = (await ProgressAsync()).ShouldNotBeNull();

        dto.StudentId.ShouldBe(_studentId);
        dto.TotalXp.ShouldBe(2500);
        dto.Level.ShouldBe(StudentLevel.FromXp(2500)); // profil formülü (#243)
        dto.Level.ShouldBe(8);
        dto.WeekStart.ShouldBe(new DateOnly(2026, 10, 5));
        dto.WeeklyXp.ShouldBe(140); // Pazartesi 100 + bugün 40 (Pazar ve yarın hariç)

        // En yeni önce; tarih yerel GÜNE kesilir; başka öğrencinin rozeti yok.
        dto.Badges.Select(b => (b.Name, b.Icon, b.EarnedOn)).ShouldBe(new[]
        {
            ("Seri Ustası", (string?)null, new DateOnly(2026, 10, 6)),
            ("İlk Adım", "rocket_launch", new DateOnly(2026, 9, 1))
        });

        // Global: rival(98765), unverified(7777), classmate(3333) önde → 4/5. Okul: yalnız doğrulanmış üyeler → classmate önde → 2/2.
        dto.Ranks.Select(r => (r.Scope, r.Rank, r.TotalCount)).ShouldBe(new[]
        {
            (ParentRankScopes.Global, 4, 5),
            (ParentRankScopes.School, 2, 2)
        });
    }

    [Fact]
    public async Task Rank_matches_the_leaderboard_endpoint_for_the_same_student()
    {
        await SeedAsync();
        var dto = (await ProgressAsync()).ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        var leaderboard = new LeaderboardService(ctx, _authApi, NullLogger<LeaderboardService>.Instance);
        var global = await leaderboard.GetLeaderboardAsync(new LeaderboardRequest(LeaderboardScope.Global, StudentUser, _schoolId, 0, 1));
        var school = await leaderboard.GetLeaderboardAsync(new LeaderboardRequest(LeaderboardScope.School, StudentUser, _schoolId, 0, 1));

        dto.Ranks.Single(r => r.Scope == ParentRankScopes.Global).Rank.ShouldBe(global.MyRank!.Value);
        dto.Ranks.Single(r => r.Scope == ParentRankScopes.Global).TotalCount.ShouldBe(global.TotalCount);
        dto.Ranks.Single(r => r.Scope == ParentRankScopes.School).Rank.ShouldBe(school.MyRank!.Value);
    }

    [Fact]
    public async Task Ties_rank_the_lower_student_id_first_like_the_leaderboard()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Rakip, sınıf arkadaşı ve doğrulanmamış öğrenci (hepsi çocuktan sonra eklendi → daha büyük id) çocukla aynı XP'ye iner.
            await ctx.StudentPoints.Where(p => p.StudentId == _rivalId || p.Student.UserId == ClassmateUser || p.Student.UserId == UnverifiedUser)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.XP, 2500));
        }

        await using var check = _db.NewContext();
        var leaderboard = new LeaderboardService(check, _authApi, NullLogger<LeaderboardService>.Instance);
        // Eşit XP (2500): çocuk (en küçük id) önde; ondan büyük id'li eşitler geride.
        (await leaderboard.GetRankForXpAsync(_studentId, 2500, LeaderboardScope.Global, null)).ShouldBe(new LeaderboardRank(1, 5));
        // Rakip (çocuktan büyük id) eşitlikte çocuğun arkasında.
        (await leaderboard.GetRankForXpAsync(_rivalId, 2500, LeaderboardScope.Global, null)).Rank.ShouldBe(2);

        var dto = (await ProgressAsync()).ShouldNotBeNull();
        var global = await leaderboard.GetLeaderboardAsync(new LeaderboardRequest(LeaderboardScope.Global, StudentUser, _schoolId, 0, 1));
        dto.Ranks.Single(r => r.Scope == ParentRankScopes.Global).Rank.ShouldBe(global.MyRank!.Value);
    }

    [Fact]
    public async Task Child_without_verified_school_gets_only_the_global_rank()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Students.Where(s => s.Id == _studentId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.SchoolVerifiedAt, (DateTime?)null));
        }

        var dto = (await ProgressAsync()).ShouldNotBeNull();
        dto.Ranks.Select(r => r.Scope).ShouldBe(new[] { ParentRankScopes.Global });
    }

    [Fact]
    public async Task Child_without_points_badges_or_ledger_gets_zeros_and_empty_list()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.StudentPoints.Where(p => p.StudentId == _studentId).ExecuteDeleteAsync();
            await ctx.StudentDailyXps.Where(p => p.StudentId == _studentId).ExecuteDeleteAsync();
            await ctx.StudentBadgeProjections.Where(p => p.StudentId == _studentId).ExecuteDeleteAsync();
        }

        var dto = (await ProgressAsync()).ShouldNotBeNull();
        (dto.TotalXp, dto.Level, dto.WeeklyXp).ShouldBe((0, 1, 0));
        dto.Badges.ShouldBeEmpty();
        dto.Ranks.Single(r => r.Scope == ParentRankScopes.Global).Rank.ShouldBe(5); // 0 XP → sonuncu
    }

    [Fact]
    public async Task Leaderboard_part_carries_no_other_students_data()
    {
        await SeedAsync();
        var json = JsonSerializer.Serialize(await ProgressAsync());

        // Başka öğrencilerin puanı, rozeti, kullanıcı id'si ya da ad/avatar alanı yok.
        foreach (var secret in new[] { "98765", "3333", "7777", "4444", "Rakip", RivalUser.ToString(), ClassmateUser.ToString(), "FullName", "Avatar", "UserId", "Entries" })
            json.ShouldNotContain(secret, Case.Sensitive);
        // auth-api (isim çözümü) hiç çağrılmaz.
        _authApi.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Other_parents_child_and_pending_link_get_null_without_audit()
    {
        await SeedAsync();

        (await ProgressAsync(OtherParentUser)).ShouldBeNull();
        (await ProgressAsync(PendingParentUser)).ShouldBeNull();
        await using (var ctx = _db.NewContext())
            (await Service(ctx).GetProgressAsync(ParentUser, 999_999)).ShouldBeNull();

        (await AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(ParentStudentLinkStatus.Pending)]
    [InlineData(ParentStudentLinkStatus.Revoked)]
    public async Task Non_active_link_is_null(ParentStudentLinkStatus status)
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var link = await ctx.ParentStudentLinks.SingleAsync(l => l.ParentId == _parentId);
            link.Status = status;
            if (status == ParentStudentLinkStatus.Revoked)
                link.RevokedAt = Now.AddHours(-1);
            await ctx.SaveChangesAsync();
        }

        (await ProgressAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Gate_failure_runs_no_data_query_and_no_audit()
    {
        await SeedAsync();
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ParentChildAccessGrant?)null);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new ParentQueryCounter();

        await using var ctx = _db.NewContext(counter);
        (await Service(ctx, access, audit).GetProgressAsync(ParentUser, _studentId)).ShouldBeNull();

        counter.Count.ShouldBe(0);
        await audit.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Audit_is_written_before_any_data_query()
    {
        await SeedAsync();
        var grant = new ParentChildAccessGrant(1, _parentId, _studentId, StudentUser, null, _schoolId);
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(ParentUser, _studentId, Arg.Any<CancellationToken>()).Returns(grant);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new ParentQueryCounter { BeforeEach = () => audit.ReceivedCalls().Count() };

        await using var ctx = _db.NewContext(counter);
        (await Service(ctx, access, audit).GetProgressAsync(ParentUser, _studentId)).ShouldNotBeNull();

        counter.Count.ShouldBeGreaterThan(0);
        counter.AuditCallsSeen.ShouldAllBe(n => n == 1);
        await audit.Received(1).RecordAsync(_parentId, _studentId, ParentAccessEndpoints.ChildProgress, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Successful_reads_are_audited_once_per_bucket()
    {
        await SeedAsync();
        await ProgressAsync();
        await ProgressAsync();

        await using var ctx = _db.NewContext();
        var row = await ctx.ParentAccessAudits.SingleAsync();
        (row.Endpoint, row.ParentId, row.StudentId, row.ResourceId).ShouldBe((ParentAccessEndpoints.ChildProgress, _parentId, _studentId, (int?)null));
    }

    [Fact]
    public void Badge_dates_are_truncated_to_the_local_day()
    {
        var tz = new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId).TimeZone;
        ParentProgressService.ToLocalDay(new DateTime(2026, 10, 6, 20, 59, 59, DateTimeKind.Utc), tz).ShouldBe(new DateOnly(2026, 10, 6)); // 23:59 yerel
        ParentProgressService.ToLocalDay(new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc), tz).ShouldBe(new DateOnly(2026, 10, 7));    // 00:00 yerel
    }

    [Fact]
    public void Dto_field_lists_are_fixed()
    {
        static string[] Props<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Props<ParentChildProgressDto>().ShouldBe(new[] { "Badges", "Level", "Ranks", "StudentId", "TotalXp", "WeekStart", "WeeklyXp" });
        Props<ParentBadgeDto>().ShouldBe(new[] { "EarnedOn", "Icon", "Name" });
        Props<ParentChildRankDto>().ShouldBe(new[] { "Rank", "Scope", "TotalCount" });
    }
}

/// <summary>Çalıştırılan SQL komutlarını sayar; her komuttan önce <see cref="BeforeEach"/>'in değerini kaydeder (#422 testleri).</summary>
internal sealed class ParentQueryCounter : DbCommandInterceptor
{
    public int Count { get; private set; }
    public Func<int>? BeforeEach { get; init; }
    public List<int> AuditCallsSeen { get; } = new();

    private void Hit()
    {
        Count++;
        if (BeforeEach != null)
            AuditCallsSeen.Add(BeforeEach());
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Hit();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Hit();
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Hit();
        return ValueTask.FromResult(result);
    }
}
