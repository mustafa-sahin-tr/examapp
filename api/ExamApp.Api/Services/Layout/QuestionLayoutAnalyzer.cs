using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Services.Layout;

internal static class QuestionLayoutAnalyzer
{
    private const double MinScale = 1.0 / 1.9; // UI shrink limit ≈ 0.625x
    private const double AnswerGap = 16;
    private const double SectionGap = 24;

    private static readonly LayoutTarget DesktopTarget = new("desktop", 840, 620);
    private static readonly LayoutTarget TabletTarget = new("tablet", 680, 560);
    private static readonly LayoutTarget MobileTarget = new("mobile", 420, 700);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions UiSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static QuestionLayoutPlan Analyze(QuestionLayoutInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var sanitizedQuestionWidth = Math.Max(input.QuestionWidth, 0);
        var sanitizedQuestionHeight = Math.Max(input.QuestionHeight, 0);
        var answers = (input.Answers ?? Array.Empty<AnswerLayoutSize>())
            .Where(a => a.Width > 0 && a.Height > 0)
            .ToList();
        var passage = input.Passage is { Width: > 0, Height: > 0 }
            ? input.Passage
            : null;

        var recommendedColumns = ComputeColumnCount(sanitizedQuestionWidth, answers);
        var summary = ComputeContentDimensions(sanitizedQuestionWidth, sanitizedQuestionHeight, passage, answers, recommendedColumns);

        var plan = new QuestionLayoutPlan
        {
            BaseQuestionWidth = sanitizedQuestionWidth,
            BaseQuestionHeight = sanitizedQuestionHeight,
            AnswerCount = answers.Count,
            RecommendedAnswerColumns = recommendedColumns,
            Answers = answers,
            Passage = passage,
            Summary = summary,
            Breakpoints = new QuestionLayoutBreakpoints
            {
                Desktop = BuildVariant(DesktopTarget, summary, sanitizedQuestionWidth, sanitizedQuestionHeight, passage, answers, recommendedColumns),
                Tablet = BuildVariant(TabletTarget, summary, sanitizedQuestionWidth, sanitizedQuestionHeight, passage, answers, recommendedColumns),
                Mobile = BuildVariant(MobileTarget, summary, sanitizedQuestionWidth, sanitizedQuestionHeight, passage, answers, recommendedColumns)
            }
        };

        return plan;
    }

    public static string SerializePlan(QuestionLayoutPlan plan)
    {
        if (plan == null)
        {
            return string.Empty;
        }

        return JsonSerializer.Serialize(plan, SerializerOptions);
    }

    public static string BuildUiPlanPayload(string? storedPlanJson, int fallbackColumns, bool hasPassage)
    {
        var uiPlan = CreateUiPlan(storedPlanJson, fallbackColumns, hasPassage);
        return SerializeUiPlan(uiPlan);
    }

    public static QuestionLayoutUiPlan CreateUiPlan(string? storedPlanJson, int fallbackColumns, bool hasPassage)
    {
        var sanitizedColumns = Math.Max(1, fallbackColumns);
        if (!string.IsNullOrWhiteSpace(storedPlanJson) && storedPlanJson.Contains("breakpoints", StringComparison.OrdinalIgnoreCase))
        {
            var plan = DeserializePlan(storedPlanJson);
            if (plan != null)
            {
                return ProjectUiPlan(plan);
            }
        }

        return BuildFallbackUiPlan(sanitizedColumns, hasPassage);
    }

    public static string SerializeUiPlan(QuestionLayoutUiPlan plan)
    {
        if (plan == null)
        {
            return string.Empty;
        }

        return JsonSerializer.Serialize(plan, UiSerializerOptions);
    }

    public static QuestionLayoutPlan? DeserializePlan(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<QuestionLayoutPlan>(json, SerializerOptions);
    }

