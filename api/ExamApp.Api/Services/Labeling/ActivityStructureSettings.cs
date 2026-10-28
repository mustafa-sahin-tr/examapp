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
    /// </summary>
    private static readonly Dictionary<string, int> TypeRevisions = new(StringComparer.Ordinal)
    {
        [MatrixChoiceGrader.Type] = 1,
        [SlotFillGrader.Type] = 1,
        [DragPlaceGrader.Type] = 1,
        [MatchGrader.Type] = 1,
        [TextSelectGrader.Type] = 1, // issue #582
        [CrosswordGrader.Type] = 1, // issue #586 (dilim e)
    };

    /// <summary>Desteklenen tipler, ekrandaki sekme sırasıyla.</summary>
    public static IReadOnlyList<string> Types { get; } =
        [MatrixChoiceGrader.Type, SlotFillGrader.Type, DragPlaceGrader.Type, MatchGrader.Type, TextSelectGrader.Type,
         CrosswordGrader.Type];

    /// <summary>Tipin kanonik adı (büyük/küçük harf duyarsız); desteklenmiyorsa null.</summary>
    public static string? Canonical(string? interactionType)
    {
        if (string.IsNullOrWhiteSpace(interactionType))
            return null;
        var trimmed = interactionType.Trim();
        return Types.FirstOrDefault(t => string.Equals(t, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static ActivityPromptSettings Default(string interactionType) =>
        new(interactionType, 0, DefaultInstruction(interactionType));

    public static string DefaultInstruction(string interactionType) => interactionType switch
    {
        MatrixChoiceGrader.Type => DefaultMatrixChoiceInstruction,
        SlotFillGrader.Type => DefaultSlotFillInstruction,
        DragPlaceGrader.Type => DefaultDragPlaceInstruction,
        MatchGrader.Type => DefaultMatchInstruction,
        TextSelectGrader.Type => DefaultTextSelectInstruction,
        CrosswordGrader.Type => DefaultCrosswordInstruction,
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

    /// <summary>
    /// issue #582: metin üzerinde işaretleme (bileşen D). Claude SATIR düzeyinde döner (metin + satır kutusu); token'lar ve token
    /// kutuları sunucuda (<see cref="TextSelectTokenizer"/>). Metin aynen kopyalanır: yazım/noktalama hataları DÜZELTİLMEZ (hata
    /// bulma etkinliği), satır sonu tireleri korunur, boşluklar "( )" yazılır.
    /// </summary>
    public const string DefaultTextSelectInstruction =
        "This image is one activity cropped from a page of a Turkish school book ({width}x{height} px). The student works " +
        "on a printed text: underlines or circles words, finds the spelling or punctuation mistakes in it, writes " +
        "punctuation marks into the empty brackets \"( )\" in the text, or picks the wrong words and writes them " +
        "correctly. Describe its structure. " + Common +
        "- mode: punctuation when the text has empty brackets \"( )\" for punctuation marks; correct when the student " +
        "must also write the correct form of the words they find; otherwise select (underline, circle, find the " +
        "mistakes).\n" +
        "- style: circle when the instruction says to circle (yuvarlak içine al), underline when it says to underline " +
        "(altını çiz), otherwise highlight.\n" +
        "- palette: for punctuation, the punctuation marks the student may use (the ones the instruction lists, " +
        "otherwise the ones that fit the text, for example \".\", \",\", \"?\", \"!\"); otherwise an empty list.\n" +
        "- lines: every printed line of the text the student works on, top to bottom (left column before right " +
        "column), without the instruction and the activity label. text = the line exactly as printed, character by " +
        "character: do NOT correct spelling, spacing or punctuation mistakes (finding them is the exercise), keep a " +
        "hyphen at the end of a line where a word is split, and write every empty bracket as \"( )\". Box (x1, y1, " +
        "x2, y2) = the line from its first to its last character.\n" +
        "Return an empty list if the image has no such text.";

    /// <summary>
    /// issue #586 (dilim e): çengel bulmaca (bileşen G). Claude ızgara boyutunu ve her kelime yerinin (numara, yön, başlangıç
    /// hücresi, uzunluk) yanında ipucu metnini döner; harfler / cevaplar istenmez (#580: cevabı öğretmen yazar). Hücre koordinatı
    /// 0 tabanlı satır / sütundur, piksel değil (oynatıcı ızgarayı HTML olarak çizer, görsel etkileşimsiz üstte kalır).
    /// </summary>
    public const string DefaultCrosswordInstruction =
        "This image is a crossword puzzle (çengel bulmaca) cropped from a page of a Turkish school book ({width}x{height} " +
        "px). Describe its grid and its clues. " + Common +
        "- rows, cols: the number of cell rows and cell columns of the whole grid (count every cell position, including " +
        "black or empty positions inside the grid's bounding rectangle).\n" +
        "- words: every answer slot of the grid (a run of 2 or more white cells that is filled with one word). dir = " +
        "across for a word read left to right (soldan sağa), down for top to bottom (yukarıdan aşağıya). row, col = the " +
        "0-based cell row and column of the word's first cell (top-left cell is 0, 0); these are cell indexes, not pixels. " +
        "length = the number of cells of the word. number = the small number printed in the first cell or next to the " +
        "slot (0 if none). clue = the clue text that belongs to this slot, copied from the clue list (matched by its " +
        "number and direction) or printed inside or next to the slot, without its number; an empty string if there is no " +
        "text clue (for example a picture clue).\n" +
        "Never write the answers or any letter of them, even if some cells are already filled in. Return empty lists if " +
        "the image has no crossword grid.";
}
