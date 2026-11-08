using ExamApp.Api.Services.BookImport;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1022: şık hizalama işaretleme ekranının "Cevapları Hizala" (<c>alignAnswers</c>) kurallarıyla aynı — tek satır,
/// tek sütun, ızgara, satırları farklı uzunlukta — ve el ile aktarım aracındaki Python portuyla (process_bts.py) aynı sonuç.
/// </summary>
public class BookQuestionLayoutTests
{
    private const int Width = 2363;
    private const int Height = 3300;

    private static LayoutBox B(int x, int y, int w, int h) => new(x, y, w, h);

    private static LayoutQuestion Q(int x, int y, int w, int h) => new(B(x, y, w, h), []);

    [Fact]
    public void AttachPassages_LinksABoxToTheQuestionBelowInItsColumn_ClippedAtItsTop()
    {
        var left = Q(100, 800, 1000, 400);
        var right = Q(1300, 300, 1000, 400);

        var passages = BookQuestionLayout.AttachPassages([left, right], [new LayoutStimulus(B(120, 300, 900, 600))], Width);

        var passage = passages.ShouldHaveSingleItem();
        passage.Box.ShouldBe(B(120, 300, 900, 500));
        passage.QuestionIndexes.ShouldBe([0]);
    }

    [Fact]
    public void AttachPassages_MergesSeveralBoxesOfOneQuestion()
    {
        var passages = BookQuestionLayout.AttachPassages([Q(100, 900, 1000, 400)],
            [new LayoutStimulus(B(100, 300, 400, 500)), new LayoutStimulus(B(600, 320, 500, 400))], Width);

        passages.ShouldHaveSingleItem().Box.ShouldBe(B(100, 300, 1000, 500));
    }

    [Fact]
    public void AttachPassages_SharesAHeadedBoxWithTheNextQuestions_UpToTheNextPassage()
    {
        LayoutQuestion[] questions = [Q(100, 700, 1000, 300), Q(100, 1100, 1000, 300), Q(100, 1900, 1000, 300)];

        var passages = BookQuestionLayout.AttachPassages(questions,
            [new LayoutStimulus(B(100, 200, 1000, 400), 3), new LayoutStimulus(B(100, 1500, 1000, 300))], Width);

        passages.Select(p => p.QuestionIndexes).ShouldBe([[0, 1], [2]]);
    }

    [Fact]
    public void AttachPassages_SkipsBoxesWithoutAQuestionBelow_InsideAQuestion_OrInAnotherColumn()
    {
        LayoutQuestion[] questions = [Q(100, 300, 1000, 900)];

        var passages = BookQuestionLayout.AttachPassages(questions,
            [new LayoutStimulus(B(100, 1500, 1000, 300)), new LayoutStimulus(B(150, 400, 800, 200)), new LayoutStimulus(B(1300, 100, 900, 300))], Width);

        passages.ShouldBeEmpty();
    }

    [Fact]
    public void OneRow_GetsTheSameYAndHeight_XKept()
    {
        var aligned = BookQuestionLayout.AlignChoices([B(100, 500, 200, 60), B(400, 510, 180, 70), B(700, 495, 210, 50)], Width);

        aligned.ShouldBe([B(100, 495, 200, 85), B(400, 495, 180, 85), B(700, 495, 210, 85)]);
    }

    [Fact]
    public void OneColumn_GetsTheSameXAndWidth_YKept()
    {
        var aligned = BookQuestionLayout.AlignChoices([B(100, 500, 300, 60), B(110, 600, 280, 60), B(95, 700, 400, 60)], Width);

        aligned.ShouldBe([B(95, 500, 400, 60), B(95, 600, 400, 60), B(95, 700, 400, 60)]);
    }

    [Fact]
    public void Grid_AlignsColumnsAndRows()
    {
        // 2 × 2: A B / C D (girdi okuma sırasında).
        var aligned = BookQuestionLayout.AlignChoices(
            [B(100, 500, 300, 60), B(800, 505, 250, 50), B(110, 600, 280, 70), B(790, 610, 300, 50)], Width);

        aligned.ShouldBe([B(100, 500, 300, 60), B(790, 500, 300, 60), B(100, 600, 300, 70), B(790, 600, 300, 70)]);
    }

    [Fact]
    public void RowsOfDifferentLength_OnlyRowsAreAligned()
    {
        var aligned = BookQuestionLayout.AlignChoices(
            [B(100, 500, 300, 60), B(800, 505, 250, 50), B(110, 600, 280, 70)], Width);

        aligned.ShouldBe([B(100, 500, 300, 60), B(800, 500, 250, 60), B(110, 600, 280, 70)]);
    }

