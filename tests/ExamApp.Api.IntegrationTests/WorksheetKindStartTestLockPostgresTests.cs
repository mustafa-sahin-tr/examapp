using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #573 (review) — sayfa türü değişimi ile oturum başlatma gerçek PostgreSQL'de serileşir. Start-test oturum INSERT'ini,
/// tür değişimi ise kaydını <c>WorksheetKindLock</c> (<c>pg_advisory_xact_lock(573001, worksheetId)</c>) altında yapar.
/// Eşzamanlı çalıştırılan iki istekten sonra durum tutarlı olmalı: tür değişimi başarılıysa oturum (varsa) yeni türü görmüş
/// (süre kopyası 0), değişim 409 WorksheetKindLocked ile reddedildiyse tam bir oturum var ve tür Test kalmış.
/// </summary>
public class WorksheetKindStartTestLockPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 12;
    private const int TestDuration = 600;

    private static int NewId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int TeacherUserId, int StudentUserId, int StudentId, int GradeId, int WorksheetId, int BookId, int BookTestId);

    private Task<Seed> SeedAsync() => WithDbAsync(async db =>
    {
        var teacherUserId = NewId();
        var studentUserId = NewId();
        var grade = new Grade { Name = "5" };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var book = new Book { Name = $"Kitap {Guid.NewGuid():N}" };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        var bookTest = new BookTest { Name = "Test", BookId = book.Id };
        db.BookTests.Add(bookTest);
        await db.SaveChangesAsync();

        var student = new Student { UserId = studentUserId, StudentNumber = $"S{studentUserId}", SchoolName = "Okul", GradeId = grade.Id };
        var question = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet
        {
            Name = "Sayfa", Description = "d", GradeId = grade.Id, MaxDurationSeconds = TestDuration,
            Kind = WorksheetKind.Test, BookTestId = bookTest.Id,
        };
        db.AddRange(student, question, worksheet);
        await db.SaveChangesAsync();

        db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 });
        await db.SaveChangesAsync();

        // Oluşturan öğretmen: sahiplik ambient kullanıcıyla ezilebildiği için açıkça yazılır.
        await db.Worksheets.Where(w => w.Id == worksheet.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreateUserId, (int?)teacherUserId));

        return new Seed(teacherUserId, studentUserId, student.Id, grade.Id, worksheet.Id, book.Id, bookTest.Id);
    });

    private async Task<TestStartResultDto> StartAsync(Seed seed)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        return await service.StartTestAsync(seed.WorksheetId, new StudentProfileDto { Id = seed.StudentId, GradeId = seed.GradeId });
    }

    private async Task<ExamSavedDto> ChangeToActivityAsync(Seed seed)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorksheetAuthoringService>();
        return await service.CreateOrUpdateAsync(new ExamDto
        {
            Id = seed.WorksheetId,
            Name = "Sayfa",
            Description = "d",
            GradeId = seed.GradeId,
            MaxDurationSeconds = TestDuration,
            BookId = seed.BookId,
            BookTestId = seed.BookTestId,
            Kind = WorksheetKind.Activity,
        }, seed.TeacherUserId, isAdmin: false);
    }

    private Task<(WorksheetKind Kind, List<int?> SessionDurations)> StateAsync(Seed seed) => WithDbAsync(async db =>
    {
        var kind = await db.Worksheets.AsNoTracking().IgnoreQueryFilters()
            .Where(w => w.Id == seed.WorksheetId).Select(w => w.Kind).SingleAsync();
        var durations = await db.TestInstances.AsNoTracking().IgnoreQueryFilters()
            .Where(ti => ti.WorksheetId == seed.WorksheetId)
            .Select(ti => ti.MaxDurationSeconds)
            .ToListAsync();
        return (kind, durations);
    });

    [Fact]
    public async Task Concurrent_start_test_and_kind_change_end_in_a_consistent_state()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync();

            var startTask = Task.Run(() => StartAsync(seed));
            var changeTask = Task.Run(() => ChangeToActivityAsync(seed));
            await Task.WhenAll(startTask, changeTask);

            var start = await startTask;
            var change = await changeTask;
            var (kind, durations) = await StateAsync(seed);

            // Oturum en fazla bir tane (IX_TestInstances_StudentId_WorksheetId).
            durations.Count.ShouldBeLessThanOrEqualTo(1, customMessage: $"round {round}");

            if (change.Success)
            {
                // Tür değişti. Oturum varsa yeni türü görmüş olmalı: süre kopyası 0 (Activity). Eski Test süresi (600) olamaz.
                kind.ShouldBe(WorksheetKind.Activity, customMessage: $"round {round}");
                foreach (var d in durations)
                    d.ShouldBe(0, customMessage: $"round {round}: oturum tür değişiminden önce açılmış olamaz");
            }
            else
            {
                // Değişim reddedildi: 409 WorksheetKindLocked, tür Test kaldı, tam bir oturum var (start-test kazandı).
                change.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked, customMessage: $"round {round}");
                kind.ShouldBe(WorksheetKind.Test, customMessage: $"round {round}");
                durations.Count.ShouldBe(1, customMessage: $"round {round}");
                durations[0].ShouldBe(TestDuration, customMessage: $"round {round}");
                start.ShouldNotBeNull();
            }
        }
    }

    [Fact]
    public async Task Kind_change_after_a_session_exists_is_refused_and_leaves_the_worksheet_untouched()
    {
        var seed = await SeedAsync();
        var started = await StartAsync(seed);
        started.ShouldNotBeNull();

        var change = await ChangeToActivityAsync(seed);

        change.Success.ShouldBeFalse();
        change.Conflict.ShouldBeTrue();
        change.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked);
        var (kind, durations) = await StateAsync(seed);
        kind.ShouldBe(WorksheetKind.Test);
        durations.ShouldBe(new int?[] { TestDuration });
    }

    [Fact]
    public async Task Kind_change_before_any_session_is_applied_and_the_later_session_copies_zero_duration()
    {
        var seed = await SeedAsync();

        var change = await ChangeToActivityAsync(seed);
        change.Success.ShouldBeTrue(change.Message);
        var started = await StartAsync(seed);

        started.ShouldNotBeNull();
        var (kind, durations) = await StateAsync(seed);
        kind.ShouldBe(WorksheetKind.Activity);
        durations.ShouldBe(new int?[] { 0 });
    }

    [Fact]
    public async Task Kind_change_waiting_on_the_lock_is_refused_when_a_session_commits_first()
    {
        // Deterministik: tür değişimi ön kontrolü (oturum yok) geçer, kilide girmeden önce başka bir transaction kilidi tutar
        // ve oturum (start-test'in kritik bölümü) yazıp commit eder. Kilit açılınca değişim oturumu görüp 409 dönmeli.
        var seed = await SeedAsync();

        var ready = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var strategy = holder.Database.CreateExecutionStrategy();
        var holderTask = strategy.ExecuteAsync(async () =>
        {
            await using var tx = await holder.Database.BeginTransactionAsync();
            // 573001 = WorksheetKindLock.LockClass (API'de internal; test projesi göremez).
            await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({573_001}, {seed.WorksheetId})");
            holder.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = seed.WorksheetId, StudentId = seed.StudentId, Status = WorksheetInstanceStatus.Started,
                StartTime = DateTime.UtcNow, MaxDurationSeconds = TestDuration,
            });
            await holder.SaveChangesAsync();
            ready.SetResult();
            await release.Task;
            await tx.CommitAsync();
        });

        await ready.Task;
        var change = Task.Run(() => ChangeToActivityAsync(seed));
        (await Task.WhenAny(change, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(change); // kilitte bekliyor

        release.SetResult();
        await holderTask;
        var result = await change;

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(WorksheetAuthoringErrorCodes.WorksheetKindLocked);
        var (kind, durations) = await StateAsync(seed);
        kind.ShouldBe(WorksheetKind.Test);
        durations.ShouldBe(new int?[] { TestDuration });
    }
}
