using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.DirectMessages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.DirectMessages;

/// <inheritdoc cref="IDirectMessagePolicy"/>
public sealed class DirectMessagePolicy : IDirectMessagePolicy
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _time;
    private readonly bool _allowSameSchool;

    public DirectMessagePolicy(AppDbContext context, TimeProvider? time = null, IOptions<DirectMessagingOptions>? options = null)
    {
        _context = context;
        _time = time ?? TimeProvider.System;
        // #361: öğrenci okul üyeliği doğrulanana kadar B yolu kapalı (varsayılan false); bkz. DirectMessagingOptions.
        _allowSameSchool = options?.Value.AllowSameSchoolMessaging ?? false;
    }

    public async Task<StudentMessagingContext?> ResolveStudentAsync(int studentUserId, CancellationToken ct = default)
    {
        if (studentUserId <= 0)
            return null;

        // Canlı satır tektir (#259 filtreli unique index); OrderBy deterministik seçim için.
        var student = await _context.Students.AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.GradeId })
            .FirstOrDefaultAsync(ct);
        if (student == null)
            return null;

        // Okulun TEK tanımı (#326/#334): öğretmen satırı önce, sonra öğrenci satırı; belirsiz → null (B yolu kapanır).
        var schoolId = await UserSchoolResolver.ResolveAsync(_context, studentUserId, ct);
        return new StudentMessagingContext(studentUserId, student.Id, student.GradeId, schoolId);
    }

    public IQueryable<RelatedTeacherRow> RelatedTeachers(StudentMessagingContext student, DateTime nowUtc, bool excludeBlocked = true)
    {
        ArgumentNullException.ThrowIfNull(student);
        var studentUserId = student.UserId;

        // (A) öğrenciyi şu an kapsayan aktif atamaların sahibi (atayan) öğretmenler. Temel ActiveAssignmentsFor (öğrencinin
        // "atanan testler" tanımı) + platform geneli (IsPlatformWide) atamalar HARİÇ (code review W1): #277 migration'ı eski
        // okulsuz sınıf atamalarını platform geneli yaptı; onları saymak atayan öğretmeni tüm okulların o sınıfına açardı.
        var assignerUserIds = _context.ActiveAssignmentsFor(student.StudentId, student.GradeId, student.SchoolId, nowUtc)
            .Where(a => !a.IsPlatformWide && a.CreateUserId != null && a.CreateUserId > 0)
            .Select(a => a.CreateUserId!.Value);

        // Onaylı/aktif öğretmen: ApprovedTeacherGuard'ın Approved kararıyla aynı iki kolon (AccountApprovedAt dolu, AccountSuspendedAt
        // boş), satır bazında SQL'de (#287/#289). Guard'dan farkı: kullanıcının birden çok canlı satırı olursa guard ilkine bakar,
        // burada her satır kendi kolonlarıyla değerlendirilir (#259 unique index bunu pratikte önler).
        var teachers = _context.Teachers.AsNoTracking()
            .Where(t => t.AccountApprovedAt != null && t.AccountSuspendedAt == null && t.UserId != studentUserId);

        if (excludeBlocked)
        {
            teachers = teachers.Where(t => !_context.DirectMessageBlocks
                .Any(b => b.TeacherUserId == t.UserId && b.StudentUserId == studentUserId));
        }

        // (B) yalnız bayrak açıkken (#361). Kapalıyken okullu öğrenci de okulsuz gibi yalnız (A) yolunu görür; okul yine
        // sınıf+okul hedefli atamaların eşleşmesi için ActiveAssignmentsFor'a verilir (yukarıda).
        if (_allowSameSchool && student.SchoolId is int schoolId)
        {
            return teachers
                .Where(t => t.SchoolId == schoolId || assignerUserIds.Contains(t.UserId))
                .Select(t => new RelatedTeacherRow
                {
                    TeacherId = t.Id,
                    TeacherUserId = t.UserId,
                    ViaSchool = t.SchoolId == schoolId,
                    ViaAssignment = assignerUserIds.Contains(t.UserId)
                });
        }

        // Okulsuz (ya da okulu belirsiz) öğrenci ya da B bayrağı kapalı: yalnız A — null=null aynı okul sayılmaz.
        return teachers
            .Where(t => assignerUserIds.Contains(t.UserId))
            .Select(t => new RelatedTeacherRow
            {
                TeacherId = t.Id,
                TeacherUserId = t.UserId,
                ViaSchool = false,
                ViaAssignment = true
            });
    }

    public Task<bool> CanMessageAsync(StudentMessagingContext student, int teacherUserId, CancellationToken ct = default)
        => RelatedTeachers(student, _time.GetUtcNow().UtcDateTime, excludeBlocked: true)
            .AnyAsync(r => r.TeacherUserId == teacherUserId, ct);

    public Task<bool> HasActiveRelationAsync(StudentMessagingContext student, int teacherUserId, CancellationToken ct = default)
        => RelatedTeachers(student, _time.GetUtcNow().UtcDateTime, excludeBlocked: false)
            .AnyAsync(r => r.TeacherUserId == teacherUserId, ct);
}
