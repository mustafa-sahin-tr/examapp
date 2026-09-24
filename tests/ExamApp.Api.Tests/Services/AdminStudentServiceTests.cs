using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #153: admin öğrenci listesi — SQL'de filtre + sayfalama (Id artan), sayfadaki kullanıcılar için tek
/// auth-api lookup (hesap durumu dahil), auth-api erişilemezken fail-soft (liste döner, IsEnabled null).
/// Gerçek <see cref="AdminUserDirectory"/> + sahte <see cref="IAuthApiClient"/> ile (#152 AdminTeacherServiceTests karşılığı).
/// </summary>
public class AdminStudentServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public AdminStudentServiceTests()
    {
        // Varsayılan: istenen her id için "Ad {id}" / "u{id}@test.local", çift id'ler aktif, tekler devre dışı.
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<UserLookupResultDto>)ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto
                {
                    Id = id, FullName = $"Ad {id}", Email = $"u{id}@test.local", Enabled = id % 2 == 0
                }).ToList());
    }

    private AdminStudentService NewService(AppDbContext ctx) => new(ctx, new AdminUserDirectory(_authApi));

    // DTO UserId taşımaz; SeedAsync öğrenci numarasını "N{userId}" verir.
    // issue #262: numara listede kısmi (****NNNN); seed numarası 2026 + 4 haneli UserId → son 4 hane UserId'dir.
    private static int UserIdOf(ExamApp.Api.Models.Dtos.Admin.AdminStudentListItemDto item) => int.Parse(item.StudentNumber[^4..]);

    private async Task<(int SchoolA, int SchoolB)> SeedAsync(int countA, int countB, int countUnassigned)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Ankara Lisesi" };
        var b = new School { Name = "Bursa Lisesi" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();

        var userId = 100;
        for (var i = 0; i < countA; i++, userId++)
            ctx.Students.Add(new Student { UserId = userId, StudentNumber = $"2026{userId:D4}", SchoolId = a.Id });
        for (var i = 0; i < countB; i++, userId++)
            ctx.Students.Add(new Student { UserId = userId, StudentNumber = $"2026{userId:D4}", SchoolId = b.Id });
        for (var i = 0; i < countUnassigned; i++, userId++)
            ctx.Students.Add(new Student { UserId = userId, StudentNumber = $"2026{userId:D4}" });
        await ctx.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task Paginates_25_students_as_20_plus_5_ordered_by_id_with_total_count()
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

        await _authApi.Received(1).GetUsersWithAccountStatusByIdsAsync(
            Arg.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(page2.Items.Select(UserIdOf).OrderBy(i => i))),
            Arg.Any<CancellationToken>());
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task School_filter_returns_only_that_schools_students()
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
    public async Task Unassigned_filter_returns_only_students_without_a_school()
    {
        await SeedAsync(countA: 3, countB: 2, countUnassigned: 2);
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(page: 1, pageSize: 20, schoolId: null, unassigned: true);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(i => i.SchoolId == null);
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
    public async Task Fills_all_columns_from_db_and_lookup_including_grade_name()
    {
        int schoolId, gradeId;
        await using (var seed = _db.NewContext())
        {
            var school = new School { Name = "Konya Lisesi" };
            var grade = new Grade { Name = "9. Sınıf" };
            seed.Schools.Add(school);
            seed.Grades.Add(grade);
            await seed.SaveChangesAsync();
            schoolId = school.Id;
            gradeId = grade.Id;
            seed.Students.Add(new Student { UserId = 42, StudentNumber = "20241234", SchoolId = school.Id, GradeId = grade.Id });
            seed.Students.Add(new Student { UserId = 43, StudentNumber = "20245678" });                               // okulsuz, sınıfsız
            seed.Students.Add(new Student { UserId = 45, StudentNumber = "20249999", SchoolName = "Eski Okul Adı" }); // legacy: SchoolId yok
            await seed.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var items = (await NewService(ctx).ListAsync(1, 20, null, false)).Items;

        var bound = items.Single(i => i.StudentNumber == "****1234");
        bound.FullName.ShouldBe("Ad 42");
        bound.Email.ShouldBe("u***@test.local"); // issue #246: listede maskeli
        bound.StudentNumber.ShouldBe("****1234"); // issue #262: yalnızca son 4
        bound.SchoolId.ShouldBe(schoolId);
        bound.SchoolName.ShouldBe("Konya Lisesi");
        bound.GradeId.ShouldBe(gradeId);
        bound.GradeName.ShouldBe("9. Sınıf");
        bound.IsEnabled.ShouldBe(true);

        var bare = items.Single(i => i.StudentNumber == "****5678");
        bare.StudentNumber.ShouldBe("****5678");
        bare.SchoolId.ShouldBeNull();
        bare.SchoolName.ShouldBeNull();
        bare.GradeId.ShouldBeNull();
        bare.GradeName.ShouldBeNull();
        bare.IsEnabled.ShouldBe(false);

        var legacy = items.Single(i => i.StudentNumber == "****9999");
        legacy.SchoolId.ShouldBeNull();
        legacy.SchoolName.ShouldBe("Eski Okul Adı");
    }

    [Fact]
    public async Task User_missing_in_lookup_or_with_unknown_status_gets_empty_name_and_null_enabled()
    {
        _authApi.GetUsersWithAccountStatusByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = 101, FullName = "Bilinen", Email = "b@test.local", Enabled = null } });
        await SeedAsync(countA: 2, countB: 0, countUnassigned: 0); // UserId 100, 101
        await using var ctx = _db.NewContext();

        var items = (await NewService(ctx).ListAsync(1, 20, null, false)).Items;

        var missing = items.Single(i => i.StudentNumber == "****0100");
        missing.FullName.ShouldBe(string.Empty);
        missing.Email.ShouldBe(string.Empty);
        missing.IsEnabled.ShouldBeNull();
        var unknownStatus = items.Single(i => i.StudentNumber == "****0101");
        unknownStatus.FullName.ShouldBe("Bilinen");
        unknownStatus.IsEnabled.ShouldBeNull();
    }

    // Fail-soft'un istisna türleri (HttpRequest/Json/Polly ExecutionRejected/TaskCanceled, kullanıcı iptali hariç)
    // AdminUserDirectory'ye aittir ve AdminTeacherServiceTests'te kapsanır; burada yalnızca öğrenci listesinin
    // bu durumda da döndüğü doğrulanır.
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
        result.Items.ShouldAllBe(i => i.SchoolName == "Ankara Lisesi" && i.StudentNumber != string.Empty);
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
    public async Task Soft_deleted_students_are_excluded()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Students.Add(new Student { UserId = 1, StudentNumber = "20240001" });
            seed.Students.Add(new Student { UserId = 2, StudentNumber = "20240002", IsDeleted = true });
            await seed.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ListAsync(1, 20, null, false);

        result.TotalCount.ShouldBe(1);
        result.Items.Single().StudentNumber.ShouldBe("****0001");
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
