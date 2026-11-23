using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Issue #523: velinin çocuğun AI asistanı kullanımını kapatması. Her veli kendi bağlantısında seçer
/// (<see cref="ParentStudentLink.AssistantBlocked"/>); velisi silinmemiş herhangi bir Active bağlantıda kapalıysa çocuk için kapalıdır.
/// Velisi olmayan öğrencide açıktır. Denetim AssistantService'te her sohbet turunda yapılır (öğrencinin token'ıyla
/// <c>GET api/student/assistant-access</c>).
/// </summary>
public static class ParentAssistantControl
{
    /// <summary>Asistanı kapatan, velisi silinmemiş Active bağlantılar.</summary>
    public static IQueryable<ParentStudentLink> BlockedLinks(AppDbContext context)
        => context.ParentStudentLinks.AsNoTracking().Where(ParentLinkPrimary.IsLiveActive).Where(l => l.AssistantBlocked);

    public static async Task<bool> IsAllowedAsync(AppDbContext context, int studentId, CancellationToken ct = default)
        => !await BlockedLinks(context).AnyAsync(l => l.StudentId == studentId, ct);
}
