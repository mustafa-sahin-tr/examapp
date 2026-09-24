using System.Data.Common;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 (madde 9): eşzamanlı öğretmen + öğrenci kaydı yarışı. İki kayıt akışı da "karşı tabloda satır var mı?"
/// kontrolünü kullanıcı kaydı kilidi (<see cref="UserRegistrationLock"/>, Postgres'te pg_advisory_xact_lock) altında,
/// transaction içinde TEKRARLAR. SQLite'ta kilit no-op; yarış, kilitsiz ön kontrol ile transaction arasına "diğer isteğin
/// commit ettiği" satırı araya sokan bir interceptor ile üretilir (gerçek eşzamanlılık: IntegrationTests).
/// Ayrıca akışların retry-on-failure strategy altında (Aspire Npgsql) transaction'ı strategy İÇİNDE açtığı doğrulanır.
/// </summary>
public class RegistrationRoleExclusivityRaceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static StudentService NewStudentService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx));

    private static TeacherService NewTeacherService(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>());

    private async Task<int> SeedGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = "7" };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    /// <summary>
    /// Transaction BAŞLAMADAN hemen önce (servisin kilitsiz ön kontrolünden SONRA) "eşzamanlı diğer istek" gibi ayrı bir
    /// context'le karşı tabloya satır yazar ve commit eder (TestDb tek SQLite bağlantısı paylaşır; o an açık transaction yok).
    /// </summary>
    private sealed class ConcurrentOtherRoleInterceptor(Func<Task> writeOther) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await writeOther();
            }

            return result;
        }
    }

    [Fact]
    public async Task Student_registration_losing_the_race_to_a_teacher_registration_writes_nothing_and_returns_409()
    {
        var gradeId = await SeedGradeAsync();
        var interceptor = new ConcurrentOtherRoleInterceptor(async () =>
        {
            await using var other = _db.NewContext();
            other.Teachers.Add(new Teacher { UserId = 800 });
            await other.SaveChangesAsync();
        });

        ResponseBaseDto r;
        await using (var ctx = _db.NewContext(interceptor))
            r = await NewStudentService(ctx).Save(800, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId });

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Öğretmen kaydı olan bir hesap öğrenci olarak kaydolamaz.");

        await using var check = _db.NewContext();
        (await check.Students.CountAsync(s => s.UserId == 800)).ShouldBe(0);
        (await check.Teachers.CountAsync(t => t.UserId == 800)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // bağımsız: normalde outbox event'leri de yazılır — geri alınmalı
    public async Task Teacher_registration_losing_the_race_to_a_student_registration_writes_nothing_and_returns_409(bool independent)
    {
        var gradeId = await SeedGradeAsync();
        var interceptor = new ConcurrentOtherRoleInterceptor(async () =>
        {
            await using var other = _db.NewContext();
            other.Students.Add(new Student { UserId = 801, StudentNumber = "n", GradeId = gradeId });
            await other.SaveChangesAsync();
        });

        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContext(interceptor))
            r = await NewTeacherService(ctx).Save(801, new RegisterTeacherDto { IsIndependentTutor = independent });

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Öğrenci kaydı olan bir hesap öğretmen olarak kaydolamaz.");

        await using var check = _db.NewContext();
        (await check.Teachers.CountAsync(t => t.UserId == 801)).ShouldBe(0);
        (await check.Students.CountAsync(s => s.UserId == 801)).ShouldBe(1);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Student_first_registration_runs_its_transaction_inside_the_retrying_execution_strategy()
    {
        var gradeId = await SeedGradeAsync();

        ResponseBaseDto r;
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            r = await NewStudentService(ctx).Save(802, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId });

        r.Success.ShouldBeTrue();
        r.ObjectId.ShouldBeGreaterThan(0);
        await using var check = _db.NewContext();
        (await check.Students.CountAsync(s => s.UserId == 802)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teacher_registration_runs_its_transaction_inside_the_retrying_execution_strategy(bool independent)
    {
        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            r = await NewTeacherService(ctx).Save(803, new RegisterTeacherDto { IsIndependentTutor = independent });

        r.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.Teachers.CountAsync(t => t.UserId == 803)).ShouldBe(1);
    }

    [Fact]
    public async Task Registration_lock_requires_an_open_transaction()
    {
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() => ctx.Database.AcquireUserRegistrationLockAsync(1));
    }

    [Fact]
    public async Task Registration_lock_is_a_no_op_on_non_postgres_providers_inside_a_transaction()
    {
        await using var ctx = _db.NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.Database.AcquireUserRegistrationLockAsync(1); // atmaz
    }
}
