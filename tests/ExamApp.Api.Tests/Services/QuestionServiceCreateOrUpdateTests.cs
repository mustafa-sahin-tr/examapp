using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class QuestionServiceCreateOrUpdateTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    [Fact]
    public async Task Creating_a_new_question_persists_answers_the_correct_answer_and_an_outbox_event()
    {
        int worksheetId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            worksheetId = ws.Id;
        }

        var dto = new QuestionDto
        {
            Id = 0,
            Text = "2 + 2 = ?",
            Point = 5,
            TestId = worksheetId,
            Answers = new List<AnswerDto>
            {
                new() { Text = "3", IsCorrect = false },
                new() { Text = "4", IsCorrect = true },
                new() { Text = "", IsCorrect = false }, // blank -> skipped
            },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeFalse(); // Success is only true for updates in this service
        result.Message.ShouldContain("kaydedildi");

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == result.QuestionId);
        q.Answers.Select(a => a.Text).ShouldBe(new[] { "3", "4" });
        q.CorrectAnswerId.ShouldBe(q.Answers.Single(a => a.Text == "4").Id);

        (await check.TestQuestions.CountAsync(tq => tq.TestId == worksheetId && tq.QuestionId == q.Id)).ShouldBe(1);

        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe(OutboxEventRegistry.NameFor<QuestionCreatedEvent>());
    }

    [Fact]
    public async Task Updating_an_existing_question_rewrites_scalar_fields_and_answers()
    {
        int qId;
        await using (var ctx = _db.NewContext())
        {
            var existing = new Question { Text = "eski", SubText = "s", Point = 1, BookName = "b" };
            ctx.Questions.Add(existing);
            await ctx.SaveChangesAsync();
            qId = existing.Id;
            ctx.Answers.Add(new Answer { QuestionId = existing.Id, Text = "eski cevap" });
            await ctx.SaveChangesAsync();
        }

        var dto = new QuestionDto
        {
            Id = qId,
            Text = "yeni",
            SubText = "yeni alt",
            Point = 9,
            BookName = "yeni kitap",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true }, new() { Text = "B" } },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeTrue();
        result.Message.ShouldContain("güncellendi");

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == qId);
        q.Text.ShouldBe("yeni");
        q.Point.ShouldBe(9);
        q.Answers.Select(a => a.Text).OrderBy(t => t).ShouldBe(new[] { "A", "B" });
    }

    [Fact]
    public async Task Updating_a_missing_question_returns_a_failure_result()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateOrUpdateQuestion(new QuestionDto { Id = 999999, Text = "x" });
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bulunamadı");
    }

    [Fact]
    public async Task An_example_question_stores_the_practice_answer_instead_of_answer_rows()
    {
        var dto = new QuestionDto
        {
            Id = 0,
            Text = "örnek",
            IsExample = true,
            PracticeCorrectAnswer = "B",
            Answers = new List<AnswerDto>(),
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == result.QuestionId);
        q.IsExample.ShouldBeTrue();
        q.PracticeCorrectAnswer.ShouldBe("B");
        q.Answers.ShouldBeEmpty();
    }

    // ---- issue #365 (S2): paragraf görseli ----

    private const string Base64Jpeg = "data:image/jpeg;base64,AAECAwQF";

    private async Task<int> SeedQuestionAsync()
    {
        await using var ctx = _db.NewContext();
        var existing = new Question { Text = "eski", SubText = "s", Point = 1, BookName = "b" };
        ctx.Questions.Add(existing);
        await ctx.SaveChangesAsync();
        return existing.Id;
    }

    private void UploadReturnsImgUrl() =>
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Task.FromResult("/img/exam-questions/" + ci.ArgAt<string>(1)));

    private string UploadedKey() =>
        (string)_minio.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IMinIoService.UploadFileAsync))
            .GetArguments()[1]!;

    [Fact]
    public async Task Update_with_a_new_base64_passage_uploads_it_under_passages_test_id_and_stores_the_img_url()
    {
        var qId = await SeedQuestionAsync();
        UploadReturnsImgUrl();

        var dto = new QuestionDto
        {
            Id = qId, Text = "yeni", TestId = 42,
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = 0, Title = "P", Text = "t", ImageUrl = Base64Jpeg },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeTrue(result.Message);
        var key = UploadedKey();
        key.ShouldMatch(@"^passages/42/[0-9a-f-]{36}\.jpg$");

        await using var check = _db.NewContext();
        var passage = await check.Passage.SingleAsync();
        passage.ImageUrl.ShouldBe("/img/exam-questions/" + key); // data URI artık ham hâliyle yazılmıyor
        (await check.Questions.SingleAsync(x => x.Id == qId)).PassageId.ShouldBe(passage.Id);
    }

    [Theory]
    [InlineData(true)]   // güncelleme dalı
    [InlineData(false)]  // oluşturma dalı
    public async Task Base64_passage_without_a_test_id_is_uploaded_without_an_empty_folder_segment(bool update)
    {
        var qId = update ? await SeedQuestionAsync() : 0;
        UploadReturnsImgUrl();

        var dto = new QuestionDto
        {
            Id = qId, Text = "yeni", TestId = null,
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = 0, Title = "P", Text = "t", ImageUrl = Base64Jpeg },
        };

        await using (var ctx = _db.NewContext())
            await NewService(ctx).CreateOrUpdateQuestion(dto);

        var key = UploadedKey();
        key.ShouldMatch(@"^passages/[0-9a-f-]{36}\.jpg$");
        key.ShouldNotContain("//");
    }

    [Theory]
    [InlineData("/img/exam-questions/questions/1/question.jpg")] // D1: aynı alan ama passages/ değil
    [InlineData("/img/exam-questions/answers/1/a.jpg")]
    [InlineData("/img/exam-questions/question-transfer/exports/default/index.json")]
    [InlineData("https://evil.example.com/p.jpg")]
    [InlineData("/img/study-pages/books/b/page_1.webp")]
    public async Task A_new_passage_with_an_image_url_outside_passages_is_rejected_before_anything_is_written(string url)
    {
        var dto = new QuestionDto
        {
            Id = 0, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = 0, Title = "P", Text = "t", ImageUrl = url },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrWhiteSpace();

        await using var check = _db.NewContext();
        (await check.Questions.CountAsync()).ShouldBe(0);
        (await check.Passage.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_new_passage_with_a_signed_passages_url_stores_the_unsigned_key()
    {
        var dto = new QuestionDto
        {
            Id = 0, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto
            {
                Id = 0, Title = "P", Text = "t",
                ImageUrl = "/img/exam-questions/passages/3/p.jpg?X-Amz-Signature=abc&X-Amz-Expires=14400",
            },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.InvalidInput.ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.Passage.SingleAsync()).ImageUrl.ShouldBe("/img/exam-questions/passages/3/p.jpg");
    }

    // issue #402: güncellemede var olan paragrafa id ile bağlanma gerçekten PassageId'yi yazar; geri gönderilen adres
    // kullanılmaz, yeni paragraf oluşturulmaz, MinIO'ya yükleme yapılmaz.
    [Fact]
    public async Task Update_linking_an_existing_passage_sets_the_passage_id()
    {
        var qId = await SeedQuestionAsync();
        int passageId;
        await using (var seed = _db.NewContext())
        {
            var p = new Passage { Title = "P", Text = "t", ImageUrl = "/img/exam-questions/passages/1/p.jpg" };
            seed.Passage.Add(p);
            await seed.SaveChangesAsync();
            passageId = p.Id;
        }

        var dto = new QuestionDto
        {
            Id = qId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId, ImageUrl = "https://legacy.example.com/p.jpg" },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync(x => x.Id == qId)).PassageId.ShouldBe(passageId);
        (await check.Passage.SingleAsync()).ImageUrl.ShouldBe("/img/exam-questions/passages/1/p.jpg");
        _minio.ReceivedCalls().ShouldNotContain(c => c.GetMethodInfo().Name == nameof(IMinIoService.UploadFileAsync));
    }

    public void Dispose() => _db.Dispose();
}