    [Fact]
    public void GridColumn_FartherThanTolerance_IsNotMerged()
    {
        var aligned = BookQuestionLayout.AlignChoices(
            [B(100, 500, 300, 60), B(800, 500, 250, 60), B(200, 600, 280, 60), B(800, 600, 250, 60)], Width);

        aligned[0].X.ShouldBe(100);
        aligned[2].X.ShouldBe(200);
        aligned[1].ShouldBe(aligned[3] with { Y = 500 });
    }

    [Fact]
    public void Tolerances_ScaleWithPageWidth()
    {
        // Yarım genişlikte satır toleransı 30 px: 40 px aralıklı iki kutu ayrı satırdır → tek sütun.
        var aligned = BookQuestionLayout.AlignChoices([B(50, 100, 100, 30), B(60, 140, 90, 30)], Width / 2);

        aligned.ShouldBe([B(50, 100, 100, 30), B(50, 140, 100, 30)]);
    }

    [Fact]
    public void TrimOverlaps_CutsPaddingHalfwayBetweenDetectedBoxes()
    {
        IReadOnlyList<LayoutBox> raw = [B(100, 100, 200, 50), B(320, 100, 200, 50), B(100, 200, 200, 50)];
        IReadOnlyList<LayoutBox> padded = [B(75, 75, 250, 100), B(295, 75, 250, 100), B(75, 175, 250, 100)];

        var trimmed = BookQuestionLayout.TrimOverlaps(raw, padded);

        // A | B ortası (300 + 320) / 2 = 310; A / C ortası (150 + 200) / 2 = 175.
        trimmed[0].ShouldBe(B(75, 75, 235, 100));
        trimmed[1].ShouldBe(B(310, 75, 235, 100));
        trimmed[2].ShouldBe(B(75, 175, 250, 100));
    }

    [Fact]
    public void Order_IsColumnThenTopToBottom_WideBoxesCountAsLeft()
    {
        var right = new LayoutQuestion(B(1300, 200, 900, 400), []);
        var leftLow = new LayoutQuestion(B(100, 900, 900, 400), []);
        var leftHigh = new LayoutQuestion(B(100, 200, 900, 400), []);
        var wide = new LayoutQuestion(B(100, 2000, 2100, 400), []);

        BookQuestionLayout.Order([right, wide, leftLow, leftHigh], Width).ShouldBe([leftHigh, leftLow, wide, right]);
    }

    [Fact]
    public void MergeSplit_JoinsAStemWithoutOptionsAndTheOptionsBoxRightBelow()
    {
        var stem = new LayoutQuestion(B(100, 200, 900, 300), []);
        var options = new LayoutQuestion(B(110, 520, 950, 200), [B(120, 530, 200, 50)]);
        var next = new LayoutQuestion(B(100, 1200, 900, 300), [B(120, 1300, 200, 50)]);

        var merged = BookQuestionLayout.MergeSplit([stem, options, next], Width, Height);

        merged.Count.ShouldBe(2);
        merged[0].Box.ShouldBe(B(100, 200, 960, 520));
        merged[0].Options.ShouldBe(options.Options);
        merged[1].ShouldBe(next);
    }

    [Fact]
    public void MergeSplit_KeepsBoxesFarApart()
    {
        var stem = new LayoutQuestion(B(100, 200, 900, 300), []);
        var far = new LayoutQuestion(B(100, 700, 900, 200), [B(120, 710, 200, 50)]);

        BookQuestionLayout.MergeSplit([stem, far], Width, Height).Count.ShouldBe(2);
    }

    [Fact]
    public void Prepare_GrowsTheQuestionToHoldPaddedOptions_AndOrdersOptionsByRowThenX()
    {
        // D şıkkı soru kutusunun altına taşıyor; şıklar karışık sırada geliyor.
        var question = new LayoutQuestion(B(100, 100, 1000, 600),
            [B(600, 650, 200, 60), B(150, 500, 200, 60), B(600, 505, 200, 50), B(150, 652, 200, 60)]);

        var prepared = BookQuestionLayout.Prepare(question, Width, Height);

        prepared.Box.ShouldBe(B(100, 100, 1000, 637)); // 652 + 60 + 25 dolgu
        prepared.Options.Select(o => (o.X, o.Y)).ShouldBe([(125, 475), (575, 475), (125, 625), (575, 625)]);
        prepared.Options.ShouldAllBe(o => o.Width == 250);
    }
}
