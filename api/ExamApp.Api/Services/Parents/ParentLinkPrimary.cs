using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #436: veli bağlantısı sorgu kuralları + birincil veli kuralı + öğrenci kilidi altındaki transaction kalıbı.
/// <see cref="ParentLinkService"/> ve <see cref="ParentLinkTransitionSweepJob"/> aynı tanımı kullanır.
/// <list type="bullet">
/// <item>Açık bağlantı: Active ya da süresi dolmamış Pending (süre kuruluş yoluna göre: LegacyV1 30 gün, diğerleri 7 gün);
/// silinmiş (soft-delete) veli/öğrenci tavan tüketmez.</item>
/// <item>Birincil veli: öğrencinin Active + <see cref="ParentStudentLink.IsPrimary"/> bağlantısı (velisi silinmemiş). Yoksa (birincil
/// koparıldı / hesabı silindi) kalan en eski Active veli — <c>(ActivatedAt ?? CreatedAt, Id)</c> sırası; migration backfill'i de
/// aynı sırayı kullanır.</item>
/// </list>
/// </summary>
public static class ParentLinkPrimary
{
    private static readonly Expression<Func<ParentStudentLink, bool>> IsActive =
        l => l.Status == ParentStudentLinkStatus.Active;

    private static readonly Expression<Func<ParentStudentLink, bool>> PartiesNotDeleted =
        l => !l.Parent.IsDeleted && !l.Student.IsDeleted;

    /// <summary>Süresi dolmamış Pending (kuruluş yoluna göre).</summary>
    public static Expression<Func<ParentStudentLink, bool>> IsLivePending(DateTime now)
    {
        var cutoff = now - ParentLinkRules.PendingValidity;
        var legacyCutoff = now - ParentLinkRules.LegacyPendingValidity;
        return l => l.Status == ParentStudentLinkStatus.Pending
            && ((l.Origin == ParentStudentLinkOrigin.LegacyV1 && l.CreatedAt > legacyCutoff)
                || (l.Origin != ParentStudentLinkOrigin.LegacyV1 && l.CreatedAt > cutoff));
    }

    /// <summary>Süresi dolmuş Pending (süpürücü / redeem öncesi temizlik bunları Revoked'a çeker).</summary>
    public static Expression<Func<ParentStudentLink, bool>> IsExpiredPending(DateTime now)
    {
        var cutoff = now - ParentLinkRules.PendingValidity;
        var legacyCutoff = now - ParentLinkRules.LegacyPendingValidity;
        return l => l.Status == ParentStudentLinkStatus.Pending
            && ((l.Origin == ParentStudentLinkOrigin.LegacyV1 && l.CreatedAt <= legacyCutoff)
                || (l.Origin != ParentStudentLinkOrigin.LegacyV1 && l.CreatedAt <= cutoff));
    }

    /// <summary>Açık bağlantı = (Active ∨ <see cref="IsLivePending"/>) ∧ taraflar silinmemiş (diğer kurallardan kurulur).</summary>
    public static Expression<Func<ParentStudentLink, bool>> IsOpen(DateTime now)
        => And(Or(IsActive, IsLivePending(now)), PartiesNotDeleted);

    /// <summary>Velisi silinmemiş Active bağlantı (birincil veli adayları).</summary>
    public static readonly Expression<Func<ParentStudentLink, bool>> IsLiveActive =
        l => l.Status == ParentStudentLinkStatus.Active && !l.Parent.IsDeleted;

    /// <summary>Birincil seçimi için Active bağlantı özeti.</summary>
    public sealed record ActiveLinkRow(int LinkId, int StudentId, int ParentId, int ParentUserId, bool IsPrimary, DateTime ActivatedOrCreatedAt);

    /// <summary>Öğrencinin kilit altındaki aile durumu: birincil, velisi silinmemiş Active satırlar ve onarımda değişen satır sayısı.</summary>
    public sealed record FamilyState(ActiveLinkRow? Primary, IReadOnlyList<ActiveLinkRow> Active, int Changed);

    /// <summary>
    /// Bellekte birincil seçimi (okuma uçları — yazmadan): işaretli Active satır varsa o, yoksa en eski Active. Liste aynı
    /// öğrenciye ait, velisi silinmemiş Active satırlar olmalı.
    /// </summary>
    public static ActiveLinkRow? PickPrimary(IEnumerable<ActiveLinkRow> activeRowsOfStudent)
    {
        var rows = activeRowsOfStudent.ToList();
        return rows.Where(r => r.IsPrimary).OrderBy(r => r.LinkId).FirstOrDefault()
               ?? rows.OrderBy(r => r.ActivatedOrCreatedAt).ThenBy(r => r.LinkId).FirstOrDefault();
    }