    private static QuestionLayoutVariant BuildVariant(
        LayoutTarget target,
        LayoutDimensionSummary summary,
        double baseQuestionWidth,
        double baseQuestionHeight,
        PassageLayoutSize? passage,
        IReadOnlyList<AnswerLayoutSize> answers,
        int columns)
    {
        var (scale, limitingAxis, requiresScroll) = ComputeScale(summary.TotalContentWidth, summary.TotalContentHeight, target);

        var scaledQuestionWidth = Round(baseQuestionWidth * scale);
        var scaledQuestionHeight = Round(baseQuestionHeight * scale);
        var scaledPassageWidth = passage.HasValue ? Round(passage.Value.Width * scale) : (double?)null;
        var scaledPassageHeight = passage.HasValue ? Round(passage.Value.Height * scale) : (double?)null;
        var scaledAnswersWidth = Round(summary.AnswersWidth * scale);
        var scaledAnswersHeight = Round(summary.AnswersHeight * scale);

        return new QuestionLayoutVariant
        {
            Key = target.Key,
            TargetWidth = target.Width,
            TargetHeight = target.Height,
            Scale = scale,
            LimitingAxis = limitingAxis,
            RequiresScroll = requiresScroll,
            EffectiveContentWidth = Round(summary.TotalContentWidth * scale),
            EffectiveContentHeight = Round(summary.TotalContentHeight * scale),
            EffectiveQuestionWidth = scaledQuestionWidth,
            EffectiveQuestionHeight = scaledQuestionHeight,
            EffectivePassageWidth = scaledPassageWidth,
            EffectivePassageHeight = scaledPassageHeight,
            EffectiveAnswersWidth = scaledAnswersWidth,
            EffectiveAnswersHeight = scaledAnswersHeight,
            AnswerColumns = columns,
            AnswerRows = summary.AnswerRows,
            AnswerMaxWidth = answers.Count > 0 ? Round(answers.Max(a => a.Width) * scale) : 0,
            AnswerMaxHeight = answers.Count > 0 ? Round(answers.Max(a => a.Height) * scale) : 0
        };
    }

    private static QuestionLayoutUiPlan ProjectUiPlan(QuestionLayoutPlan plan)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var columns = Math.Max(1, plan.RecommendedAnswerColumns);
        var hasPassage = plan.Passage.HasValue;
        var desktopVariant = plan.Breakpoints?.Desktop ?? new QuestionLayoutVariant
        {
            TargetWidth = DesktopTarget.Width,
            TargetHeight = DesktopTarget.Height
        };

        var inlineAnswers = ShouldInlineAnswers(desktopVariant, plan.Passage, columns);
        var layoutClass = BuildLayoutClass(hasPassage, inlineAnswers, columns);

        double? questionFlex = null;
        double? answersFlex = null;
        if (inlineAnswers && desktopVariant.EffectiveQuestionWidth > 0 && desktopVariant.EffectiveAnswersWidth > 0)
        {
            var total = desktopVariant.EffectiveQuestionWidth + desktopVariant.EffectiveAnswersWidth;
            if (total > 0)
            {
                questionFlex = Math.Round(desktopVariant.EffectiveQuestionWidth / total * 10, 2, MidpointRounding.AwayFromZero);
                answersFlex = Math.Round(desktopVariant.EffectiveAnswersWidth / total * 10, 2, MidpointRounding.AwayFromZero);
            }
        }
        else if (!inlineAnswers && desktopVariant.EffectiveQuestionHeight > 0 && desktopVariant.EffectiveAnswersHeight > 0)
        {
            var total = desktopVariant.EffectiveQuestionHeight + desktopVariant.EffectiveAnswersHeight;
            if (total > 0)
            {
                questionFlex = Math.Round(desktopVariant.EffectiveQuestionHeight / total * 10, 2, MidpointRounding.AwayFromZero);
                answersFlex = Math.Round(desktopVariant.EffectiveAnswersHeight / total * 10, 2, MidpointRounding.AwayFromZero);
            }
        }

