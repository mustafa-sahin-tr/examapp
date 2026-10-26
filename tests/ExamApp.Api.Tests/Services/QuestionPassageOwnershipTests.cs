using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573 (review): (A) var olan kapsamın tür/yönergesini yalnız kapsamın sahibi ya da admin değiştirir; başkası
/// gönderdiğinde alanlar sessizce yok sayılır ama bağlama (PassageId) yine yapılır; sahipsiz kapsam yalnız admin tarafından
/// değiştirilir. (B) Yönerge düz metin: kontrol karakteri (\n \r \t hariç, DEL dahil) reddedilir, hiçbir şey yazılmaz.
/// </summary>
public class QuestionPassageOwnershipTests : IDisposable
{
    private const int Owner = 5001;
    private const int Stranger = 5002;
    private const int Admin = 5999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    public void Dispose() => _db.Dispose();

    /// <summary>Sahipli (ownerId) ya da sahipsiz (null) bir kapsam; kapsam türü Reading, yönerge "Eski yönerge".</summary>
    private async Task<int> SeedPassageAsync(int? ownerId)
    {
        await using var ctx = _db.NewContext();
        var p = new Passage { Title = "P", Text = "t", Kind = PassageKind.Reading, Instruction = "Eski yönerge" };
        ctx.Passage.Add(p);
        await ctx.SaveChangesAsync();
        await ctx.Passage.IgnoreQueryFilters().Where(x => x.Id == p.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CreateUserId, (int?)ownerId));
        return p.Id;
    }

    private async Task<int> SeedQuestionAsync()
    {
        await using var ctx = _db.NewContext();
        var q = new Question { Text = "eski", Point = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private async Task<QuestionSavedDto> LinkAsync(int questionId, int passageId, int actingUserId, bool isAdmin,
        PassageKind? kind, string? instruction)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateOrUpdateQuestion(new QuestionDto
        {
            Id = questionId, Text = "yeni",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true } },
            Passage = new PassageDto { Id = passageId, Kind = kind, Instruction = instruction },
        }, actingUserId, isAdmin);
    }

    private async Task<Passage> PassageAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Passage.AsNoTracking().SingleAsync(p => p.Id == id);
    }

    // ---------------------------------------------------------------- (A) kapsam sahipliği

    [Fact]
    public async Task CreateOrUpdateQuestion_OwnerChangesPassageKindAndInstruction_Applied()
    {
        var passageId = await SeedPassageAsync(Owner);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Owner, isAdmin: false, PassageKind.Activity, "Yeni yönerge");

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageAsync(passageId);
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Yeni yönerge");
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_StrangerSendsKindAndInstruction_FieldsIgnoredButLinkStillHappens()
    {
        var passageId = await SeedPassageAsync(Owner);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Stranger, isAdmin: false, PassageKind.Activity, "Saldırı yönergesi");

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageAsync(passageId);
        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBe("Eski yönerge");
        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(q => q.Id == qId)).PassageId.ShouldBe(passageId);
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_AdminChangesAnotherTeachersPassage_Applied()
    {
        var passageId = await SeedPassageAsync(Owner);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Admin, isAdmin: true, PassageKind.Activity, "Admin yönergesi");

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageAsync(passageId);
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Admin yönergesi");
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_OwnerlessPassageByNonAdmin_FieldsIgnoredButLinkStillHappens()
    {
        var passageId = await SeedPassageAsync(ownerId: null);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Owner, isAdmin: false, PassageKind.Activity, "Yönerge");

        r.Success.ShouldBeTrue(r.Message);
        (await PassageAsync(passageId)).Kind.ShouldBe(PassageKind.Reading);
        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(q => q.Id == qId)).PassageId.ShouldBe(passageId);
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_OwnerlessPassageByAdmin_Applied()
    {
        var passageId = await SeedPassageAsync(ownerId: null);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Admin, isAdmin: true, PassageKind.Activity, "Admin yönergesi");

        r.Success.ShouldBeTrue(r.Message);
        var passage = await PassageAsync(passageId);
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe("Admin yönergesi");
    }

    [Fact]
    public async Task CreateOrUpdateQuestion_ZeroActingUserOnOwnedPassage_FieldsIgnored()
    {
        // actingUserId 0 (kimliksiz / servis çağrısı) hiçbir sahipliği karşılamaz; CreateUserId 0 da sahip sayılmaz.
        var passageId = await SeedPassageAsync(Owner);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, actingUserId: 0, isAdmin: false, PassageKind.Activity, "Yönerge");

        r.Success.ShouldBeTrue(r.Message);
        (await PassageAsync(passageId)).Instruction.ShouldBe("Eski yönerge");
    }

    // ---------------------------------------------------------------- (B) yönerge düz metin

    [Theory]
    [InlineData("satır1\nsatır2\r\n\tsekme")]
    [InlineData("Normal, Türkçe metin: ğüşıöç.")]
    public void PlainText_AllowedCharacters_PassValidation(string instruction)
    {
        PlainTextAttribute.ContainsControlCharacters(instruction).ShouldBeFalse();
        Validate(new PassageDto { Instruction = instruction }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a\u0000b")]   // NUL
    [InlineData("a\u0001b")]   // SOH
    [InlineData("a\u001Bb")]   // ESC
    [InlineData("a\u007Fb")]   // DEL
    public void PlainText_ControlCharacters_FailValidation(string instruction)
    {
        PlainTextAttribute.ContainsControlCharacters(instruction).ShouldBeTrue();
        Validate(new PassageDto { Instruction = instruction }).ShouldNotBeEmpty();
    }

    [Fact]
    public void PlainText_OnBulkPassageDto_RejectsControlCharacters()
    {
        Validate(new BulkPassageDto { Id = "p", Instruction = "a\u0007" }).ShouldNotBeEmpty();
        Validate(new BulkPassageDto { Id = "p", Instruction = "a\nb" }).ShouldBeEmpty();
    }

    [Fact]
    public void PlainText_InstructionOverTheLimit_FailsValidation()
    {
        Validate(new PassageDto { Instruction = new string('a', Passage.MaxInstructionLength + 1) }).ShouldNotBeEmpty();
    }

    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.Where(r => r.MemberNames.Contains("Instruction")).ToList();
    }

    [Theory]
    [InlineData("a\u0001b")]
    [InlineData("a\u007Fb")]
    public async Task CreateOrUpdateQuestion_ControlCharacterInstruction_IsInvalidInputAndWritesNothing(string instruction)
    {
        var passageId = await SeedPassageAsync(Owner);
        var qId = await SeedQuestionAsync();

        var r = await LinkAsync(qId, passageId, Owner, isAdmin: false, PassageKind.Activity, instruction);

        r.Success.ShouldBeFalse();
        r.InvalidInput.ShouldBeTrue();
        r.Message.ShouldNotBeNullOrWhiteSpace();
        (await PassageAsync(passageId)).Instruction.ShouldBe("Eski yönerge");
        await using var check = _db.NewContext();
        (await check.Questions.AsNoTracking().SingleAsync(q => q.Id == qId)).PassageId.ShouldBeNull();
    }

    [Fact]
    public async Task SaveBulkQuestion_ControlCharacterInstruction_IsRejectedBeforeAnyWriteOrUpload()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).SaveBulkQuestion(new BulkQuestionCreateDto
        {
            ImageData = null!,
            Passages = new List<BulkPassageDto>
            {
                new() { Id = "gecici-1", X = 0, Y = 0, Width = 10, Height = 10, Kind = PassageKind.Activity, Instruction = "kötü\u0002" },
            },
            Questions = new List<BulkQuestionDto>
            {
                new() { Name = "S1", InteractionType = "mcq", PassageId = "gecici-1", Answers = new List<BulkAnswerDto> { new() { Label = "A", IsCorrect = true } } },
            },
            Header = new HeaderInfo { SubjectId = 1, TopicId = 1, Subtopics = new() { 1 } },
        });

        result.Success.ShouldBeFalse();
        result.Message.ShouldNotBeNullOrWhiteSpace();
        await using var check = _db.NewContext();
        (await check.Passage.CountAsync()).ShouldBe(0);
        (await check.Questions.CountAsync()).ShouldBe(0);
        _minio.ReceivedCalls().ShouldBeEmpty();
    }
}
