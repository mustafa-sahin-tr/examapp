using System;

namespace ExamApp.Api.Data;

public class Passage : BaseEntity
{
    public int Id { get; set; }
    public string? Title { get; set; } // Kapsam başlığı (isteğe bağlı)
    public string? Text { get; set; } // Kapsam metni
    public string? ImageUrl { get; set; } // Eğer tablo, grafik vs. varsa resim URL'si
    public ICollection<Question> Questions { get; set; } = new List<Question>(); // Bu kapsama bağlı sorular

    public double? X { get; set; }

    public double? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool IsCanvasQuestion { get; set; } = false;

    /// <summary>
    /// issue #573: kapsamın türü. <see cref="PassageKind.Reading"/> (varsayılan) okuma metni/görseli; <see cref="PassageKind.Activity"/>
    /// etkinlik sayfasının ortak yönergesi + (varsa) kitap görseli. Mevcut satırlar Reading.
    /// </summary>
    public PassageKind Kind { get; set; } = PassageKind.Reading;

    /// <summary>
    /// issue #573: etkinlik yönergesi (ör. "Resimdeki varlıkları adlarıyla eşleştir."). En çok <see cref="MaxInstructionLength"/>
    /// karakter. DÜZ METİNDİR: istemci asla <c>innerHTML</c>/<c>[innerHTML]</c>/bypassSecurityTrust* ile işlememeli, yalnız metin
    /// bağlamasıyla (interpolation) göstermeli. ASCII kontrol karakterleri (\n, \r, \t hariç) girişte reddedilir (400).
    /// </summary>
    public string? Instruction { get; set; }

    public const int MaxInstructionLength = 2000;
}

/// <summary>issue #573: kapsam türü — okuma metni ya da etkinlik yönergesi.</summary>
public enum PassageKind
{
    Reading = 0,
    Activity = 1
}
