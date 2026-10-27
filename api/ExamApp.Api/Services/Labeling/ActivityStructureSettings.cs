using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #579: bir etkileşim tipinin düzenlenebilir talimatı. <see cref="Version"/> 0 = koddaki varsayılan. Önbellek anahtarı
/// sürüm numarası değil <see cref="Fingerprint"/>'tir (tip + talimat + <see cref="ActivityStructureSettings.SchemaRevision"/>
/// + tipin şema revizyonu): bir tipin talimatı değişince yalnız o tipin kayıtları geçersiz olur.
/// </summary>
public sealed record ActivityPromptSettings(string InteractionType, int Version, string Instruction)
{
    /// <summary>Önbelleğe alınmaz: <c>with</c> kopyası eski izi taşımasın.</summary>
    public string Fingerprint => ActivityStructureSettings.ComputeFingerprint(InteractionType, Instruction);

    public bool HasSameContent(ActivityPromptSettings other) => Fingerprint == other.Fingerprint;
}

/// <summary>
/// Issue #579: etkinlik yapısı çıkarımının tipleri, varsayılan (İngilizce) talimatları ve şema revizyonları. Tip başına
/// ayrı talimat + ayrı yanıt şeması (<see cref="ActivityDrafts"/>); tek bir dev talimat yok. Yeni tip eklemek mevcut tiplerin
/// parmak izini değiştirmez.
/// </summary>
public static class ActivityStructureSettings
{
    public const int MaxInstructionLength = 4000;

    /// <summary>
    /// Bütün tiplerde ortak, kodda sabit kısmın revizyonu: sistem mesajı, kırpma payı, koordinat dönüşümü (blok kutusuna kırpma),
    /// etiket/yönerge temizleme. Bunlardan biri değişince artırın: TÜM tiplerin eski sonuçları kayıttan dönmez.
    /// 1: ilk sürüm.
    /// </summary>
    public const int SchemaRevision = 1;

    /// <summary>
    /// Tipe özgü sabit kısmın (yanıt şeması + şema → plan eşlemesi) revizyonu. Yalnız o tipin şeması/eşlemesi değişince o tipi
    /// artırın: diğer tiplerin kayıtları geçerli kalır. Hepsi 1: ilk sürüm.
    /// Issue #580: üç çözüm talimatı (<see cref="AnswerKeyKind"/>, <see cref="SolveFromKeyKind"/>, <see cref="SolveAiKind"/>) da burada:
    /// kodda sabit kısımları (sistem mesajı, anahtar şeması / çözüm talimatına eklenen ortak açıklama) değişirse yalnız o türü artırın.
    /// Çözüm şemasının TİPE özgü kısmı <see cref="SolutionTypeRevisions"/>'da.
    /// </summary>
    private static readonly Dictionary<string, int> TypeRevisions = new(StringComparer.Ordinal)
    {
        [MatrixChoiceGrader.Type] = 1,
        [SlotFillGrader.Type] = 1,
        [DragPlaceGrader.Type] = 1,
        [MatchGrader.Type] = 1,
        // 2: test (çoktan seçmeli) anahtar tabloları — soru başına bir madde ("Test 01: 1.B 2.C").
        [AnswerKeyKind] = 2,
        [SolveFromKeyKind] = 1,
        [SolveAiKind] = 1,
    };

    /// <summary>
    /// Issue #580: tipe göre çözüm şeması + şema → çözüm eşlemesi + talimata eklenen tip açıklamasının revizyonu
    /// (<see cref="ActivitySolutionDrafts"/>). Yalnız o tipin çözüm önbelleğini geçersiz kılar (her iki modda).
    /// </summary>
    private static readonly Dictionary<string, int> SolutionTypeRevisions = new(StringComparer.Ordinal)
    {
        [MatrixChoiceGrader.Type] = 1,
        [SlotFillGrader.Type] = 1,
        [DragPlaceGrader.Type] = 1,
        [MatchGrader.Type] = 1,
    };

