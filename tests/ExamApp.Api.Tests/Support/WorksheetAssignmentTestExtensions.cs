using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// issue #222: servis yalnızca <see cref="SchoolScope"/> alır. Testlerde "userId + isAdmin" ile atama yapmak için
/// scope, controller'ın <c>GetSchoolScopeAsync</c>'ine denk biçimde kurulur: admin → Unrestricted, aksi halde okul
/// öğretmen kaydından (<c>Teachers.SchoolId</c>, deterministik) çözülür; kayıt yoksa okulsuz.
/// </summary>
public static class WorksheetAssignmentTestExtensions
{
    public static async Task<ResponseBaseDto> AssignAsUserAsync(
        this IWorksheetAssignmentService service, AppDbContext context,
        WorksheetAssignmentRequestDto request, int userId, bool isAdmin = false)
    {
        var scope = isAdmin
            ? SchoolScope.Unrestricted(userId)
            : SchoolScope.For(userId, (await context.ResolveTeacherRecordAsync(userId)).SchoolId);

        return await service.AssignWorksheetAsync(request, scope);
    }
}
