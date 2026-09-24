using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #259: öğrenci okul kilidi (<c>POST student/register</c> tekrar çağrısı okulu değiştiremez) ve
/// Teachers/Students.UserId filtreli unique index ihlalinin register akışlarında 409'a (Conflict) eşlenmesi.
/// </summary>
public class RegistrationSchoolLockTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static StudentService NewStudentService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx));

    private static TeacherService NewTeacherService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>());

    private async Task<(int SchoolA, int SchoolB, int GradeId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A Okulu" };
        var b = new School { Name = "B Okulu" };
        var g = new Grade { Name = "7" };
        ctx.AddRange(a, b, g);
        await ctx.SaveChangesAsync();
        return (a.Id, b.Id, g.Id);
    }

    private async Task<ResponseBaseDto> RegisterStudentAsync(int userId, int? schoolId, int gradeId, string number = "n1")
    {
        await using var ctx = _db.NewContext();
        return await NewStudentService(ctx).Save(userId,
            new RegisterStudentDto { StudentNumber = number, SchoolId = schoolId, GradeId = gradeId });
    }

    private async Task<Student> SingleStudentAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Students.AsNoTracking().SingleAsync(s => s.UserId == userId);
    }

    [Fact]
    public async Task Student_without_school_can_get_a_school_on_the_first_assignment()
    {
        var (schoolA, _, gradeId) = await SeedAsync();
        (await RegisterStudentAsync(500, null, gradeId)).Success.ShouldBeTrue();

        var r = await RegisterStudentAsync(500, schoolA, gradeId, "n2");

        r.Success.ShouldBeTrue();
        r.Conflict.ShouldBeFalse();
        var student = await SingleStudentAsync(500);
        student.SchoolId.ShouldBe(schoolA);
        student.StudentNumber.ShouldBe("n2");
    }

    [Fact]
    public async Task Student_re_registering_with_the_same_school_is_a_no_op_for_the_school_and_updates_other_fields()
    {
        var (schoolA, _, gradeId) = await SeedAsync();
        (await RegisterStudentAsync(501, schoolA, gradeId)).Success.ShouldBeTrue();

        var r = await RegisterStudentAsync(501, schoolA, gradeId, "n-new");

        r.Success.ShouldBeTrue();
        var student = await SingleStudentAsync(501);
        student.SchoolId.ShouldBe(schoolA);
        student.StudentNumber.ShouldBe("n-new");
    }

    [Fact]
    public async Task Student_with_a_school_cannot_switch_to_a_different_school_409()
    {
        var (schoolA, schoolB, gradeId) = await SeedAsync();
        (await RegisterStudentAsync(502, schoolA, gradeId)).Success.ShouldBeTrue();

        var r = await RegisterStudentAsync(502, schoolB, gradeId, "n-hijack");

        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Okul bilgisi kayıttan sonra değiştirilemez. Okul değişikliği için sistem yöneticisiyle iletişime geçin.");
        var student = await SingleStudentAsync(502);
        student.SchoolId.ShouldBe(schoolA);
        student.StudentNumber.ShouldBe("n1"); // reddedilen istek hiçbir alanı değiştirmez
    }

    [Fact]
    public async Task Student_with_a_school_cannot_clear_it_so_the_lock_cannot_be_bypassed_via_null()
    {
        var (schoolA, schoolB, gradeId) = await SeedAsync();
        (await RegisterStudentAsync(503, schoolA, gradeId)).Success.ShouldBeTrue();

        var cleared = await RegisterStudentAsync(503, null, gradeId);
        cleared.Success.ShouldBeFalse();
        cleared.Conflict.ShouldBeTrue();

        // X → null → Y zinciri de çalışmaz: okul hâlâ A.
        (await RegisterStudentAsync(503, schoolB, gradeId)).Conflict.ShouldBeTrue();
        (await SingleStudentAsync(503)).SchoolId.ShouldBe(schoolA);
    }

    [Fact]
    public async Task Student_unique_violation_from_a_concurrent_first_registration_maps_to_409()
    {
        var (schoolA, schoolB, gradeId) = await SeedAsync();
        var interceptor = new ConcurrentDuplicateInterceptor(ctx =>
            ctx.Add(new Student { UserId = 504, StudentNumber = "concurrent", SchoolId = schoolB, GradeId = gradeId }));

        ResponseBaseDto r;
        await using (var ctx = _db.NewContext(interceptor))
        {
            r = await NewStudentService(ctx).Save(504,
                new RegisterStudentDto { StudentNumber = "n1", SchoolId = schoolA, GradeId = gradeId });
        }

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Kaydınız aynı anda başka bir istekle güncellendi. Lütfen sayfayı yenileyip tekrar deneyin.");

        await using var check = _db.NewContext();
        check.Students.Count(s => s.UserId == 504).ShouldBe(0); // aynı batch geri alındı, çift satır yok
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // bağımsız öğretmen yolu: normalde outbox event'leri de yazılır
    public async Task Teacher_unique_violation_from_a_concurrent_first_registration_maps_to_409_without_outbox(bool independent)
    {
        var (schoolA, _, _) = await SeedAsync();
        var userId = independent ? 515 : 505;
        var interceptor = new ConcurrentDuplicateInterceptor(ctx =>
            ctx.Add(new Teacher { UserId = userId, SchoolId = schoolA }));

        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContext(interceptor))
        {
            r = await NewTeacherService(ctx).Save(userId, independent
                ? new RegisterTeacherDto { IsIndependentTutor = true }
                : new RegisterTeacherDto { SchoolId = schoolA });
        }

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Kaydınız aynı anda başka bir istekle oluşturuldu. Lütfen sayfayı yenileyip tekrar deneyin.");

        await using var check = _db.NewContext();
        check.Teachers.Count(t => t.UserId == userId).ShouldBe(0);
        check.OutboxMessages.Count().ShouldBe(0); // transaction geri alındı, event yazılmadı
    }

    [Theory]
    [InlineData(true, false)]  // eşzamanlı istek FARKLI okul yazdı → 409, onun okulu kalır
    [InlineData(false, true)]  // eşzamanlı istek AYNI okulu yazdı → idempotent başarı
    public async Task Student_first_school_assignment_is_conditional_under_a_concurrent_writer(bool otherSchool, bool expectSuccess)
    {
        var (schoolA, schoolB, gradeId) = await SeedAsync();
        (await RegisterStudentAsync(520, null, gradeId)).Success.ShouldBeTrue();
        var concurrentSchool = otherSchool ? schoolB : schoolA;
        var interceptor = new ConcurrentSchoolWriteInterceptor(520, concurrentSchool);

        ResponseBaseDto r;
        await using (var ctx = _db.NewContext(interceptor))
        {
            r = await NewStudentService(ctx).Save(520,
                new RegisterStudentDto { StudentNumber = "n2", SchoolId = schoolA, GradeId = gradeId });
        }

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBe(expectSuccess);
        r.Conflict.ShouldBe(!expectSuccess);
        if (!expectSuccess)
            r.Message.ShouldBe("Okul bilgisi kayıttan sonra değiştirilemez. Okul değişikliği için sistem yöneticisiyle iletişime geçin.");

        var student = await SingleStudentAsync(520);
        student.SchoolId.ShouldBe(concurrentSchool);
        student.StudentNumber.ShouldBe(expectSuccess ? "n2" : "n1");
    }

    [Fact]
    public async Task Filtered_unique_index_allows_a_new_live_row_next_to_a_soft_deleted_one()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 506, IsDeleted = true });
            ctx.Teachers.Add(new Teacher { UserId = 506 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 506 });
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            ExamApp.Api.Helpers.DbUpdateExceptionClassifier.IsUniqueViolation(ex).ShouldBeTrue();
        }
    }

    /// <summary>
    /// "Eşzamanlı ikinci istek": servis mevcut satırı sorgulayıp bulamadıktan SONRA, SaveChanges'e girerken aynı UserId
    /// ile canlı bir satır daha eklenir. TestDb tek bir SQLite bağlantısı paylaştığı (ve öğretmen akışı açık bir
    /// transaction içinde kaydettiği) için ikinci satır ayrı context yerine aynı context'e eklenir — sonuç aynı:
    /// unique index INSERT'i reddeder.
    /// </summary>
    private sealed class ConcurrentDuplicateInterceptor(Action<DbContext> addDuplicate) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                addDuplicate(eventData.Context!);
            }

            return ValueTask.FromResult(result);
        }
    }
    /// <summary>
    /// Koşullu okul UPDATE'i (ExecuteUpdate) çalışmadan hemen önce, "başka bir istek" gibi aynı öğrenciye okul yazar.
    /// </summary>
    private sealed class ConcurrentSchoolWriteInterceptor(int userId, int schoolId) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE \"Students\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"SchoolId\" IS NULL", StringComparison.Ordinal))
            {
                Fired = true;
                await using var concurrent = command.Connection!.CreateCommand();
                concurrent.CommandText = $"UPDATE \"Students\" SET \"SchoolId\" = {schoolId} WHERE \"UserId\" = {userId}";
                await concurrent.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