        return new QuestionLayoutUiPlan
        {
            LayoutClass = layoutClass,
            AnswerColumns = columns,
            HasPassage = hasPassage,
            Overrides = CloneOverrides(plan.Overrides),
            QuestionFlex = questionFlex,
            AnswersFlex = answersFlex
        };
    }

    private static (double Scale, string LimitingAxis, bool RequiresScroll) ComputeScale(double contentWidth, double contentHeight, LayoutTarget target)
    {
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return (1, "none", false);
        }

        var widthScale = target.Width > 0 ? target.Width / contentWidth : 1;
        var heightScale = target.Height > 0 ? target.Height / contentHeight : 1;

        var limitingScale = Math.Min(widthScale, heightScale);
        var limitingAxis = widthScale < heightScale ? "width" : "height";

        if (limitingScale >= 1 || double.IsNaN(limitingScale) || double.IsInfinity(limitingScale))
        {
            limitingScale = 1;
            limitingAxis = "none";
        }

        var clampedScale = Math.Max(limitingScale, MinScale);
        var requiresScroll = clampedScale <= MinScale && limitingScale < MinScale;

        return (Round(clampedScale), limitingAxis, requiresScroll);
    }

    private static LayoutDimensionSummary ComputeContentDimensions(
        double questionWidth,
        double questionHeight,
        PassageLayoutSize? passage,
        IReadOnlyList<AnswerLayoutSize> answers,
        int columns)
    {
        var rows = BuildAnswerRows(answers, columns);
        var answersWidth = rows.Count > 0 ? rows.Max(row => row.TotalWidth) : 0;
        var answersHeight = rows.Count > 0 ? rows.Sum(row => row.MaxHeight) + Math.Max(rows.Count - 1, 0) * AnswerGap : 0;

        var passageWidth = passage?.Width ?? 0;
        var passageHeight = passage?.Height ?? 0;

        var totalWidth = new[] { questionWidth, passageWidth, answersWidth }.Where(v => v > 0).DefaultIfEmpty(questionWidth).Max();
        var totalHeight = 0.0;

        if (passageHeight > 0)
        {
            totalHeight += passageHeight;
        }

        if (passageHeight > 0 && questionHeight > 0)
        {
            totalHeight += SectionGap;
        }

        if (questionHeight > 0)
        {
            totalHeight += questionHeight;
        }

        if (questionHeight > 0 && answersHeight > 0)
        {
            totalHeight += SectionGap;
        }

        if (answersHeight > 0)
        {
            totalHeight += answersHeight;
        }

        totalWidth = Round(totalWidth);
        totalHeight = Round(totalHeight);
        answersWidth = Round(answersWidth);
        answersHeight = Round(answersHeight);

        return new LayoutDimensionSummary
        {
            TotalContentWidth = totalWidth,
            TotalContentHeight = totalHeight,
            AnswersWidth = answersWidth,
            AnswersHeight = answersHeight,
            AnswerRows = rows.Count,
            AnswerRowDetails = rows
        };
    }

    private static List<AnswerRowInfo> BuildAnswerRows(IReadOnlyList<AnswerLayoutSize> answers, int columns)
    {
        var sanitizedColumns = Math.Max(1, columns);
        var rows = new List<AnswerRowInfo>();

        if (answers == null || answers.Count == 0)
        {
            return rows;
        }

        for (var index = 0; index < answers.Count; index += sanitizedColumns)
        {
            var rowAnswers = answers.Skip(index).Take(sanitizedColumns).ToList();
            var rowWidth = rowAnswers.Sum(a => Math.Max(a.Width, 0)) + Math.Max(rowAnswers.Count - 1, 0) * AnswerGap;
            var rowHeight = rowAnswers.Max(a => Math.Max(a.Height, 0));

            rows.Add(new AnswerRowInfo
            {
                TotalWidth = Round(rowWidth),
                MaxHeight = Round(rowHeight),
                AnswerCount = rowAnswers.Count
            });
        }

        return rows;
    }

    private static int ComputeColumnCount(double questionWidth, IReadOnlyList<AnswerLayoutSize> answers)
    {
        var answerCount = answers?.Count ?? 0;
        if (answerCount <= 0)
        {
            return 1;
        }

        var maxAnswerWidth = answers.Max(a => a.Width);
        if (maxAnswerWidth <= 0)
        {
            return 1;
        }

        var candidateQuestionWidth = questionWidth > 0 ? questionWidth : maxAnswerWidth;
        var minColumnWidth = Math.Max(180, maxAnswerWidth);
        var maxColumns = candidateQuestionWidth > 0
            ? Math.Max(1, (int)Math.Floor((candidateQuestionWidth + AnswerGap) / (minColumnWidth + AnswerGap)))
            : Math.Max(1, answerCount);

        var columns = Math.Max(1, Math.Min(answerCount, maxColumns));

        if (answerCount == 4)
        {
            if (maxColumns >= 4)
            {
                columns = 4;
            }
            else if (columns == 3)
            {
                columns = maxColumns >= 2 ? 2 : 1;
            }
        }
        else if (answerCount == 5)
        {
            columns = Math.Min(2, Math.Max(1, maxColumns));
        }

        return Math.Max(1, Math.Min(answerCount, columns));
    }

    private static double Round(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static bool ShouldInlineAnswers(QuestionLayoutVariant variant, PassageLayoutSize? passage, int columns)
    {
        if (variant == null)
        {
            return false;
        }

        var availableWidth = variant.TargetWidth > 0 ? variant.TargetWidth : DesktopTarget.Width;
        if (availableWidth <= 0)
        {
            return false;
        }

        if (variant.EffectiveAnswersWidth <= 0 || variant.EffectiveQuestionWidth <= 0)
        {
            return false;
        }

        var combinedWidth = variant.EffectiveQuestionWidth + AnswerGap + variant.EffectiveAnswersWidth;
        var inlineCandidate = combinedWidth <= availableWidth && variant.Scale >= 0.51;

        if (!inlineCandidate)
        {
            return false;
        }

        if (passage.HasValue && variant.EffectivePassageWidth.HasValue && variant.EffectivePassageWidth.Value > 0)
        {
            // Passage çok genişse yan yana yerine üst üste yerleşmek daha güvenli
            var passageShare = variant.EffectivePassageWidth.Value / availableWidth;
            if (passageShare >= 0.55)
            {
                return false;
            }
        }

        // Çok fazla sütun varsa, sıkışmayı önlemek için üst üste yerleşime zorla
        if (columns >= 3 && variant.EffectiveAnswersWidth < variant.EffectiveQuestionWidth * 0.66)
        {
            return false;
        }

        return true;
    }

    private static string BuildLayoutClass(bool hasPassage, bool inlineAnswers, int columns)
    {
        var passageSegment = hasPassage ? "passage" : "solo";
        var flowSegment = inlineAnswers ? "inline" : "stack";
        var sanitizedColumns = Math.Max(1, Math.Min(columns, 4));
        return $"canvas-layout--{passageSegment}-{flowSegment}--cols-{sanitizedColumns}";
    }

    private static QuestionLayoutUiPlan BuildFallbackUiPlan(int columns, bool hasPassage)
    {
        var sanitizedColumns = Math.Max(1, Math.Min(columns, 4));
        return new QuestionLayoutUiPlan
        {
            LayoutClass = BuildLayoutClass(hasPassage, false, sanitizedColumns),
            AnswerColumns = sanitizedColumns,
            HasPassage = hasPassage,
            Overrides = null
        };
    }

    private static QuestionLayoutOverrides? CloneOverrides(QuestionLayoutOverrides? source)
    {
        if (source == null)
        {
            return null;
        }

        return new QuestionLayoutOverrides
        {
            InitialScale = source.InitialScale,
            MinScale = source.MinScale,
            MaxScale = source.MaxScale,
            AnswerScale = source.AnswerScale,
            Question = source.Question == null
                ? null
                : new QuestionLayoutOverrideDimensions
                {
                    MaxHeight = source.Question.MaxHeight,
                    MaxWidth = source.Question.MaxWidth
                },
            Answers = source.Answers == null
                ? null
                : new QuestionLayoutOverrideDimensions
                {
                    MaxHeight = source.Answers.MaxHeight,
                    MaxWidth = source.Answers.MaxWidth
                }
        };
    }

    private readonly record struct LayoutTarget(string Key, double Width, double Height);
}

