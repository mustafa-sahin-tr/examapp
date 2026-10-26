using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: kapsam (<see cref="Passage"/>) tür + yönerge alanları yazımı — tekil oluşturma/güncelleme
/// (<see cref="QuestionService.CreateOrUpdateQuestion"/>: yeni kapsam null tür = Reading; var olan kapsamda null alan
/// değişmez, boş yönerge temizler) ve canvas toplu yolu (<see cref="QuestionService.SaveBulkQuestion"/>: BulkPassageDto
/// Kind/Instruction; geçici Title id'siyle soru→kapsam eşlemesi hâlâ çalışır).
/// </summary>
public class QuestionServiceActivityPassageTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedWorksheetAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task<int> SeedQuestionAsync(int? passageId = null)
    {
        await using var ctx = _db.NewContext();
        var q = new Question { Text = "eski", Point = 1, PassageId = passageId };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    /// <summary>issue #573 review: var olan kapsamın alanlarını yalnız sahibi/admin değiştirir — sahip damgası için <paramref name="ownerUserId"/>.</summary>
    private const int PassageOwnerUserId = 42;

    private async Task<int> SeedPassageAsync(PassageKind kind, string? instruction, int? ownerUserId = null)
    {
        await using var ctx = _db.NewContext();
        if (ownerUserId is { } owner)
            ctx.SetCurrentUser(owner);
        var p = new Passage { Title = "P", Text = "t", Kind = kind, Instruction = instruction };
        ctx.Passage.Add(p);
        await ctx.SaveChangesAsync();
        return p.Id;
    }

    private async Task<QuestionSavedDto> SaveSingleAsync(QuestionDto dto, int actingUserId = 0)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateOrUpdateQuestion(dto, actingUserId);
    }

    private async Task<Passage> PassageOfQuestionAsync(int questionId)
    {
        await using var ctx = _db.NewContext();
        var q = await ctx.Questions.AsNoTracking().SingleAsync(x => x.Id == questionId);
        return await ctx.Passage.AsNoTracking().SingleAsync(p => p.Id == q.PassageId);
    }

    private static QuestionDto NewQuestionWithPassage(int worksheetId, PassageDto passage) => new()
    {
        Id = 0, Text = "Soru", Point = 5, TestId = worksheetId,
        Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
        Passage = passage,
    };

    // ---------------------------------------------------------------- tekil: yeni kapsam

    [Fact]
    public async Task CreateOrUpdateQuestion_NewPassageWithoutKindOrInstruction_DefaultsToReading()
    {
        var wsId = await SeedWorksheetAsync();

        var r = await SaveSingleAsync(NewQuestionWithPassage(wsId, new PassageDto { Id = 0, Title = "P", Text = "t" }));

        r.Success.ShouldBeFalse(); // yeni soru: başarı yalnız güncellemede (mevcut davranış)
        var passage = await PassageOfQuestionAsync(r.QuestionId!.Value);
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_NewPassageWithActivityKind_StoresKindAndTrimmedInstruction()
    {
        var wsId = await SeedWorksheetAsync();

        var r = await SaveSingleAsync(NewQuestionWithPassage(wsId,
            new PassageDto { Id = 0, Title = "P", Kind = PassageKind.Activity, Instruction = "  Eşleştir.  " }));

        var passage = await PassageOfQuestionAsync(r.QuestionId!.Value);
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Eşleştir.");
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_NewPassageWithWhitespaceInstruction_StoresNull()
    {
        var wsId = await SeedWorksheetAsync();

        var r = await SaveSingleAsync(NewQuestionWithPassage(wsId,
            new PassageDto { Id = 0, Title = "P", Kind = PassageKind.Activity, Instruction = "   " }));

        (await PassageOfQuestionAsync(r.QuestionId!.Value)).Instruction.ShouldBeNull();
    }

    // ---------------------------------------------------------------- tekil: var olan kapsamı bağlama

    [Fact]
    public async Task CreateOrUpdateQuestion_LinkExistingActivityPassageWithNullFields_KeepsKindAndInstruction()
    {
        var wsId = await SeedWorksheetAsync();
        var passageId = await SeedPassageAsync(PassageKind.Activity, "Eşleştir.");
        var qId = await SeedQuestionAsync();

        var r = await SaveSingleAsync(new QuestionDto
        {
            Id = qId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId }, // tür ve yönerge gönderilmedi
        });

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageOfQuestionAsync(qId);
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Eşleştir.");
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_LinkExistingPassageWithEmptyInstruction_ClearsInstruction()
    {
        var passageId = await SeedPassageAsync(PassageKind.Activity, "Eşleştir.", PassageOwnerUserId);
        var qId = await SeedQuestionAsync();

        var r = await SaveSingleAsync(new QuestionDto
        {
            Id = qId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId, Instruction = "" },
        }, actingUserId: PassageOwnerUserId);

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageOfQuestionAsync(qId);
        passage.Instruction.ShouldBeNull();
        passage.Kind.ShouldBe(PassageKind.Activity); // tür alanı gönderilmedi → değişmedi
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_LinkExistingPassageWithExplicitKind_ChangesKind()
    {
        var passageId = await SeedPassageAsync(PassageKind.Activity, "Eşleştir.", PassageOwnerUserId);
        var qId = await SeedQuestionAsync();

        var r = await SaveSingleAsync(new QuestionDto
        {
            Id = qId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId, Kind = PassageKind.Reading },
        }, actingUserId: PassageOwnerUserId);

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageOfQuestionAsync(qId);
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBe("Eşleştir."); // yönerge gönderilmedi → korunur
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_ReadingPassageLinkedWithoutFields_StaysReadingWithNullInstruction()
    {
        // Regresyon: eski istemci (tür/yönerge yok) mevcut okuma metnini olduğu gibi bırakır.
        var passageId = await SeedPassageAsync(PassageKind.Reading, null);
        var qId = await SeedQuestionAsync();

        var r = await SaveSingleAsync(new QuestionDto
        {
            Id = qId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId, Title = "P", Text = "metin" },
        });

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageOfQuestionAsync(qId);
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }

    // ---------------------------------------------------------------- canvas toplu yol

    private async Task<(int SubjectId, int TopicId, int SubTopicId)> SeedTaxonomyAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Fen" };
        var grade = new Grade { Name = "6" };
        ctx.AddRange(subject, grade);
        await ctx.SaveChangesAsync();
        var topic = new Topic { Name = "Kuvvet", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        var sub = new SubTopic { Name = "Sürtünme", TopicId = topic.Id };
        ctx.SubTopics.Add(sub);
        await ctx.SaveChangesAsync();
        return (subject.Id, topic.Id, sub.Id);
    }

    private static BulkQuestionDto McqWithPassage(string name, string passageRef) => new()
    {
        Name = name,
        InteractionType = "mcq",
        PassageId = passageRef,
        Answers = new List<BulkAnswerDto>
        {
            new() { Label = "A", IsCorrect = true },
            new() { Label = "B", IsCorrect = false },
        },
    };

    private async Task<ResponseBaseDto> BulkAsync(BulkQuestionCreateDto dto)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).SaveBulkQuestion(dto);
    }

    [Fact]
    public async Task SaveBulkQuestion_ActivityPassageWithInstruction_StoresKindAndInstructionAndLinksQuestion()
    {
        var tax = await SeedTaxonomyAsync();

        var result = await BulkAsync(new BulkQuestionCreateDto
        {
            ImageData = null!,
            Passages = new List<BulkPassageDto>
            {
                new() { Id = "gecici-1", X = 0, Y = 0, Width = 10, Height = 10, Kind = PassageKind.Activity, Instruction = "Yönerge metni" },
            },
            Questions = new List<BulkQuestionDto> { McqWithPassage("S1", "gecici-1") },
            Header = new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicId } },
        });

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var passage = await check.Passage.AsNoTracking().SingleAsync();
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Yönerge metni");
        var question = await check.Questions.AsNoTracking().SingleAsync();
        question.PassageId.ShouldBe(passage.Id); // geçici Title id'siyle eşleme hâlâ çalışır
    }

    [Fact]
    public async Task SaveBulkQuestion_PassageWithoutKind_DefaultsToReadingWithNullInstruction()
    {
        var tax = await SeedTaxonomyAsync();

        var result = await BulkAsync(new BulkQuestionCreateDto
        {
            ImageData = null!,
            Passages = new List<BulkPassageDto>
            {
                new() { Id = "gecici-1", X = 0, Y = 0, Width = 10, Height = 10 },
            },
            Questions = new List<BulkQuestionDto> { McqWithPassage("S1", "gecici-1") },
            Header = new HeaderInfo { SubjectId = tax.SubjectId, TopicId = tax.TopicId, Subtopics = new() { tax.SubTopicId } },
        });

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var passage = await check.Passage.AsNoTracking().SingleAsync();
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }
}
