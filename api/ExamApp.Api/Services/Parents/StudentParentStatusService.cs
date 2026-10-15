using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #440 / #441: öğrencinin veli durumu (yaptırımın tek kaynağı).</summary>
public enum StudentParentMode
{
    /// <summary>Veli gerekmiyor (yetişkin rolü / öğrenci kaydı yok) ya da Active velisi var — kısıt yok.</summary>
    Ok,
    /// <summary>
    /// #440: açık veli daveti var (veli aktive etmedi / onaylamadı) ve davet süresi dolmamış YA DA hoşgörü süresi bitmemiş —
    /// sosyal/ders/mesaj kapalı (<c>ParentActivationRequired</c>).
    /// </summary>
    AwaitingActivation,
    /// <summary>#441: velisiz, davetsiz; hoşgörü süresi içinde — kısıt yok, banner/modal.</summary>
    Grace,
    /// <summary>
    /// #441: hoşgörü süresi doldu (ve açık davet yok ya da onun da süresi doldu) — yalnız "veli bilgisi ekle" (<c>ParentRequired</c>);
    /// veri silinmez. Süresi dolmuş davet özeti yine döner (UI yeniden gönder / düzelt sunar).
    /// </summary>
    Expired
}

/// <summary>Açık davetin önbelleğe alınan özeti (durum hesaplaması ve öğrenci paneli için).</summary>
public sealed record OpenParentInvitationFacts(
    Guid Id,
    string Email,
    string FullName,
    DateTime ExpiresAt,
    bool IsNewAccount,
    DateTime CreatedAt,
    int TotalSendsToEmail,
    DateTime LastSentToEmail);

/// <summary>DB'den okunan ham durum (zamandan bağımsız; mod okuma anında hesaplanır).</summary>
public sealed record StudentParentFacts(
    int StudentId,
    bool HasActiveParent,
    DateTime? ParentRequirementStartedAt,
    OpenParentInvitationFacts? Invitation,
    IReadOnlyList<ParentEmailAttemptFact> RecentAttempts);

/// <summary>Kilit penceresindeki e-posta denemesi (hash + an).</summary>
public sealed record ParentEmailAttemptFact(string EmailHash, DateTime At);

/// <summary>Hesaplanmış durum.</summary>
public sealed record StudentParentState(
    StudentParentMode Mode,
    bool RequiresParent,
    int? StudentId,
    DateTime? GraceEndsAt,
    int? DaysLeft,
    bool IsFinalWarning,
    StudentParentFacts? Facts)
{
    public static StudentParentState NotApplicable(bool requiresParent, StudentParentFacts? facts = null)
        => new(StudentParentMode.Ok, requiresParent, facts?.StudentId, null, null, false, facts);
}

/// <summary>
/// Issue #440 / #441: öğrencinin veli durumunun TEK kaynağı. <see cref="StudentParentMode.Ok"/> = veli gerekmiyor ya da Active
/// bağlantı var (<see cref="ParentChildAccess.GrantsAccess"/>); <see cref="StudentParentMode.AwaitingActivation"/> = açık davet (Pending;
/// bağlantısı yok ya da Pending) VE (davet süresi dolmamış YA DA hoşgörü bitmemiş); aksi halde <see cref="Student.ParentRequirementStartedAt"/>
/// + <c>GraceDays</c>'e göre Grace / Expired (bkz. <see cref="StudentParentStatusService.Evaluate"/>; SQL karşılığı
/// <see cref="StudentParentStatusService.QueryByMode"/>).
/// Ham durum öğrenci (kullanıcı id) başına <see cref="ParentRequirementOptions.StatusCacheSeconds"/> süre bellekte tutulur; bu servisin
/// ve davet servisinin yazımları <see cref="Invalidate"/> ile geçersizler.
/// </summary>
public interface IStudentParentStatusService
{
    /// <summary>Önbellekli okuma (yazmaz). Öğrenci kaydı yoksa Ok.</summary>
    Task<StudentParentState> GetAsync(int studentUserId, bool requiresParent, CancellationToken ct = default);

    /// <summary>
    /// Profil tazeleme (ilk giriş) yolu: önbelleği atlar, sayacı damgalar (veli gerekir + Active yok + damga yok → şimdi; açık davet olsa
    /// da) ya da Active veli varsa damgayı temizler, sonra güncel durumu döner.
    /// </summary>
    Task<StudentParentState> RefreshAndStampAsync(int studentUserId, bool requiresParent, CancellationToken ct = default);