    /// <summary>Issue #580: cevap anahtarı sayfasını okuma talimatı.</summary>
    public const string AnswerKeyKind = "answerKey";

    /// <summary>Issue #580: etkinlik + anahtar (metin ve görsel kırpmalar) → çözüm talimatı (tüm tiplerde ortak).</summary>
    public const string SolveFromKeyKind = "solveFromKey";

    /// <summary>Issue #580: anahtarsız etkinlikte Claude'un çözüm önerisi talimatı (tüm tiplerde ortak).</summary>
    public const string SolveAiKind = "solveAi";

    /// <summary>Desteklenen tipler, ekrandaki sekme sırasıyla.</summary>
    public static IReadOnlyList<string> Types { get; } =
        [MatrixChoiceGrader.Type, SlotFillGrader.Type, DragPlaceGrader.Type, MatchGrader.Type];

    /// <summary>Issue #580: Prompt ekranındaki tüm sekmeler: dört yapı tipi + üç çözüm talimatı.</summary>
    public static IReadOnlyList<string> PromptKinds { get; } =
    [
        MatrixChoiceGrader.Type, SlotFillGrader.Type, DragPlaceGrader.Type, MatchGrader.Type,
        AnswerKeyKind, SolveFromKeyKind, SolveAiKind,
    ];

    /// <summary>Tipin kanonik adı (büyük/küçük harf duyarsız); desteklenmiyorsa null. Yalnız dört etkinlik tipi.</summary>
    public static string? Canonical(string? interactionType) => CanonicalIn(Types, interactionType);

    /// <summary>Issue #580: talimat türünün kanonik adı (yapı tipi ya da çözüm talimatı); desteklenmiyorsa null.</summary>
    public static string? CanonicalPromptKind(string? kind) => CanonicalIn(PromptKinds, kind);

