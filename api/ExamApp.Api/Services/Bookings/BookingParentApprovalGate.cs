using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Bookings;

/// <summary>issue #439: veli onayının hangi yoldan geldiği.</summary>
public enum BookingParentApprovalKind
{
    /// <summary>Veli, çocuğun AwaitingParent talebini onaylıyor (<c>BookingId</c> dolu).</summary>
    ApproveRequest,

    /// <summary>Veli, çocuk için doğrudan talep oluşturuyor (henüz booking yok, <c>BookingId</c> null).</summary>
    DirectCreate
}

/// <summary>issue #439: ödeme/yetkilendirme kapısına verilen bağlam (#408 ödeme yetkilendirmesi bunlarla karar verecek).</summary>
public sealed record BookingParentApprovalContext(
    BookingParentApprovalKind Kind,
    int? BookingId,
    int StudentId,
    int TeacherId,
    int AvailabilitySlotId,
    int ParentUserId);

/// <summary>
/// Kapı sonucu. <see cref="Allowed"/> false ise durum değişmez ve istemciye 409 + <see cref="ErrorCode"/> döner;
/// <see cref="MessageKey"/> mesaj sözlüğü anahtarıdır (ileride ör. "booking.parent.paymentRequired").
/// </summary>
public sealed record BookingParentApprovalGateResult(bool Allowed, string? ErrorCode = null, string? MessageKey = null)
{
    public static readonly BookingParentApprovalGateResult Allow = new(true);
}

/// <summary>
/// issue #439 — ödeme kancası: veli onayı (talebi onaylama ve doğrudan oluşturma) TEK bu kapıdan geçer; çağrı durum
/// değişikliğinden (transaction) ÖNCE yapılır. #408 ödeme yetkilendirmesi buraya eklenecek (ör. ön provizyon); şimdilik
/// <see cref="NoOpBookingParentApprovalGate"/> her zaman izin verir.
/// </summary>
public interface IBookingParentApprovalGate
{
    Task<BookingParentApprovalGateResult> AuthorizeAsync(BookingParentApprovalContext context, CancellationToken ct = default);
}

/// <summary>issue #439: ödeme henüz yok (#408) → her veli onayına izin verir.</summary>
public sealed class NoOpBookingParentApprovalGate : IBookingParentApprovalGate
{
    public static readonly NoOpBookingParentApprovalGate Instance = new();

    public Task<BookingParentApprovalGateResult> AuthorizeAsync(BookingParentApprovalContext context, CancellationToken ct = default)
        => Task.FromResult(BookingParentApprovalGateResult.Allow);
}
