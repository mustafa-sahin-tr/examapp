using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// İşaretleme ekranındaki bir sayfa görseli. Kimlik ham görsel baytlarının SHA-256'sıdır; question-detector'ın
/// <c>dataset_store.content_id</c>'si bu hash'in ilk 16 hanesidir (eşleşir). Görselin kendisi DB'ye yazılmaz.
/// </summary>
public class LabelingPage
{
    public int Id { get; set; }

    /// <summary>Ham görsel baytlarının SHA-256'sı, küçük harf hex (64 karakter).</summary>
    [MaxLength(64)]
    public string ImageSha256 { get; set; } = string.Empty;

    /// <summary>Orijinal görsel boyutu (piksel); kutular bu uzaydadır.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<LabelingResult> Results { get; set; } = new();
}