internal sealed class QuestionLayoutUiPlan
{
    public string LayoutClass { get; set; } = string.Empty;
    public int AnswerColumns { get; set; }
    public bool HasPassage { get; set; }
    public QuestionLayoutOverrides? Overrides { get; set; }
    // Flex ratios for question and answers panels (for inline layouts)
    public double? QuestionFlex { get; set; }
    public double? AnswersFlex { get; set; }
}

internal sealed class QuestionLayoutPlan
{
    public double BaseQuestionWidth { get; set; }
    public double BaseQuestionHeight { get; set; }
    public int AnswerCount { get; set; }
    public int RecommendedAnswerColumns { get; set; }
    public IReadOnlyList<AnswerLayoutSize> Answers { get; set; } = Array.Empty<AnswerLayoutSize>();
    public PassageLayoutSize? Passage { get; set; }
    public LayoutDimensionSummary Summary { get; set; } = new();
    public QuestionLayoutBreakpoints Breakpoints { get; set; } = new();
    public QuestionLayoutOverrides? Overrides { get; set; }
}

internal sealed class QuestionLayoutBreakpoints
{
    public QuestionLayoutVariant Desktop { get; set; } = new();
    public QuestionLayoutVariant Tablet { get; set; } = new();
    public QuestionLayoutVariant Mobile { get; set; } = new();
}

