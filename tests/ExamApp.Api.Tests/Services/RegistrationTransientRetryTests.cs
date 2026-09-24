using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 takip: kayıt akışlarının execution strategy delegate'i retry-güvenli mi? İlk denemede tüm SaveChanges'ler
/// başarılı, COMMIT geçici hatayla düşer (<see cref="FailFirstCommitInterceptor"/>) → transaction geri alınır, strategy
/// delegate'i baştan çalıştırır. İkinci deneme her şeyi (öğretmen/öğrenci satırı + outbox) YENİDEN yazmalı; eskiden
/// ilk denemenin kabul edilmiş (Unchanged) entity'leri yüzünden satırlar sessizce kaybolabiliyordu.
/// </summary>
public class RegistrationTransientRetryTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        var s = new School { Name = $"S{Guid.NewGuid():N}" };
        ctx.Schools.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    [Theory]
    [InlineData(true)]  // bağımsız: teacher + IndependentTeacherRegistered + TeacherApplicationSubmitted
    [InlineData(false)] // okul talebi: teacher + TeacherSchoolRequestSubmitted
    public async Task New_teacher_registration_survives_a_transient_commit_failure(bool independent)
    {
        var school = await SeedSchoolAsync();
        var interceptor = new FailFirstCommitInterceptor();

        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            r = await new TeacherService(ctx, _authApi).Save(900, independent
                ? new RegisterTeacherDto { IsIndependentTutor = true }
                : new RegisterTeacherDto { SchoolId = school });

        interceptor.Failures.ShouldBe(1);
        r.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var teacher = await check.Teachers.AsNoTracking().SingleAsync(t => t.UserId == 900);
        r.ObjectId.ShouldBe(teacher.Id);
        var outbox = await check.OutboxMessages.AsNoTracking().ToListAsync();
        if (independent)
        {
            outbox.Select(m => m.Type).ShouldBe(new[]
            {
                OutboxEventRegistry.NameFor<IndependentTeacherRegisteredEvent>(),
                OutboxEventRegistry.NameFor<TeacherApplicationSubmittedEvent>()
            }, ignoreOrder: true);
        }
        else
        {
            var e = JsonSerializer.Deserialize<TeacherSchoolRequestSubmittedEvent>(outbox.ShouldHaveSingleItem().Content)!;
            e.TeacherId.ShouldBe(teacher.Id);
            e.RequestedSchoolId.ShouldBe(school);
        }
    }

    [Fact]
    public async Task Existing_teacher_switch_to_independent_survives_a_transient_commit_failure()
    {
        var school = await SeedSchoolAsync();
        await using (var seed = _db.NewContext())
        {
            seed.Teachers.Add(new Teacher
            {
                UserId = 901, SchoolId = school, ApprovalStatus = TeacherApprovalStatus.Approved,
                AccountApprovedAt = DateTime.UtcNow, ThemePreset = "minimal"
            });
            await seed.SaveChangesAsync();
        }

        var interceptor = new FailFirstCommitInterceptor();
        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            r = await new TeacherService(ctx, _authApi).Save(901, new RegisterTeacherDto { IsIndependentTutor = true });

        interceptor.Failures.ShouldBe(1);
        r.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var teacher = await check.Teachers.AsNoTracking().SingleAsync(t => t.UserId == 901);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.SchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.ThemePreset.ShouldBe("minimal"); // kararla değişmeyen alanlar yeniden okunan kayıttan korunur
        (await check.OutboxMessages.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task New_student_registration_survives_a_transient_commit_failure()
    {
        int gradeId;
        await using (var seed = _db.NewContext())
        {
            var g = new Grade { Name = "7" };
            seed.Grades.Add(g);
            await seed.SaveChangesAsync();
            gradeId = g.Id;
        }

        var interceptor = new FailFirstCommitInterceptor();
        ResponseBaseDto r;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            r = await new StudentService(ctx, _authApi, new SchoolAccessPolicy(ctx))
                .Save(902, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId });

        interceptor.Failures.ShouldBe(1);
        r.Success.ShouldBeTrue();
        await using var check = _db.NewContext();
        (await check.Students.AsNoTracking().SingleAsync(s => s.UserId == 902)).Id.ShouldBe(r.ObjectId);
    }

    /// <summary>Her SaveChanges denemesinde eklenen okul talebi event'lerinin EventId'lerini toplar (geri alınan deneme dahil).</summary>
    private sealed class SchoolRequestEventIdCapture : SaveChangesInterceptor
    {
        public List<Guid> EventIds { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<ExamApp.Foundation.Persistence.OutboxMessage>())
            {
                if (entry.State == EntityState.Added
                    && entry.Entity.Type == OutboxEventRegistry.NameFor<TeacherSchoolRequestSubmittedEvent>())
                    EventIds.Add(JsonSerializer.Deserialize<TeacherSchoolRequestSubmittedEvent>(entry.Entity.Content)!.EventId);
            }

            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task School_request_event_id_is_stable_across_a_transient_commit_retry()
    {
        // issue #277 review (NIT): retry'da aynı talep için AYNI EventId yazılır (tüketici tekilleştirmesi).
        var school = await SeedSchoolAsync();
        var commitFailure = new FailFirstCommitInterceptor();
        var capture = new SchoolRequestEventIdCapture();

        await using (var ctx = _db.NewContextWithTransientRetry(commitFailure, capture))
            (await new TeacherService(ctx, _authApi).Save(903, new RegisterTeacherDto { SchoolId = school })).Success.ShouldBeTrue();

        commitFailure.Failures.ShouldBe(1);
        capture.EventIds.Count.ShouldBe(2); // geri alınan deneme + başarılı deneme
        capture.EventIds.Distinct().ShouldHaveSingleItem();

        await using var check = _db.NewContext();
        var stored = JsonSerializer.Deserialize<TeacherSchoolRequestSubmittedEvent>(
            (await check.OutboxMessages.AsNoTracking().SingleAsync()).Content)!;
        stored.EventId.ShouldBe(capture.EventIds[0]);
    }

    /// <summary>Transaction açılmadan hemen önce (kilitsiz karar okumasından SONRA) "eşzamanlı diğer istek" gibi satırı değiştirir.</summary>
    private sealed class ConcurrentTeacherChange(Func<Task> change) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await change();
            }

            return result;
        }
    }

    [Fact]
    public async Task Parallel_school_requests_second_request_is_rejected_under_the_lock_without_a_second_event()
    {
        // issue #277 review (NIT 4): iki paralel okul talebinden kaybeden, kilit altında güncel satırı görüp 409 alır —
        // ikinci admin bildirimi (event) yazılmaz, birincinin talebi ezilmez.
        var schoolA = await SeedSchoolAsync();
        var schoolB = await SeedSchoolAsync();
        await using (var seed = _db.NewContext())
        {
            seed.Teachers.Add(new Teacher { UserId = 904, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        var interceptor = new ConcurrentTeacherChange(async () =>
        {
            await using var other = _db.NewContext();
            await other.Teachers.Where(t => t.UserId == 904).ExecuteUpdateAsync(set => set
                .SetProperty(t => t.RequestedSchoolId, schoolA)
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Pending));
        });

        TeacherRegistrationResultDto r;
        await using (var ctx = _db.NewContext(interceptor))
            r = await new TeacherService(ctx, _authApi).Save(904, new RegisterTeacherDto { SchoolId = schoolB });

        interceptor.Fired.ShouldBeTrue();
        r.Success.ShouldBeFalse();
        r.Conflict.ShouldBeTrue();
        r.Message.ShouldBe("Kaydınız aynı anda başka bir istekle oluşturuldu. Lütfen sayfayı yenileyip tekrar deneyin.");

        await using var check = _db.NewContext();
        (await check.Teachers.AsNoTracking().SingleAsync(t => t.UserId == 904)).RequestedSchoolId.ShouldBe(schoolA);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }
}
