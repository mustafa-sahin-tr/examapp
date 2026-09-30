using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #289. <see cref="AdminStudentSchoolService"/> (#277) ile aynı audit ve koşullu güncelleme deseni. Keycloak'a
/// gidilmez (hesap devre dışı bırakma #155 ayrı akış) — bu yüzden hedef çözücü (<c>IAdminAccountTargetResolver</c>) ve
/// upstream hata yolu yoktur. Öğretmen yetkisi DB'den (<c>IApprovedTeacherGuard</c>) her istekte okunduğu için profil
/// önbelleğini düşürmek gerekmez; askı bir sonraki istekte etkilidir.
/// Loglar yalnızca Teacher.Id ve aktör sub'ını içerir; neden (serbest metin) loglanmaz ve audit'e yazılmaz.
/// <para>
/// issue #298 yan etkileri (askı commit'iyle AYNI transaction'da): öğretmenin Pending randevu talepleri otomatik
/// reddedilir (her biri için <see cref="BookingDecisionEvent"/> <c>TeacherUnavailable=true</c>) ve henüz bitmemiş Approved
/// randevusu olan her öğrenciye TEK <see cref="BookingTeacherUnavailableEvent"/> yazılır. Approved randevular iptal
/// edilmez (#315). Commit'ten SONRA öğretmenin açık çizim tahtaları hemen kapatılır (#311); bu adım başarısız olursa
/// askı geri alınmaz, loglanır — <see cref="WhiteboardCleanupService"/> bir sonraki turda yine kapatır.
/// </para>
/// </summary>
public class AdminTeacherSuspensionService : IAdminTeacherSuspensionService
{
    public const int ReasonMaxLength = 500;

    private readonly AppDbContext _context;
    private readonly IAdminUserActionAuditService _audit;
    private readonly ILogger<AdminTeacherSuspensionService> _logger;

    // issue #298: DI her zaman verir. DI'siz kurulan (birim test) örneklerde null → auth-api lookup'ı atlanır (event
    // alanları boş), tahta anlık kapatma atlanır; saat sistem saatidir.
    private readonly IAuthApiClient? _authApiClient;
    private readonly TimeProvider _timeProvider;
    private readonly IWhiteboardStore? _whiteboardStore;
    private readonly IWhiteboardSessionCloser? _whiteboardCloser;

    public AdminTeacherSuspensionService(
        AppDbContext context,
        IAdminUserActionAuditService audit,
        ILogger<AdminTeacherSuspensionService>? logger = null,
        IAuthApiClient? authApiClient = null,
        TimeProvider? timeProvider = null,
        IWhiteboardStore? whiteboardStore = null,
        IWhiteboardSessionCloser? whiteboardCloser = null)
    {
        _context = context;
        _audit = audit;
        _logger = logger ?? NullLogger<AdminTeacherSuspensionService>.Instance;
        _authApiClient = authApiClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _whiteboardStore = whiteboardStore;
        _whiteboardCloser = whiteboardCloser;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AdminTeacherSuspensionResult> SuspendAsync(
        int teacherId, string? reason, string actorKeycloakId, int actorUserId = 0, CancellationToken ct = default)
    {
        RequireActor(actorKeycloakId);

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
            return new(AdminTeacherSuspensionStatus.ReasonRequired);
        if (trimmedReason.Length > ReasonMaxLength)
            return new(AdminTeacherSuspensionStatus.ReasonTooLong);

        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.TeacherSuspended,
            AdminUserTargetType.Teacher, teacherId);

        var current = await ReadStateAsync(teacherId, ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSuspensionStatus.TargetNotFound);
        }

