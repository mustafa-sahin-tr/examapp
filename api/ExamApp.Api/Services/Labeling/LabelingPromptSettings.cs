using System.Globalization;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Claude ön-etiketleyicisinin düzenlenebilir kısmı: iki aşamanın talimatı ve kutu sonrası işleme. Koordinat kuralı ve JSON
/// şeması <see cref="ClaudeLabelDetector"/>'da sabittir. <see cref="Version"/> 0 = koddaki varsayılanlar (DB'de kayıt yok).
/// </summary>
public sealed record LabelingPromptSettings(
    int Version,
    string QuestionInstruction,
    string OptionInstruction,
    int QuestionPadding,
    int OptionPadding,
    bool ExpandQuestionToOptions)
{
    public const int MaxInstructionLength = 4000;
    public const int MaxPadding = 50;

    public const string DefaultQuestionInstruction =
        "This image is one test-book page ({width}x{height} px). Find every multiple-choice question. " +
        "For each question return ONE box that encloses the question stem, any figure or table that belongs to it, " +
        "and all of its answer options. Do not include the question number. Exclude page headers, footers, section " +
        "titles and reading passages shared by several questions. Return an empty list if the page has no questions.";

    public const string DefaultOptionInstruction =
        "This image is a single multiple-choice question cropped from a test page ({width}x{height} px). " +
        "Find each answer option (usually labeled A), B), C), D), E) or A. B. ...). For each option return one box " +
        "that encloses the option letter completely (including its left edge and parenthesis) and its whole content " +
        "(text, formula or picture), with a margin of a few pixels. Ignore the question number and the stem. " +
        "Return the options in label order.";

    public static LabelingPromptSettings Default { get; } =
        new(0, DefaultQuestionInstruction, DefaultOptionInstruction, QuestionPadding: 6, OptionPadding: 4,
            ExpandQuestionToOptions: true);

    /// <summary><c>{width}</c>/<c>{height}</c> → Claude'a gönderilen görselin boyutu (piksel).</summary>
    public static string Render(string instruction, int width, int height) =>
        instruction
            .Replace("{width}", width.ToString(CultureInfo.InvariantCulture))
            .Replace("{height}", height.ToString(CultureInfo.InvariantCulture));
}
