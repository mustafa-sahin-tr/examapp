using System.Globalization;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Claude prompt'unun saf (DB'siz, ağsız) kısmı: yer tutucu işleme ve varsayılan sürüm.
/// </summary>
public class LabelingPromptSettingsTests
{
    [Fact]
    public void Render_ReplacesWidthAndHeightPlaceholders()
    {
        var rendered = LabelingPromptSettings.Render("page {width}x{height} px", 800, 1200);

        rendered.ShouldBe("page 800x1200 px");
    }

    [Fact]
    public void Render_RepeatedPlaceholders_AreAllReplaced()
    {
        var rendered = LabelingPromptSettings.Render("{width} / {width} / {height}", 10, 20);

        rendered.ShouldBe("10 / 10 / 20");
    }

    [Fact]
    public void Render_NoPlaceholders_ReturnsInstructionUnchanged()
    {
        const string instruction = "Find every question. Return an empty list if none.";

        LabelingPromptSettings.Render(instruction, 640, 480).ShouldBe(instruction);
    }

    [Fact]
    public void Render_CurrentCultureUsesGroupSeparators_NumbersStayPlain()
    {
        // Sayılar kültüre göre "1.234" gibi biçimlenirse Claude'a giden talimat bozulur; InvariantCulture şart.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            LabelingPromptSettings.Render("{width}x{height}", 1234, 2345).ShouldBe("1234x2345");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Default_IsVersionZeroWithCodeDefaults()
    {
        var defaults = LabelingPromptSettings.Default;

        defaults.Version.ShouldBe(0);
        defaults.QuestionPadding.ShouldBe(6);
        defaults.OptionPadding.ShouldBe(4);
        defaults.ExpandQuestionToOptions.ShouldBeTrue();
        defaults.QuestionInstruction.ShouldBe(LabelingPromptSettings.DefaultQuestionInstruction);
        defaults.OptionInstruction.ShouldBe(LabelingPromptSettings.DefaultOptionInstruction);
    }

    [Fact]
    public void Default_BothInstructionsUseDimensionPlaceholders()
    {
        LabelingPromptSettings.DefaultQuestionInstruction.ShouldContain("{width}x{height}");
        LabelingPromptSettings.DefaultOptionInstruction.ShouldContain("{width}x{height}");
    }

    [Fact]
    public void Default_InstructionsFitWithinMaxInstructionLength()
    {
        LabelingPromptSettings.DefaultQuestionInstruction.Length.ShouldBeLessThanOrEqualTo(LabelingPromptSettings.MaxInstructionLength);
        LabelingPromptSettings.DefaultOptionInstruction.Length.ShouldBeLessThanOrEqualTo(LabelingPromptSettings.MaxInstructionLength);
    }

    [Fact]
    public void Fingerprint_SameContent_IsStableAndLowercaseSha256Hex()
    {
        var a = LabelingPromptSettings.Default;
        var b = LabelingPromptSettings.Default with { };

        a.Fingerprint.ShouldBe(b.Fingerprint);
        a.Fingerprint.Length.ShouldBe(64);
        a.Fingerprint.ShouldAllBe(c => char.IsAsciiHexDigitLower(c));
    }

    [Fact]
    public void Fingerprint_IgnoresVersionAndHasSameContentIsTrue()
    {
        // Sürüm numarası önbellek anahtarı değil: içerik aynıysa v7 ile v0 aynı sonucu üretir.
        var v0 = LabelingPromptSettings.Default;
        var v7 = v0 with { Version = 7 };

        v7.Fingerprint.ShouldBe(v0.Fingerprint);
        v7.HasSameContent(v0).ShouldBeTrue();
    }

    [Theory]
    [InlineData("question")]
    [InlineData("option")]
    [InlineData("questionPadding")]
    [InlineData("optionPadding")]
    [InlineData("expand")]
    public void Fingerprint_ChangesWhenAnyContentFieldChanges_AndWithCopyIsNotStale(string field)
    {
        var original = LabelingPromptSettings.Default;

        var changed = field switch
        {
            "question" => original with { QuestionInstruction = original.QuestionInstruction + " x" },
            "option" => original with { OptionInstruction = original.OptionInstruction + " x" },
            "questionPadding" => original with { QuestionPadding = original.QuestionPadding + 1 },
            "optionPadding" => original with { OptionPadding = original.OptionPadding + 1 },
            _ => original with { ExpandQuestionToOptions = !original.ExpandQuestionToOptions },
        };

        changed.Fingerprint.ShouldNotBe(original.Fingerprint);
        changed.HasSameContent(original).ShouldBeFalse();
        // Orijinal değişmemeli (with kopyası eski izi taşımamalı).
        original.Fingerprint.ShouldBe(LabelingPromptSettings.Default.Fingerprint);
    }

    // ---------- Issue #578: blok türleri ----------

    [Fact]
    public void SchemaRevision_IsFiveForTextSelectComponentD()
    {
        LabelingPromptSettings.SchemaRevision.ShouldBe(5);
    }

    [Fact]
    public void DefaultQuestionInstruction_MapsComponentDToTextSelect_IncludingPunctuationGaps()
    {
        // review #582: Süper Combo s.134 "( ) içine noktalama" bloğu B/slotFill sanılıyordu.
        var instruction = LabelingPromptSettings.Default.QuestionInstruction;
        instruction.ShouldContain("-> textSelect");
        instruction.ShouldContain("it is D, not B");
        instruction.ShouldContain("For E-I use interactionType other");
        // Türkçe Ustası s.184: D bloğu yalnız yönergeye kutulanıyordu; metin/hikâye (başlığıyla) bloğun içinde olmalı.
        instruction.ShouldContain("the whole passage, including its title, is inside the box");
        instruction.ShouldNotContain("For D-I");
        instruction.Length.ShouldBeLessThanOrEqualTo(LabelingPromptSettings.MaxInstructionLength);
    }

    [Fact]
    public void Fingerprint_SchemaRevisionBump_ChangesCacheKeyForSameContent()
    {
        // Revizyon 1 ile üretilmiş (ör. özel kaydedilmiş bir sürümün) Claude sonucu, aynı talimatla bile kayıttan dönmez.
        var custom = new LabelingPromptSettings(3, "Find questions {width}x{height}", "Options {width}x{height}", 6, 4, true);

        custom.Fingerprint.ShouldBe(custom.ComputeFingerprint(LabelingPromptSettings.SchemaRevision));
        custom.Fingerprint.ShouldNotBe(custom.ComputeFingerprint(1));
        custom.ComputeFingerprint(3).ShouldNotBe(custom.ComputeFingerprint(4));
    }

    [Theory]
    [InlineData("mcq_question")]
    [InlineData("activity")]
    [InlineData("info_box")]
    [InlineData("answer_key")]
    [InlineData("noise")]
    [InlineData("matrixChoice")]
    [InlineData("slotFill")]
    [InlineData("dragPlace")]
    [InlineData("match")]
    [InlineData("karekod")]
    [InlineData("Örnek")]
    [InlineData("Cevap Anahtarı")]
    public void DefaultQuestionInstruction_NamesEveryKindTypeAndKeyCase(string term)
    {
        LabelingPromptSettings.DefaultQuestionInstruction.ShouldContain(term);
    }

    [Theory]
    [InlineData("A matrix choice")]
    [InlineData("B answer box")]
    [InlineData("C drag and place")]
    [InlineData("D mark on text")]
    [InlineData("E regions")]
    [InlineData("F grid canvas")]
    [InlineData("G game engines")]
    [InlineData("H open-ended")]
    [InlineData("I special simulation")]
    public void DefaultQuestionInstruction_DescribesEveryComponentFamily(string family)
    {
        LabelingPromptSettings.DefaultQuestionInstruction.ShouldContain(family);
    }

    [Fact]
    public void DefaultQuestionInstruction_KeepsMcqBoxRules()
    {
        // Mevcut MCQ kutusu kuralları korunur (gerileme olmasın): numara dışarıda, kök + şekil + şıklar içeride.
        var instruction = LabelingPromptSettings.DefaultQuestionInstruction;
        instruction.ShouldContain("must stay OUTSIDE the box");
        instruction.ShouldContain("any figure or table that belongs to it");
        instruction.ShouldContain("reading passages shared by several questions");
    }

    [Fact]
    public void MaxConstants_MatchDocumentedLimits()
    {
        LabelingPromptSettings.MaxInstructionLength.ShouldBe(4000);
        LabelingPromptSettings.MaxPadding.ShouldBe(50);
    }
}
