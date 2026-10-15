using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Bookings;

/// <summary>issue #439: öğrencinin randevu talebinin hangi yoldan açılacağı.</summary>
internal enum StudentBookingParentMode
{
    /// <summary>Veli kuralına tabi değil (yetişkin rolü olan hesap) → eski akış: doğrudan Pending + öğretmene bildirim.</summary>
    NotRequired,

    /// <summary>Veli gerekiyor ve Active velisi var → AwaitingParent + velilere bildirim.</summary>
    AwaitingParent,

    /// <summary>Veli gerekiyor ama Active velisi yok → 403 <c>ParentRequired</c>.</summary>
    ParentMissing
}

/// <summary>
/// issue #439: randevu talebinde veli onayı kuralları — TEK yer. Hangi hesabın veliye tabi olduğu
/// <see cref="ParentRequirement"/>'tadır (controller principal'dan hesaplar); burada "veliye tabi öğrenci şu an talep açabilir mi,
/// hangi durumla" kararı, onay penceresi (7 gün) ve süre aşımı koşulu durur.
/// </summary>
internal static class BookingParentApproval
{
    /// <summary>Veli onayı penceresi: AwaitingParent talep oluşturmadan bu kadar sonra (ya da ders saati gelince) Expired olur.</summary>
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Öğrencinin talebi hangi yoldan açılır. <paramref name="requiresParent"/> = <see cref="ParentRequirement.RequiresParent(System.Security.Claims.ClaimsPrincipal)"/>.
    /// Veliye tabi öğrencinin en az bir Active (koparılmamış, silinmemiş) velisi yoksa talep açamaz.
    /// <para>#441 KARARI: hoşgörü süresi booking'i AÇMAZ — süre içindeki velisiz öğrenci de <see cref="StudentBookingParentMode.ParentMissing"/>
    /// (403 <c>ParentRequired</c>) alır; özel ders her zaman veli onayından geçer. #440: aktivasyon bekleyen öğrenci bu metoda gelmeden
    /// <c>ParentActivated</c> policy'sinde 403 <c>ParentActivationRequired</c> alır; süresi dolmuş öğrenci <c>ParentRequiredMiddleware</c>'de
    /// 403 <c>ParentRequired</c>.</para>
    /// </summary>
    public static async Task<StudentBookingParentMode> ResolveStudentModeAsync(
        AppDbContext context, int studentId, bool requiresParent, CancellationToken ct)
    {
        if (!requiresParent)
            return StudentBookingParentMode.NotRequired;

        var hasActiveParent = await context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == studentId)
            .Where(ParentChildAccess.GrantsAccess)
            .AnyAsync(ct);
        return hasActiveParent ? StudentBookingParentMode.AwaitingParent : StudentBookingParentMode.ParentMissing;
    }

    /// <summary>Talebin otomatik Expired olacağı an: oluşturma + <see cref="ApprovalWindow"/> ya da ders başlangıcı, hangisi önceyse.</summary>
    public static DateTime ExpiresAt(DateTime createdAtUtc, DateOnly date, TimeOnly startTime)
    {
        var byWindow = createdAtUtc + ApprovalWindow;
        var lessonStart = SlotTimeRange.ToUtc(date, startTime);
        return byWindow < lessonStart ? byWindow : lessonStart;
    }

    public static bool IsExpired(DateTime createdAtUtc, DateOnly date, TimeOnly startTime, DateTime nowUtc)
        => ExpiresAt(createdAtUtc, date, startTime) <= nowUtc;

    /// <summary>
    /// SQL'e çevrilebilir süre aşımı koşulu (<see cref="IsExpired"/> ile aynı): oluşturma + 7 gün geçti YA DA ders başladı.
    /// Slot saatleri UTC duvar saatidir (gün + saat ayrı kolon).
    /// </summary>
    public static Expression<Func<Booking, bool>> ExpiredAt(DateTime nowUtc)
    {
        var createdCutoff = nowUtc - ApprovalWindow;
        var today = DateOnly.FromDateTime(nowUtc);
        var timeNow = TimeOnly.FromDateTime(nowUtc);
        return b => b.CreatedAt <= createdCutoff
            || b.AvailabilitySlot.Date < today
            || (b.AvailabilitySlot.Date == today && b.AvailabilitySlot.StartTime <= timeNow);
    }

    /// <summary><see cref="ExpiredAt"/>'ın tersi: hâlâ karar verilebilir AwaitingParent talep (veli listesi).</summary>
    public static Expression<Func<Booking, bool>> OpenAt(DateTime nowUtc)
    {
        var createdCutoff = nowUtc - ApprovalWindow;
        var today = DateOnly.FromDateTime(nowUtc);
        var timeNow = TimeOnly.FromDateTime(nowUtc);
        return b => b.CreatedAt > createdCutoff
            && (b.AvailabilitySlot.Date > today
                || (b.AvailabilitySlot.Date == today && b.AvailabilitySlot.StartTime > timeNow));
    }

    /// <summary>
    /// Öğrencilerin Active bağlantılı (silinmemiş) velilerinin user id'leri (öğrenci id → veli user id'leri). Bildirim alıcıları
    /// için kilitsiz ön okuma; bağlantı durum makinesi Revoked'a çekerken RevokedAt'i de yazar.
    /// </summary>
    public static async Task<Dictionary<int, List<int>>> ActiveParentUserIdsByStudentAsync(
        AppDbContext context, IEnumerable<int> studentIds, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, List<int>>();

        var rows = await context.ParentStudentLinks.AsNoTracking()
            .Where(l => ids.Contains(l.StudentId))
            .Where(ParentChildAccess.GrantsAccess)
            .Select(l => new { l.StudentId, ParentUserId = l.Parent.UserId })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ParentUserId).Distinct().OrderBy(id => id).ToList());
    }

    /// <summary>
    /// issue #439 review: <see cref="ActiveParentUserIdsByStudentAsync"/>'ın transaction İÇİNDE kilitli sürümü. PostgreSQL'de önce
    /// ilgili Active bağlantı satırları <c>FOR SHARE</c> ile kilitlenir (koparma = koşullu UPDATE → satır kilidi; önce gelen
    /// koparmanın commit'i beklenir), ardından aynı transaction'da <see cref="ParentChildAccess.GrantsAccess"/> ile güncel durum
    /// okunur → koparılmış / silinmiş veliye bildirim yazılmaz (ParentHomeworkOverdueSweepJob / #423 security M1 deseni).
    /// Postgres dışında (SQLite birim testleri) kilitsiz aynı sorgu.
    /// </summary>
    public static async Task<Dictionary<int, List<int>>> ActiveParentUserIdsLockedAsync(
        AppDbContext context, IEnumerable<int> studentIds, CancellationToken ct)
    {
        var ids = studentIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, List<int>>();

        if (context.Database.IsNpgsql())
        {
            var active = nameof(ParentStudentLinkStatus.Active);
            await context.Database
                .SqlQuery<int>($"SELECT l.\"Id\" AS \"Value\" FROM \"ParentStudentLinks\" l WHERE l.\"StudentId\" = ANY({ids}) AND l.\"Status\" = {active} FOR SHARE OF l")
                .ToListAsync(ct);
        }

        return await ActiveParentUserIdsByStudentAsync(context, ids, ct);
    }

    /// <summary>issue #439 review: bir öğrencinin aynı anda açık (karar verilebilir) AwaitingParent talep tavanı.</summary>
    public const int MaxOpenAwaitingParentPerStudent = 3;

    /// <summary>issue #439 review: bir öğrencinin AYNI öğretmen için aynı anda açık AwaitingParent talep tavanı.</summary>
    public const int MaxOpenAwaitingParentPerTeacher = 1;

    /// <summary>Velinin ret notu üst sınırı (DTO doğrulamasıyla aynı).</summary>
    public const int NoteMaxLength = 500;

    /// <summary>
    /// issue #439 review: veli ret notunun temizliği — #105 yorum kuralı (<see cref="CommentBodySanitizer"/>): NUL dahil C0/C1
    /// kontrol karakterleri (\t \n hariç) reddedilir (false), bidi override/isolate ve diğer biçim karakterleri ayıklanır.
    /// Boş/yalnız görünmez karakter → null not (geçerli).
    /// </summary>
    public static bool TrySanitizeNote(string? note, out string? sanitized)
    {
        sanitized = null;
        if (string.IsNullOrWhiteSpace(note))
            return true;

        switch (CommentBodySanitizer.TrySanitize(note, NoteMaxLength, out var cleaned))
        {
            case CommentBodySanitizer.Outcome.Ok:
                sanitized = cleaned;
                return true;
            case CommentBodySanitizer.Outcome.Required:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Veli user id'lerinden event alıcı listesi (sub sözlükte yoksa boş).</summary>
    public static List<BookingParentRecipient> Recipients(IEnumerable<int> parentUserIds, Func<int, string> subOf)
        => parentUserIds
            .Distinct()
            .Select(id => new BookingParentRecipient { ParentUserId = id, KeycloakId = subOf(id) ?? string.Empty })
            .ToList();
}
