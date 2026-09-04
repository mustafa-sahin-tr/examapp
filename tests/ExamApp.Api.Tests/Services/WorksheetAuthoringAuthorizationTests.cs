using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #4 — "sahibi VEYA admin" yetki modelinin worksheet yazma yollarına
/// uygulandığının doğrulanması (WorksheetAuthoringService).
/// </summary>
public class WorksheetAuthoringAuthorizationTests : IDisposable
{
    private const int Owner = 10;
    private const int Stranger = 20;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private WorksheetAuthoringService NewAuthoring(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private static readonly byte[] PngBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

    private static IFormFile PngImage() =>
        new FormFile(new MemoryStream(PngBytes), 0, PngBytes.Length, "file", "bg.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };

    /// <summary>Worksheet oluşturur; <paramref name="createUserId"/> null ise legacy kayıt gibi kalır.</summary>
    private async Task<int> SeedWorksheetAsync(int? createUserId, string? imageUrl = null)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id, ImageUrl = imageUrl };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        // ApplyAuditInfo Added durumunda CreateUserId'i _currentUserId (0) ile ezer;
        // istenen sahibi ikinci bir (Modified) kayıtla yaz.
        ws.CreateUserId = createUserId;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    // ---- UpdateWorksheetBackgroundImageAsync ----

    [Fact]
    public async Task UpdateWorksheetBackgroundImageAsync_OwnerUploadsValidImage_SucceedsAndPersistsUrl()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("http://minio/new.png");
        var wsId = await SeedWorksheetAsync(Owner);

        await using (var ctx = _db.NewContext())
        {
            var r = await NewAuthoring(ctx).UpdateWorksheetBackgroundImageAsync(wsId, PngImage(), Owner, isAdmin: false);
            r.Success.ShouldBeTrue();
            r.ImageUrl.ShouldBe("http://minio/new.png");
        }

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.ImageUrl.ShouldBe("http://minio/new.png");
    }

    [Fact]
    public async Task UpdateWorksheetBackgroundImageAsync_AdminNotOwner_Succeeds()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("http://minio/admin.png");
        var wsId = await SeedWorksheetAsync(Owner);

        await using var ctx = _db.NewContext();
        var r = await NewAuthoring(ctx).UpdateWorksheetBackgroundImageAsync(wsId, PngImage(), Stranger, isAdmin: true);
        r.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateWorksheetBackgroundImageAsync_NotOwnerAndNotAdmin_FailsAsNotFoundWithoutUpload()
    {
        var wsId = await SeedWorksheetAsync(Owner);

        await using var ctx = _db.NewContext();
        var r = await NewAuthoring(ctx).UpdateWorksheetBackgroundImageAsync(wsId, PngImage(), Stranger, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Message.ShouldBe("Worksheet bulunamadı.");
        await _minio.DidNotReceive().UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateWorksheetBackgroundImageAsync_LegacyWorksheetNullOwner_NormalUser_FailsAsNotFound()
    {
        var wsId = await SeedWorksheetAsync(createUserId: null);

        await using var ctx = _db.NewContext();
        var r = await NewAuthoring(ctx).UpdateWorksheetBackgroundImageAsync(wsId, PngImage(), Owner, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        await _minio.DidNotReceive().UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ---- DeleteWorksheetAsync ----

    [Fact]
    public async Task DeleteWorksheetAsync_Owner_SoftDeletes()
    {
        var wsId = await SeedWorksheetAsync(Owner);

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).DeleteWorksheetAsync(wsId, Owner, isAdmin: false)).Success.ShouldBeTrue();

        var soft = await _db.NewContext().Worksheets.IgnoreQueryFilters().FirstAsync(w => w.Id == wsId);
        soft.IsDeleted.ShouldBeTrue();
        soft.DeleteUserId.ShouldBe(Owner);
    }

    [Fact]
    public async Task DeleteWorksheetAsync_NotOwnerAndNotAdmin_DoesNotDelete()
    {
        var wsId = await SeedWorksheetAsync(Owner);

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).DeleteWorksheetAsync(wsId, Stranger, isAdmin: false)).Success.ShouldBeFalse();

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteWorksheetAsync_AdminNotOwner_SoftDeletes()
    {
        var wsId = await SeedWorksheetAsync(Owner);

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).DeleteWorksheetAsync(wsId, Stranger, isAdmin: true)).Success.ShouldBeTrue();