    private static string? CanonicalIn(IReadOnlyList<string> set, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return set.FirstOrDefault(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Issue #580: çözüm önbelleğinin prompt anahtarı = modun talimat parmak izi + etkinlik tipi + tipin çözüm revizyonu. Bir modun
    /// talimatı değişince yalnız o modun, bir tipin çözüm şeması değişince yalnız o tipin satırları geçersiz olur.
    /// </summary>
    internal static string SolutionPromptHash(ActivityPromptSettings modePrompt, string interactionType) =>
        SolutionPromptHash(modePrompt.Fingerprint, interactionType,
            SolutionTypeRevisions.TryGetValue(interactionType, out var revision) ? revision : 0);

    internal static string SolutionPromptHash(string modeFingerprint, string interactionType, int solutionTypeRevision)
    {
        var payload = JsonSerializer.Serialize(new object[] { modeFingerprint, interactionType, solutionTypeRevision });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static ActivityPromptSettings Default(string interactionType) =>
        new(interactionType, 0, DefaultInstruction(interactionType));

    public static string DefaultInstruction(string interactionType) => interactionType switch
    {
        MatrixChoiceGrader.Type => DefaultMatrixChoiceInstruction,
        SlotFillGrader.Type => DefaultSlotFillInstruction,
        DragPlaceGrader.Type => DefaultDragPlaceInstruction,
        MatchGrader.Type => DefaultMatchInstruction,
        AnswerKeyKind => DefaultAnswerKeyInstruction,
        SolveFromKeyKind => DefaultSolveFromKeyInstruction,
        SolveAiKind => DefaultSolveAiInstruction,
        _ => throw new ArgumentOutOfRangeException(nameof(interactionType), interactionType, "Unsupported activity type."),
    };

    internal static string ComputeFingerprint(string interactionType, string instruction) =>
        ComputeFingerprint(interactionType, instruction, SchemaRevision,
            TypeRevisions.TryGetValue(interactionType, out var typeRevision) ? typeRevision : 0);

    /// <summary>Verilen revizyonlarla parmak izi (test: revizyon artınca aynı içeriğin anahtarı değişir).</summary>
    internal static string ComputeFingerprint(string interactionType, string instruction, int schemaRevision, int typeRevision)
    {
        var payload = JsonSerializer.Serialize(new object[] { schemaRevision, interactionType, typeRevision, instruction });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private const string Common =
        "Copy printed text exactly as printed (Turkish, keep Turkish characters and the printed symbols). " +
        "Never solve the activity and never say which choice, card or answer is correct.\n" +
        "- instruction: the activity's printed instruction sentence (for example \"Aşağıdaki ifadelerden doğru olanların " +
        "yanına D, yanlış olanların yanına Y yazınız.\"), without its label such as \"ETKİNLİK 3\"; an empty string if " +
        "there is none.\n";

    public const string DefaultMatrixChoiceInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). In each row the " +
        "student marks one of a fixed set of choices: true/false columns (D / Y, Doğru / Yanlış), ticking which group an " +
        "item belongs to in a table, multiple selection, or choosing <, > or =. Describe its structure. " + Common +
        "- columns: the choices in printed order (2 to 10). label = the printed column heading or symbol (\"D\", \"Y\", " +
        "\"<\", a group name).\n" +
        "- multiPerRow: true only if a row may have more than one marked choice (the instruction says so or a table " +
        "clearly allows several ticks per row); otherwise false.\n" +
        "- rows: every row the student answers, top to bottom (left column before right column). label = the row's " +
        "printed statement or item without its number. Box (x1, y1, x2, y2) = the whole row including its statement and " +
        "answer cells. cells = for each choice of the row, the place where the student marks it (the empty cell, box, " +
        "circle or blank), with column = the 0-based index into columns. When all choices share one blank (for example " +
        "one box where the student writes D or Y), return a single cell for column 0 with that blank's box.\n" +
        "Skip a printed example row that is already filled in. Return empty lists if the image has no such activity.";

    public const string DefaultSlotFillInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). The student " +
        "writes short answers into blanks or boxes: fill in the blank, a short numeric answer, filling a table, labelling " +
        "a map, picture or diagram. Describe its structure. " + Common +
        "- slots: every empty place where the student writes an answer, in reading order (top to bottom, left to right). " +
        "Box (x1, y1, x2, y2) = only the writing area (the blank line, empty box or empty table cell), not the text " +
        "around it. label = a short identifier printed next to it (the text just before the blank, or the row and column " +
        "heading of a table cell); may be empty.\n" +
        "  kind: number when the answer is a number (including fractions and mixed numbers); select when the student " +
        "chooses one of a few alternatives printed for this blank (for example \"(az / çok)\"); palette when the answer is " +
        "one symbol from a small printed set (for example <, > or =); otherwise text.\n" +
        "  entry: for number, fraction if the blank is a fraction (numerator over denominator), mixed for a mixed " +
        "number, otherwise plain; \"none\" for other kinds.\n" +
        "  unit: the unit printed right after the blank (\"cm\", \"TL\", \"kg\"), otherwise an empty string.\n" +
        "  options: for select, the printed alternatives in order; otherwise an empty list.\n" +
        "  symbols: for palette, the allowed symbols; otherwise an empty list.\n" +
        "Skip blanks of a printed example that are already filled in. Return an empty list if there are no blanks.";

    public const string DefaultDragPlaceInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). The student " +
        "places given cards, words or pictures into places: filling blanks from a word pool, sorting items into groups, " +
        "or putting items in order. Describe its structure. " + Common +
        "- mode: order when the cards are put into a sequence of positions (first, second, ... or numbered steps); " +
        "otherwise place.\n" +
        "- reuse: true only if the instruction says a card may be used more than once; otherwise false.\n" +
        "- targets: every place a card goes, in reading order. label = the printed group heading, or the text right " +
        "before the blank (may be empty). Box (x1, y1, x2, y2) = the empty area where cards are placed (the blank, empty " +
        "box or the group's empty area); use 0, 0, 0, 0 if it is not visible. capacity = how many cards fit: 1 for a " +
        "blank or a position; for a group, the number of cards that belong there if you can tell from the layout, " +
        "otherwise the number of cards in the pool.\n" +
        "- cards: every card, word or picture of the pool in printed order, including the ones that fit nowhere. " +
        "label = its printed text (empty for a picture); box (x1, y1, x2, y2) = the card itself in the image.\n" +
        "Return empty lists if the image has no such activity.";

    public const string DefaultMatchInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). The student " +
        "matches items of one column with items of another (drawing lines, or writing a letter or number next to each " +
        "item). Describe its structure. " + Common +
        "- left: the items of the first group (the left column; for stacked groups the upper one), in printed order. " +
        "label = the item's printed text without its leading number or letter (empty for a picture); box (x1, y1, x2, " +
        "y2) = the item.\n" +
        "- right: the items of the second group in printed order, the same way. It may contain extra items that match " +
        "nothing.\n" +
        "- prelinked: pairs already connected in a printed example, as 0-based indexes into left and right; an empty " +
        "list if there is no example.\n" +
        "Return empty lists if the image has no such activity.";

