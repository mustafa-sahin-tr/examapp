using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: sayfa türü (<see cref="WorksheetKind"/>) yazma kuralları — <c>WorksheetAuthoringService.CreateOrUpdateAsync</c>
/// (null = oluşturmada Test / güncellemede korunur; Activity → süre 0, çalışma testi değil), tür kilidi (oturumu olan — silinmiş
/// dahil — sayfada 409 <c>WorksheetKindLocked</c>, hiçbir şey yazılmaz) ve <c>CopyWorksheetAsync</c> türü taşıması.
/// </summary>
public class WorksheetKindAuthoringTests : IDisposable
{
    private const int Owner = 4101;
    private const int StudentUserId = 4102;

    private readonly TestDb _db = TestDb.Create();
    private int _bookSeq;

    public void Dispose() => _db.Dispose();

    private static WorksheetAuthoringService NewAuthoring(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    /// <summary>Her sayfa kendi kitap testine bağlanır (BookTest tekil sayfaya ait); sınıf tek.</summary>
    private async Task<(int GradeId, int BookId, int BookTestId)> SeedBookAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync();
        if (grade == null)
        {
            grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
        }
        var seq = ++_bookSeq;
        var book = new Book { Name = $"Kitap {seq}", BookTests = { new BookTest { Name = $"Test {seq}" } } };
        ctx.Books.Add(book);
        await ctx.SaveChangesAsync();
        return (grade.Id, book.Id, book.BookTests.First().Id);
    }

    private static ExamDto Dto(int gradeId, int bookId, int bookTestId, int? id = null, string name = "Sayfa 1",
        WorksheetKind? kind = null, int maxDurationSeconds = 600, bool isPracticeTest = false) => new()
    {
        Id = id, Name = name, Description = "d", GradeId = gradeId,
        MaxDurationSeconds = maxDurationSeconds, IsPracticeTest = isPracticeTest,
        BookId = bookId, BookTestId = bookTestId, Kind = kind,
    };

    private async Task<ExamSavedDto> SaveAsync(ExamDto dto, int userId = Owner)
    {
        await using var ctx = _db.NewContext();
        return await NewAuthoring(ctx).CreateOrUpdateAsync(dto, userId, isAdmin: false);
    }

