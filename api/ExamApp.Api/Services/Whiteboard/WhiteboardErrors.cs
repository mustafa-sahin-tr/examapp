using System;

namespace ExamApp.Api.Services.Whiteboard;

/// <summary>
/// Hub'ın istemciye döndüğü hata kodları (issue #98). <c>HubException.Message</c> YALNIZCA bu kodlardan biridir — UI
/// kodu kendi sözlüğünden çevirir; sunucu metni/iç ayrıntı istemciye sızmaz.
/// </summary>
public static class WhiteboardErrorCodes
{
    /// <summary>Token'daki kullanıcı profili çözülemedi.</summary>
    public const string UserNotResolved = "UserNotResolved";

    public const string BookingNotFound = "BookingNotFound";

    /// <summary>Çağıran randevunun öğretmeni ya da öğrencisi değil.</summary>
    public const string NotParticipant = "NotParticipant";

    /// <summary>Öğretmen hesabı onaysız ya da askıda (#287/#289).</summary>
    public const string TeacherNotApproved = "TeacherNotApproved";

    /// <summary>Randevu Approved değil (Pending/Rejected).</summary>
    public const string BookingNotApproved = "BookingNotApproved";

    public const string WindowNotOpen = "WindowNotOpen";
    public const string WindowClosed = "WindowClosed";

    /// <summary>Bu bağlantı verilen booking'in tahtasına <c>JoinBoard</c> ile katılmamış.</summary>
    public const string NotJoined = "NotJoined";

    /// <summary>Tahta kapatıldı (pencere bitti / randevu artık geçerli değil).</summary>
    public const string BoardClosed = "BoardClosed";

    public const string TooManyElementsInMessage = "TooManyElementsInMessage";
    public const string SceneElementLimit = "SceneElementLimit";
    public const string SceneSizeLimit = "SceneSizeLimit";

    /// <summary>
    /// Eleman JSON nesnesi değil; <c>type</c> yok; <c>id</c> (string, 1-64) / <c>version</c> (int ≥ 0) /
    /// <c>versionNonce</c> (int) geçersiz; <c>link</c> http(s) değil ya da &gt; 2048; <c>customData</c> var; ya da
    /// <c>version</c> sıçraması &gt; 10000 (yeni elemanda version &gt; 10000).
    /// </summary>
    public const string InvalidElement = "InvalidElement";

    /// <summary>
    /// Eleman tipi izin listesinde değil. İzinli: rectangle, ellipse, diamond, line, arrow, freedraw, text, frame
    /// (image, embeddable, iframe, magicframe ve bilinmeyen tipler reddedilir).
    /// </summary>
    public const string ElementTypeNotAllowed = "ElementTypeNotAllowed";

    /// <summary>İzinli tipte ama <c>fileId</c> taşıyan eleman (MVP'de görsel yok).</summary>
    public const string ImagesNotSupported = "ImagesNotSupported";

    public const string InvalidPointer = "InvalidPointer";
    public const string RateLimited = "RateLimited";
}

/// <summary><c>BoardClosed(reason)</c> istemci mesajının <c>reason</c> değerleri.</summary>
public static class WhiteboardCloseReasons
{
    /// <summary>Katılım penceresi (bitiş + JoinWindowAfterMinutes) doldu.</summary>
    public const string WindowClosed = "WindowClosed";

    /// <summary>Randevu artık Approved değil ya da silindi.</summary>
    public const string BookingCancelled = "BookingCancelled";

    /// <summary>Öğretmen hesabı askıya alındı / onayı kalktı.</summary>
    public const string TeacherNotApproved = "TeacherNotApproved";

    /// <summary>
    /// Yalnızca bu bağlantıya gönderilir: periyodik yeniden doğrulamada bağlantının yetkisi düştü (temizlik servisi
    /// ya da çağrı anındaki kullanıcıya özgü red). Tahta diğer katılımcı için açık kalabilir.
    /// </summary>
    public const string AccessRevoked = "AccessRevoked";
}

/// <summary>İş kuralı ihlali; hub bunu <c>HubException(Code)</c>'a çevirir.</summary>
public sealed class WhiteboardException : Exception
{
    public WhiteboardException(string code) : base(code) => Code = code;

    public string Code { get; }
}
