using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Leaderboards;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #193: GET /api/leaderboard — LeaderboardService okul/global kapsamı.
/// Puan kaynağı StudentPoints.XP (profil XP formülüyle aynı); sıralama XP azalan, eşitlikte StudentId artan.
/// Seed (UserId → FullName "User {UserId}"): Okul A: A1(u1)=300, A2(u2)=100, A3(u3)=puan yok → 0;
/// Okul B: B1(u4)=500, B2(u5)=200; Okulsuz: N1(u6)=400.
/// Level (issue #243) StudentPoints.Level kolonundan DEĞİL, XP'den hesaplanır: 1 + floor(sqrt(XP/50)).
/// Seed'deki kolon değerleri (99) bilerek formülle çelişir → kolonun okunmadığı kanıtlanır.
/// Global sıra: B1 N1 A1 B2 A2 A3. Okul A: A1 A2 A3. Okul B: B1 B2.
/// DTO PII taşımaz (UserId/StudentId/StudentNumber/SchoolId yok) → satırlar FullName/Xp ile tanınır.
/// </summary>
public class LeaderboardServiceSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private int _schoolA;
    private int _schoolB;

    public void Dispose() => _db.Dispose();

    private LeaderboardService NewService(AppDbContext ctx) =>
        new(ctx, _authApi, Substitute.For<ILogger<LeaderboardService>>());

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();
        _schoolA = a.Id;
        _schoolB = b.Id;

        var a1 = new Student { UserId = 1, StudentNumber = "A1", SchoolId = a.Id };
        var a2 = new Student { UserId = 2, StudentNumber = "A2", SchoolId = a.Id };
        var a3 = new Student { UserId = 3, StudentNumber = "A3", SchoolId = a.Id };
        var b1 = new Student { UserId = 4, StudentNumber = "B1", SchoolId = b.Id };
        var b2 = new Student { UserId = 5, StudentNumber = "B2", SchoolId = b.Id };
        var n1 = new Student { UserId = 6, StudentNumber = "N1", SchoolId = null };
        ctx.Students.AddRange(a1, a2, a3, b1, b2, n1);
        await ctx.SaveChangesAsync();

        ctx.StudentPoints.AddRange(
            new StudentPoint { StudentId = a1.Id, XP = 300 },
            new StudentPoint { StudentId = a2.Id, XP = 100 },
            new StudentPoint { StudentId = b1.Id, XP = 500 },
            new StudentPoint { StudentId = b2.Id, XP = 200 },
            new StudentPoint { StudentId = n1.Id, XP = 400 });
        await ctx.SaveChangesAsync();

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                ((IEnumerable<int>)call[0]).Select(id => new UserLookupResultDto { Id = id, FullName = $"User {id}", Avatar = $"av{id}" }).ToList()));
    }

    private static LeaderboardRequest Req(LeaderboardScope scope, int userId, int? schoolId, int skip = 0, int take = 20)
        => new(scope, userId, schoolId, skip, take);

    private static string[] Names(LeaderboardDto dto) => dto.Entries.Select(e => e.FullName).ToArray();

    // ---- Global ----

    [Fact]
    public async Task Global_ListsAllStudentsOrderedByXpDesc_WithLevel()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 1, schoolId: _schoolA));

        dto.Success.ShouldBeTrue();
        dto.Scope.ShouldBe("global");
        dto.SchoolId.ShouldBeNull();
        dto.TotalCount.ShouldBe(6);
        Names(dto).ShouldBe(new[] { "User 4", "User 6", "User 1", "User 5", "User 2", "User 3" });
        dto.Entries.Select(e => e.Rank).ShouldBe(new[] { 1, 2, 3, 4, 5, 6 });
        dto.Entries.Select(e => e.Xp).ShouldBe(new[] { 500, 400, 300, 200, 100, 0 });
        // 500→1+floor(√10)=4, 400→1+floor(√8)=3, 300→3, 200→3, 100→1+floor(√2)=2, 0 (satır yok)→1
        dto.Entries.Select(e => e.Level).ShouldBe(new[] { 4, 3, 3, 3, 2, 1 });
    }

    [Fact]
    public async Task Global_MyRankIsComputedAcrossAllSchools()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 1, schoolId: _schoolA));

        dto.MyRank.ShouldBe(3); // B1(500), N1(400) önümde
        dto.MyXp.ShouldBe(300);
        dto.Entries.Single(e => e.IsMe).FullName.ShouldBe("User 1");
        dto.SchoolScopeAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task Global_SchoollessRequester_SchoolScopeAvailableFalse_ButListWorks()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 6, schoolId: null));

        dto.Success.ShouldBeTrue();
        dto.SchoolScopeAvailable.ShouldBeFalse();
        dto.TotalCount.ShouldBe(6);
        dto.MyRank.ShouldBe(2);
    }

    [Fact]
    public async Task Global_AdminWithoutStudentRow_MyRankIsNull()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 999, schoolId: null));

        dto.Success.ShouldBeTrue();
        dto.MyRank.ShouldBeNull();
        dto.MyXp.ShouldBeNull();
        dto.Entries.ShouldAllBe(e => !e.IsMe);
    }

    // ---- School ----

    [Fact]
    public async Task School_ContainsOnlySameSchoolStudents_RankedWithinSchool()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA));

        dto.Success.ShouldBeTrue();
        dto.Scope.ShouldBe("school");
        dto.SchoolId.ShouldBe(_schoolA);
        dto.TotalCount.ShouldBe(3);
        Names(dto).ShouldBe(new[] { "User 1", "User 2", "User 3" });
        dto.Entries.Select(e => e.Rank).ShouldBe(new[] { 1, 2, 3 });
        dto.Entries.Select(e => e.Xp).ShouldBe(new[] { 300, 100, 0 });
    }

    [Fact]
    public async Task School_OtherSchoolStudentsAreNotInListAndDoNotAffectRank()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        // A1 global'de 3. (B1 ve N1 önünde) — okul kapsamında 1. olmalı.
        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 1, schoolId: _schoolA));

        dto.MyRank.ShouldBe(1);
        dto.MyXp.ShouldBe(300);
        Names(dto).ShouldNotContain("User 4"); // B1
        Names(dto).ShouldNotContain("User 6"); // N1
        dto.Entries.ShouldAllBe(e => e.Xp <= 300);
    }

    [Fact]
    public async Task School_MyRankIsWithinScope_ForLowerRankedStudent()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        // A2 global'de 5.; okulda A1'in arkasında 2.
        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA));

        dto.MyRank.ShouldBe(2);
        dto.Entries.Single(e => e.IsMe).Rank.ShouldBe(2);
    }

    [Fact]
    public async Task School_SchoolB_HasItsOwnRanking()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 5, schoolId: _schoolB));

        Names(dto).ShouldBe(new[] { "User 4", "User 5" });
        dto.MyRank.ShouldBe(2);
        dto.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task School_SchoollessRequester_ReturnsNotApplicableWithLocalizedMessage()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 6, schoolId: null));

        dto.Success.ShouldBeFalse();
        dto.SchoolScopeAvailable.ShouldBeFalse();
        dto.Message.ShouldNotBeNullOrWhiteSpace();
        // Fallback localizer varsayılan dile (tr) kilitli; anahtar çözülmüşse anahtarın kendisi dönmez.
        dto.Message.ShouldNotBe("student.leaderboard.schoolScopeUnavailable");
        dto.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task School_SchoolBoundTeacherWithoutStudentRow_ListIsFull_MyRankNull()
    {
        // Okullu öğretmen (UserId=77, Student satırı yok): okul kapsamı çalışır, kendi sırası yoktur.
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 77, schoolId: _schoolA));

        dto.Success.ShouldBeTrue();
        dto.SchoolScopeAvailable.ShouldBeTrue();
        dto.TotalCount.ShouldBe(3);
        Names(dto).ShouldBe(new[] { "User 1", "User 2", "User 3" });
        dto.MyRank.ShouldBeNull();
        dto.MyXp.ShouldBeNull();
        dto.Entries.ShouldAllBe(e => !e.IsMe);
    }

    [Fact]
    public async Task School_RequesterOutsideScope_MyRankIsNull()
    {
        // Servis düzeyi savunma: istekçi (UserId=4, Okul B öğrencisi) yanlışlıkla Okul A kapsamıyla çağrılsa bile
        // kendi satırı kapsam içinde aranır → listede yok, MyRank null; A dışı hiçbir satır sızmaz.
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 4, schoolId: _schoolA));

        dto.MyRank.ShouldBeNull();
        Names(dto).ShouldBe(new[] { "User 1", "User 2", "User 3" });
    }

    // ---- Eşitlik / soft delete / isimler ----

    [Fact]
    public async Task TiesAreBrokenByStudentIdAscending_AndMyRankMatchesListRank()
    {
        await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var a2 = await setup.Students.SingleAsync(s => s.StudentNumber == "A2");
            var sp = await setup.StudentPoints.SingleAsync(p => p.StudentId == a2.Id);
            sp.XP = 300; // A1 ile eşit; A1.Id < A2.Id → A1 önce
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA));

        Names(dto).ShouldBe(new[] { "User 1", "User 2", "User 3" });
        dto.MyRank.ShouldBe(2);
        dto.Entries.Single(e => e.IsMe).Rank.ShouldBe(dto.MyRank!.Value);
    }

    [Fact]
    public async Task SoftDeletedStudent_IsExcludedFromListCountAndRank()
    {
        await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var a1 = await setup.Students.SingleAsync(s => s.StudentNumber == "A1");
            setup.Students.Remove(a1); // BaseEntity → soft delete
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA));

        dto.TotalCount.ShouldBe(2);
        Names(dto).ShouldBe(new[] { "User 2", "User 3" });
        dto.MyRank.ShouldBe(1);
    }

    [Fact]
    public async Task SoftDeletedStudentPoints_CountAsZeroXp_AndAffectRank()
    {
        // StudentResetJob puanı soft-delete eder (StudentPoints BaseEntity) → öğrenci 0 XP ile listede kalır.
        await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var a1 = await setup.Students.SingleAsync(s => s.StudentNumber == "A1");
            var sp = await setup.StudentPoints.SingleAsync(p => p.StudentId == a1.Id);
            setup.StudentPoints.Remove(sp); // soft delete
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        (await ctx.StudentPoints.IgnoreQueryFilters().CountAsync(p => p.IsDeleted)).ShouldBe(1);

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 1, schoolId: _schoolA));

        dto.TotalCount.ShouldBe(3);
        Names(dto).ShouldBe(new[] { "User 2", "User 1", "User 3" }); // A2=100, A1=0 (Id küçük), A3=0
        dto.Entries.Select(e => e.Xp).ShouldBe(new[] { 100, 0, 0 });
        dto.Entries.Select(e => e.Level).ShouldBe(new[] { 2, 1, 1 });
        dto.MyRank.ShouldBe(2);
        dto.MyXp.ShouldBe(0);
    }

    [Fact]
    public async Task Names_AreResolvedOnlyForPageUsers_InOneCall()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 1, schoolId: _schoolA));

        dto.Entries.Select(e => e.AvatarUrl).ShouldBe(new[] { "av1", "av2", "av3" });
        // Okul dışı UserId'ler (4,5,6) auth-api'ye bile gitmez (isim sızmaz).
        await _authApi.Received(1).GetUsersByIdsAsync(
            Arg.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2, 3 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthApiFailure_StillReturnsListWithBaseFields_AndLogsWarning()
    {
        await SeedAsync();
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("auth down"));
        var logger = Substitute.For<ILogger<LeaderboardService>>();
        await using var ctx = _db.NewContext();

        var dto = await new LeaderboardService(ctx, _authApi, logger)
            .GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 1, schoolId: _schoolA));

        dto.Success.ShouldBeTrue();
        dto.Entries.Count.ShouldBe(6);
        dto.Entries.Select(e => e.Xp).ShouldBe(new[] { 500, 400, 300, 200, 100, 0 });
        dto.Entries.ShouldAllBe(e => e.FullName == string.Empty && e.AvatarUrl == string.Empty);
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<HttpRequestException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // ---- Sayfalama (doğrulama controller'da; servis değerleri olduğu gibi uygular) ----

    [Fact]
    public async Task Paging_RankContinuesAcrossPages_AndMyRankIndependentOfPage()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var page2 = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 1, schoolId: _schoolA, skip: 2, take: 2));

        Names(page2).ShouldBe(new[] { "User 1", "User 5" });
        page2.Entries.Select(e => e.Rank).ShouldBe(new[] { 3, 4 });
        page2.TotalCount.ShouldBe(6);
        page2.Skip.ShouldBe(2);
        page2.Take.ShouldBe(2);
        page2.MyRank.ShouldBe(3);

        var page3 = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.Global, userId: 1, schoolId: _schoolA, skip: 4, take: 2));
        page3.MyRank.ShouldBe(3); // sayfada değilim ama sıram değişmez
        page3.Entries.ShouldAllBe(e => !e.IsMe);
    }

    [Fact]
    public async Task Paging_SkipBeyondTotal_EmptyEntries_ButMyRankAndTotalStillFilled()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA, skip: 3, take: 20));

        dto.Success.ShouldBeTrue();
        dto.Entries.ShouldBeEmpty();
        dto.TotalCount.ShouldBe(3);
        dto.MyRank.ShouldBe(2);
        dto.MyXp.ShouldBe(100);
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    // ---- ToQueryString kanıtı: SchoolId filtresi Skip/Take'ten önce, SQL düzeyinde ----

    [Fact]
    public void School_QueryFiltersBySchoolIdBeforeOffsetLimit_SingleStudentPointsSubqueryPerColumn()
    {
        using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var sql = LeaderboardService.Rank(service.BuildScopedQuery(LeaderboardScope.School, 42))
            .Skip(10)
            .Take(5)
            .ToQueryString();

        sql.ShouldContain("\"StudentPoints\"");
        // Toplama yok (UNIQUE StudentId → tek satır alt sorgu)
        sql.ShouldNotContain("SUM(");
        sql.ShouldNotContain("LastUpdated");
        sql.ShouldNotContain("\"Level\"", customMessage: "#243: Level kolonu okunmaz");
        // XP için korelasyonlu alt sorgu (Level SQL'de yok, #243); dış sorgunun sayfalaması SON LIMIT/OFFSET'tir.
        var whereIdx = sql.IndexOf("\"SchoolId\" = ", StringComparison.Ordinal);
        var orderIdx = sql.LastIndexOf("ORDER BY", StringComparison.Ordinal);
        var limitIdx = sql.LastIndexOf("LIMIT", StringComparison.Ordinal);
        var offsetIdx = sql.LastIndexOf("OFFSET", StringComparison.Ordinal);
        whereIdx.ShouldBeGreaterThan(0, sql);
        orderIdx.ShouldBeGreaterThan(whereIdx, sql);
        limitIdx.ShouldBeGreaterThan(orderIdx, sql);
        offsetIdx.ShouldBeGreaterThan(orderIdx, sql);
        // SchoolId filtresi yalnızca bir kez ve dış sorguda (alt sorgularda değil).
        sql.Split("\"SchoolId\" = ").Length.ShouldBe(2, sql);
        // Soft-delete filtresi de SQL'de
        sql.ShouldContain("\"IsDeleted\"");
    }

    [Fact]
    public void Global_QueryHasNoSchoolIdFilter()
    {
        using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var sql = LeaderboardService.Rank(service.BuildScopedQuery(LeaderboardScope.Global, null)).ToQueryString();

        sql.ShouldNotContain("\"SchoolId\" = ");
        sql.ShouldNotContain("\"SchoolId\" IS NULL");
    }

    [Fact]
    public async Task MyRank_IsResolvedInSingleQuery_WithScopeFilterInsideCorrelatedCount()
    {
        // Sorgu sayısı kanıtı: me + ahead tek roundtrip. Komut sayısını EF log'undan sayıyoruz.
        await SeedAsync();
        var executed = new List<string>();
        await using var ctx = _db.NewContext();
        ctx.Database.SetCommandTimeout(30);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ctx.Database.GetDbConnection())
            .LogTo(msg => { if (msg.Contains("Executed DbCommand")) executed.Add(msg); }, LogLevel.Information)
            .Options;
        await using var logged = new AppDbContext(options);

        var dto = await new LeaderboardService(logged, _authApi, Substitute.For<ILogger<LeaderboardService>>())
            .GetLeaderboardAsync(Req(LeaderboardScope.School, userId: 2, schoolId: _schoolA));

        dto.MyRank.ShouldBe(2);
        // 1) COUNT, 2) sayfa, 3) me+ahead → toplam 3 DB komutu; N+1 yok.
        executed.Count.ShouldBe(3, string.Join("\n---\n", executed));
        var meQuery = executed[2];
        meQuery.ShouldContain("COUNT(*)");
        // Kapsam filtresi korelasyonlu COUNT'un içinde de var (2 kez: dış WHERE + COUNT alt sorgusu).
        meQuery.Split("\"SchoolId\" = ").Length.ShouldBe(3, meQuery);
    }

    [Fact]
    public void School_WithoutResolvedSchoolId_Throws_FailClosed()
    {
        using var ctx = _db.NewContext();

        Should.Throw<ArgumentNullException>(() => NewService(ctx).BuildScopedQuery(LeaderboardScope.School, null));
    }
}
