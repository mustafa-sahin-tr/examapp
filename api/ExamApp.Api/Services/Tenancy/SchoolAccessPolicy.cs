using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Tenancy;

/// <summary>
/// issue #190 varsayılan kural: okul eşitliği. issue #192 eklentisi: bağımsız (okulsuz) öğretmenin öğrencisi
/// = en az bir <see cref="BookingStatus.Approved"/> Booking'i olan öğrenci. Bkz. <see cref="ISchoolAccessPolicy"/>.
/// <para>
/// Booking zinciri: <c>Booking.TeacherId → Teacher.Id</c> (UserId değil); istek sahibi ise
/// <see cref="SchoolScope.UserId"/> taşır, bu yüzden alt sorgu <c>b.Teacher.UserId</c> üzerinden bağlanır.
/// <c>Teachers.UserId</c> UNIQUE DEĞİLDİR (migration <c>20251025161204_change-user</c> index'i düşürdü): aynı UserId'li
/// birden fazla Teacher satırı olabilir; EXISTS alt sorgusu bunların herhangi birinin Approved booking'ini sayar,
/// tekillik varsayımı yoktur.
/// </para>
/// <para>
/// Soft-delete kararları: Booking'in kendi <c>IsDeleted</c>'ı global query filter ile dışarıda. Booking'in bağlı olduğu
/// Teacher soft-delete edilmişse booking SAYILMAZ (bilinçli — silinmiş öğretmen kimliği üzerinden kapsam kazanılmaz;
/// filtre alt sorguda açıkça uygulanır, EF'in navigasyon filtresine güvenilmez). Slot soft-delete edilmiş ya da tarih
/// geçmişte olsa da Approved booking "öğrencim" ilişkisini kurar (ders verilmiş öğrenci ilişkisi kalıcıdır).
/// </para>
/// </summary>
public sealed class SchoolAccessPolicy : ISchoolAccessPolicy
{
    private readonly AppDbContext _context;

    public SchoolAccessPolicy(AppDbContext context)
    {
        _context = context;
    }

    public bool CanAccess(SchoolScope requester, int? targetSchoolId)
    {
        if (requester.IsUnrestricted)
            return true;

        // null == null → okulsuz kullanıcı yalnızca okulsuz kayıtları görür (okulsuz→okullu red).
        // Öğrenci hedefi için bu yeterli DEĞİL (#192) — CanAccessStudentAsync kullanılmalı.
        return requester.SchoolId == targetSchoolId;
    }

    public async Task<bool> CanAccessStudentAsync(SchoolScope requester, int studentId, CancellationToken ct = default)
    {
        if (requester.IsUnrestricted)
            return true;

        // Var/yok ve kapsam kararı tek sorguda: kapsam dışı öğrenci, olmayan öğrenci ve soft-delete edilmiş öğrenci
        // (global filter) aynı sonucu (false) verir.
        return await ApplyScope(_context.Students.AsNoTracking(), requester)
            .AnyAsync(s => s.Id == studentId, ct);
    }

    public IQueryable<T> ApplyScope<T>(IQueryable<T> query, SchoolScope requester) where T : class, ISchoolScoped
    {
        if (requester.IsUnrestricted)
            return query;

        // issue #192: bağımsız istek sahibi için Student sorgusu okul eşitliğiyle değil,
        // Approved Booking ilişkisiyle daraltılır. Diğer ISchoolScoped tipler (Teacher) için #190 kuralı sürer.
        if (requester.IsIndependent && query is IQueryable<Student> students)
            return (IQueryable<T>)(object)ApplyApprovedBookingScope(students, requester.UserId);

        // Parametre null ise EF Core "SchoolId IS NULL" üretir (okulsuz → yalnızca okulsuz kayıtlar).
        var schoolId = requester.SchoolId;
        return query.Where(x => x.SchoolId == schoolId);
    }

    /// <summary>
    /// Bağımsız öğretmenin öğrencileri: istek sahibinin (herhangi bir aktif Teacher satırı, Teacher.UserId) en az bir
    /// Approved Booking'i olan öğrenciler. Öğrencinin kendi SchoolId'si kararı etkilemez — okullu bir öğrenci de
    /// bağımsız öğretmenden ders alabilir. Alt sorgu SQL düzeyinde (EXISTS) — Skip/Take ile tutarlı.
    /// </summary>
    private IQueryable<Student> ApplyApprovedBookingScope(IQueryable<Student> students, int teacherUserId)
    {
        return students.Where(s => _context.Bookings.Any(b =>
            b.StudentId == s.Id
            && b.Status == BookingStatus.Approved
            && b.Teacher.UserId == teacherUserId
            && !b.Teacher.IsDeleted));
    }
}
