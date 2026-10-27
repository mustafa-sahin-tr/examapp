using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Claude ön-etiketleyicisinin düzenlenebilir kısmı: iki aşamanın talimatı ve kutu sonrası işleme. Koordinat kuralı ve JSON
/// şeması <see cref="ClaudeLabelDetector"/>'da sabittir. <see cref="Version"/> 0 = koddaki varsayılanlar (DB'de kayıt yok).
/// Önbellek anahtarı sürüm numarası değil <see cref="Fingerprint"/>'tir: v0'ın içeriği kod değiştikçe değişir, aynı içerik
/// farklı sürümlerde tekrar kaydedilebilir.
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

    /// <summary>
    /// Kodda sabit kalan ve sonucu etkileyen her şeyin (sistem mesajı, yanıt şeması, kırpma payı, görsel boyutu kuralları)
    /// revizyonu. Bunlardan biri değişince artırın: eski Claude sonuçları kayıttan dönmez.
    /// 1: soru kutuları + şıklar. 2 (issue #578): 1. aşama bütün blokları türüyle döner (<c>kind</c>, <c>component</c>,
    /// <c>interactionType</c>); şık aşaması yalnız <c>mcq_question</c> için. 3 (issue #578): bileşensiz <c>activity</c>'ye de şık
    /// aşaması sorulur, >= 2 şık bulunursa <c>mcq_question</c>'a çevrilir (son işleme değişti). 4: sınır ayrıldı —
    /// <c>MaxQuestionsPerPage</c> yalnız şık çağrısı alabilecek blokları (<c>mcq_question</c> + bileşensiz <c>activity</c>),
    /// toplam blok sınırı <c>MaxQuestionsPerPage * 3</c> (yoğun sayfada gürültü son soruları itmez).
    /// </summary>
    public const int SchemaRevision = 4;

    /// <summary>Talimatlar + paylar + <see cref="SchemaRevision"/> SHA-256'sı (hex); AI sonuçlarının önbellek anahtarı.</summary>
    /// Önbelleğe alınmaz: record'un <c>with</c> kopyası alanı da kopyalar ve eski izi taşırdı.
    public string Fingerprint => ComputeFingerprint();

    /// <summary>
    /// Issue #578: sayfadaki BÜTÜN bloklar tek çağrıda, her biri türüyle (tip tip tarama yok). Bileşen aileleri epic #569.
    /// </summary>
    public const string DefaultQuestionInstruction =
        "This image is one page of a Turkish school book ({width}x{height} px). Find every block on the page and " +
        "classify each one with a kind. Return the blocks in reading order.\n" +
        "Kinds:\n" +
        "- mcq_question: a numbered multiple-choice question with lettered options (A) B) C) D) or A. B. ...). Return ONE " +
        "box that encloses the question stem, any figure or table that belongs to it, and all of its answer options. " +
        "The question number (for example \"5.\") must stay OUTSIDE the box: the left edge starts at the first letter of " +
        "the stem text, to the right of the number, and the top edge at the top of the stem's first line. Do not include " +
        "reading passages shared by several questions. A question that has lettered options is ALWAYS mcq_question, " +
        "even when it has a figure, abacus, clock, grid, table or picture options, and even when its stem contains " +
        "rules, symbols or an \"Örnek\" line that explains the question; that text is part of the question box.\n" +
        "- activity: an interactive exercise the student completes on the page without lettered options (true/false, " +
        "fill in the blanks, matching, tables to fill, ordering, sorting, drawing, colouring, puzzles, open-ended " +
        "writing). One box per exercise, enclosing its label (\"ETKİNLİK 3\", \"UYGULAMA - 1\", \"TAK - TİK\", " +
        "\"EŞLEŞTİRME\"), its instruction sentence and all of its items, tables and pictures together; never return the " +
        "instruction or the label as a separate block. A numbered exercise with several sub-items is one block.\n" +
        "- info_box: topic explanations and information boxes (\"Bilgi\", \"Hatırlayalım\", \"Biliyor musunuz?\", " +
        "concept or definition boxes, summaries) and worked examples (\"Örnek\" with its solution, \"Çözüm\").\n" +
        "- answer_key: a printed answer key (\"Cevap Anahtarı\", tables of question numbers and answers).\n" +
        "- noise: optical answer forms (bubble sheets), QR codes (karekod), page headers and footers, page numbers, " +
        "section or unit titles, logos, publisher marks, decorative pictures, cover pages, tables of contents, and " +
        "self-assessment or result charts (\"Süreç Değerlendirme\", \"Analiz\", grids of topic or question numbers to " +
        "tick, Doğru / Yanlış / Boş counters).\n" +
        "Anything that asks the student to answer or do something (a numbered question, \"PROBLEM\", exercise or " +
        "task) is never noise or info_box: it is mcq_question if it has lettered options, otherwise activity.\n" +
        "For an activity also give its component family:\n" +
        "A matrix choice: mark a choice per row (D/Y true-false, classifying items in a table, multiple selection, " +
        "choosing <, > or =) -> interactionType matrixChoice.\n" +
        "B answer box: write a short answer into boxes (short answer, fill in the blank, fill a table, label a map or " +
        "diagram) -> slotFill.\n" +
        "C drag and place: place given cards or words (fill from a word pool, sort into groups, put in order) -> " +
        "dragPlace; match two columns with lines -> match.\n" +
        "D mark on text (underline, circle words in a passage). E regions / colouring (colour or select areas of a " +
        "picture). F grid canvas (drawing on a grid, number line, graph). G game engines (maze, crossword, word search, " +
        "cipher). H open-ended (explain, write a sentence or paragraph). I special simulation (anything else).\n" +
        "For D-I use interactionType other. For kinds other than activity use \"none\" for component and interactionType. " +
        "Return an empty list if the page has no blocks.";

    public const string DefaultOptionInstruction =
        "This image is a single multiple-choice question cropped from a test page ({width}x{height} px). " +
        "Find each answer option (usually labeled A), B), C), D), E) or A. B. ...). For each option return one box " +
        "that encloses the option letter completely (including its left edge and parenthesis) and its whole content " +
        "(text, formula or picture), with a margin of a few pixels. Ignore the question number and the stem. " +
        "Return the options in label order.";

    public static LabelingPromptSettings Default { get; } =
        new(0, DefaultQuestionInstruction, DefaultOptionInstruction, QuestionPadding: 6, OptionPadding: 4,
            ExpandQuestionToOptions: true);

    /// <summary>İki ayar aynı sonucu üretir mi (sürüm numarası ve kim kaydettiği hariç).</summary>
    public bool HasSameContent(LabelingPromptSettings other) => Fingerprint == other.Fingerprint;

    private string ComputeFingerprint() => ComputeFingerprint(SchemaRevision);

    /// <summary>Verilen şema revizyonuyla parmak izi (test: revizyon artınca aynı içeriğin anahtarı değişir).</summary>
    internal string ComputeFingerprint(int schemaRevision)
    {
        var payload = JsonSerializer.Serialize(new object[]
        {
            schemaRevision, QuestionInstruction, OptionInstruction, QuestionPadding, OptionPadding, ExpandQuestionToOptions,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary><c>{width}</c>/<c>{height}</c> → Claude'a gönderilen görselin boyutu (piksel).</summary>
    public static string Render(string instruction, int width, int height) =>
        instruction
            .Replace("{width}", width.ToString(CultureInfo.InvariantCulture))
            .Replace("{height}", height.ToString(CultureInfo.InvariantCulture));
}
