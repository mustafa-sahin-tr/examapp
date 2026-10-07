using System;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #423 (epic #407 V5): "bu (test, öğrenci) için gecikmiş ödev bildirimi işlendi" işaretçisi. Süpürücü job
/// (<c>ParentHomeworkOverdueSweepJob</c>) bunu, <c>ParentHomeworkOverdueEvent</c> outbox satırlarıyla AYNI transaction'da
/// yazar; <c>(WorksheetId, StudentId)</c> tekil index'i "test ve çocuk başına en fazla bir kez (aynı test birden çok kez atansa da)" kuralının (job tekrar
/// çalışsa, iki örnek yarışsa da) veritabanı seviyesindeki garantisidir. FK yok: atama/öğrenci silinse de kayıt kalır (tekrar
/// bildirim üretilmez). PII yok. Çok eski işaretçiler job'ın geri bakış penceresi dışında kaldığından zararsız birikir; saklama
/// temizliği ayrı iştir.
/// </summary>
public class ParentHomeworkOverdueMarker
{
    public long Id { get; set; }

    public int WorksheetId { get; set; }

    public int StudentId { get; set; }

    /// <summary>İşlendiği an (UTC).</summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>Bu işlemde event yazılan Active veli sayısı (tanı amaçlı).</summary>
    public int NotifiedParentCount { get; set; }
}