    // ------------------------------------------------------------------ issue #580: çözüm talimatları

    public const string DefaultAnswerKeyInstruction =
        "This image is one page of the answer key at the back of a Turkish school book ({width}x{height} px). List every " +
        "answer on it in reading order (top to bottom; a left column before a right column).\n" +
        "- printedPage: the book page number of the activity the answer belongs to, as printed in the key. A heading such as " +
        "\"Sayfa 12\", \"s. 12\" or \"12. sayfa\" applies to all answers below it until the next heading. 0 if the key shows " +
        "no page.\n" +
        "- activityLabel: the activity's label as printed (for example \"Etkinlik 3\", \"3. Etkinlik\", \"Alıştırma 2\" or just " +
        "\"3\"), without trailing punctuation; an empty string if there is none.\n" +
        "- itemLabel: the item inside the activity (a letter, a number or a row such as \"a\", \"2\", \"1. satır\"); an empty " +
        "string when the answer is for the whole activity.\n" +
        "- answerText: the answer exactly as printed (keep Turkish characters, numbers, fractions such as 3/4 and symbols such " +
        "as <, >, =, ✓, D, Y). When the answer is a picture (matching lines, a ticked or coloured table, a drawn shape), write " +
        "what it shows as text if it can be read, for example \"1-c, 2-a, 3-b\"; otherwise an empty string.\n" +
        "- visual: true when the answer is, at least partly, a picture that the text does not fully express; then (x1, y1, " +
        "x2, y2) is the tight box of that picture. Otherwise false and 0, 0, 0, 0.\n" +
        "Give one entry per item when the items are answered separately, and one entry for the whole activity when its answer " +
        "is a single picture.\n" +
        "Multiple-choice test keys (a test or section title such as \"Test 01\", \"TEST 3\" or \"Destekleme Testi 2\" with " +
        "question numbers and option letters, for example \"1.B 2.C 3.A\" or a grid of numbers and letters): give ONE entry per " +
        "question with activityLabel = the test or section title as printed, itemLabel = the question number (\"1\"), answerText " +
        "= the option letter (\"B\"), visual = false, and printedPage = 0 unless a page number is printed for that test.\n" +
        "Return an empty list if the page has no answers.";

    public const string DefaultSolveFromKeyInstruction =
        "The first image is one activity cropped from a page of a Turkish school book ({width}x{height} px). Its answer from " +
        "the book's answer key is listed below as text; when an answer is a picture, the next images are the crops of that " +
        "answer from the key, in the order listed. Fill in the activity's solution using ONLY this answer: assign each answer " +
        "to the matching item of the structure below using the item labels, the order of the items and the layout you can " +
        "see. Do not change the key's answers and do not answer anything the key does not answer; leave out an item you cannot " +
        "match.";

    public const string DefaultSolveAiInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). The book gives no " +
        "answer for it. Solve the activity yourself as a careful teacher would and fill in its solution for the structure " +
        "below. Answer every item, with correct Turkish spelling. When an item allows several answers, give the most likely " +
        "one the book intends first and the other acceptable forms as alternatives.";
}
