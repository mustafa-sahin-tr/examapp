using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #570 review: okuma yolu (<see cref="InteractionPlanSolution.Split"/>, <see cref="InteractionPlanSolution.ForOutput"/>)
/// yinelenen anahtarda ve bozuk JSON'da istisna atmaz ve kapalı başarısız davranır; yazma yolu katıdır (yinelenen anahtar
/// her derinlikte, bozuk JSON, nesne olmayan plan/çözüm, sınır aşımı reddedilir); plan üst sınırı 262144 karakter.
/// </summary>
public class InteractionPlanSolutionReviewTests
{
    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    // ---- 1. yinelenen anahtar: okuma asla atmaz ----

    [Fact]
    public void Split_DuplicateKeysWithoutSolution_ReturnsPlanWithDuplicatesAndNoSolution()
    {
        var (plan, solution) = InteractionPlanSolution.Split("""{"a":1,"a":2,"solution":{"x":1}}""");

        plan.ShouldBe("""{"a":1,"a":2}""");
        solution.ShouldBe("""{"x":1}""");
        ShouldHaveNoSolutionKey(plan);
    }

    [Fact]
    public void Split_DuplicateSolutionKeys_KeepsLastSolutionAndStripsAll()
    {
        var (plan, solution) = InteractionPlanSolution.Split("""{"a":1,"solution":{"x":1},"solution":{"y":2}}""");

        plan.ShouldBe("""{"a":1}""");
        solution.ShouldBe("""{"y":2}""");
    }

    [Fact]
    public void ForOutput_DuplicateKeyPlan_DoesNotThrowAndReturnsCleanPlan()
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput("""{"a":1,"a":2,"solution":{"x":1}}""", null, includeSolution: false);

