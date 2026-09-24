using System;
using System.Security.Claims;
using ExamApp.Api.Data;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #277 review (security HIGH): öğretmen/öğrenci dal kararlarında kullanılacak ETKİN rol.
/// <para>
/// Profil rolü (<c>UserProfileDto.Role</c>) auth-api <c>Users.Role</c>'den gelir ve Keycloak'la gecikmeli senkronlanır
/// (#277 madde 4); #287 <c>ApprovedTeacher</c> kapısı ise JWT rollerine bakar. İkisi ayrışırsa (ör. JWT Student, profil
/// Teacher) profil rolüyle dal seçmek, kapının hiç uygulanmadığı öğretmen dalını açıyordu. Kural: profil rolü YALNIZCA
/// JWT'de de varsa kullanılır; aksi halde JWT'deki uygulama rolüne (Student → Teacher → Parent önceliği) düşülür; JWT'de
/// hiçbiri yoksa boş rol (hiçbir dal). Böylece öğretmen dalına yalnızca JWT'si Teacher olan — dolayısıyla kapıdan geçmiş —
/// çağıran girer.
/// </para>
/// </summary>
public static class EffectiveRole
{
    private static readonly string[] FallbackOrder =
    {
        nameof(UserRole.Student), nameof(UserRole.Teacher), nameof(UserRole.Parent)
    };

    public static string Resolve(string? profileRole, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (!string.IsNullOrWhiteSpace(profileRole) && principal.IsInRole(profileRole))
            return profileRole;

        foreach (var role in FallbackOrder)
        {
            if (principal.IsInRole(role))
                return role;
        }

        return string.Empty;
    }
}
