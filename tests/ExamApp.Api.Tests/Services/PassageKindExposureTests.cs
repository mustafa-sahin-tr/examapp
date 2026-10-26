using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: <c>PassageDto</c> tür + yönerge alanları çözüm DTO'larında (pratik oturumu <c>NextQuestion</c>, soru sorgusu
/// <c>GetQuestionById</c>) ve sayfa detayında <c>WorksheetDto.Kind</c>. Okuma metni değişmez: varsayılan Reading, yönerge yok.
/// </summary>
public class PassageKindExposureTests : IDisposable
{
    private const int StudentUserId = 4701;
    private const int TeacherUserId = 4702;
    private const string Instruction = "Kartları doğru yere sürükle.";

    private static readonly string Plan =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record World(int StudentId, int GradeId, int QuestionId, int WorksheetId);

    /// <summary>
    /// Öğrenci + konu; sınıfa bağlı normal bir sayfada, kapsamlı (passage) bir soru. Kapsam türü parametreyle.
    /// </summary>
    private async Task<World> SeedAsync(PassageKind kind, string? instruction, WorksheetKind worksheetKind = WorksheetKind.Test)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Fen" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Kuvvet", SubjectId = subject.Id, GradeId = grade.Id };
        var student = new Student { UserId = StudentUserId, StudentNumber = "S4701", SchoolName = "S", GradeId = grade.Id };
        ctx.AddRange(topic, student);
        await ctx.SaveChangesAsync();

        var passage = new Passage { Title = "Görsel", Text = "metin", Kind = kind, Instruction = instruction, IsCanvasQuestion = true };
        ctx.Passage.Add(passage);
        await ctx.SaveChangesAsync();

        var question = new Question
        {
            Text = "Kartları yerleştir", Point = 1, DifficultyLevel = 1, SubjectId = subject.Id, TopicId = topic.Id,
            IsCanvasQuestion = true, PassageId = passage.Id,
            InteractionType = "dragDropLabeling", InteractionPlan = Plan,
        };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();

        var answer = new Answer { QuestionId = question.Id, Text = "ok", Tag = "A", Order = 0 };
        var worksheet = new Worksheet
        {
            Name = "Sayfa", Description = "", GradeId = grade.Id, SubjectId = subject.Id, Kind = worksheetKind,
            CreateUserId = TeacherUserId,
        };
        ctx.AddRange(answer, worksheet);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = answer.Id;
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 });
        await ctx.SaveChangesAsync();

        // CreateUserId kayıt sırasında ambient kullanıcıyla ezilebildiği için sahibi ayrıca yazılır (parity testleriyle aynı desen).
        await ctx.Worksheets.Where(x => x.Id == worksheet.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreateUserId, (int?)TeacherUserId));

        return new World(student.Id, grade.Id, question.Id, worksheet.Id);
    }

    // ---------------------------------------------------------------- pratik oturumu

    [Fact]
    public async Task PracticeNextQuestion_ActivityPassage_CarriesKindActivityAndInstruction()
    {
        var w = await SeedAsync(PassageKind.Activity, Instruction);
        var sessionId = await StartPracticeAsync(w);

        var question = await NextQuestionAsync(sessionId, w);

        var passage = question.Passage.ShouldNotBeNull();
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe(Instruction);
    }

    [Fact]
    public async Task PracticeNextQuestion_ReadingPassage_KeepsReadingAndNoInstruction()
    {
        var w = await SeedAsync(PassageKind.Reading, instruction: null);
        var sessionId = await StartPracticeAsync(w);

        var question = await NextQuestionAsync(sessionId, w);

        var passage = question.Passage.ShouldNotBeNull();
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }

    private async Task<int> StartPracticeAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var dto = await new PracticeSessionService(ctx).StartAsync(
            new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId }, new PracticeSessionStartDto());
        return dto.Id;
    }

    private async Task<QuestionDto> NextQuestionAsync(int sessionId, World w)
    {
        await using var ctx = _db.NewContext();
        var next = await new PracticeSessionService(ctx).NextQuestionAsync(sessionId, w.StudentId);
        next.ShouldNotBeNull();
        return next!.Question.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------- soru sorgusu

    [Fact]
    public async Task GetQuestionById_ActivityPassage_CarriesKindActivityAndInstruction()
    {
        var w = await SeedAsync(PassageKind.Activity, Instruction);

        QuestionDto? question;
        await using (var ctx = _db.NewContext())
            question = await new QuestionQueryService(ctx).GetQuestionById(w.QuestionId);

        var passage = question.ShouldNotBeNull().Passage.ShouldNotBeNull();
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe(Instruction);
    }

    // ---------------------------------------------------------------- sayfa detayı: WorksheetDto.Kind

    [Theory]
    [InlineData(WorksheetKind.Activity)]
    [InlineData(WorksheetKind.Test)]
    public async Task GetWorksheetDetail_TeacherView_CarriesWorksheetKind(WorksheetKind kind)
    {
        var w = await SeedAsync(PassageKind.Reading, instruction: null, worksheetKind: kind);
        var authApi = Substitute.For<IAuthApiClient>();

        WorksheetDetailDto? detail;
        await using (var ctx = _db.NewContext())
            detail = await new WorksheetDetailService(ctx, authApi).GetWorksheetDetailAsync(w.WorksheetId, "Teacher", null, TeacherUserId);

        detail.ShouldNotBeNull();
        detail!.Worksheet.Kind.ShouldBe(kind);
    }
}