    void Invalidate(int studentUserId);
}

/// <summary>
/// Issue #440/#441 review: öğrenci veli durumu ham verisinin önbelleği (öğrenci KULLANICI id'si anahtarı). Bağlantı/davet durumu değiştiren
/// HER yazım yolu (<c>ParentLinkService</c>, davet servisi) <see cref="Invalidate"/> çağırır. DİKKAT: süreç içi (IMemoryCache) — çok
/// örnekli kurulumda diğer örnekler <see cref="ParentRequirementOptions.StatusCacheSeconds"/> kadar eski veriyle çalışabilir; o noktada
/// Redis'e taşınmalı (bilinçli ertelendi).
/// </summary>
public interface IStudentParentStatusCache
{
    bool TryGet(int studentUserId, out StudentParentFacts? facts);

    void Set(int studentUserId, StudentParentFacts? facts, TimeSpan ttl);

    void Invalidate(int studentUserId);
}

/// <inheritdoc cref="IStudentParentStatusCache"/>
public sealed class StudentParentStatusCache(IMemoryCache cache) : IStudentParentStatusCache
{
    private static string Key(int studentUserId) => "student-parent-facts:" + studentUserId;

    public bool TryGet(int studentUserId, out StudentParentFacts? facts) => cache.TryGetValue(Key(studentUserId), out facts);

    public void Set(int studentUserId, StudentParentFacts? facts, TimeSpan ttl) => cache.Set(Key(studentUserId), facts, ttl);

    public void Invalidate(int studentUserId) => cache.Remove(Key(studentUserId));
}

/// <inheritdoc cref="IStudentParentStatusService"/>
public sealed class StudentParentStatusService : IStudentParentStatusService
{
    private readonly AppDbContext _context;
    private readonly IStudentParentStatusCache _cache;
    private readonly IOptionsMonitor<ParentRequirementOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<StudentParentStatusService> _logger;

    public StudentParentStatusService(
        AppDbContext context,
        IStudentParentStatusCache cache,
        IOptionsMonitor<ParentRequirementOptions> options,
        TimeProvider? time = null,
        ILogger<StudentParentStatusService>? logger = null)
    {
        _context = context;
        _cache = cache;
        _options = options;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<StudentParentStatusService>.Instance;
    }

    public void Invalidate(int studentUserId) => _cache.Invalidate(studentUserId);

    public async Task<StudentParentState> GetAsync(int studentUserId, bool requiresParent, CancellationToken ct = default)
    {
        if (!requiresParent || studentUserId <= 0)
            return StudentParentState.NotApplicable(requiresParent);

        var seconds = _options.CurrentValue.StatusCacheSeconds;
        StudentParentFacts? facts;
        if (seconds > 0 && _cache.TryGet(studentUserId, out var cached))
        {
            facts = cached;
        }
        else
        {
            facts = await LoadFactsAsync(_context, studentUserId, _time.GetUtcNow().UtcDateTime, ct);
            if (seconds > 0)
                _cache.Set(studentUserId, facts, TimeSpan.FromSeconds(seconds));
        }

        return Evaluate(facts, requiresParent, _time.GetUtcNow().UtcDateTime, _options.CurrentValue);
    }