    private async Task<Worksheet> WorksheetAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Worksheets.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    /// <summary>Sayfaya bağlı bir öğrenci oturumu (isteğe bağlı olarak soft-delete edilmiş).</summary>
    private async Task AddInstanceAsync(int worksheetId, int gradeId, bool softDeleted = false)
    {
        await using var ctx = _db.NewContext();
        var student = await ctx.Students.FirstOrDefaultAsync(s => s.UserId == StudentUserId)
            ?? new Student { UserId = StudentUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = gradeId };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();

        ctx.TestInstances.Add(new WorksheetInstance
        {
            StudentId = student.Id,
            WorksheetId = worksheetId,
            Status = WorksheetInstanceStatus.Started,
            StartTime = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            MaxDurationSeconds = 600,
        });
        await ctx.SaveChangesAsync();

        if (softDeleted)
        {
            await ctx.TestInstances.IgnoreQueryFilters().Where(ti => ti.WorksheetId == worksheetId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.IsDeleted, true));
        }
    }

    // ---------------------------------------------------------------- oluşturma: tür ve etkinlik zorlamaları

    [Fact]
    public async Task CreateOrUpdateAsync_CreateWithoutKind_StoresTest()
    {
        var (g, b, bt) = await SeedBookAsync();

        var r = await SaveAsync(Dto(g, b, bt, kind: null));

        r.Success.ShouldBeTrue(r.Message);
        (await WorksheetAsync(r.ExamId!.Value)).Kind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_CreateActivityWithDurationAndPracticeFlag_ForcesZeroDurationAndNotPractice()
    {
        var (g, b, bt) = await SeedBookAsync();

        var r = await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Activity, maxDurationSeconds: 600, isPracticeTest: true));

        r.Success.ShouldBeTrue(r.Message);
        var stored = await WorksheetAsync(r.ExamId!.Value);
        stored.Kind.ShouldBe(WorksheetKind.Activity);
        stored.MaxDurationSeconds.ShouldBe(0);
        stored.IsPracticeTest.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateOrUpdateAsync_CreateTestWithDuration_KeepsDurationAndPracticeFlag()
    {
        // Regresyon: Test türünde süre ve çalışma testi bayrağı aynen saklanır.
        var (g, b, bt) = await SeedBookAsync();

        var r = await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test, maxDurationSeconds: 600, isPracticeTest: true));

        r.Success.ShouldBeTrue(r.Message);
        var stored = await WorksheetAsync(r.ExamId!.Value);
        stored.MaxDurationSeconds.ShouldBe(600);
        stored.IsPracticeTest.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- güncelleme: null = korunur

    [Fact]
    public async Task CreateOrUpdateAsync_UpdateWithoutKindOnActivity_KeepsActivityAndZeroDuration()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Activity))).ExamId!.Value;

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: null, name: "Yeni ad", maxDurationSeconds: 900, isPracticeTest: true));

        r.Success.ShouldBeTrue(r.Message);
        var stored = await WorksheetAsync(id);
        stored.Kind.ShouldBe(WorksheetKind.Activity);
        stored.Name.ShouldBe("Yeni ad");
        stored.MaxDurationSeconds.ShouldBe(0);
        stored.IsPracticeTest.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateOrUpdateAsync_UpdateWithoutKindOnTest_KeepsTest()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test))).ExamId!.Value;

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: null, name: "Yeni ad"));

        r.Success.ShouldBeTrue(r.Message);
        (await WorksheetAsync(id)).Kind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_ChangeKindWithoutAnyInstance_IsAllowed()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test))).ExamId!.Value;

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: WorksheetKind.Activity));

        r.Success.ShouldBeTrue(r.Message);
        r.ErrorCode.ShouldBeNull();
        (await WorksheetAsync(id)).Kind.ShouldBe(WorksheetKind.Activity);
    }

    // ---------------------------------------------------------------- tür kilidi

    [Fact]
    public async Task CreateOrUpdateAsync_ChangeKindWhenInstanceExists_ReturnsConflictWithKindLockedAndWritesNothing()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test, name: "Eski ad"))).ExamId!.Value;
        await AddInstanceAsync(id, g);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: WorksheetKind.Activity, name: "Yeni ad"));

        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked);
        var stored = await WorksheetAsync(id);
        stored.Kind.ShouldBe(WorksheetKind.Test);
        stored.Name.ShouldBe("Eski ad");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_ChangeActivityToTestWhenInstanceExists_ReturnsKindLocked()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Activity))).ExamId!.Value;
        await AddInstanceAsync(id, g);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: WorksheetKind.Test));

        r.Conflict.ShouldBeTrue();
        r.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked);
        (await WorksheetAsync(id)).Kind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_ChangeKindWhenOnlySoftDeletedInstanceExists_ReturnsKindLocked()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test))).ExamId!.Value;
        await AddInstanceAsync(id, g, softDeleted: true);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: WorksheetKind.Activity));

        r.Conflict.ShouldBeTrue();
        r.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked);
        (await WorksheetAsync(id)).Kind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_SameKindWhenInstanceExists_IsNotLocked()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Activity))).ExamId!.Value;
        await AddInstanceAsync(id, g);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: WorksheetKind.Activity, name: "Ad değişti"));

        r.Success.ShouldBeTrue(r.Message);
        r.ErrorCode.ShouldBeNull();
        (await WorksheetAsync(id)).Name.ShouldBe("Ad değişti");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_OmittedKindWhenInstanceExists_IsNotLocked()
    {
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test))).ExamId!.Value;
        await AddInstanceAsync(id, g);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: null, name: "Ad değişti"));

        r.Success.ShouldBeTrue(r.Message);
        (await WorksheetAsync(id)).Kind.ShouldBe(WorksheetKind.Test);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_NonKindEditWithInstances_PersistsEveryFieldWithoutKindLock()
    {
        // Tür değişmediğinde (kilit yolu hiç çalışmaz) diğer alanlar oturum varken de normal kaydolur.
        var (g, b, bt) = await SeedBookAsync();
        var id = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test, maxDurationSeconds: 600))).ExamId!.Value;
        await AddInstanceAsync(id, g);

        var r = await SaveAsync(Dto(g, b, bt, id: id, kind: null, name: "Yeni ad", maxDurationSeconds: 900));

        r.Success.ShouldBeTrue(r.Message);
        r.ErrorCode.ShouldBeNull();
        var stored = await WorksheetAsync(id);
        stored.Name.ShouldBe("Yeni ad");
        stored.Description.ShouldBe("d");
        stored.MaxDurationSeconds.ShouldBe(900);
        stored.Kind.ShouldBe(WorksheetKind.Test);
    }

    // ---------------------------------------------------------------- kopyalama

    [Fact]
    public async Task CopyWorksheetAsync_ActivitySource_CopyIsActivity()
    {
        var (g, b, bt) = await SeedBookAsync();
        var sourceId = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Activity))).ExamId!.Value;

        await using var ctx = _db.NewContext();
        var copy = await NewAuthoring(ctx).CopyWorksheetAsync(sourceId, Owner, isAdmin: false);

        copy.Success.ShouldBeTrue(copy.Message);
        (await WorksheetAsync(copy.WorksheetId)).Kind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task CopyWorksheetAsync_TestSource_CopyStaysTest()
    {
        var (g, b, bt) = await SeedBookAsync();
        var sourceId = (await SaveAsync(Dto(g, b, bt, kind: WorksheetKind.Test))).ExamId!.Value;

        await using var ctx = _db.NewContext();
        var copy = await NewAuthoring(ctx).CopyWorksheetAsync(sourceId, Owner, isAdmin: false);

        copy.Success.ShouldBeTrue(copy.Message);
        (await WorksheetAsync(copy.WorksheetId)).Kind.ShouldBe(WorksheetKind.Test);
    }
}
