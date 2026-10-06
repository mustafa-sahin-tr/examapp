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
}