    /// <summary>Verilen öğrencilerin velisi silinmemiş Active bağlantıları (tek sorgu; öğrenci başına en fazla 4 satır).</summary>
    public static Task<List<ActiveLinkRow>> ActiveRowsAsync(AppDbContext context, IReadOnlyCollection<int> studentIds, CancellationToken ct = default)
        => context.ParentStudentLinks.AsNoTracking()
            .Where(l => studentIds.Contains(l.StudentId))
            .Where(IsLiveActive)
            .Select(l => new ActiveLinkRow(l.Id, l.StudentId, l.ParentId, l.Parent.UserId, l.IsPrimary, l.ActivatedAt ?? l.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Birincil işaretini kalıcı olarak düzeltir ve aile durumunu döner. ÇAĞIRAN öğrenci kilidini (<see cref="InStudentLockAsync"/>)
    /// aynı transaction'da almış olmalı. Geçerli birincil (Active + işaretli + velisi silinmemiş) varsa yalnızca başka Active
    /// satırlardaki bayat işaret temizlenir; yoksa tüm Active işaretler temizlenip en eski Active satır işaretlenir (önce temizle,
    /// sonra işaretle — filtreli tekil index). <see cref="FamilyState.Changed"/> = gerçekten değişen satır sayısı.
    /// </summary>
    public static async Task<FamilyState> EnsurePrimaryAsync(AppDbContext context, int studentId, CancellationToken ct = default)
    {
        var rows = await ActiveRowsAsync(context, new[] { studentId }, ct);
        var current = rows.Where(r => r.IsPrimary).OrderBy(r => r.LinkId).FirstOrDefault();
        var primary = current ?? rows.OrderBy(r => r.ActivatedOrCreatedAt).ThenBy(r => r.LinkId).FirstOrDefault();

        // Silinmiş velinin Active satırı ya da ikinci işaretli satır gibi bayat işaretler (index yalnız Active'e bakar).
        var keepId = primary?.LinkId ?? 0;
        var changed = await context.ParentStudentLinks
            .Where(l => l.StudentId == studentId && l.IsPrimary && l.Status == ParentStudentLinkStatus.Active && l.Id != keepId)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.IsPrimary, false), ct);

        if (primary != null && current == null)
        {
            changed += await context.ParentStudentLinks
                .Where(l => l.Id == primary.LinkId && l.Status == ParentStudentLinkStatus.Active)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.IsPrimary, true), ct);
            primary = primary with { IsPrimary = true };
        }
        var active = rows.Select(r => r with { IsPrimary = r.LinkId == primary?.LinkId }).ToList();
        return new FamilyState(primary, active, changed);
    }

    /// <summary>
    /// Öğrenci (ve verilmişse veli) advisory kilidi altında, execution strategy içinde tek transaction. <paramref name="body"/>
    /// yeniden denemede baştan çalışır (yakaladığı durumu sıfırlamalı); true dönerse commit, false dönerse geri alınır.
    /// Kilit zamanında alınamazsa <see cref="ParentLinkLockTimeoutException"/> fırlar. Change tracker önce ve sonra temizlenir.
    /// </summary>
    public static async Task InStudentLockAsync(
        AppDbContext context, int studentId, Func<CancellationToken, Task<bool>> body, CancellationToken ct, int? parentId = null)
    {
        try
        {
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var tx = await context.Database.BeginTransactionAsync(ct);
                await context.Database.AcquireStudentParentLinkLockAsync(studentId, ct);
                if (parentId is int p)
                    await context.Database.AcquireParentChildLinkLockAsync(p, ct);
                if (await body(ct))
                    await tx.CommitAsync(ct);
            });
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    // ---- ifade birleştirme ----------------------------------------------------------------------------------------------

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
        => Combine(a, b, Expression.OrElse);

    private static Expression<Func<T, bool>> And<T>(Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
        => Combine(a, b, Expression.AndAlso);

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> a, Expression<Func<T, bool>> b, Func<Expression, Expression, BinaryExpression> op)
    {
        var parameter = a.Parameters[0];
        var right = new ParameterReplacer(b.Parameters[0], parameter).Visit(b.Body)!;
        return Expression.Lambda<Func<T, bool>>(op(a.Body, right), parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
