using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #570: <see cref="InteractionPlanSolution"/> — drag-drop çözümünün plandan ayrılması, çıkışta gizlenmesi
/// ve kayıt önceliği (açık çözüm &gt; plandan ayrılan &gt; mevcut kolon).
/// </summary>
public class InteractionPlanSolutionTests
{
    private const string SolutionJson = """{"placements":[{"zone":"a","label":"x"}]}""";

    private static readonly string PlanWithSolution =
        """{"labels":["x","y"],"zones":[{"id":"a"}],"solution":{"placements":[{"zone":"a","label":"x"}]}}""";

    private static readonly string PlanWithoutSolution = """{"labels":["x","y"],"zones":[{"id":"a"}]}""";

    /// <summary>Çıktı planında <c>solution</c> anahtarı olmamalı (JSON seviyesinde doğrula).</summary>
    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        doc.RootElement.TryGetProperty(InteractionPlanSolution.SolutionKey, out _).ShouldBeFalse();
    }

    // ---- Split ----

    [Fact]
    public void Split_ValidPlanWithSolution_ReturnsPlanWithoutSolutionKeyAndSolutionJson()
    {
        var (plan, solution) = InteractionPlanSolution.Split(PlanWithSolution);

        ShouldHaveNoSolutionKey(plan);
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.GetProperty("labels").GetArrayLength().ShouldBe(2);
        solution.ShouldNotBeNull();
        using var sol = JsonDocument.Parse(solution!);
        sol.RootElement.GetProperty("placements")[0].GetProperty("label").GetString().ShouldBe("x");
    }

    [Fact]
    public void Split_PlanWithoutSolution_ReturnsPlanUnchangedAndNullSolution()
    {
        var (plan, solution) = InteractionPlanSolution.Split(PlanWithoutSolution);

        plan.ShouldBe(PlanWithoutSolution);
        solution.ShouldBeNull();
    }

    [Fact]
    public void Split_InvalidJson_ReturnsPlanAsIsAndNullSolution()
    {
        const string broken = "{not json";

        var (plan, solution) = InteractionPlanSolution.Split(broken);

        plan.ShouldBe(broken);
        solution.ShouldBeNull();
    }

    [Fact]
    public void Split_ArrayPlan_ReturnsPlanAsIsAndNullSolution()
    {
        const string arrayPlan = """[{"solution":{"placements":[]}}]""";

        var (plan, solution) = InteractionPlanSolution.Split(arrayPlan);

        plan.ShouldBe(arrayPlan);
        solution.ShouldBeNull();
    }

    [Fact]
    public void Split_SolutionNull_RemovesKeyAndReturnsNullSolution()
    {
        var (plan, solution) = InteractionPlanSolution.Split("""{"labels":["x"],"solution":null}""");

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Split_EmptyOrWhitespacePlan_ReturnsPlanAsIsAndNullSolution(string? plan)
    {
        var (cleanPlan, solution) = InteractionPlanSolution.Split(plan);

        cleanPlan.ShouldBe(plan);
        solution.ShouldBeNull();
    }

    // ---- ForOutput ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForOutput_StoredPlanWithEmbeddedSolution_NeverReturnsSolutionInPlan(bool includeSolution)
    {
        var (plan, _) = InteractionPlanSolution.ForOutput(PlanWithSolution, SolutionJson, includeSolution);

        ShouldHaveNoSolutionKey(plan);
    }

    [Fact]
    public void ForOutput_IncludeSolutionFalse_ReturnsNullSolutionEvenWhenStored()
    {
        var (_, solution) = InteractionPlanSolution.ForOutput(PlanWithoutSolution, SolutionJson, includeSolution: false);

        solution.ShouldBeNull();
    }

    [Fact]
    public void ForOutput_IncludeSolutionTrue_ReturnsStoredSolution()
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput(PlanWithoutSolution, SolutionJson, includeSolution: true);

        plan.ShouldBe(PlanWithoutSolution);
        solution.ShouldBe(SolutionJson);
    }

    [Fact]
    public void ForOutput_LegacyPlanWithEmbeddedSolutionAndEmptyColumn_ReturnsLegacySolutionWhenIncluded()
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput(PlanWithSolution, storedSolution: null, includeSolution: true);

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldNotBeNull();
        solution!.ShouldContain("placements");
    }

    [Fact]
    public void ForOutput_LegacyPlanWithEmbeddedSolutionAndEmptyColumn_ReturnsNoSolutionWhenNotIncluded()
    {
        var (plan, solution) = InteractionPlanSolution.ForOutput(PlanWithSolution, storedSolution: null, includeSolution: false);

        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBeNull();
    }

    // ---- IsValidSolution ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("""{"placements":[]}""")]
    public void IsValidSolution_NullEmptyOrObject_ReturnsTrue(string? solution)
    {
        InteractionPlanSolution.IsValidSolution(solution).ShouldBeTrue();
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{broken")]
    public void IsValidSolution_NonObjectOrInvalidJson_ReturnsFalse(string solution)
    {
        InteractionPlanSolution.IsValidSolution(solution).ShouldBeFalse();
    }

    [Fact]
    public void IsValidSolution_LongerThanMaxLength_ReturnsFalse()
    {
        var tooLong = "{\"x\":\"" + new string('a', InteractionPlanSolution.MaxSolutionLength) + "\"}";

        InteractionPlanSolution.IsValidSolution(tooLong).ShouldBeFalse();
    }

    // ---- TryResolveForWrite ----

    [Fact]
    public void TryResolveForWrite_ExplicitSolutionWins_OverPlanEmbeddedAndExisting()
    {
        var ok = InteractionPlanSolution.TryResolveForWrite(PlanWithSolution, """{"placements":[{"explicit":true}]}""",
            existingSolution: """{"placements":[{"old":true}]}""", out var plan, out var solution);

        ok.ShouldBeTrue();
        ShouldHaveNoSolutionKey(plan);
        solution.ShouldBe("""{"placements":[{"explicit":true}]}""");
    }

    [Fact]
    public void TryResolveForWrite_ExplicitSolutionIsTrimmed()
    {
        InteractionPlanSolution.TryResolveForWrite(PlanWithoutSolution, "  " + SolutionJson + "\n", null,
            out _, out var solution).ShouldBeTrue();

        solution.ShouldBe(SolutionJson);
    }

    [Fact]
    public void TryResolveForWrite_NoExplicitSolution_UsesPlanEmbeddedSolutionOverExisting()
    {
        var ok = InteractionPlanSolution.TryResolveForWrite(PlanWithSolution, null,
            existingSolution: """{"placements":[{"old":true}]}""", out var plan, out var solution);

        ok.ShouldBeTrue();
        ShouldHaveNoSolutionKey(plan);
        solution!.ShouldContain("\"label\"");
        solution!.ShouldNotContain("\"old\"");
    }

    [Fact]
    public void TryResolveForWrite_NoSolutionAnywhere_KeepsExistingSolution()
    {
        var ok = InteractionPlanSolution.TryResolveForWrite(PlanWithoutSolution, null,
            existingSolution: SolutionJson, out var plan, out var solution);

        ok.ShouldBeTrue();
        plan.ShouldBe(PlanWithoutSolution);
        solution.ShouldBe(SolutionJson);
    }

    [Fact]
    public void TryResolveForWrite_NoSolutionAndNoExisting_StoresNullSolution()
    {
        InteractionPlanSolution.TryResolveForWrite(PlanWithoutSolution, null, null, out _, out var solution).ShouldBeTrue();

        solution.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveForWrite_EmptyPlan_ReturnsNullPlanAndNullSolution(string? plan)
    {
        var ok = InteractionPlanSolution.TryResolveForWrite(plan, null, SolutionJson, out var storedPlan, out var storedSolution);

        ok.ShouldBeTrue();
        storedPlan.ShouldBeNull();
        storedSolution.ShouldBeNull();
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("{broken")]
    public void TryResolveForWrite_InvalidExplicitSolution_ReturnsFalse(string explicitSolution)
    {
        var ok = InteractionPlanSolution.TryResolveForWrite(PlanWithSolution, explicitSolution, SolutionJson,
            out _, out _);

        ok.ShouldBeFalse();
    }
}
