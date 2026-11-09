using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>İki taraflı denetimde kimin planı engelledi.</summary>
public enum SharedEntitlementBlock
{
    None,
    /// <summary>Çağıranın planında yok ya da sınırı doldu: çağıran yükseltir.</summary>
    Self,
    /// <summary>Karşı tarafın planı izin vermiyor: çağıran bir şey yapamaz, karşı taraf (öğrencide velisi) yükseltir.</summary>
    Counterpart,
}

public sealed record SharedEntitlementDecision(SharedEntitlementBlock Block, EntitlementCheck SelfCheck, EntitlementCheck? CounterpartCheck)
{
    public bool Allowed => Block == SharedEntitlementBlock.None;
}

public static class SharedEntitlementMessages
{
    /// <summary>Karşı taraf mesajının sözlük anahtarı: <c>entitlements.counterpart.{teacher|student}.{feature}</c>.</summary>
    public static string Key(bool counterpartIsTeacher, string featureKey)
    {
        var feature = featureKey switch
        {
            EntitlementFeatures.Messaging => "messaging",
            EntitlementFeatures.Tutoring => "tutoring",
            EntitlementFeatures.LiveLesson => "liveLesson",
            _ => throw new ArgumentOutOfRangeException(nameof(featureKey), featureKey, null),
        };
        return $"entitlements.counterpart.{(counterpartIsTeacher ? "teacher" : "student")}.{feature}";
    }
}

/// <summary>
/// Issue #1088: iki tarafın da planında olması gereken özellikler (<c>messaging</c>, <c>tutoring</c>, <c>live-lesson</c>). Öğrenci
/// öğretmene ancak ikisinin de planında varsa yazar, randevu alır ya da derse katılır. Önce çağıran denetlenir (kendi planını
/// yükseltmesi gerektiğini görsün), sonra karşı taraf. Karşı tarafın rolleri bilinmez: rol muafiyeti yalnız çağırana uygulanır.
/// </summary>
public interface ISharedEntitlementGate
{
    /// <param name="selfReference">
    /// Sayılan özellikte bu işin kullanım anahtarı (ör. aynı derse yeniden katılım). Kayıtlıysa dolu sınır o tarafı engellemez;
    /// planda olmaması (sınır 0) yine engeller.
    /// </param>
    Task<SharedEntitlementDecision> CheckAsync(string featureKey, EntitlementSubject self, int counterpartUserId, CancellationToken ct = default,
        string? selfReference = null, string? counterpartReference = null);

    // Karşı tarafın exam kullanıcı id'si. Bulunamazsa null: uç noktanın kendi denetimi (404 / 403) yanıt verir.

    /// <param name="teacherId"><c>Teachers.Id</c>.</param>
    Task<int?> TeacherUserIdAsync(int teacherId, CancellationToken ct = default);

    /// <summary>Konuşmanın öbür tarafı; çağıran konuşmanın tarafı değilse null.</summary>
    Task<int?> ConversationCounterpartAsync(int conversationId, int userId, CancellationToken ct = default);

    Task<int?> SlotTeacherUserIdAsync(int availabilitySlotId, CancellationToken ct = default);

    Task<BookingParticipants?> BookingParticipantsAsync(int bookingId, CancellationToken ct = default);
}

public sealed record BookingParticipants(int TeacherUserId, int StudentUserId);

public sealed class SharedEntitlementGate(IEntitlementService entitlements, AppDbContext db) : ISharedEntitlementGate
{
    public async Task<SharedEntitlementDecision> CheckAsync(string featureKey, EntitlementSubject self, int counterpartUserId,
        CancellationToken ct = default, string? selfReference = null, string? counterpartReference = null)
    {
        var selfCheck = await entitlements.CheckAsync(self, featureKey, ct);
        if (!await AllowedAsync(featureKey, selfCheck, selfReference, ct))
            return new SharedEntitlementDecision(SharedEntitlementBlock.Self, selfCheck, null);

        var counterpartCheck = await entitlements.CheckAsync(EntitlementSubject.Create(counterpartUserId), featureKey, ct);
        return new SharedEntitlementDecision(
            await AllowedAsync(featureKey, counterpartCheck, counterpartReference, ct) ? SharedEntitlementBlock.None : SharedEntitlementBlock.Counterpart,
            selfCheck, counterpartCheck);
    }

    private async Task<bool> AllowedAsync(string featureKey, EntitlementCheck check, string? reference, CancellationToken ct) =>
        check.Allowed
        || (check.Limit > 0 && reference != null
            && await db.EntitlementUsages.AsNoTracking().AnyAsync(u => u.FeatureKey == featureKey && u.ReferenceKey == reference, ct));

    public Task<int?> TeacherUserIdAsync(int teacherId, CancellationToken ct = default) =>
        db.Teachers.AsNoTracking().Where(t => t.Id == teacherId).Select(t => (int?)t.UserId).FirstOrDefaultAsync(ct);

    public Task<int?> ConversationCounterpartAsync(int conversationId, int userId, CancellationToken ct = default) =>
        db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId && (c.StudentUserId == userId || c.TeacherUserId == userId))
            .Select(c => (int?)(c.StudentUserId == userId ? c.TeacherUserId : c.StudentUserId))
            .FirstOrDefaultAsync(ct);

    public Task<int?> SlotTeacherUserIdAsync(int availabilitySlotId, CancellationToken ct = default) =>
        db.TeacherAvailabilitySlots.AsNoTracking()
            .Where(s => s.Id == availabilitySlotId)
            .Select(s => (int?)s.Teacher.UserId)
            .FirstOrDefaultAsync(ct);

    public Task<BookingParticipants?> BookingParticipantsAsync(int bookingId, CancellationToken ct = default) =>
        db.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new BookingParticipants(b.Teacher.UserId, b.Student.UserId))
            .FirstOrDefaultAsync(ct);
}
