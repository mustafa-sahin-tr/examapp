using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class StudyPageServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private StudyPageService NewService(AppDbContext ctx) => new(ctx, _minio);

    private static UserProfileDto Teacher(int id = 10) => new() { Id = id, Role = "Teacher", FullName = "T" };
    private static UserProfileDto Student(int id = 20) => new() { Id = id, Role = "Student", FullName = "S" };

    private async Task<int> AddPageAsync(string title, bool published, int createdBy = 10, int? subjectId = null)
    {
        await using var ctx = _db.NewContext();
        var p = new StudyPage
        {
            Title = title, Description = "d", IsPublished = published,
            CreatedByUserId = createdBy, CreatedByName = "T", CreatedByRole = "Teacher", SubjectId = subjectId,
        };
        ctx.StudyPages.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Students_only_see_published_pages()
    {
        await AddPageAsync("Yayında", published: true);
        await AddPageAsync("Taslak", published: false);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        var forStudent = await svc.GetPagedAsync(new StudyPageFilterDto { PageNumber = 1, PageSize = 10 }, Student());
        forStudent.Items.Select(i => i.Title).ShouldBe(new[] { "Yayında" });

        var forTeacher = await svc.GetPagedAsync(new StudyPageFilterDto { PageNumber = 1, PageSize = 10 }, Teacher());
        forTeacher.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Search_matches_title_or_description_case_insensitively()
    {
        await AddPageAsync("Kesirler", true);
        await AddPageAsync("Ondalık", true);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetPagedAsync(
            new StudyPageFilterDto { PageNumber = 1, PageSize = 10, Search = "KESIR" }, Teacher());

        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Kesirler");
    }

    [Fact]
    public async Task Filters_by_subject()
    {
        int subjectId;
        await using (var ctx = _db.NewContext())
        {
            var s = new Subject { Name = "Mat" };
            ctx.Subjects.Add(s);
            await ctx.SaveChangesAsync();
            subjectId = s.Id;
        }
        await AddPageAsync("A", true, subjectId: subjectId);
        await AddPageAsync("B", true);

        await using var ctx2 = _db.NewContext();
        var page = await NewService(ctx2).GetPagedAsync(
            new StudyPageFilterDto { PageNumber = 1, PageSize = 10, SubjectId = subjectId }, Teacher());
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("A");
    }

    [Fact]
    public async Task GetById_hides_an_unpublished_page_from_a_student()
    {
        var id = await AddPageAsync("Taslak", published: false);
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.GetByIdAsync(id, Student())).ShouldBeNull();
        (await svc.GetByIdAsync(id, Teacher())).ShouldNotBeNull();
        (await svc.GetByIdAsync(9999, Teacher())).ShouldBeNull();
    }

    [Fact]
    public async Task Create_persists_the_page_with_author_metadata()
    {
        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).CreateAsync(
            new CreateStudyPageRequestDto { Title = "  Yeni  ", Description = "aç", IsPublished = true },
            new List<IFormFile>(), Teacher(42));

        dto.Title.ShouldBe("Yeni");

        await using var check = _db.NewContext();
        var saved = await check.StudyPages.FirstAsync();
        saved.CreatedByUserId.ShouldBe(42);
        saved.CreatedByRole.ShouldBe("Teacher");
    }

    [Fact]
    public async Task Delete_is_refused_for_a_non_author()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);
        await using var ctx = _db.NewContext();

        var other = await NewService(ctx).DeleteAsync(id, Teacher(id: 999));
        other.Success.ShouldBeFalse();
        other.Message.ShouldContain("yetkiniz yok");

        var missing = await NewService(ctx).DeleteAsync(4242, Teacher(id: 10));
        missing.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_by_the_author_soft_deletes_the_page()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteAsync(id, Teacher(id: 10))).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.StudyPages.AnyAsync(p => p.Id == id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_service_caller_can_delete_any_page()
    {
        var id = await AddPageAsync("Sayfa", true, createdBy: 10);
        await using var ctx = _db.NewContext();
        (await NewService(ctx).DeleteAsync(id, new UserProfileDto { Id = 0, Role = "Service" })).Success.ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
