namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1310: öğrencinin bir soruda çizdikleri — soru görselinin üstüne (#1175) ve karalama alanına (#782). Öğretmen ve veli
/// salt okunur görür. Cevap satırından ayrı tablo: sonuç / puan sorguları <see cref="WorksheetInstanceQuestion"/>'ı yüklerken
/// bu büyük alanları taşımaz.
/// </summary>
public class WorksheetInstanceQuestionDrawing
{
    /// <summary>Hem birincil anahtar hem <see cref="WorksheetInstanceQuestion"/> yabancı anahtarı (soru başına tek satır).</summary>
    public int WorksheetInstanceQuestionId { get; set; }

    public WorksheetInstanceQuestion WorksheetInstanceQuestion { get; set; } = null!;

    /// <summary>Soru görseli üstündeki çizgiler: JSON dizi (jsonb), her öğe <c>[renk:]M… L…</c> SVG yolu, genişlik 1000 birim.</summary>
    public string Ink { get; set; } = "[]";

    /// <summary>Karalama alanındaki çizgiler, <see cref="Ink"/> ile aynı biçim.</summary>
    public string ScratchStrokes { get; set; } = "[]";

    /// <summary>Karalama alanına yazılan not (en çok <see cref="Services.Worksheets.SessionDrawingRules.TextMaxLength"/>).</summary>
    public string? ScratchText { get; set; }

    public DateTime UpdatedAt { get; set; }
}
