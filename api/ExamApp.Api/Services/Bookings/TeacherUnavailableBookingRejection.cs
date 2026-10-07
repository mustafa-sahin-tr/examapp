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

/// <summary>Askıdaki / bağımsız olmayan (#418) öğretmenin otomatik reddedilecek Pending talebi (bildirim alanlarıyla).</summary>
internal sealed record PendingBookingRow(
    int Id,
    int TeacherId,
    int TeacherUserId,
    int StudentId,
    int StudentUserId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime);

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

    /// <summary>Verilen sorgudaki Pending talepleri bildirim alanlarıyla projekte eder.</summary>
    public static IQueryable<PendingBookingRow> SelectPending(IQueryable<Booking> bookings)
        => bookings
            // issue #376: slotu soft-delete edilmiş (eski veri) Pending talep de reddedilsin; INNER JOIN'de düşmesin.
            .WithSoftDeletedSlots()
            .Where(b => b.Status == BookingStatus.Pending)
            .Select(b => new PendingBookingRow(
                b.Id,
                b.TeacherId,
                b.Teacher.UserId,
                b.StudentId,
                b.Student.UserId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime));

    /// <summary>
    /// Talepleri koşullu olarak Rejected yapar (gerekçe null, <c>DecisionAt</c>/<c>UpdateTime</c> = <paramref name="nowUtc"/>,
    /// <c>UpdateUserId</c> = <paramref name="updateUserId"/>) ve YALNIZCA gerçekten değişen satırlar için öğrenciye
    /// <see cref="BookingDecisionEvent"/> (<c>TeacherUnavailable=true</c>) outbox'ı ekler. Gerçekten reddedilen satırları döner.
    /// </summary>
    public static async Task<IReadOnlyList<PendingBookingRow>> RejectAsync(
        AppDbContext context,
        IEnumerable<PendingBookingRow> rows,
        DateTime nowUtc,
        int? updateUserId,
        Func<int, string> teacherNameOf,
        Func<int, string> studentKeycloakIdOf,
        CancellationToken ct = default)
    {
        var rejected = new List<PendingBookingRow>();
        foreach (var booking in rows)
        {
            var id = booking.Id;
            var updated = await context.Bookings
                .Where(b => b.Id == id && b.Status == BookingStatus.Pending)
                .Where(TeacherUnavailable)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, BookingStatus.Rejected)
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
                TargetKeycloakId = studentKeycloakIdOf(booking.StudentUserId),
                Approved = false,
                RejectionReason = null,
                TeacherUnavailable = true,
                Date = booking.Date,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                DecidedAt = nowUtc
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