    public async Task<StudentParentState> RefreshAndStampAsync(int studentUserId, bool requiresParent, CancellationToken ct = default)
    {
        Invalidate(studentUserId);
        if (!requiresParent || studentUserId <= 0)
            return StudentParentState.NotApplicable(requiresParent);

        var now = _time.GetUtcNow().UtcDateTime;
        var facts = await LoadFactsAsync(_context, studentUserId, now, ct);
        if (facts != null)
        {
            if (facts.HasActiveParent && facts.ParentRequirementStartedAt != null)
            {
                // Active veli var: sayaç sıfırlanır — sonraki bir koparmada yeni (tam) süre başlar.
                await _context.Students.Where(s => s.Id == facts.StudentId && s.ParentRequirementStartedAt != null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ParentRequirementStartedAt, (DateTime?)null), ct);
                facts = facts with { ParentRequirementStartedAt = null };
            }
            else if (!facts.HasActiveParent && facts.ParentRequirementStartedAt == null)
            {
                // İlk giriş: sayaç şimdi başlar — açık davet olsa da (review M3: davet beklerken de süre işler; koşullu — eşzamanlı iki
                // tazeleme ilk damgayı ezmez).
                var stamped = await _context.Students.Where(s => s.Id == facts.StudentId && s.ParentRequirementStartedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ParentRequirementStartedAt, now), ct);
                var startedAt = stamped > 0
                    ? now
                    : await _context.Students.AsNoTracking().Where(s => s.Id == facts.StudentId)
                        .Select(s => s.ParentRequirementStartedAt).FirstOrDefaultAsync(ct);
                facts = facts with { ParentRequirementStartedAt = startedAt };
                if (stamped > 0)
                    _logger.LogInformation("[ParentRequirement] Veli ekleme süresi başladı: studentId={StudentId}", facts.StudentId);
            }
        }

        var seconds = _options.CurrentValue.StatusCacheSeconds;
        if (seconds > 0)
            _cache.Set(studentUserId, facts, TimeSpan.FromSeconds(seconds));
        return Evaluate(facts, requiresParent, now, _options.CurrentValue);
    }

    /// <summary>
    /// Saf hesaplama (birim testlerin hedefi). Kural (review M3): Active veli → Ok; açık davet VE (davet süresi dolmamış YA DA hoşgörü
    /// bitmemiş) → AwaitingActivation; hoşgörü bitmiş (davet yok ya da süresi dolmuş) → Expired; aksi Grace. Böylece hoşgörü bittikten
    /// sonra form göndermek yalnız token ömrü (7 gün) kadar kısıtlı mod verir; yeniden gönderimler sınırlıdır.
    /// </summary>
    public static StudentParentState Evaluate(StudentParentFacts? facts, bool requiresParent, DateTime now, ParentRequirementOptions options)
    {
        if (!requiresParent || facts == null || facts.HasActiveParent)
            return StudentParentState.NotApplicable(requiresParent, facts);

        // Damga yoksa (henüz profil tazelenmedi) sayaç "şimdi" başlamış sayılır; GraceDays=0 → anında dolmuş.
        var graceEndsAt = DateTime.SpecifyKind((facts.ParentRequirementStartedAt ?? now) + options.GracePeriod, DateTimeKind.Utc);
        var remaining = graceEndsAt - now;
        var graceOver = remaining <= TimeSpan.Zero;
        var daysLeft = graceOver ? 0 : (int)Math.Ceiling(remaining.TotalDays);
        var finalWarning = remaining <= TimeSpan.FromDays(options.FinalWarningDays);

        if (facts.Invitation is { } invitation && (invitation.ExpiresAt > now || !graceOver))
            return new StudentParentState(StudentParentMode.AwaitingActivation, true, facts.StudentId, graceEndsAt, daysLeft, finalWarning, facts);

        return graceOver
            ? new StudentParentState(StudentParentMode.Expired, true, facts.StudentId, graceEndsAt, 0, true, facts)
            : new StudentParentState(StudentParentMode.Grace, true, facts.StudentId, graceEndsAt, daysLeft, finalWarning, facts);
    }

    /// <summary>Mod kırılımı (admin panosu): <see cref="Evaluate"/> ile AYNI kuralın SQL karşılığı.</summary>
    public sealed record ModeQueries(
        IQueryable<Student> WithoutActiveParent,
        IQueryable<Student> AwaitingActivation,
        IQueryable<Student> Grace,
        IQueryable<Student> Expired);

    /// <summary>
    /// Review R2: <see cref="Evaluate"/> kuralının SQL karşılığı (veli kuralı öğrenci profilinde tutulmaz — öğrenci satırı olan herkes).
    /// Active veli yok ∧ açık davet ∧ (davet süresi dolmamış ∨ hoşgörü bitmemiş) → AwaitingActivation; kalan Active velisizler hoşgörüye
    /// göre Grace (damgasız = henüz başlamadı, Evaluate'te olduğu gibi Grace) / Expired. Biri değişirse ikisi birlikte değişmeli.
    /// </summary>
    public static ModeQueries QueryByMode(AppDbContext context, DateTime now, ParentRequirementOptions options)
    {
        var graceCutoff = now - options.GracePeriod;
        var withoutActive = context.Students.AsNoTracking()
            .Where(s => !context.ParentStudentLinks.Where(l => l.StudentId == s.Id).Where(ParentChildAccess.GrantsAccess).Any());
        var awaiting = withoutActive.Where(s =>
            context.ParentInvitations.Where(i => i.StudentId == s.Id).Where(IsOpenInvitation)
                .Any(i => i.ExpiresAt > now || s.ParentRequirementStartedAt == null || s.ParentRequirementStartedAt > graceCutoff));
        var notAwaiting = withoutActive.Where(s =>
            !context.ParentInvitations.Where(i => i.StudentId == s.Id).Where(IsOpenInvitation)
                .Any(i => i.ExpiresAt > now || s.ParentRequirementStartedAt == null || s.ParentRequirementStartedAt > graceCutoff));
        return new ModeQueries(
            withoutActive,
            awaiting,
            notAwaiting.Where(s => s.ParentRequirementStartedAt == null || s.ParentRequirementStartedAt > graceCutoff),
            notAwaiting.Where(s => s.ParentRequirementStartedAt != null && s.ParentRequirementStartedAt <= graceCutoff));
    }

    /// <summary>Ham durum: öğrenci satırı (yoksa null), Active bağlantı, açık davet (+ aynı e-postaya gönderim geçmişi), kilit penceresi.</summary>
    internal static async Task<StudentParentFacts?> LoadFactsAsync(AppDbContext context, int studentUserId, DateTime now, CancellationToken ct)
    {
        var student = await context.Students.AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.ParentRequirementStartedAt })
            .FirstOrDefaultAsync(ct);
        if (student == null)
            return null;

        var hasActive = await context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == student.Id)
            .Where(ParentChildAccess.GrantsAccess)
            .AnyAsync(ct);

        var invitation = await context.ParentInvitations.AsNoTracking()
            .Where(OpenInvitationOf(student.Id))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Email, i.FullName, i.ExpiresAt, i.IsNewAccount, i.CreatedAt })
            .FirstOrDefaultAsync(ct);

        OpenParentInvitationFacts? open = null;
        if (invitation != null)
        {
            var history = await SendHistoryAsync(context, student.Id, invitation.Email, ct);
            open = new OpenParentInvitationFacts(invitation.Id, invitation.Email, invitation.FullName,
                AsUtc(invitation.ExpiresAt), invitation.IsNewAccount, AsUtc(invitation.CreatedAt), history.Total, history.LastSent ?? AsUtc(invitation.CreatedAt));
        }

        var attempts = await RecentAttemptsAsync(context, studentUserId, now, ct);
        return new StudentParentFacts(student.Id, hasActive, student.ParentRequirementStartedAt is DateTime d ? AsUtc(d) : null, open, attempts);
    }

    /// <summary>Kilit penceresindeki e-posta denemeleri (öğrenci satırı henüz yokken de — kayıt ön kontrolü).</summary>
    internal static async Task<IReadOnlyList<ParentEmailAttemptFact>> RecentAttemptsAsync(AppDbContext context, int studentUserId, DateTime now, CancellationToken ct)
    {
        var windowStart = now - ParentInvitationRules.EmailChangeWindow;
        var attempts = await context.ParentInvitationAttempts.AsNoTracking()
            .Where(a => a.StudentUserId == studentUserId && a.AttemptedAt > windowStart)
            .Select(a => new ParentEmailAttemptFact(a.EmailHash, a.AttemptedAt))
            .ToListAsync(ct);
        return attempts.Select(a => a with { At = AsUtc(a.At) }).ToList();
    }

    /// <summary>
    /// Açık davet: Pending ve (bağlantısı yok — yeni veli, hesap aktivasyonda açılır — ya da bağlantısı hâlâ Pending; veli bağlantıyı
    /// iptal ettiyse davet de kapalı sayılır). Süresi dolmuş davet de açıktır (yeniden gönderilebilir).
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<ParentInvitation, bool>> IsOpenInvitation =
        i => i.Status == ParentInvitationStatus.Pending && (i.LinkId == null || i.Link!.Status == ParentStudentLinkStatus.Pending);

    internal static System.Linq.Expressions.Expression<Func<ParentInvitation, bool>> OpenInvitationOf(int studentId)
        => i => i.StudentId == studentId
                && i.Status == ParentInvitationStatus.Pending
                && (i.LinkId == null || i.Link!.Status == ParentStudentLinkStatus.Pending);

    /// <summary>Öğrenci + e-posta çiftine yapılmış toplam gönderim ve son gönderim (tüm davetler; "24 saatte 1, toplam 1+5").</summary>
    internal static async Task<(int Total, DateTime? LastSent)> SendHistoryAsync(AppDbContext context, int studentId, string email, CancellationToken ct)
    {
        var rows = await context.ParentInvitations.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.Email == email)
            .Select(i => new { i.SendCount, i.LastSentAt })
            .ToListAsync(ct);
        return rows.Count == 0
            ? (0, null)
            : (rows.Sum(r => r.SendCount), AsUtc(rows.Max(r => r.LastSentAt)));
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