        if (current.AccountSuspendedAt is not null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.AlreadySuspended, current.AccountApprovedAt, current.AccountSuspendedAt);
        }

        if (current.AccountApprovedAt is null)
        {
            // Hesabı hiç onaylanmamış öğretmen zaten kapalı; onay bekleyen başvurusu varsa ret akışı (#157) kullanılır.
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.AccountNotApproved);
        }

        var now = UtcNow();

        // issue #298: bildirim event'leri için isim/Keycloak sub'ları transaction AÇILMADAN ÖNCE best-effort çözülür
        // (BookingService.DecideAsync / WorksheetAccessRequestService deseni). Bu arada eklenen bir talep/öğrenci için
        // alanlar boş kalır; consumer sub'ı kendi verisinden çözer, çözemezse retry → dead-letter (bildirim kaybolmaz).
        var lookup = await TryLookupUsersAsync(teacherId, now, ct);

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), askıya alınmaz.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Koşullu güncelleme: okunduğu an hâlâ "onaylı ve askıda değil" ise. Eşzamanlı askıya alma/açma ya da silme 0 satır
        // üretir → Conflict. ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime açıkça yazılır. Bu noktadan sonra istemci
        // iptali akışı yarıda bırakmasın (DB değişip audit Requested'da kalmasın).
        // issue #298: askı + otomatik ret + bildirim outbox'ları tek transaction'da (retry-on-failure için execution
        // strategy içinde; TeacherApprovalService.ApproveAsync ile aynı desen).
        var affected = 0;
        var rejectedCount = 0;
        var notifiedStudentCount = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Retry'da önceki denemenin (commit edilmemiş) booking/outbox değişiklikleri tekrar eklenmesin.
            _context.ChangeTracker.Clear();
            rejectedCount = 0;
            notifiedStudentCount = 0;

            await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);

            affected = await _context.Teachers
                .Where(t => t.Id == teacherId && t.AccountApprovedAt != null && t.AccountSuspendedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.AccountApprovedAt, (DateTime?)null)
                    .SetProperty(t => t.AccountSuspendedAt, now)
                    .SetProperty(t => t.AccountSuspensionReason, trimmedReason)
                    .SetProperty(t => t.UpdateTime, now), CancellationToken.None);

            if (affected == 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return;
            }

            rejectedCount = await RejectPendingBookingsAsync(teacherId, now, actorUserId, lookup);
            notifiedStudentCount = await AddTeacherUnavailableNotificationsAsync(teacherId, now, lookup);
            await _context.SaveChangesAsync(CancellationToken.None);

            await tx.CommitAsync(CancellationToken.None);
        });

        // Code review D3: COMMIT gerçekleşti ama onayı kayboldu (bağlantı koptu) → strateji delegeyi yeniden çalıştırır, koşullu
        // UPDATE bu kez 0 satır döner. Satır BU isteğin yazdığı askıyı taşıyorsa (aynı an + aynı neden) işlem başarılıdır;
        // yan etkiler ilk denemede aynı transaction'la commit edildi. Başka bir admin'in askısı "Conflict" kalır.
        var appliedByEarlierAttempt = affected == 0 && await IsOwnCommittedSuspensionAsync(teacherId, now, trimmedReason);

        if (affected == 0 && !appliedByEarlierAttempt)
        {
            _logger.LogWarning("[AdminTeacherSuspension] Eşzamanlı değişiklik: Teacher#{TeacherId} askıya alınamadı", teacherId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        if (appliedByEarlierAttempt)
            _logger.LogInformation(
                "[AdminTeacherSuspension] Askı önceki denemede commit edilmiş (onay kaybı sonrası retry): Teacher#{TeacherId} actor={Actor}",
                teacherId, actorKeycloakId);
        else
            _logger.LogInformation(
                "[AdminTeacherSuspension] Öğretmen hesap onayı askıya alındı: Teacher#{TeacherId} actor={Actor} rejectedRequests={Rejected} notifiedStudents={Notified}",
                teacherId, actorKeycloakId, rejectedCount, notifiedStudentCount);

        // issue #311: commit'ten SONRA — açık tahtalar 60 sn'lik temizlik turunu beklemeden kapanır.
        await CloseOpenWhiteboardsAsync(teacherId);

        return new(AdminTeacherSuspensionStatus.Success, AccountApprovedAt: null, AccountSuspendedAt: now);
    }

    // ------------------------------------------------------------------
    // issue #298 yan etkileri
    // ------------------------------------------------------------------

    private sealed record UserLookup(string TeacherName, IReadOnlyDictionary<int, string> KeycloakIds)
    {
        public static readonly UserLookup Empty = new(string.Empty, new Dictionary<int, string>());

        public string KeycloakIdOf(int userId) => KeycloakIds.TryGetValue(userId, out var sub) ? sub : string.Empty;
    }

    /// <summary>Öğretmenin tüm Pending talepleri (zamandan bağımsız — geçmiş Pending talep de takılı kalmasın).</summary>
    private IQueryable<Booking> PendingBookings(int teacherId)
        => _context.Bookings.Where(b => b.TeacherId == teacherId && b.Status == BookingStatus.Pending);

    /// <summary>
    /// Henüz BİTMEMİŞ (bitiş &gt; şimdi; devam eden ders dahil) Approved randevular. Rejected ve silinmiş (global filtre)
    /// randevular hariç. Slot saatleri UTC duvar saatidir; gün aşan slot (dünün hâlâ süren ve bugünün gün aşan slotu)
    /// dahildir — koşul <see cref="SlotTimeRange.BookingNotEndedAt"/> (issue #300).
    /// </summary>
    private IQueryable<Booking> UpcomingApprovedBookings(int teacherId, DateTime nowUtc)
        => _context.Bookings
            .Where(b => b.TeacherId == teacherId && b.Status == BookingStatus.Approved)
            .Where(SlotTimeRange.BookingNotEndedAt(nowUtc));

    private async Task<UserLookup> TryLookupUsersAsync(int teacherId, DateTime nowUtc, CancellationToken ct)
    {
        if (_authApiClient is null)
            return UserLookup.Empty;

        try
        {
            var teacherUserId = await _context.Teachers.AsNoTracking()
                .Where(t => t.Id == teacherId)
                .Select(t => t.UserId)
                .FirstOrDefaultAsync(ct);
            var pendingStudents = await PendingBookings(teacherId).AsNoTracking()
                .Select(b => b.Student.UserId)
                .ToListAsync(ct);
            var upcomingStudents = await UpcomingApprovedBookings(teacherId, nowUtc).AsNoTracking()
                .Select(b => b.Student.UserId)
                .ToListAsync(ct);
            var studentUserIds = pendingStudents.Concat(upcomingStudents).Distinct().ToList();

            if (studentUserIds.Count == 0)
                return UserLookup.Empty; // Etkilenen randevu yok → event yazılmayacak, lookup gereksiz.

            var users = await _authApiClient.GetUsersByIdsAsync(studentUserIds.Append(teacherUserId).Distinct(), ct);
            var teacherName = users.FirstOrDefault(u => u.Id == teacherUserId)?.FullName ?? string.Empty;
            var subs = users
                .Where(u => u.Id != teacherUserId && !string.IsNullOrWhiteSpace(u.KeycloakId))
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First().KeycloakId);
            return new UserLookup(teacherName, subs);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "[AdminTeacherSuspension] auth-api lookup başarısız; randevu bildirim alanları boş geçilecek. Teacher#{TeacherId}",
                teacherId);
            return UserLookup.Empty;
        }
    }

    /// <summary>
    /// Pending talepleri Rejected yapar ve YALNIZCA gerçekten reddedilen her talep için öğrenciye
    /// <see cref="BookingDecisionEvent"/> yazar (<c>TeacherUnavailable=true</c>, gerekçe null — askı nedeni öğrenciye gitmez).
    /// <para>
    /// Code review O1: öğretmenin eşzamanlı kararıyla (<c>BookingService.DecideAsync</c>, o da koşullu) yarışmamak için her
    /// satır <c>Status == Pending</c> koşullu UPDATE ile güncellenir; 0 satır = öğretmen az önce karar verdi → bu talep için
    /// event yazılmaz (çelişen iki bildirim olmaz). Satır başına tek UPDATE bilinçli: etkilenen id'leri sağlayıcıdan bağımsız
    /// (RETURNING'siz) kesin bilmenin yolu bu; bir öğretmenin bekleyen talep sayısı küçüktür. Denetim alanları
    /// (<c>UpdateTime</c>/<c>UpdateUserId</c> = admin, code review D5) açıkça yazılır. Outbox SaveChanges'i çağıranda.
    /// </para>
    /// </summary>
    private async Task<int> RejectPendingBookingsAsync(int teacherId, DateTime nowUtc, int actorUserId, UserLookup lookup)
    {
        var pending = await PendingBookings(teacherId)
            .AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.TeacherId,
                b.StudentId,
                StudentUserId = b.Student.UserId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime
            })
            .ToListAsync(CancellationToken.None);

        int? updateUserId = actorUserId > 0 ? actorUserId : null;
        var rejected = 0;
        foreach (var booking in pending)
        {
            var id = booking.Id;
            var updated = await _context.Bookings
                .Where(b => b.Id == id && b.Status == BookingStatus.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, BookingStatus.Rejected)
                    .SetProperty(b => b.DecisionAt, nowUtc)
                    .SetProperty(b => b.RejectionReason, (string?)null)
                    .SetProperty(b => b.UpdateTime, nowUtc)
                    .SetProperty(b => b.UpdateUserId, updateUserId), CancellationToken.None);
            if (updated == 0)
                continue;

            rejected++;
            AddOutbox(new BookingDecisionEvent
            {
                BookingId = booking.Id,
                TeacherId = booking.TeacherId,
                TeacherName = lookup.TeacherName,
                StudentId = booking.StudentId,
                StudentUserId = booking.StudentUserId,
                TargetKeycloakId = lookup.KeycloakIdOf(booking.StudentUserId),
                Approved = false,
                RejectionReason = null,
                TeacherUnavailable = true,
                Date = booking.Date,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                DecidedAt = nowUtc
            }, nowUtc);
        }

        return rejected;
    }

    /// <summary>
    /// Code review D3: satır, BU isteğin yazdığı askıyı mı taşıyor? Aynı an (sağlayıcı hassasiyeti için ±1 ms) VE aynı
    /// neden. Aktör Teacher satırında tutulmadığı için anın kendisi ayırt edicidir (başka bir admin'in askısı farklı andadır).
    /// </summary>
    private async Task<bool> IsOwnCommittedSuspensionAsync(int teacherId, DateTime nowUtc, string reason)
    {
        var state = await _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new { t.AccountSuspendedAt, t.AccountSuspensionReason })
            .FirstOrDefaultAsync(CancellationToken.None);

        return state?.AccountSuspendedAt is { } suspendedAt
            && Math.Abs((suspendedAt - nowUtc).TotalMilliseconds) < 1
            && state.AccountSuspensionReason == reason;
    }

    /// <summary>
    /// Henüz bitmemiş Approved randevusu olan her öğrenci için TEK <see cref="BookingTeacherUnavailableEvent"/>
    /// (aynı öğretmenle birden fazla randevu → tek bildirim). Randevular değişmez. SaveChanges çağıranda.
    /// </summary>
    private async Task<int> AddTeacherUnavailableNotificationsAsync(int teacherId, DateTime nowUtc, UserLookup lookup)
    {
        var rows = await UpcomingApprovedBookings(teacherId, nowUtc)
            .AsNoTracking()
            .Select(b => new { b.Id, StudentUserId = b.Student.UserId })
            .ToListAsync(CancellationToken.None);

        var byStudent = rows.GroupBy(r => r.StudentUserId).ToList();
        foreach (var group in byStudent)
        {
            AddOutbox(new BookingTeacherUnavailableEvent
            {
                EventId = Guid.NewGuid(),
                TeacherId = teacherId,
                StudentUserId = group.Key,
                TargetKeycloakId = lookup.KeycloakIdOf(group.Key),
                BookingIds = group.Select(r => r.Id).OrderBy(id => id).ToList(),
                UnavailableSinceUtc = nowUtc
            }, nowUtc);
        }

        return byStudent.Count;
    }

    private void AddOutbox<TEvent>(TEvent @event, DateTime nowUtc)
        => _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TEvent>(),
            Content = JsonSerializer.Serialize(@event),
            CreatedAt = nowUtc
        });

    /// <summary>
    /// issue #311: öğretmenin açık tahtalarını <c>BoardClosed("TeacherUnavailable")</c> ile hemen kapatır. Hata askıyı geri
    /// almaz; loglanır ve <see cref="WhiteboardCleanupService"/> bir sonraki turda (FindInvalidBoardsAsync) yine kapatır.
    /// </summary>
    private async Task CloseOpenWhiteboardsAsync(int teacherId)
    {
        if (_whiteboardStore is null || _whiteboardCloser is null)
            return;

        try
        {
            var openBoardIds = _whiteboardStore.ListBoards().Select(b => b.BookingId).ToList();
            if (openBoardIds.Count == 0)
                return;

            var teacherBoardIds = await _context.Bookings.AsNoTracking()
                .Where(b => b.TeacherId == teacherId && openBoardIds.Contains(b.Id))
                .Select(b => b.Id)
                .ToListAsync(CancellationToken.None);

            foreach (var bookingId in teacherBoardIds)
                await _whiteboardCloser.CloseAsync(bookingId, WhiteboardCloseReasons.TeacherUnavailable, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AdminTeacherSuspension] Açık tahtalar hemen kapatılamadı; temizlik turu kapatacak. Teacher#{TeacherId}", teacherId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AccountApprovedAt = now</c> yazılır: ilk hesap onayı tarihi (#287) askıya almada silinmişti ve bilinçli olarak geri
    /// getirilmez (ilk onay anı <c>AdminUserActionLogs</c>'taki <c>TeacherApproved</c> kaydındadır).
    /// </remarks>
    public async Task<AdminTeacherSuspensionResult> UnsuspendAsync(
        int teacherId, string actorKeycloakId, CancellationToken ct = default)
    {
        RequireActor(actorKeycloakId);

        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.TeacherUnsuspended,
            AdminUserTargetType.Teacher, teacherId);

        var current = await ReadStateAsync(teacherId, ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSuspensionStatus.TargetNotFound);
        }

        if (current.AccountSuspendedAt is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.NotSuspended, current.AccountApprovedAt);
        }

        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        var now = DateTime.UtcNow;
        var affected = await _context.Teachers
            .Where(t => t.Id == teacherId && t.AccountSuspendedAt != null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.AccountApprovedAt, now)
                .SetProperty(t => t.AccountSuspendedAt, (DateTime?)null)
                .SetProperty(t => t.AccountSuspensionReason, (string?)null)
                .SetProperty(t => t.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminTeacherSuspension] Eşzamanlı değişiklik: Teacher#{TeacherId} askısı kaldırılamadı", teacherId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminTeacherSuspension] Öğretmen hesap onayının askısı kaldırıldı: Teacher#{TeacherId} actor={Actor}",
            teacherId, actorKeycloakId);
        return new(AdminTeacherSuspensionStatus.Success, AccountApprovedAt: now, AccountSuspendedAt: null);
    }

    private sealed record TeacherState(DateTime? AccountApprovedAt, DateTime? AccountSuspendedAt);

    /// <summary>Soft-delete edilmiş öğretmen global filtre ile dışarıda → null (404).</summary>
    private Task<TeacherState?> ReadStateAsync(int teacherId, CancellationToken ct) =>
        _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new TeacherState(t.AccountApprovedAt, t.AccountSuspendedAt))
            .FirstOrDefaultAsync(ct)!;

    private static void RequireActor(string actorKeycloakId)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin teacher suspension requires the actor's Keycloak subject.");
    }
}
