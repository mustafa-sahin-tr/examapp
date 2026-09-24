using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #152: admin öğretmen listesi — SQL'de filtre + sayfalama (Id artan), sayfadaki kullanıcılar için tek
/// auth-api lookup (hesap durumu dahil), auth-api erişilemezken fail-soft (liste döner, IsEnabled null).
/// Gerçek <see cref="AdminUserDirectory"/> + sahte <see cref="IAuthApiClient"/> ile.
/// </summary>
public class AdminTeacherServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public AdminTeacherServiceTests()
    {
        // Varsayılan: istenen her id için "Ad {id}" / "u{id}@test.local", çift id'ler aktif, tekler devre dışı.
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<UserLookupResultDto>)ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto
                {
                    Id = id, FullName = $"Ad {id}", Email = $"u{id}@test.local", Enabled = id % 2 == 0
                }).ToList());
    }

    private AdminTeacherService NewService(AppDbContext ctx) => new(ctx, new AdminUserDirectory(_authApi));

    private async Task<(int SchoolA, int SchoolB)> SeedAsync(int countA, int countB, int countUnassigned)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Ankara Lisesi" };
        var b = new School { Name = "Bursa Lisesi" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();

        var userId = 100;
        for (var i = 0; i < countA; i++)
            ctx.Teachers.Add(new Teacher { UserId = userId++, SchoolId = a.Id });
        for (var i = 0; i < countB; i++)
            ctx.Teachers.Add(new Teacher { UserId = userId++, SchoolId = b.Id });
        for (var i = 0; i < countUnassigned; i++)
            ctx.Teachers.Add(new Teacher { UserId = userId++, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending });
        await ctx.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Paginates_25_teachers_as_20_plus_5_ordered_by_id_with_total_count()
    {
        await SeedAsync(countA: 25, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var page1 = await service.ListAsync(page: 1, pageSize: 20, schoolId: null, unassigned: false);
        var page2 = await service.ListAsync(page: 2, pageSize: 20, schoolId: null, unassigned: false);

        page1.TotalCount.ShouldBe(25);
        page1.PageNumber.ShouldBe(1);
        page1.PageSize.ShouldBe(20);
        page1.Items.Count.ShouldBe(20);
        page2.TotalCount.ShouldBe(25);
        page2.PageNumber.ShouldBe(2);
        page2.Items.Count.ShouldBe(5);

        var ids = page1.Items.Concat(page2.Items).Select(i => i.Id).ToList();
        ids.ShouldBeUnique();
        ids.ShouldBe(ids.OrderBy(i => i).ToList());
    }

    [Fact]
    public async Task Looks_up_only_the_current_pages_users_in_a_single_call_with_account_status()
    {
        await SeedAsync(countA: 25, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var page2 = await NewService(ctx).ListAsync(page: 2, pageSize: 20, schoolId: null, unassigned: false);
        // issue #262: UserId DTO'da yok; varsayılan lookup adı "Ad {userId}" üzerinden geri çözülür.
        var pageUserIds = page2.Items.Select(i => int.Parse(i.FullName.Substring("Ad ".Length))).OrderBy(i => i).ToList();

        await _authApi.Received(1).GetUsersWithAccountStatusByIdsAsync(
            Arg.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(pageUserIds)),
            Arg.Any<CancellationToken>());
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task School_filter_returns_only_that_schools_teachers()
    {
        var (schoolA, schoolB) = await SeedAsync(countA: 3, countB: 2, countUnassigned: 1);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page: 1, pageSize: 20, schoolId: schoolB, unassigned: false);

        result.TotalCount.ShouldBe(2);
        result.Items.Count.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.SchoolId == schoolB && i.SchoolName == "Bursa Lisesi");
        schoolA.ShouldNotBe(schoolB);
    }

    [Fact]
    public async Task Unassigned_filter_returns_only_teachers_without_a_school()
    {
        await SeedAsync(countA: 3, countB: 2, countUnassigned: 2);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page: 1, pageSize: 20, schoolId: null, unassigned: true);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.SchoolId == null && i.IsIndependentTutor);
    }

    [Fact]
    public async Task Unknown_school_returns_empty_page_without_calling_auth_api()
    {
        await SeedAsync(countA: 3, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page: 1, pageSize: 20, schoolId: 9999, unassigned: false);

        result.TotalCount.ShouldBe(0);
        result.Items.ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fills_all_columns_from_db_and_lookup()
    {
        int schoolId;
        await using (var seed = _db.NewContext())
        {
            var school = new School { Name = "Konya Lisesi" };
            seed.Schools.Add(school);
            await seed.SaveChangesAsync();
            schoolId = school.Id;
            seed.Teachers.Add(new Teacher { UserId = 42, SchoolId = school.Id, ApprovalStatus = TeacherApprovalStatus.Approved });
            seed.Teachers.Add(new Teacher { UserId = 43, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected });
            seed.Teachers.Add(new Teacher { UserId = 45, SchoolName = "Eski Okul Adı" }); // legacy: SchoolId yok
            await seed.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var items = (await NewService(ctx).ListAsync(1, 20, null, false)).Items;

        var bound = items.Single(i => i.FullName == "Ad 42");
        bound.FullName.ShouldBe("Ad 42");
        bound.Email.ShouldBe("u***@test.local"); // issue #246: listede maskeli
        bound.SchoolId.ShouldBe(schoolId);
        bound.SchoolName.ShouldBe("Konya Lisesi");
        bound.IsIndependentTutor.ShouldBeFalse();
        bound.ApprovalStatus.ShouldBe("Approved");
        bound.IsEnabled.ShouldBe(true);

        var independent = items.Single(i => i.FullName == "Ad 43");
        independent.SchoolId.ShouldBeNull();
        independent.SchoolName.ShouldBeNull();
        independent.IsIndependentTutor.ShouldBeTrue();
        independent.ApprovalStatus.ShouldBe("Rejected");
        independent.IsEnabled.ShouldBe(false);

        items.Single(i => i.FullName == "Ad 45").SchoolName.ShouldBe("Eski Okul Adı");
    }

    [Fact]
    public async Task User_missing_in_lookup_or_with_unknown_status_gets_empty_name_and_null_enabled()
    {
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = 101, FullName = "Bilinen", Email = "b@test.local", Enabled = null } });
        await SeedAsync(countA: 2, countB: 0, countUnassigned: 0); // UserId 100, 101
        await using var ctx = _db.NewContext();

        var items = (await NewService(ctx).ListAsync(1, 20, null, false)).Items;

        var missing = items.Single(i => i.FullName == string.Empty);
        missing.FullName.ShouldBe(string.Empty);
        missing.Email.ShouldBe(string.Empty);
        missing.IsEnabled.ShouldBeNull();
        var unknownStatus = items.Single(i => i.FullName == "Bilinen");
        unknownStatus.FullName.ShouldBe("Bilinen");
        unknownStatus.IsEnabled.ShouldBeNull();
    }

    [Fact]
    public async Task Auth_api_unreachable_still_returns_the_list_with_null_account_status()
    {
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("auth-api down"));
        await SeedAsync(countA: 3, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(1, 20, null, false);

        result.TotalCount.ShouldBe(3);
        result.Items.Count.ShouldBe(3);
        result.Items.ShouldAllBe(i => i.IsEnabled == null && i.FullName == string.Empty && i.Email == string.Empty);
        result.Items.ShouldAllBe(i => i.SchoolName == "Ankara Lisesi");
    }

    private static Exception Rejection(string kind) => kind == "timeout"
        ? new Polly.Timeout.TimeoutRejectedException("attempt timeout")
        : new Polly.CircuitBreaker.BrokenCircuitException("circuit open");

    [Theory]
    [InlineData("timeout")]
    [InlineData("circuit")]
    public async Task Resilience_pipeline_rejection_is_fail_soft(string kind)
    {
        var rejection = Rejection(kind);
        // ServiceDefaults standart resilience handler'ı zaman aşımı/devre kesicide HttpRequestException değil
        // Polly.ExecutionRejectedException fırlatır; liste yine dönmeli (issue #152 review).
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw rejection);
        await SeedAsync(countA: 2, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(1, 20, null, false);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.IsEnabled == null && i.FullName == string.Empty);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(107374183)] // (page-1)*20 int'te taşar
    [InlineData(3)]         // 25 kayıt, 20'lik sayfa → 3. sayfa boş
    public async Task Page_beyond_the_end_returns_empty_page_without_overflow_or_lookup(int page)
    {
        await SeedAsync(countA: 25, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page, 20, null, false);

        result.PageNumber.ShouldBe(page);
        result.TotalCount.ShouldBe(25);
        result.Items.ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auth_api_timeout_is_fail_soft_but_caller_cancellation_propagates()
    {
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new TaskCanceledException("timeout"));
        await SeedAsync(countA: 1, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(1, 20, null, false);
        result.Items.Single().IsEnabled.ShouldBeNull();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Kullanıcı iptali (istek düştü) yutulmaz — yalnızca HttpClient zaman aşımı fail-soft.
        await Should.ThrowAsync<TaskCanceledException>(() =>
            new AdminUserDirectory(_authApi).ResolveWithAccountStatusAsync([1], cts.Token));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 500, 1, 100)]
    [InlineData(2, 100, 2, 100)]
    public async Task Page_and_page_size_are_clamped(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        await SeedAsync(countA: 2, countB: 0, countUnassigned: 0);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page, pageSize, null, false);

        result.PageNumber.ShouldBe(expectedPage);
        result.PageSize.ShouldBe(expectedPageSize);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Soft_deleted_teachers_are_excluded()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Teachers.Add(new Teacher { UserId = 1 });
            seed.Teachers.Add(new Teacher { UserId = 2, IsDeleted = true });
            await seed.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(1, 20, null, false);

        result.TotalCount.ShouldBe(1);
        result.Items.Single().FullName.ShouldBe("Ad 1"); // issue #262: UserId artık DTO'da yok
    }

    [Fact]
    public async Task Issue246_list_never_returns_a_full_email_address()
    {
        await SeedAsync(countA: 3, countB: 0, countUnassigned: 0);
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>
            {
                new() { Id = 100, FullName = "A", Email = "ali.veli@okul.k12.tr", Enabled = true },
                new() { Id = 101, FullName = "B", Email = "x@okul.k12.tr", Enabled = true },   // tek karakterlik yerel kısım
                new() { Id = 102, FullName = "C", Email = "bozuk-adres", Enabled = true },    // @ yok
            });
        await using var ctx = _db.NewContext();

        var items = (await NewService(ctx).ListAsync(1, 20, null, false)).Items.OrderBy(i => i.Id).ToList();

        items.Select(i => i.Email).ShouldBe(["a***@okul.k12.tr", "***@okul.k12.tr", "***"]);
    }

    public void Dispose() => _db.Dispose();
}
