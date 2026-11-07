using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #933: çalışma sayfası içerik blokları ve sayfanın sorularından pratik oturumu.</summary>
public class StudyItemBlockTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private StudyItemService NewService(AppDbContext ctx) => new(ctx, _minio);

    private static UserProfileDto Teacher(int id = 10) => new() { Id = id, Role = "Teacher", FullName = "T" };
    private static UserProfileDto Student(int id = 20) => new() { Id = id, Role = "Student", FullName = "S" };

    private static string Json(params object[] blocks) => JsonSerializer.Serialize(blocks);

    private async Task<int> AddQuestionAsync(string text = "Soru?", int owner = 10)
    {
        await using var ctx = _db.NewContext();
        var q = new Question { Text = text, Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
        ctx.Answers.Add(correct);
        await ctx.SaveChangesAsync();
        q.CorrectAnswerId = correct.Id;
        q.CreateUserId = owner; // eklemede bağlamın kullanıcısıyla damgalanır; sahibi sonradan yazılır
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private async Task<StudyItemMutationResult> CreateAsync(string? blocks, bool published = true)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateAsync(
            new CreateStudyItemRequestDto
            {
                Title = "Bloklu", ContentType = StudyItemContentType.Image, IsPublished = published, Blocks = blocks
            },
            new List<IFormFile>(), Teacher());
    }

    [Fact]
    public async Task Create_with_blocks_needs_no_image_set_and_keeps_block_order()
    {
        var q = await AddQuestionAsync("Kök metni");

        var result = await CreateAsync(Json(
            new { type = 0, text = "  Giriş  " },
            new { type = 1, imageUrl = "/img/study-pages/pages/blocks/10/a.png?X-Amz-Signature=x", text = "Şekil" },
            new { type = 2, url = "https://www.youtube.com/watch?v=1", platform = 2, text = "Video" },
            new { type = 3, questionId = q },
            new { type = 4, text = "Ön", backText = "Arka" }));

        result.Error.ShouldBeNull();
        var blocks = result.Item!.Blocks;
        blocks.Select(b => b.Type).ShouldBe(new[]
        {
            StudyItemBlockType.Text, StudyItemBlockType.Image, StudyItemBlockType.Link,
            StudyItemBlockType.Question, StudyItemBlockType.Flashcard,
        });
        blocks.Select(b => b.SortOrder).ShouldBe(new[] { 1, 2, 3, 4, 5 });
        blocks[0].Text.ShouldBe("Giriş");
        blocks[1].ImageUrl.ShouldBe("/img/study-pages/pages/blocks/10/a.png");
        blocks[2].Platform.ShouldBe(StudyItemLinkPlatform.YouTube);
        blocks[3].QuestionId.ShouldBe(q);
        blocks[3].QuestionText.ShouldBe("Kök metni");
        blocks[4].BackText.ShouldBe("Arka");
    }

    [Fact]
    public async Task Create_image_type_without_images_or_blocks_is_still_rejected()
    {
        (await CreateAsync(null)).Error.ShouldNotBeNull();
        (await CreateAsync("[]")).Error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("""[{"type":0,"text":"   "}]""")]                                      // boş metin
    [InlineData("""[{"type":1,"imageUrl":"/img/exam-questions/questions/1/q.jpg"}]""")]   // yabancı bucket
    [InlineData("""[{"type":1,"imageUrl":"https://evil.example/x.png"}]""")]
    [InlineData("""[{"type":2,"url":"javascript:alert(1)"}]""")]
    [InlineData("""[{"type":3,"questionId":999999}]""")]                                  // bankada yok
    [InlineData("""[{"type":4,"text":"Ön"}]""")]                                          // arka yüz yok
    [InlineData("""[{"type":9,"text":"x"}]""")]                                           // bilinmeyen tip
    [InlineData("""{"type":0}""")]                                                        // dizi değil
    [InlineData("""[null]""")]
    public async Task Create_rejects_an_invalid_block_and_saves_nothing(string blocks)
    {
        (await CreateAsync(blocks)).Error.ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        (await ctx.StudyItems.CountAsync()).ShouldBe(0);
        (await ctx.StudyItemBlocks.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_question_the_teacher_cannot_view_is_rejected_but_one_already_on_the_page_is_kept()
    {
        var foreign = await AddQuestionAsync(owner: 99);
        (await CreateAsync(Json(new { type = 3, questionId = foreign }))).Error.ShouldNotBeNull();

        // Sayfada zaten olan soru (ör. görünürlüğü sonradan değişti) yeniden kayıtta denetlenmez.
        var mine = await AddQuestionAsync();
        var page = (await CreateAsync(Json(new { type = 3, questionId = mine }))).Item!;
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Questions.SingleAsync(x => x.Id == mine)).CreateUserId = 99;
            await ctx.SaveChangesAsync();
        }

        await using var update = _db.NewContext();
        var result = await NewService(update).UpdateAsync(page.Id,
            new UpdateStudyItemRequestDto
            {
                Title = "Bloklu", ContentType = StudyItemContentType.Image, IsPublished = true,
                Blocks = Json(new { id = page.Blocks[0].Id, type = 3, questionId = mine }, new { type = 0, text = "Yeni" }),
            },
            new List<IFormFile>(), Teacher());
        result.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Create_rejects_more_than_the_block_limit()
    {
        var many = Enumerable.Range(0, StudyItemBlock.MaxBlocksPerItem + 1).Select(i => (object)new { type = 0, text = $"m{i}" }).ToArray();
        (await CreateAsync(Json(many))).Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Update_reorders_kept_blocks_in_place_removes_missing_and_adds_new()
    {
        var created = (await CreateAsync(Json(new { type = 0, text = "A" }, new { type = 0, text = "B" }, new { type = 0, text = "C" }))).Item!;
        var (a, b, c) = (created.Blocks[0].Id, created.Blocks[1].Id, created.Blocks[2].Id);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).UpdateAsync(created.Id,
                new UpdateStudyItemRequestDto
                {
                    Title = "Bloklu", ContentType = StudyItemContentType.Image, IsPublished = true,
                    Blocks = Json(new { id = c, type = 0, text = "C2" }, new { type = 4, text = "Ön", backText = "Arka" }, new { id = a, type = 0, text = "A" }),
                },
                new List<IFormFile>(), Teacher());

            result.Error.ShouldBeNull();
            result.Item!.Blocks.Select(x => x.Text).ShouldBe(new[] { "C2", "Ön", "A" });
            result.Item.Blocks[0].Id.ShouldBe(c);
            result.Item.Blocks[2].Id.ShouldBe(a);
        }

        await using var check = _db.NewContext();
        (await check.StudyItemBlocks.IgnoreQueryFilters().SingleAsync(x => x.Id == b)).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_without_blocks_field_keeps_existing_blocks_and_waives_the_image_requirement()
    {
        var created = (await CreateAsync(Json(new { type = 0, text = "A" }))).Item!;

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateAsync(created.Id,
            new UpdateStudyItemRequestDto { Title = "Yeni ad", ContentType = StudyItemContentType.Image, IsPublished = true },
            new List<IFormFile>(), Teacher());

        result.Error.ShouldBeNull();
        result.Item!.Blocks.ShouldHaveSingleItem().Text.ShouldBe("A");
    }

    [Fact]
    public async Task A_block_id_from_another_page_is_treated_as_a_new_block()
    {
        var other = (await CreateAsync(Json(new { type = 0, text = "Diğer" }))).Item!;
        var mine = (await CreateAsync(Json(new { type = 0, text = "Benim" }))).Item!;

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).UpdateAsync(mine.Id,
                new UpdateStudyItemRequestDto
                {
                    Title = "Bloklu", ContentType = StudyItemContentType.Image, IsPublished = true,
                    Blocks = Json(new { id = other.Blocks[0].Id, type = 0, text = "Ele geçir" }),
                },
                new List<IFormFile>(), Teacher());
            result.Item!.Blocks.ShouldHaveSingleItem().Id.ShouldNotBe(other.Blocks[0].Id);
        }

        await using var check = _db.NewContext();
        (await check.StudyItemBlocks.SingleAsync(x => x.Id == other.Blocks[0].Id)).Text.ShouldBe("Diğer");
    }

    [Fact]
    public async Task A_deleted_question_block_is_hidden_from_students_but_kept_for_the_teacher()
    {
        var q = await AddQuestionAsync();
        var page = (await CreateAsync(Json(new { type = 0, text = "A" }, new { type = 3, questionId = q }))).Item!;
        await using (var ctx = _db.NewContext())
        {
            var question = await ctx.Questions.SingleAsync(x => x.Id == q);
            question.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var svc = NewService(read);
        (await svc.GetByIdAsync(page.Id, Student()))!.Blocks.ShouldHaveSingleItem().Type.ShouldBe(StudyItemBlockType.Text);
        var forTeacher = (await svc.GetByIdAsync(page.Id, Teacher()))!.Blocks;
        forTeacher.Count.ShouldBe(2);
        forTeacher[1].QuestionId.ShouldBeNull();
    }

    [Fact]
    public async Task Upload_block_image_rejects_non_images_and_stores_under_the_blocks_prefix()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), "study-pages", Arg.Any<string>())
            .Returns(ci => $"/img/study-pages/{ci.ArgAt<string>(1)}");

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        var (_, error) = await svc.UploadBlockImageAsync(FakeFile("x.svg", "image/svg+xml"), Teacher());
        error.ShouldNotBeNull();

        var (image, ok) = await svc.UploadBlockImageAsync(FakeFile("Şekil.PNG", "image/png"), Teacher(7));
        ok.ShouldBeNull();
        image!.ImageUrl.ShouldStartWith("/img/study-pages/pages/blocks/7/");
        image.ImageUrl.ShouldEndWith(".png");
    }

    private static IFormFile FakeFile(string name, string contentType)
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        return new FormFile(stream, 0, stream.Length, "image", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    // ---- pratik oturumu ----

    private async Task<StudentProfileDto> AddStudentAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = 55, StudentNumber = "S55", SchoolName = "S", GradeId = grade.Id };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return new StudentProfileDto { Id = student.Id, GradeId = grade.Id };
    }

    [Fact]
    public async Task Practice_session_from_a_page_serves_its_questions_in_block_order()
    {
        var student = await AddStudentAsync();
        var q1 = await AddQuestionAsync();
        var q2 = await AddQuestionAsync();
        var page = (await CreateAsync(Json(new { type = 3, questionId = q2 }, new { type = 0, text = "x" }, new { type = 3, questionId = q1 }))).Item!;

        await using var ctx = _db.NewContext();
        var service = new PracticeSessionService(ctx);

        var all = await service.StartAsync(student, new PracticeSessionStartDto { StudyItemId = page.Id });
        (await service.NextQuestionAsync(all.Id, student.Id))!.Question!.Id.ShouldBe(q2);

        var single = await service.StartAsync(student, new PracticeSessionStartDto { StudyItemId = page.Id, StudyQuestionId = q1 });
        (await service.NextQuestionAsync(single.Id, student.Id))!.Question!.Id.ShouldBe(q1);
    }

    [Fact]
    public async Task Practice_session_refuses_unpublished_pages_and_questions_not_on_the_page()
    {
        var student = await AddStudentAsync();
        var q = await AddQuestionAsync();
        var other = await AddQuestionAsync();
        var draft = (await CreateAsync(Json(new { type = 3, questionId = q }), published: false)).Item!;
        var page = (await CreateAsync(Json(new { type = 3, questionId = q }))).Item!;

        await using var ctx = _db.NewContext();
        var service = new PracticeSessionService(ctx);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.StartAsync(student, new PracticeSessionStartDto { StudyItemId = draft.Id }));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.StartAsync(student, new PracticeSessionStartDto { StudyItemId = page.Id, StudyQuestionId = other }));
    }

    public void Dispose() => _db.Dispose();
}
