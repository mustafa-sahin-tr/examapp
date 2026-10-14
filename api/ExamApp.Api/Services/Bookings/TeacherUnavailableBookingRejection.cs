using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Teachers;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Askıdaki / bağımsız olmayan (#418) öğretmenin otomatik reddedilecek açık talebi (Pending ya da issue #439 ile AwaitingParent;
/// bildirim alanlarıyla).
/// </summary>
internal sealed record PendingBookingRow(
    int Id,
    int TeacherId,
    int TeacherUserId,
    int StudentId,
    int StudentUserId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingStatus Status = BookingStatus.Pending);

/// <summary>
/// issue #298 / #331: askıdaki öğretmenin Pending taleplerini "öğretmen müsait değil" gerekçesiyle reddetme — askıya alma
/// anında (<c>AdminTeacherSuspensionService</c>, askıyla aynı transaction) ve yarış sonrası güvenlik ağında
/// (<see cref="SuspendedTeacherBookingSweepJob"/>) AYNI yol kullanılır.
/// <para>
/// Her satır koşullu UPDATE ile güncellenir: <c>Status == Pending</c> VE öğretmen hâlâ askıda. 0 satır = talep başka bir yoldan
/// karara bağlandı (öğretmen kararı, önceki süpürme turu, eşzamanlı askı akışı) ya da askı kalktı → event YAZILMAZ. Böylece
/// aynı talebe ikinci bildirim gitmez (idempotent). Satır başına tek UPDATE bilinçli: etkilenen id'leri sağlayıcıdan bağımsız
/// (RETURNING'siz) kesin bilmenin yolu bu. Outbox satırları context'e eklenir; SaveChanges/commit çağıranda.
/// </para>
/// </summary>
internal static class TeacherUnavailableBookingRejection
{
    /// <summary>
    /// Talebin öğretmeni talep alamaz: askıda (#298/#331) YA DA bağımsız değil (issue #418 — randevu bağımsız öğretmen
    /// özelliği; okula bağlı/hibrit öğretmende kalmış Pending talepler de kapanır). Bağımsızlık tek kuraldan
    /// (<see cref="TeacherIndependence.Holds"/>) gömülür; süpürme seçimi ve koşullu UPDATE aynı ifadeyi kullanır.
    /// </summary>
    public static readonly Expression<Func<Booking, bool>> TeacherUnavailable = BuildTeacherUnavailable();

    private static Expression<Func<Booking, bool>> BuildTeacherUnavailable()
    {
        var booking = Expression.Parameter(typeof(Booking), "b");
        var teacher = Expression.Property(booking, nameof(Booking.Teacher));
        var suspended = Expression.NotEqual(
            Expression.Property(teacher, nameof(Teacher.AccountSuspendedAt)),
            Expression.Constant(null, typeof(DateTime?)));
        var notIndependent = Expression.Not(PredicateComposer.Inline(TeacherIndependence.Holds, teacher));
        return Expression.Lambda<Func<Booking, bool>>(Expression.OrElse(suspended, notIndependent), booking);
    }

    /// <summary>
    /// issue #439: öğretmen müsait değilken kapatılan açık talepler — Pending ve veli onayı bekleyen AwaitingParent (slotu tutar;
    /// veli onaylasa da öğretmen talep alamaz). Approved randevulara dokunulmaz (#298 bildirim yolu ayrı).
    /// </summary>
    internal static readonly BookingStatus[] OpenStatuses = { BookingStatus.Pending, BookingStatus.AwaitingParent };

    /// <summary>Verilen sorgudaki açık (Pending / AwaitingParent) talepleri bildirim alanlarıyla projekte eder.</summary>
    public static IQueryable<PendingBookingRow> SelectPending(IQueryable<Booking> bookings)
        => bookings
            // issue #376: slotu soft-delete edilmiş (eski veri) Pending talep de reddedilsin; INNER JOIN'de düşmesin.
            .WithSoftDeletedSlots()
            .Where(b => OpenStatuses.Contains(b.Status))
            .Select(b => new PendingBookingRow(
                b.Id,
                b.TeacherId,
                b.Teacher.UserId,
                b.StudentId,
                b.Student.UserId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime,
                b.Status));

    /// <summary>
    /// issue #439 review: kapatılan talebin yeni durumu. Öğretmene ulaşmış Pending → <see cref="BookingStatus.Rejected"/> (eski
    /// davranış); öğretmene hiç ulaşmamış AwaitingParent → <see cref="BookingStatus.Expired"/> (öğretmen listesine/kararına düşmez).
    /// </summary>
    internal static BookingStatus ClosedStatusFor(BookingStatus open)
        => open == BookingStatus.AwaitingParent ? BookingStatus.Expired : BookingStatus.Rejected;

    /// <summary>
    /// Talepleri koşullu olarak kapatır (Pending → Rejected, issue #439: AwaitingParent → Expired; gerekçe null, <c>DecisionAt</c>/<c>UpdateTime</c> = <paramref name="nowUtc"/>,
    /// <c>UpdateUserId</c> = <paramref name="updateUserId"/>) ve YALNIZCA gerçekten değişen satırlar için öğrenciye
    /// <see cref="BookingDecisionEvent"/> (<c>TeacherUnavailable=true</c>) outbox'ı ekler. Gerçekten reddedilen satırları döner.
    /// issue #439: <paramref name="parentUserIdsByStudent"/> (öğrenci id → Active veli user id'leri) verilirse event'in
    /// <c>Parents</c> alanı dolar; <paramref name="userKeycloakIdOf"/> öğrenci ve veli user id'leri için sub döner (yoksa boş).
    /// </summary>
    public static async Task<IReadOnlyList<PendingBookingRow>> RejectAsync(
        AppDbContext context,
        IEnumerable<PendingBookingRow> rows,
        DateTime nowUtc,
        int? updateUserId,
        Func<int, string> teacherNameOf,
        Func<int, string> userKeycloakIdOf,
        IReadOnlyDictionary<int, List<int>>? parentUserIdsByStudent = null,
        CancellationToken ct = default,
        Func<int, string>? studentNameOf = null,
        bool requireTeacherUnavailable = true)
    {
        var rejected = new List<PendingBookingRow>();
        foreach (var booking in rows)
        {
            var id = booking.Id;
            // Satırın OKUNDUĞU durum koşul: arada veli onayıyla AwaitingParent → Pending olduysa 0 satır (bir sonraki tur görür).
            var readStatus = booking.Status;
            var closedStatus = ClosedStatusFor(readStatus);
            var query = context.Bookings.Where(b => b.Id == id && b.Status == readStatus && OpenStatuses.Contains(b.Status));
            // issue #439 review: öğretmenin slotu silmesi (yalnız AwaitingParent tutuyorsa) askı koşulu olmadan kapatır.
            if (requireTeacherUnavailable)
                query = query.Where(TeacherUnavailable);
            var updated = await query
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, closedStatus)
                    .SetProperty(b => b.DecisionAt, nowUtc)
                    .SetProperty(b => b.RejectionReason, (string?)null)
                    .SetProperty(b => b.UpdateTime, nowUtc)
                    .SetProperty(b => b.UpdateUserId, updateUserId), ct);
            if (updated == 0)
                continue;

            rejected.Add(booking);
            var @event = new BookingDecisionEvent
            {
                BookingId = booking.Id,
                TeacherId = booking.TeacherId,
                TeacherName = teacherNameOf(booking.TeacherUserId),
                StudentId = booking.StudentId,
                StudentUserId = booking.StudentUserId,
                StudentName = studentNameOf?.Invoke(booking.StudentUserId) ?? string.Empty,
                TargetKeycloakId = userKeycloakIdOf(booking.StudentUserId),
                Approved = false,
                RejectionReason = null,
                TeacherUnavailable = true,
                Date = booking.Date,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                DecidedAt = nowUtc,
                Parents = parentUserIdsByStudent != null && parentUserIdsByStudent.TryGetValue(booking.StudentId, out var parentIds)
                    ? BookingParentApproval.Recipients(parentIds, userKeycloakIdOf)
                    : new List<BookingParentRecipient>()
            };
            context.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<BookingDecisionEvent>(),
                Content = JsonSerializer.Serialize(@event),
                CreatedAt = nowUtc
            });
        }

        return rejected;
    }
}
