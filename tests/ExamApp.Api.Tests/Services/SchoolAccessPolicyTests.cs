using ExamApp.Api.Data;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190: merkezi okul izolasyonu kararı (SchoolAccessPolicy).
/// Matris: aynı okul (izin), farklı okul (red), okulsuz→okullu (red), okullu→okulsuz (red), admin/servis (izin, tüm okullar).
/// ApplyScope: filtre SQL düzeyinde — sayfalama (Take) farklı okul kayıtlarıyla dolmaz.
/// <para>
/// issue #192: bağımsız (okulsuz) istek sahibi için Student kapsamı okul eşitliği DEĞİL, Approved Booking'dir.
/// #190'daki "okulsuz→okulsuz izinli" Student testleri bu nedenle bilinçli olarak değişti
/// (ApplyScope_IndependentRequester_SeesOnlyIndependentRecords → ..._SeesOnlyApprovedBookingStudents,
/// ApplyScope_Independent_GeneratesIsNullPredicate → ..._GeneratesBookingsExistsPredicate). Teacher sorgusu ve
/// CanAccess(scope, schoolId) için #190 kuralı aynen sürer.
/// </para>
/// </summary>
public class SchoolAccessPolicyTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static SchoolAccessPolicy Policy(AppDbContext ctx) => new(ctx);

    // ---- CanAccess (okul eşitliği, #190 — değişmedi) ----

    [Fact]
    public async Task CanAccess_SameSchool_Allowed()
    {
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: 10).ShouldBeTrue();
    }

    [Fact]
    public async Task CanAccess_DifferentSchool_Denied()
    {
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: 20).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccess_IndependentRequester_SchoolBoundTarget_Denied()
    {
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.For(userId: 1, schoolId: null), targetSchoolId: 10).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccess_SchoolBoundRequester_IndependentTarget_Denied()
    {
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: null).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccess_IndependentRequester_IndependentTarget_Allowed()
    {
        // Okul eşitliği kuralı (Teacher hedefi, BookingService slots). Öğrenci hedefi için CanAccessStudentAsync (#192).
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.For(userId: 1, schoolId: null), targetSchoolId: null).ShouldBeTrue();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(null)]
    public async Task CanAccess_Unrestricted_AlwaysAllowed(int? targetSchoolId)
    {
        await using var ctx = _db.NewContext();
        Policy(ctx).CanAccess(SchoolScope.Unrestricted(userId: 1), targetSchoolId).ShouldBeTrue();
    }

    // ---- Seed ----

    private sealed record Seed(int SchoolA, int SchoolB, int StudentA1, int StudentA2, int StudentB1, int StudentN1, int StudentN2,
        int TutorUserId, int OtherTutorUserId, int SchoolTeacherAUserId);

    /// <summary>
    /// Okul A: A1, A2; Okul B: B1; okulsuz: N1, N2. Öğretmenler: 11 (A), 12 (B), 13 (bağımsız), 14 (bağımsız, diğer).
    /// Booking'ler (bağımsız 13): A1 Approved, N1 Approved, B1 Pending, N2 Rejected. Bağımsız 14: A2 Approved.
    /// Yani 13'ün öğrencileri = {A1, N1}; 14'ün öğrencileri = {A2}.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();

        var a1 = new Student { UserId = 1, StudentNumber = "A1", SchoolId = a.Id };
        var a2 = new Student { UserId = 2, StudentNumber = "A2", SchoolId = a.Id };
        var b1 = new Student { UserId = 3, StudentNumber = "B1", SchoolId = b.Id };
        var n1 = new Student { UserId = 4, StudentNumber = "N1", SchoolId = null };
        var n2 = new Student { UserId = 5, StudentNumber = "N2", SchoolId = null };
        ctx.Students.AddRange(a1, a2, b1, n1, n2);

        var tA = new Teacher { UserId = 11, SchoolId = a.Id };
        var tB = new Teacher { UserId = 12, SchoolId = b.Id };
        var tutor = new Teacher { UserId = 13, SchoolId = null, IsIndependentTutor = true };
        var otherTutor = new Teacher { UserId = 14, SchoolId = null, IsIndependentTutor = true };
        ctx.Teachers.AddRange(tA, tB, tutor, otherTutor);
        await ctx.SaveChangesAsync();

        BookingSeed.Add(ctx, tutor.Id, a1.Id, BookingStatus.Approved, 8);
        BookingSeed.Add(ctx, tutor.Id, n1.Id, BookingStatus.Approved, 9);
        BookingSeed.Add(ctx, tutor.Id, b1.Id, BookingStatus.Pending, 10);
        BookingSeed.Add(ctx, tutor.Id, n2.Id, BookingStatus.Rejected, 11);
        BookingSeed.Add(ctx, otherTutor.Id, a2.Id, BookingStatus.Approved, 8);
        await ctx.SaveChangesAsync();

        return new Seed(a.Id, b.Id, a1.Id, a2.Id, b1.Id, n1.Id, n2.Id, tutor.UserId, otherTutor.UserId, tA.UserId);
    }

    private static SchoolScope Tutor(Seed s) => SchoolScope.For(s.TutorUserId, null);

    // ---- ApplyScope: okullu / admin (#190 — değişmedi) ----

    [Fact]
    public async Task ApplyScope_SameSchool_ReturnsOnlyThatSchool()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(s.SchoolTeacherAUserId, s.SchoolA))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "A2" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyScope_DifferentSchoolRecords_NeverReturned()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, s.SchoolB))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "B1" });
        students.ShouldNotContain("A1");
        students.ShouldNotContain("N1");
    }

    [Fact]
    public async Task ApplyScope_SchoolBoundTeacher_BookingsDoNotWidenScope()
    {
        // Regresyon: okullu öğretmen için #192 kuralı devreye girmez — Approved booking okul dışı öğrenciyi görünür yapmaz.
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var tA = await setup.Teachers.SingleAsync(t => t.UserId == s.SchoolTeacherAUserId);
            BookingSeed.Add(setup, tA.Id, s.StudentB1, BookingStatus.Approved, 8);
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(s.SchoolTeacherAUserId, s.SchoolA))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "A2" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyScope_Unrestricted_ReturnsAllSchools()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var count = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.Unrestricted(1)).CountAsync();

        count.ShouldBe(5);
    }

    [Fact]
    public async Task ApplyScope_WorksForTeachersToo()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var teachers = await Policy(ctx).ApplyScope(ctx.Teachers.AsNoTracking(), SchoolScope.For(99, s.SchoolA))
            .Select(t => t.UserId).ToListAsync();

        teachers.ShouldBe(new[] { 11 });
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_TeacherQuery_StillUsesSchoolEquality()
    {
        // #192 yalnızca Student sorgusunu değiştirir; Teacher için okulsuz→okulsuz (IS NULL) kuralı sürer.
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var query = Policy(ctx).ApplyScope(ctx.Teachers.AsNoTracking(), Tutor(s));
        var teachers = await query.Select(t => t.UserId).ToListAsync();
        var sql = query.ToQueryString();

        teachers.ShouldBe(new[] { 13, 14 }, ignoreOrder: true);
        sql.ShouldContain("IS NULL");
        sql.ShouldNotContain("Bookings");
    }

    // ---- ApplyScope: bağımsız öğretmen (#192) ----

    [Fact]
    public async Task ApplyScope_IndependentRequester_SeesOnlyApprovedBookingStudents()
    {
        // #190'da "yalnızca okulsuz öğrenciler" (N1) idi; #192: Approved Booking'i olanlar — öğrencinin okulu önemsiz.
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "N1" }, ignoreOrder: true);
        students.ShouldNotContain("B1", customMessage: "Pending sayılmaz");
        students.ShouldNotContain("N2", customMessage: "Rejected sayılmaz; okulsuz olması yetmez");
        students.ShouldNotContain("A2", customMessage: "başka bağımsız öğretmenin Approved booking'i sayılmaz");
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_WithoutAnyBooking_SeesNobody()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(999, null)).ToListAsync();

        students.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_SoftDeletedBooking_DoesNotCount()
    {
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var booking = await setup.Bookings.SingleAsync(b => b.StudentId == s.StudentA1 && b.Status == BookingStatus.Approved);
            setup.Bookings.Remove(booking); // BaseEntity → soft delete
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "N1" });
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_SoftDeletedSlot_BookingStillCounts()
    {
        // Karar: slot silinse de Approved booking "öğrencim" ilişkisini korur (ders verilmiş öğrenci kaybolmaz).
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            // Slot Restrict FK'lı; soft-delete'i doğrudan IsDeleted ile uygula (Remove tracked booking ile çakışır).
            var slotId = await setup.Bookings
                .Where(b => b.StudentId == s.StudentA1 && b.Status == BookingStatus.Approved)
                .Select(b => b.AvailabilitySlotId).SingleAsync();
            await setup.TeacherAvailabilitySlots.Where(x => x.Id == slotId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDeleted, true));
        }

        await using (var verify = _db.NewContext())
            (await verify.TeacherAvailabilitySlots.CountAsync()).ShouldBe(4, customMessage: "slot soft-delete edilmiş olmalı");

        await using var ctx = _db.NewContext();
        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "N1" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyScope_Independent_GeneratesBookingsExistsPredicate()
    {
        // #190'da "SchoolId IS NULL" idi; #192: Student sorgusunda okul koşulu yok, Bookings EXISTS alt sorgusu var.
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s)).ToQueryString();

        sql.ShouldContain("Bookings");
        sql.ShouldContain("EXISTS");
        sql.ShouldContain("Teachers"); // TeacherId → Teacher.UserId zinciri SQL'de
        var wherePart = sql[sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)..];
        wherePart.ShouldNotContain("\"SchoolId\" IS NULL");
    }

    [Fact]
    public async Task ApplyScope_Independent_IsQueryLevel_TakeIsNotFilledByOtherStudents()
    {
        // Sonradan süzme olsaydı OrderBy(StudentNumber).Take(1) "A1"... değil, kapsam dışı ilk kaydı alıp boş dönebilirdi.
        // Kapsam dışı öğrenciler sıralamada araya girsin diye Skip(1) ile ikinci öğrenciye bakıyoruz: A2 (kapsam dışı) atlanmalı.
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var page = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s))
            .OrderBy(x => x.StudentNumber)
            .Skip(1)
            .Take(1)
            .Select(x => x.StudentNumber)
            .ToListAsync();

        page.ShouldBe(new[] { "N1" });
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_ApprovedAndRejectedOnSameStudent_StillVisible()
    {
        // "En az bir Approved" kuralı: aynı öğrencide sonradan Rejected bir booking daha olsa da öğrenci görünür kalır.
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var tutor = await setup.Teachers.SingleAsync(t => t.UserId == s.TutorUserId);
            BookingSeed.Add(setup, tutor.Id, s.StudentA1, BookingStatus.Rejected, 14);
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "N1" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_SoftDeletedTeacher_BookingDoesNotCount()
    {
        // Bilinçli karar: silinmiş Teacher satırı üzerinden kapsam kazanılmaz (alt sorguda !Teacher.IsDeleted açık).
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            await setup.Teachers.Where(t => t.UserId == s.TutorUserId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.IsDeleted, true));
        }

        await using var ctx = _db.NewContext();
        var students = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), Tutor(s)).ToListAsync();

        students.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_TwoTeacherRowsSameUserId_OnlyLiveRowsBookingsCount()
    {
        // Teachers.UserId UNIQUE değil (20251025161204_change-user). Aynı UserId'li iki satır: eski (soft-deleted, booking'ler
        // onda) + yeni (aktif, booking'siz). Beklenen: silinmiş satırın booking'leri sayılmaz → boş; yeni satıra Approved
        // eklenince görünür. EXISTS herhangi bir AKTİF satırı eşler, tekillik varsayımı yok.
        var s = await SeedAsync();
        int newTeacherId;
        await using (var setup = _db.NewContext())
        {
            await setup.Teachers.Where(t => t.UserId == s.TutorUserId)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.IsDeleted, true));
            var reborn = new Teacher { UserId = s.TutorUserId, SchoolId = null, IsIndependentTutor = true };
            setup.Teachers.Add(reborn);
            await setup.SaveChangesAsync();
            newTeacherId = reborn.Id;
        }

        await using (var ctx1 = _db.NewContext())
            (await Policy(ctx1).ApplyScope(ctx1.Students.AsNoTracking(), Tutor(s)).ToListAsync()).ShouldBeEmpty();

        await using (var setup2 = _db.NewContext())
        {
            BookingSeed.Add(setup2, newTeacherId, s.StudentB1, BookingStatus.Approved, 15);
            await setup2.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var students = await Policy(ctx2).ApplyScope(ctx2.Students.AsNoTracking(), Tutor(s))
            .Select(x => x.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "B1" });
    }

    // ---- CanAccessStudentAsync (#192) ----

    [Fact]
    public async Task CanAccessStudentAsync_Independent_ApprovedBooking_Allowed()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentA1)).ShouldBeTrue();
        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentN1)).ShouldBeTrue();
    }

    [Fact]
    public async Task CanAccessStudentAsync_Independent_PendingBooking_Denied()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentB1)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_Independent_RejectedBooking_Denied()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentN2)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_Independent_OtherTutorsApprovedBooking_Denied()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentA2)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_Independent_NoBooking_Denied()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(SchoolScope.For(999, null), s.StudentN1)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_UnknownStudent_Denied_ForAllNonUnrestricted()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), 99999)).ShouldBeFalse();
        (await Policy(ctx).CanAccessStudentAsync(SchoolScope.For(11, s.SchoolA), 99999)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_SoftDeletedStudent_Denied()
    {
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            await setup.Students.Where(x => x.Id == s.StudentA1)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDeleted, true));
        }

        await using var ctx = _db.NewContext();
        (await Policy(ctx).CanAccessStudentAsync(Tutor(s), s.StudentA1)).ShouldBeFalse();
        (await Policy(ctx).CanAccessStudentAsync(SchoolScope.For(s.SchoolTeacherAUserId, s.SchoolA), s.StudentA1)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_SchoolBound_UsesSchoolEquality()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();
        var scopeA = SchoolScope.For(s.SchoolTeacherAUserId, s.SchoolA);

        (await Policy(ctx).CanAccessStudentAsync(scopeA, s.StudentA1)).ShouldBeTrue();
        (await Policy(ctx).CanAccessStudentAsync(scopeA, s.StudentB1)).ShouldBeFalse();
        (await Policy(ctx).CanAccessStudentAsync(scopeA, s.StudentN1)).ShouldBeFalse();
    }

    [Fact]
    public async Task CanAccessStudentAsync_Unrestricted_AlwaysAllowed()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await Policy(ctx).CanAccessStudentAsync(SchoolScope.Unrestricted(1), s.StudentB1)).ShouldBeTrue();
        (await Policy(ctx).CanAccessStudentAsync(SchoolScope.Unrestricted(1), s.StudentN2)).ShouldBeTrue();
    }

    // ---- SQL kanıtı (#190 — değişmedi) ----

    [Fact]
    public async Task ApplyScope_SchoolBound_FilterIsInGeneratedSql()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, s.SchoolA)).ToQueryString();

        sql.ShouldContain("SchoolId");
        sql.ShouldNotContain("IS NULL", customMessage: "okullu istek sahibi için IS NULL değil parametre eşitliği beklenir");
        sql.ShouldNotContain("Bookings");
    }

    [Fact]
    public async Task ApplyScope_Unrestricted_DoesNotAddSchoolPredicate()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.Unrestricted(1)).ToQueryString();

        // Global soft-delete filtresi dışında WHERE koşulu yok: "SchoolId" yalnızca SELECT listesinde geçer.
        var wherePart = sql[(sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) is var i && i >= 0 ? i : sql.Length)..];
        wherePart.ShouldNotContain("SchoolId");
        wherePart.ShouldNotContain("Bookings");
    }

    [Fact]
    public async Task ApplyScope_IsQueryLevel_TakeIsNotFilledByOtherSchools()
    {
        // Sonradan süzme olsaydı Take(1) ilk sıradaki (başka okul) kaydı alıp süzer ve boş dönerdi.
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var page = await Policy(ctx).ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, s.SchoolB))
            .OrderBy(x => x.StudentNumber)
            .Skip(0)
            .Take(1)
            .Select(x => x.StudentNumber)
            .ToListAsync();

        page.ShouldBe(new[] { "B1" });
    }
}
