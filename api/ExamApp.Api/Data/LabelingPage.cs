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

    /// <summary>
    /// Sayfanın soruları soru bankasına (teste bağlanmadan) en son ne zaman başarıyla kaydedildi; null = hiç. Aynı sayfanın
    /// ikinci kaydı bu alanla uyarılır (issue #470).
    /// </summary>
    public DateTime? BankSavedAt { get; set; }

    /// <summary>
    /// Bankaya kayıt sürüyor (başlangıç anı); bitince null. Doluyken eşzamanlı ikinci kayıt (<c>Force</c> dahil) reddedilir;
    /// süreç kayıt ortasında öldüyse işaret bir süre sonra bayat sayılır.
    /// </summary>
    public DateTime? BankSaveStartedAt { get; set; }

    [MaxLength(64)]
    public string? BankSavedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? BankSavedByName { get; set; }

    /// <summary>Son bankaya kayıtta oluşturulan soru sayısı.</summary>
    public int? BankQuestionCount { get; set; }

    public List<LabelingResult> Results { get; set; } = new();
}
