using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Tenancy;

namespace ExamApp.Api.Services.Coaching;

public static class SchoolAccessPolicyCoachExtensions
{
    /// <summary>
    /// Issue #696, #700 (kullanıcı kararı 2026-11-05): öğretmen kendi okulundaki öğrenciyi ya da özel ders verdiği (Approved Booking)
    /// öğrenciyi görür. Okullu öğretmenin kapsamı okul eşitliğidir; özel ders ilişkisi bağımsız kapsamda (#192) tanımlı, ikisi de geçerli.
    /// </summary>
    public static async Task<bool> CanAccessAsTeacherOrTutorAsync(this ISchoolAccessPolicy policy, SchoolScope scope, int studentId, CancellationToken ct = default) =>
        await policy.CanAccessStudentAsync(scope, studentId, ct)
        || await policy.CanAccessStudentAsync(SchoolScope.For(scope.UserId, null), studentId, ct);
}
