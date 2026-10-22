using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Claude ön-etiketleyicisinin kullanıcı tarafından düzenlenen talimatları. Her kayıt yeni bir sürümdür (geçmiş silinmez);
/// etkin olan en yüksek <see cref="Version"/>. Tablo boşsa koddaki varsayılanlar sürüm 0 olarak kullanılır. Koordinat kuralı ve
/// JSON şeması burada değil kodda sabittir (düzenlenirse yanıt ayrıştırılamazdı).
/// </summary>
public class LabelingPromptVersion
{
    public int Id { get; set; }

    public int Version { get; set; }

    /// <summary>Sayfa aşaması: soru kutuları. <c>{width}</c>/<c>{height}</c> gönderilen görselin boyutuyla değiştirilir.</summary>
    public string QuestionInstruction { get; set; } = string.Empty;

    /// <summary>Kırpma aşaması: şık kutuları. Aynı yer tutucular.</summary>
    public string OptionInstruction { get; set; } = string.Empty;

    /// <summary>Soru kutusuna her yönden eklenen pay (orijinal piksel).</summary>
    public int QuestionPadding { get; set; }

    /// <summary>Şık kutusuna her yönden eklenen pay (orijinal piksel).</summary>
    public int OptionPadding { get; set; }

    /// <summary>Soru kutusu, şık kutularının birleşimini kapsayacak kadar genişletilsin mi.</summary>
    public bool ExpandQuestionToOptions { get; set; }

    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }
}
