using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.ParentDashboard;

/// <summary>
/// Issue #421 (epic #407 V3): velinin gördüğü atama kovası — V2 özetindeki sayılarla aynı üç kova
/// (<c>ParentAssignmentBucket</c>). Hem yanıtta (<see cref="ParentChildAssignmentItemDto.Status"/>) hem <c>?status=</c>
/// filtresinde bu değerler kullanılır.
/// </summary>
public static class ParentAssignmentStatuses
{
    /// <summary>Tamamlandı.</summary>
    public const string Completed = "completed";

    /// <summary>Gecikti: teslim tarihi geçti ve bitirilmedi, ya da oturumun süresi doldu (tekrar çözülemez).</summary>
    public const string Overdue = "overdue";

    /// <summary>Bekliyor: süresi devam ediyor, başlanmadı ya da devam ediyor.</summary>
    public const string Pending = "pending";
}

/// <summary>
/// Issue #421: <c>GET api/parent/children/{studentId}/assignments?status=&amp;page=</c>. Kapsam V2 özetiyle aynı (açık ödevler +
/// son <see cref="ParentChildAssignmentCountsDto.WindowDays"/> günde teslim tarihi geçenler); worksheet başına tek satır,
/// en yeni teslim tarihi önce (teslim tarihsiz açık ödevler en üstte).
/// </summary>
public sealed class ParentChildAssignmentListDto
{
    public int StudentId { get; set; }

    /// <summary>Uygulanan filtre (<see cref="ParentAssignmentStatuses"/>); filtresizse null.</summary>
    public string? Status { get; set; }

    /// <summary>1 tabanlı sayfa.</summary>
    public int Page { get; set; }

    public int PageSize { get; set; }

    /// <summary>Filtre uygulandıktan sonraki toplam satır.</summary>
    public int TotalCount { get; set; }

    /// <summary>Filtreden bağımsız kova sayıları (çiplerdeki sayılar; V2 özet kartıyla aynı).</summary>
    public ParentChildAssignmentCountsDto Counts { get; set; } = new();

    public List<ParentChildAssignmentItemDto> Items { get; set; } = new();
}

/// <summary>
/// Listenin bir satırı. Yalnızca başlık/ders/öğretmen adı, tarihler, kova ve sonuç SAYILARI — soru metni/görseli, cevap
/// anahtarı, doğru şık ya da çocuğun seçtiği şık YOK. Yeni alan eklerken bu kural korunmalı (alan listesi
/// ParentAssignmentServiceTests ve ParentAssignmentEndpointsTests'te kilitli).
/// </summary>
public sealed class ParentChildAssignmentItemDto
{
    public int WorksheetId { get; set; }

    /// <summary>Worksheet adı.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Ders adı; worksheet'e ders atanmamışsa null.</summary>
    public string? Subject { get; set; }

    /// <summary>Atamayı yapan öğretmenin görünen adı (auth-api'den, en iyi çaba); çözülemezse null.</summary>
    public string? TeacherName { get; set; }

    /// <summary>Atamanın başlangıcı (UTC).</summary>
    public DateTime StartAt { get; set; }

    /// <summary>Son teslim tarihi (UTC); açık uçlu atamada null.</summary>
    public DateTime? Deadline { get; set; }

    /// <summary><see cref="ParentAssignmentStatuses"/>.</summary>
    public string Status { get; set; } = ParentAssignmentStatuses.Pending;

    /// <summary>
    /// Sonucu olan (bitmiş) test oturumunun id'si — özet ucu <c>test-results/{testInstanceId}</c> için. Tamamlanan ve süresi
    /// dolan testlerde dolu; başlanmamış/devam eden işte null.
    /// </summary>
    public int? TestInstanceId { get; set; }

    /// <summary><see cref="TestInstanceId"/> doluysa sonuç sayıları.</summary>
    public ParentTestScoreDto? Result { get; set; }
}

/// <summary>Bir test oturumunun sonuç sayıları — öğrencinin kendi sonuç ekranındaki formülle aynı.</summary>
public sealed class ParentTestScoreDto
{
    /// <summary>Doğru / toplam soru × 100 (aşağı yuvarlanır).</summary>
    public int ScorePercent { get; set; }

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    /// <summary>Boş bırakılan soru.</summary>
    public int BlankCount { get; set; }

    public int TotalCount { get; set; }

    /// <summary>Bitiş − başlangıç (saniye), DAKİKAYA AŞAĞI yuvarlanmış (#421 review); bitiş anı yoksa 0.</summary>
    public int DurationSeconds { get; set; }
}

/// <summary>
/// Issue #421: <c>GET api/parent/children/{studentId}/test-results/{testInstanceId}</c> — çocuğun bitmiş bir test oturumunun
/// özeti. Yalnızca SAYILAR, zaman damgaları ve konu adları: soru metni/görseli, cevap anahtarı, doğru şık ya da çocuğun
/// seçtiği şık YOK (alan listesi testlerde kilitli).
/// </summary>
public sealed class ParentChildTestResultDto
{
    public int StudentId { get; set; }

    public int TestInstanceId { get; set; }

    public int WorksheetId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subject { get; set; }

    /// <summary><c>completed</c> ya da süre sınırıyla kapanan oturumda <c>timedOut</c>.</summary>
    public string Outcome { get; set; } = ParentTestOutcomes.Completed;

    /// <summary>Oturumun başlangıcı (UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Oturumun bitişi (UTC).</summary>
    public DateTime? FinishedAt { get; set; }

    public ParentTestScoreDto Score { get; set; } = new();

    /// <summary>Konu bazında doğru/yanlış/boş (en çok sorulu konu önce). Konusu atanmamış sorular tek grupta.</summary>
    public List<ParentTestTopicResultDto> Topics { get; set; } = new();
}

/// <summary><see cref="ParentChildTestResultDto.Outcome"/> değerleri.</summary>
public static class ParentTestOutcomes
{
    public const string Completed = "completed";
    public const string TimedOut = "timedOut";
}

/// <summary>Konu bazında sonuç sayıları.</summary>
public sealed class ParentTestTopicResultDto
{
    /// <summary>Konu id'si; konusu atanmamış sorular için null.</summary>
    public int? TopicId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int CorrectCount { get; set; }

    public int WrongCount { get; set; }

    public int BlankCount { get; set; }

    public int TotalCount { get; set; }
}
