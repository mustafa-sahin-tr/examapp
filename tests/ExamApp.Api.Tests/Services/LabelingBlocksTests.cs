using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #578: Claude blok türü normalleştirmesi (bilinmeyen → noise, bileşen/etkileşim tutarlılığı) ve elle kayıt doğrulaması.
/// </summary>
public class LabelingBlocksTests
{
    [Theory]
    [InlineData("mcq_question", "mcq_question")]
    [InlineData("activity", "activity")]
    [InlineData("info_box", "info_box")]
    [InlineData("answer_key", "answer_key")]
    [InlineData("noise", "noise")]
    [InlineData("  INFO_BOX ", "info_box")]
    public void Normalize_KnownKinds_AreKept(string kind, string expected)
    {
        var c = LabelingBlocks.Normalize(kind, "none", "none");

        c.Kind.ShouldBe(expected);
        c.UnknownKind.ShouldBeFalse();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("qr_code")]
    public void Normalize_UnknownKind_BecomesNoiseAndIsFlagged(string? kind)
    {
        var c = LabelingBlocks.Normalize(kind, "A", "matrixChoice");

        c.Kind.ShouldBe(LabelingBlocks.Noise);
        c.Component.ShouldBeNull();
        c.InteractionType.ShouldBeNull();
        c.UnknownKind.ShouldBeTrue();
    }

    [Theory]
    [InlineData("mcq_question")]
    [InlineData("info_box")]
    [InlineData("answer_key")]
    [InlineData("noise")]
    public void Normalize_NonActivity_DropsComponentAndInteraction(string kind)
    {
        var c = LabelingBlocks.Normalize(kind, "B", "slotFill");

        c.Component.ShouldBeNull();
        c.InteractionType.ShouldBeNull();
    }

    [Theory]
    // A ve B tek tipli: model ne yazarsa yazsın tip bileşenden türetilir.
    [InlineData("A", "matrixChoice", "A", "matrixChoice")]
    [InlineData("A", "other", "A", "matrixChoice")]
    [InlineData("a", "slotFill", "A", "matrixChoice")]
    [InlineData("B", "none", "B", "slotFill")]
    [InlineData("B", "match", "B", "slotFill")]
    // C: modelin dragPlace/match seçimi geçerliyse o, değilse null.
    [InlineData("C", "dragPlace", "C", "dragPlace")]
    [InlineData("C", "MATCH", "C", "match")]
    [InlineData("C", "other", "C", null)]
    [InlineData("C", "slotFill", "C", null)]
    // D tek tipli (#582): model "other" yazar, tip bileşenden türetilir.
    [InlineData("D", "other", "D", "textSelect")]
    [InlineData("d", "matrixChoice", "D", "textSelect")]
    // H tek tipli (#583): açık uçlu → openText.
    [InlineData("H", "other", "H", "openText")]
    [InlineData("h", "matrixChoice", "H", "openText")]
    // E, F, G, I: kontrolcü yok.
    [InlineData("E", "textSelect", "E", null)]
    [InlineData("G", "openText", "G", null)]
    [InlineData("I", null, "I", null)]
    // A–I dışı bileşen → null, tip de null.
    [InlineData("none", "matrixChoice", null, null)]
    [InlineData("Z", "slotFill", null, null)]
    [InlineData(null, "dragPlace", null, null)]
    public void Normalize_Activity_DerivesInteractionTypeFromComponent(
        string? component, string? interactionType, string? expectedComponent, string? expectedType)
    {
        var c = LabelingBlocks.Normalize("activity", component, interactionType);

        c.Kind.ShouldBe(LabelingBlocks.Activity);
        c.Component.ShouldBe(expectedComponent);
        c.InteractionType.ShouldBe(expectedType);
        c.UnknownKind.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("mcq_question", null, null)]
    [InlineData("info_box", null, null)]
    [InlineData("answer_key", null, null)]
    [InlineData("noise", null, null)]
    [InlineData("activity", null, null)]
    [InlineData("activity", "E", null)]
    [InlineData("activity", "A", "matrixChoice")]
    [InlineData("activity", "B", "slotFill")]
    [InlineData("activity", "C", "dragPlace")]
    [InlineData("activity", "C", "match")]
    [InlineData("activity", "D", "textSelect")]
    [InlineData("activity", "H", "openText")]
    public void IsValidManual_AllowedCombinations_AreValid(string? kind, string? component, string? interactionType)
    {
        LabelingBlocks.IsValidManual(kind, component, interactionType).ShouldBeTrue();
    }

    [Theory]
    [InlineData("question", null, null)]
    [InlineData("Activity", null, null)]
    [InlineData("", null, null)]
    [InlineData("info_box", "A", null)]
    [InlineData(null, "A", null)]
    [InlineData("noise", null, "slotFill")]
    [InlineData("activity", "J", null)]
    [InlineData("activity", "a", null)]
    [InlineData("activity", "A", "slotFill")]
    [InlineData("activity", "C", "matrixChoice")]
    [InlineData("activity", "D", "matrixChoice")]
    [InlineData("activity", "E", "textSelect")]
    [InlineData("activity", "H", "textSelect")]
    [InlineData("activity", "E", "openText")]
    [InlineData("activity", null, "slotFill")]
    [InlineData("activity", "B", "other")]
    public void IsValidManual_InvalidCombinations_AreRejected(string? kind, string? component, string? interactionType)
    {
        LabelingBlocks.IsValidManual(kind, component, interactionType).ShouldBeFalse();
    }
}