internal sealed class QuestionLayoutOverrides
{
    public double? InitialScale { get; set; }
    public double? MinScale { get; set; }
    public double? MaxScale { get; set; }
    public double? AnswerScale { get; set; }
    public QuestionLayoutOverrideDimensions? Question { get; set; }
    public QuestionLayoutOverrideDimensions? Answers { get; set; }
}

internal sealed class QuestionLayoutOverrideDimensions
{
    public double? MaxHeight { get; set; }
    public double? MaxWidth { get; set; }
}

internal sealed class QuestionLayoutVariant
{
    public string Key { get; set; } = string.Empty;
    public double TargetWidth { get; set; }
    public double TargetHeight { get; set; }
    public double Scale { get; set; } = 1;
    public string LimitingAxis { get; set; } = "none";
    public bool RequiresScroll { get; set; }
    public double EffectiveContentWidth { get; set; }
    public double EffectiveContentHeight { get; set; }
    public double EffectiveQuestionWidth { get; set; }
    public double EffectiveQuestionHeight { get; set; }
    public double? EffectivePassageWidth { get; set; }
    public double? EffectivePassageHeight { get; set; }
    public double EffectiveAnswersWidth { get; set; }
    public double EffectiveAnswersHeight { get; set; }
    public int AnswerColumns { get; set; }
    public int AnswerRows { get; set; }
    public double AnswerMaxWidth { get; set; }
    public double AnswerMaxHeight { get; set; }
}

internal sealed class LayoutDimensionSummary
{
    public double TotalContentWidth { get; set; }
    public double TotalContentHeight { get; set; }
    public double AnswersWidth { get; set; }
    public double AnswersHeight { get; set; }
    public int AnswerRows { get; set; }
    public IReadOnlyList<AnswerRowInfo> AnswerRowDetails { get; set; } = Array.Empty<AnswerRowInfo>();
}

internal sealed class AnswerRowInfo
{
    public double TotalWidth { get; set; }
    public double MaxHeight { get; set; }
    public int AnswerCount { get; set; }
}

internal readonly record struct AnswerLayoutSize(int Width, int Height);

internal readonly record struct PassageLayoutSize(int Width, int Height);

internal sealed class QuestionLayoutInput
{
    public double QuestionWidth { get; set; }
    public double QuestionHeight { get; set; }
    public IReadOnlyList<AnswerLayoutSize>? Answers { get; set; }
    public PassageLayoutSize? Passage { get; set; }
}