        var soft = await _db.NewContext().Worksheets.IgnoreQueryFilters().FirstAsync(w => w.Id == wsId);
        soft.IsDeleted.ShouldBeTrue();
    }

    // ---- CreateOrUpdateAsync (edit branch) ----

    private async Task<(int worksheetId, int bookId, int bookTestId)> SeedWorksheetWithBookAsync(int? createUserId)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var book = new Book { Name = "Kitap", BookTests = { new BookTest { Name = "Test" } } };
        ctx.AddRange(grade, book);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet
        {
            Name = "Orijinal", Description = "d", GradeId = grade.Id, BookTestId = book.BookTests.First().Id,
        };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = createUserId;
        await ctx.SaveChangesAsync();
        return (ws.Id, book.Id, book.BookTests.First().Id);
    }

    private static ExamDto EditDto(int worksheetId, int gradeId, int bookId, int bookTestId) => new()
    {
        Id = worksheetId, Name = "Güncel Ad", Description = "d", GradeId = gradeId,
        MaxDurationSeconds = 600, BookId = bookId, BookTestId = bookTestId,
    };

    [Fact]
    public async Task CreateOrUpdateAsync_EditByNonOwnerNonAdmin_FailsAndDoesNotPersist()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        await using (var ctx = _db.NewContext())
        {
            var r = await NewAuthoring(ctx).CreateOrUpdateAsync(
                EditDto(wsId, gradeId, bookId, bookTestId), Stranger, isAdmin: false);
            r.Success.ShouldBeFalse();
            r.Message.ShouldBe("Bu testi düzenleme yetkiniz yok.");
        }

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Name.ShouldBe("Orijinal");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_EditByAdminNotOwner_Succeeds()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        await using (var ctx = _db.NewContext())
        {
            var r = await NewAuthoring(ctx).CreateOrUpdateAsync(
                EditDto(wsId, gradeId, bookId, bookTestId), Stranger, isAdmin: true);
            r.Success.ShouldBeTrue();
        }

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Name.ShouldBe("Güncel Ad");
    }

    // ---- CreateOrUpdateAsync (Id == 0, ama Name + BookTestId eşleşen mevcut kayıt) ----

    private ExamDto CollisionDto(int gradeId, int bookId, int bookTestId) => new()
    {
        Id = 0, Name = "Orijinal", Description = "EZILDI", GradeId = gradeId,
        MaxDurationSeconds = 600, BookId = bookId, BookTestId = bookTestId,
    };

    [Fact]
    public async Task CreateOrUpdateAsync_CreateHittingForeignExistingWorksheet_NonOwnerNonAdmin_FailsAndDoesNotOverwrite()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        await using (var ctx = _db.NewContext())
        {
            var r = await NewAuthoring(ctx).CreateOrUpdateAsync(
                CollisionDto(gradeId, bookId, bookTestId), Stranger, isAdmin: false);
            r.Success.ShouldBeFalse();
            r.Message.ShouldBe("Bu testi düzenleme yetkiniz yok.");
        }

        var ws = (await _db.NewContext().Worksheets.FindAsync(wsId))!;
        ws.Name.ShouldBe("Orijinal");
        ws.Description.ShouldBe("d");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_CreateHittingForeignExistingWorksheet_Admin_Overwrites()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).CreateOrUpdateAsync(
                CollisionDto(gradeId, bookId, bookTestId), Stranger, isAdmin: true)).Success.ShouldBeTrue();

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Description.ShouldBe("EZILDI");
    }

    [Fact]
    public async Task CreateOrUpdateAsync_CreateHittingOwnExistingWorksheet_Owner_Overwrites()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).CreateOrUpdateAsync(
                CollisionDto(gradeId, bookId, bookTestId), Owner, isAdmin: false)).Success.ShouldBeTrue();

        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Description.ShouldBe("EZILDI");
    }

    // ---- CreateBulkExamsAsync (isAdmin) ----

    private BulkExamCreateDto BulkWithCollision(int gradeId, int bookId, int bookTestId) => new()
    {
        Exams =
        {
            new BulkExamItemDto
            {
                Name = "Orijinal", Description = "BULK-EZILDI", GradeId = gradeId,
                MaxDurationSeconds = 600, BookId = bookId, BookTestId = bookTestId,
            },
        },
    };

    [Fact]
    public async Task CreateBulkExamsAsync_NonAdminHittingForeignWorksheet_ReportsFailureAndDoesNotOverwrite()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        BulkExamResultDto result;
        await using (var ctx = _db.NewContext())
            result = await NewAuthoring(ctx).CreateBulkExamsAsync(
                BulkWithCollision(gradeId, bookId, bookTestId), Stranger, isAdmin: false);

        result.FailureCount.ShouldBe(1);
        result.FailedExams.ShouldContain(f => f.ExamName == "Orijinal" && f.ErrorMessage == "Bu testi düzenleme yetkiniz yok.");
        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Description.ShouldBe("d");
    }

    [Fact]
    public async Task CreateBulkExamsAsync_AdminHittingForeignWorksheet_Overwrites()
    {
        var (wsId, bookId, bookTestId) = await SeedWorksheetWithBookAsync(Owner);
        var gradeId = (await _db.NewContext().Worksheets.FindAsync(wsId))!.GradeId;

        BulkExamResultDto result;
        await using (var ctx = _db.NewContext())
            result = await NewAuthoring(ctx).CreateBulkExamsAsync(
                BulkWithCollision(gradeId, bookId, bookTestId), Stranger, isAdmin: true);

        result.SuccessCount.ShouldBe(1);
        (await _db.NewContext().Worksheets.FindAsync(wsId))!.Description.ShouldBe("BULK-EZILDI");
    }

    public void Dispose() => _db.Dispose();
}
