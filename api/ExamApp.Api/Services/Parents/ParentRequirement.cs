using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #437 (epic #435): "hangi hesap veli gerektirir" kuralının TEK yeri. Kullanıcı kararı (2026-10-07): her öğrencinin
/// velisi olmalı — 18 yaş üstü ya da admin muafiyeti YOK, bu yüzden öğrenci profilinde <c>RequiresParent</c> bayrağı tutulmaz.
/// Öğretmen (bağımsız öğretmen dahil — o da Teacher rolüdür), admin ve veli hesapları bu kurala hiç tabi değildir; aynı hesapta
/// öğrenci rolüyle birlikte bu rollerden biri varsa (eski/dev hesaplar) yetişkin rolü kazanır.
/// Yaptırım (veli yokken kısıtlar) burada değil: #440 / #441 bu yardımcıya bakar.
/// </summary>
public static class ParentRequirement
{
    /// <summary>Ürün kuralı: her öğrenci hesabının velisi olmalı (istisna yok).</summary>
    public const bool EveryStudentRequiresParent = true;

    private const string StudentRole = "Student";

    /// <summary>Bu rollerden biri olan hesap veli kuralına tabi değildir.</summary>
    private static readonly string[] AdultRoles = ["Teacher", "Admin", "SuperAdmin", "Parent"];

    /// <summary>Rol listesine göre: öğrenci (ve yetişkin rolü yok) ise true.</summary>
    public static bool RequiresParent(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var set = roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EveryStudentRequiresParent && set.Contains(StudentRole) && !AdultRoles.Any(set.Contains);
    }

    /// <summary>
    /// Kimliği doğrulanmış kullanıcının rol claim'lerine göre. <see cref="ClaimsPrincipal.IsInRole"/> büyük/küçük harfe duyarlı
    /// olduğundan kullanılmaz: her kimliğin rol claim tipindeki değerler toplanıp rol listesi sürümüne (duyarsız) verilir — iki
    /// sürüm aynı sonucu verir.
    /// </summary>
    public static bool RequiresParent(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return RequiresParent(user.Identities.SelectMany(i => i.FindAll(i.RoleClaimType)).Select(c => c.Value));
    }

    /// <summary>Öğrenci profili (Students satırı) olan hesap — rol bilgisi olmadan çağrılan yerler için.</summary>
    public static bool RequiresParentForStudentProfile() => EveryStudentRequiresParent;
}