        plan.ShouldBe("""{"a":1,"a":2}""");
        solution.ShouldBeNull();
    }

    [Fact]
    public void ForOutput_DuplicateSolutionKeysAndIncludeSolution_ReturnsLastEmbeddedSolution()
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput("""{"a":1,"solution":{"x":1},"solution":{"y":2}}""", null, includeSolution: true);

        plan.ShouldBe("""{"a":1}""");
        solution.ShouldBe("""{"y":2}""");
    }

    // ---- 2. kapalı başarısızlık: ayrıştırılamayan plan ----

    [Theory]
    [InlineData("""{"solution":{"x":1}""")]        // eksik kapanış parantezi
    [InlineData("""{"SOLUTION":{"x":1}""")]        // büyük harf
    [InlineData("""{"Solution":[1,2,""")]          // yarım dizi
    public void ForOutput_UnparseablePlanMentioningSolution_ReturnsNullPlan(string broken)
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput(broken, storedSolution: null, includeSolution: false);

        plan.ShouldBeNull();
        solution.ShouldBeNull();
    }

    [Fact]
    public void ForOutput_UnparseablePlanMentioningSolutionAndStoredSolution_NeverReturnsPlanText()
    {
        var (plan, _) = InteractionPlanSolution.ForOutput("""{"solution":{"x":1}""", storedSolution: """{"x":1}""", includeSolution: true);

        plan.ShouldBeNull();
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("""{"labels":["a",""")]
    public void ForOutput_UnparseablePlanWithoutSolutionText_ReturnsPlanUnchanged(string broken)
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput(broken, storedSolution: null, includeSolution: true);

        plan.ShouldBe(broken);
        solution.ShouldBeNull();
    }

    [Fact]
    public void Split_UnparseablePlan_DoesNotThrow()
    {
        var (plan, solution) = InteractionPlanSolution.Split("""{"solution":{"x":1}""");

        // Kapalı başarısızlık (#570): ayrıştırılamayan ve "solution" geçen plan null döner (istisna yok).
        plan.ShouldBeNull();
        solution.ShouldBeNull();
    }

    // ---- 3. yazma yolu katı ----

    [Fact]
    public void TryResolveForWrite_DuplicateKeyAtTopLevelPlan_ReturnsFalse()
    {
        InteractionPlanSolution.TryResolveForWrite("""{"a":1,"a":2}""", null, null, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_DuplicateKeyAtAnyDepthInPlan_ReturnsFalse()
    {
        InteractionPlanSolution.TryResolveForWrite("""{"zones":[{"id":"z1","id":"z2"}]}""", null, null, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_DuplicateKeyAtAnyDepthInExplicitSolution_ReturnsFalse()
    {
        InteractionPlanSolution.TryResolveForWrite("""{"a":1}""", """{"placements":[{"zone":"z1","zone":"z2"}]}""", null,
            out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_DuplicateKeyInSolutionSplitOutOfPlan_ReturnsFalse()
    {
        InteractionPlanSolution.TryResolveForWrite("""{"a":1,"solution":{"x":1,"x":2}}""", null, null, out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[1,2]")]
    [InlineData("\"metin\"")]
    [InlineData("42")]
    public void TryResolveForWrite_InvalidOrNonObjectPlan_ReturnsFalse(string plan)
    {
        InteractionPlanSolution.TryResolveForWrite(plan, null, null, out _, out _).ShouldBeFalse();
    }

    // ---- 4. plandan ayrılan çözüm ----

    [Theory]
    [InlineData("""{"a":1,"solution":"abc"}""")]
    [InlineData("""{"a":1,"solution":[1,2]}""")]
    [InlineData("""{"a":1,"solution":42}""")]
    [InlineData("""{"a":1,"solution":true}""")]
    public void TryResolveForWrite_NonObjectSolutionSplitOutOfPlan_ReturnsFalse(string plan)
    {
        InteractionPlanSolution.TryResolveForWrite(plan, null, existingSolution: null, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_NonObjectPlanSolutionWithValidExplicitSolution_StripsPlanAndStoresExplicit()
    {
        var ok = InteractionPlanSolution.TryResolveForWrite("""{"a":1,"solution":"abc"}""", """{"placements":[]}""", null,
            out var plan, out var solution);

        ok.ShouldBeTrue();
        plan.ShouldBe("""{"a":1}""");
        solution.ShouldBe("""{"placements":[]}""");
    }

    [Fact]
    public void TryResolveForWrite_SolutionNullInPlan_StoresNoSolution()
    {
        var ok = InteractionPlanSolution.TryResolveForWrite("""{"a":1,"solution":null}""", null, null, out var plan, out var solution);

        ok.ShouldBeTrue();
        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBeNull();
    }

    [Fact]
    public void TryResolveForWrite_SolutionNullInPlanWithExistingSolution_KeepsExisting()
    {
        // "solution": null açık bir silme değil; kolonu bilmeyen istemci yazdığında mevcut çözüm korunur.
        var ok = InteractionPlanSolution.TryResolveForWrite("""{"a":1,"solution":null}""", null, """{"placements":[]}""",
            out _, out var solution);

        ok.ShouldBeTrue();
        solution.ShouldBe("""{"placements":[]}""");
    }

    [Fact]
    public void TryResolveForWrite_SplitOutSolutionAboveMaxLength_ReturnsFalse()
    {
        var big = "{\"x\":\"" + new string('a', InteractionPlanSolution.MaxSolutionLength) + "\"}";
        var plan = "{\"a\":1,\"solution\":" + big + "}";

        InteractionPlanSolution.TryResolveForWrite(plan, null, null, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_OversizedSplitOutSolutionWithValidExplicit_ReturnsTrue()
    {
        // Ayrı çözüm verildiğinde plandaki çözüm kullanılmaz; yalnız plandan silinir (doğrulanmaz).
        var big = "{\"x\":\"" + new string('a', InteractionPlanSolution.MaxSolutionLength) + "\"}";
        var plan = "{\"a\":1,\"solution\":" + big + "}";

        var ok = InteractionPlanSolution.TryResolveForWrite(plan, """{"placements":[]}""", null, out var stored, out var solution);

        ok.ShouldBeTrue();
        stored.ShouldBe("""{"a":1}""");
        solution.ShouldBe("""{"placements":[]}""");
    }

    // ---- 5. plan üst sınırı 262144 ----

    private static string PlanOfLength(int length)
    {
        // {"x":"aaa..."} toplam uzunluk = length
        const string prefix = "{\"x\":\"";
        const string suffix = "\"}";
        return prefix + new string('a', length - prefix.Length - suffix.Length) + suffix;
    }

    [Fact]
    public void IsValidPlan_ExactlyMaxPlanLength_ReturnsTrue()
    {
        InteractionPlanSolution.MaxPlanLength.ShouldBe(262144);
        InteractionPlanSolution.IsValidPlan(PlanOfLength(262144)).ShouldBeTrue();
    }

    [Fact]
    public void IsValidPlan_OneOverMaxPlanLength_ReturnsFalse()
    {
        InteractionPlanSolution.IsValidPlan(PlanOfLength(262145)).ShouldBeFalse();
    }

    [Fact]
    public void TryResolveForWrite_PlanOverMaxPlanLength_ReturnsFalse()
    {
        InteractionPlanSolution.TryResolveForWrite(PlanOfLength(262145), null, null, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void QuestionDto_InteractionPlanAtMaxLength_PassesDataAnnotations()
    {
        Validate(new QuestionDto { InteractionPlan = PlanOfLength(262144) }, nameof(QuestionDto.InteractionPlan)).ShouldBeEmpty();
    }

    [Fact]
    public void QuestionDto_InteractionPlanOverMaxLength_FailsDataAnnotations()
    {
        Validate(new QuestionDto { InteractionPlan = PlanOfLength(262145) }, nameof(QuestionDto.InteractionPlan)).ShouldNotBeEmpty();
    }

    [Fact]
    public void BulkQuestionDto_InteractionPlanOverMaxLength_FailsDataAnnotations()
    {
        Validate(new BulkQuestionDto { InteractionPlan = PlanOfLength(262145) }, nameof(BulkQuestionDto.InteractionPlan))
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void BulkQuestionDto_InteractionPlanAtMaxLength_PassesDataAnnotations()
    {
        Validate(new BulkQuestionDto { InteractionPlan = PlanOfLength(262144) }, nameof(BulkQuestionDto.InteractionPlan))
            .ShouldBeEmpty();
    }

    private static List<ValidationResult> Validate(object instance, string member)
    {
        var results = new List<ValidationResult>();
        var value = instance.GetType().GetProperty(member)!.GetValue(instance);
        Validator.TryValidateProperty(value, new ValidationContext(instance) { MemberName = member }, results);
        return results;
    }
}
