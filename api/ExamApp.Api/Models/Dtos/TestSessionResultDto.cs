namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #367: <c>save-answer</c> / <c>end-test</c> yanıtı — <see cref="ResponseBaseDto"/> + makine tarafından okunabilir hata
/// kodu. JSON: <c>{ "success": false, "conflict": true, "errorCode": "TestNotInProgress", "message": "..." }</c>
/// (mesaj istek diline göre). Başarıda <see cref="ErrorCode"/> null.
/// </summary>
public class TestSessionResultDto : ResponseBaseDto
{
    /// <summary>Hata durumunda UI özel davranışı için kod — bkz. <see cref="TestSessionErrorCodes"/>. Başarıda null.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// issue #396: <see cref="ErrorCode"/>'un alt nedeni (sözleşme aynı kalır: ErrorCode yine TestNotInProgress). Şimdilik
    /// yalnız <see cref="TestSessionRejectReasons.TimeExpired"/> — oturum süre sınırı yüzünden Expired. Diğer durumlarda null.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// issue #571: istek yazmadan önce reddedildi (bozuk/geçersiz etkileşim cevabı ya da kontrol isteği) — controller 400'e eşler.
    /// </summary>
    public bool InvalidInput { get; set; }
}

public static class TestSessionRejectReasons
{
    /// <summary>issue #396: oturum süre sınırı (+ tolerans) dolduğu için Expired; UI "süre doldu" mesajı gösterir.</summary>
    public const string TimeExpired = "TimeExpired";
}

public static class TestSessionErrorCodes
{
    /// <summary>
    /// 409 — test oturumu artık <c>Started</c> değil (Completed/Expired). <c>save-answer</c>: cevap yazılmadı, outbox event'i
    /// üretilmedi. <c>end-test</c>: yalnız Expired için (Completed'a tekrar end-test idempotent başarıdır).
    /// </summary>
    public const string TestNotInProgress = "TestNotInProgress";

    /// <summary>
    /// issue #571: 400 — etkileşim cevabı geçersiz (ayrıştırılamıyor, tip uyuşmuyor, 16 KB'tan büyük, plan dışı hedef/kart id'si,
    /// aynı hedef/kart iki kez) ya da seçilen şık sorunun şıkkı değil. Hiçbir şey yazılmadı (AnswerRevision artmadı).
    /// </summary>
    public const string InvalidAnswerPayload = "InvalidAnswerPayload";

    /// <summary>issue #571: 400 — kontrol isteği geçersiz (boş, çok madde, tekrarlanan ya da oturuma ait olmayan madde).</summary>
    public const string InvalidCheckRequest = "InvalidCheckRequest";

    /// <summary>issue #571: 409 — kontrol / "doğru cevapları göster" yalnız etkinlikte (<c>Worksheet.Kind = Activity</c>).</summary>
    public const string NotAnActivity = "NotAnActivity";

    /// <summary>issue #571: 409 — "doğru cevapları göster" en az bir kontrolden sonra açılır.</summary>
    public const string ActivityCheckRequired = "ActivityCheckRequired";
}
