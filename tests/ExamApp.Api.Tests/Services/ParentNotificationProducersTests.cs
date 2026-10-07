using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #423 (epic #407 V5) — exam API tarafı: "test tamamlandı" event'i (<c>EndTest</c>) ve gecikmiş ödev süpürücüsü.
/// Ortak kural: yalnız Active bağlantılı veliler için, veli başına bir event; Pending/Revoked bağlantı bildirim almaz;
/// auth-api yoksa event yine yazılır (boş sub/ad). SQLite (gerçek ilişkisel davranış).
/// </summary>
public class ParentNotificationProducersTests : IDisposable
{
    private const int StudentUser = 8001;
    private const int ParentUserA = 8101;
    private const int ParentUserB = 8102;
    private const int ParentUserC = 8103;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    public ParentNotificationProducersTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id,
                KeycloakId = $"kc-{id}",
                FullName = id == StudentUser ? "Ayşe Kaya" : $"Veli {id}",
                Email = $"u{id}@example.com"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record World(int StudentId, int GradeId, int SchoolId, int WorksheetId, int Q1Correct, int ParentA, int ParentB, int ParentC);

    /// <summary>Öğrenci (doğrulanmış okul + sınıf), 3 veli (bağlantı yok), 2 soruluk test (Q1'in doğrusu belli).</summary>
    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var grade = new Grade { Name = "7" };
        ctx.AddRange(school, grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Kesirler Testi", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var q1 = new Question { Text = "Q1", Point = 10 };
        var q2 = new Question { Text = "Q2", Point = 10 };
        var parents = new[] { new Parent { UserId = ParentUserA }, new Parent { UserId = ParentUserB }, new Parent { UserId = ParentUserC } };
        ctx.AddRange(student, worksheet, q1, q2);
        ctx.Parents.AddRange(parents);
        await ctx.SaveChangesAsync();

        var a1 = new Answer { QuestionId = q1.Id, Text = "doğru", Tag = "A", Order = 0 };
        ctx.Answers.Add(a1);
        await ctx.SaveChangesAsync();
        q1.CorrectAnswerId = a1.Id;
        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q1.Id, Order = 1 },
            new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q2.Id, Order = 2 });
        await ctx.SaveChangesAsync();

        return new World(student.Id, grade.Id, school.Id, worksheet.Id, a1.Id, parents[0].Id, parents[1].Id, parents[2].Id);
    }

    private async Task LinkAsync(int parentId, int studentId, ParentStudentLinkStatus status, DateTime? activatedAt = null)
    {
        await using var ctx = _db.NewContext();
        ctx.ParentStudentLinks.Add(new ParentStudentLink
        {
            ParentId = parentId,
            StudentId = studentId,
            Status = status,
            CreatedAt = (activatedAt ?? _time.Now.UtcDateTime.AddDays(-10)).AddMinutes(-5),
            ActivatedAt = status == ParentStudentLinkStatus.Pending ? null : activatedAt ?? _time.Now.UtcDateTime.AddDays(-10),
            RevokedAt = status == ParentStudentLinkStatus.Revoked ? _time.Now.UtcDateTime.AddDays(-1) : null
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<List<T>> EventsAsync<T>() where T : class
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<T>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<T>(o.Content)!)
            .ToList();
    }

    // ---- EndTest → ParentChildTestCompletedEvent -----------------------------------------------------------------------

    private async Task<int> StartAsync(World w, bool assigned = true)
    {
        // issue #423 (m1): test-tamamlandı bildirimi yalnız GÖRÜNÜR bir atamanın penceresindeki oturum için gider.
        if (assigned)
            await AssignAsync(w, endAt: null, startAt: _time.Now.UtcDateTime.AddDays(-1));
        await using var ctx = _db.NewContext();
        var result = await new TestSessionService(ctx, null, _authApi)
            .StartTestAsync(w.WorksheetId, new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId });
        result.Success.ShouldBeTrue();
        return result.InstanceId;
    }

    private async Task AnswerQ1CorrectlyAsync(int instanceId, World w)
    {
        await using var ctx = _db.NewContext();
        var row = await ctx.TestInstanceQuestions
            .Where(q => q.WorksheetInstanceId == instanceId)
            .OrderBy(q => q.Id)
            .FirstAsync();
        row.SelectedAnswerId = w.Q1Correct;
        await ctx.SaveChangesAsync();
    }

    private async Task<TestSessionResultDto> EndAsync(int instanceId, bool retryingStrategy = false)
    {
        await using var ctx = retryingStrategy ? _db.NewContextWithRetryingExecutionStrategy() : _db.NewContext();
        return await new TestSessionService(ctx, null, _authApi).EndTest(instanceId, StudentUser);
    }

    [Fact]
    public async Task EndTest_writes_one_event_per_ACTIVE_parent_with_score_and_targets()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(w.ParentC, w.StudentId, ParentStudentLinkStatus.Revoked);
        var instanceId = await StartAsync(w);
        await AnswerQ1CorrectlyAsync(instanceId, w);

        (await EndAsync(instanceId, retryingStrategy: true)).Success.ShouldBeTrue();

        var events = await EventsAsync<ParentChildTestCompletedEvent>();
        events.Count.ShouldBe(2);
        events.Select(e => e.ParentUserId).OrderBy(i => i).ShouldBe(new[] { ParentUserA, ParentUserB });
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(2);
        var e0 = events.Single(e => e.ParentUserId == ParentUserA);
        e0.TestInstanceId.ShouldBe(instanceId);
        e0.StudentId.ShouldBe(w.StudentId);
        e0.WorksheetName.ShouldBe("Kesirler Testi");
        e0.CorrectAnswers.ShouldBe(1);
        e0.TotalQuestions.ShouldBe(2);
        e0.Score.ShouldBe(50);
        e0.StudentDisplayName.ShouldBe("Ayşe K.");
        e0.ParentKeycloakId.ShouldBe($"kc-{ParentUserA}");

        await using var check = _db.NewContext();
        (await check.TestInstances.SingleAsync()).Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await check.OutboxMessages.AnyAsync(o => o.Content.Contains("u8101@example.com"))).ShouldBeFalse(); // e-posta taşınmaz
    }

    [Fact]
    public async Task EndTest_writes_nothing_for_Pending_or_Revoked_only_parents()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Pending);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Revoked);
        var instanceId = await StartAsync(w);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task EndTest_without_any_parent_keeps_the_old_path_and_never_calls_auth_api()
    {
        var w = await SeedAsync();
        var instanceId = await StartAsync(w);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task EndTest_repeated_call_does_not_duplicate_events()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var instanceId = await StartAsync(w);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();
        (await EndAsync(instanceId)).Success.ShouldBeTrue(); // #367: idempotent

        (await EventsAsync<ParentChildTestCompletedEvent>()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task EndTest_still_completes_with_empty_targets_when_auth_api_is_down()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var instanceId = await StartAsync(w);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("down"));

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        var evt = (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldHaveSingleItem();
        evt.ParentKeycloakId.ShouldBeEmpty();
        evt.StudentDisplayName.ShouldBeEmpty();
        evt.ParentUserId.ShouldBe(ParentUserA);
    }

    [Fact]
    public async Task EndTest_on_an_expired_instance_writes_no_parent_event()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Expired,
                StartTime = DateTime.UtcNow.AddHours(-3), MaxDurationSeconds = 60
            });
            await ctx.SaveChangesAsync();
        }
        var instanceId = await _db.NewContext().TestInstances.Select(i => i.Id).SingleAsync();

        (await EndAsync(instanceId)).Success.ShouldBeFalse();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    // ---- ParentHomeworkOverdueSweepJob -------------------------------------------------------------------------------------

    private ParentHomeworkOverdueSweepJob Job(AppDbContext ctx, int batchSize = 200, int lookbackDays = 3)
        => new(ctx, _authApi,
            new StaticOptionsMonitor<ParentHomeworkOverdueSweepOptions>(new ParentHomeworkOverdueSweepOptions { BatchSize = batchSize, LookbackDays = lookbackDays }),
            _time);

    private async Task<int> SweepAsync(int batchSize = 200)
    {
        await using var ctx = _db.NewContext();
        return await Job(ctx, batchSize).SweepAsync();
    }

    private async Task<int> AssignAsync(World w, DateTime? endAt, bool toStudent = true, bool platformWide = false, int? schoolId = null,
        int? worksheetId = null, DateTime? startAt = null)
    {
        await using var ctx = _db.NewContext();
        var a = new WorksheetAssignment
        {
            WorksheetId = worksheetId ?? w.WorksheetId,
            StudentId = toStudent ? w.StudentId : null,
            GradeId = toStudent ? null : w.GradeId,
            SchoolId = schoolId,
            IsPlatformWide = platformWide,
            StartAt = startAt ?? _time.Now.UtcDateTime.AddDays(-5),
            EndAt = endAt
        };
        ctx.WorksheetAssignments.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    private DateTime Ago(TimeSpan span) => _time.Now.UtcDateTime - span;

    [Fact]
    public async Task Sweep_notifies_each_active_parent_once_for_an_overdue_incomplete_assignment()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Active);
        var assignmentId = await AssignAsync(w, Ago(TimeSpan.FromHours(2)));

        (await SweepAsync()).ShouldBe(1);
        (await SweepAsync()).ShouldBe(0); // işaretçi: tekrar çalışınca yeni event yok

        var events = await EventsAsync<ParentHomeworkOverdueEvent>();
        events.Count.ShouldBe(2);
        events.Select(e => e.ParentUserId).OrderBy(i => i).ShouldBe(new[] { ParentUserA, ParentUserB });
        events.ShouldAllBe(e => e.AssignmentId == assignmentId && e.StudentId == w.StudentId && e.WorksheetName == "Kesirler Testi");
        events[0].StudentDisplayName.ShouldBe("Ayşe K.");
        events.Single(e => e.ParentUserId == ParentUserA).ParentKeycloakId.ShouldBe($"kc-{ParentUserA}");
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(2);

        await using var check = _db.NewContext();
        var marker = await check.ParentHomeworkOverdueMarkers.SingleAsync();
        marker.WorksheetId.ShouldBe(w.WorksheetId);
        marker.NotifiedParentCount.ShouldBe(2);
    }

    [Fact]
    public async Task Sweep_ignores_pending_and_revoked_parents_and_does_not_even_mark()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Pending);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Revoked);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));

        (await SweepAsync()).ShouldBe(0);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty();
        (await _db.NewContext().ParentHomeworkOverdueMarkers.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sweep_skips_assignments_that_are_not_overdue_or_outside_the_lookback_window()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, _time.Now.UtcDateTime.AddHours(5)); // henüz geçmedi
        await AssignAsync(w, null); // bitiş yok
        await AssignAsync(w, Ago(TimeSpan.FromDays(10))); // pencere dışı

        (await SweepAsync()).ShouldBe(0);
        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(WorksheetInstanceStatus.Completed, false)]
    [InlineData(WorksheetInstanceStatus.Expired, true)] // süresi dolarak kapanan oturum veli panelinde de Gecikmiş (InstanceExpired)
    [InlineData(WorksheetInstanceStatus.Started, true)] // hâlâ açık/bitirilmemiş → gecikmiş
    public async Task Sweep_respects_completion_state_of_the_childs_attempt(WorksheetInstanceStatus status, bool expectNotification)
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = status, StartTime = Ago(TimeSpan.FromDays(1))
            });
            await ctx.SaveChangesAsync();
        }

        await SweepAsync();

        (await EventsAsync<ParentHomeworkOverdueEvent>()).Count.ShouldBe(expectNotification ? 1 : 0);
    }

    [Fact]
    public async Task Sweep_does_not_notify_a_parent_who_linked_after_the_deadline()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active, activatedAt: Ago(TimeSpan.FromDays(4)));
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Active, activatedAt: Ago(TimeSpan.FromMinutes(30)));
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));

        (await SweepAsync()).ShouldBe(1);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldHaveSingleItem().ParentUserId.ShouldBe(ParentUserA);
    }

    [Fact]
    public async Task Sweep_covers_grade_assignments_only_for_platform_wide_or_matching_verified_school()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var otherSchool = await AddSchoolAsync();
        await AssignAsync(w, Ago(TimeSpan.FromHours(1)), toStudent: false, schoolId: otherSchool); // başka okul → görünmez
        // Aynı test birden çok atanırsa TEK bildirim (test, öğrenci) — bu yüzden her kapsam ayrı testte.
        var ws2 = await NewWorksheetAsync(w);
        var ws3 = await NewWorksheetAsync(w);
        var own = await AssignAsync(w, Ago(TimeSpan.FromHours(2)), toStudent: false, schoolId: w.SchoolId, worksheetId: ws2);
        var wide = await AssignAsync(w, Ago(TimeSpan.FromHours(3)), toStudent: false, platformWide: true, worksheetId: ws3);

        (await SweepAsync()).ShouldBe(2);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).Select(e => e.AssignmentId).OrderBy(i => i)
            .ShouldBe(new[] { own, wide }.OrderBy(i => i));
    }

    [Fact]
    public async Task Sweep_existing_marker_blocks_a_second_notification_even_for_a_late_run()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var assignmentId = await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        await using (var ctx = _db.NewContext())
        {
            ctx.ParentHomeworkOverdueMarkers.Add(new ParentHomeworkOverdueMarker
            {
                WorksheetId = w.WorksheetId, StudentId = w.StudentId, ProcessedAt = Ago(TimeSpan.FromHours(1))
            });
            await ctx.SaveChangesAsync();
        }

        (await SweepAsync()).ShouldBe(0);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sweep_competing_instance_that_wins_the_marker_race_is_a_silent_noop()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var assignmentId = await AssignAsync(w, Ago(TimeSpan.FromHours(2)));

        // Aday okunduktan SONRA başka örnek işaretçiyi yazar: bu örneğin insert'i unique ihlaline düşer, atlanır.
        await using var ctx = _db.NewContext(new MarkerRaceInterceptor(_db, w.WorksheetId, w.StudentId));
        var processed = await Job(ctx).SweepAsync();

        processed.ShouldBe(0);
        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty(); // transaction geri alındı: outbox yok
        // Not: SQLite testinde rakip aynı bağlantıyı/transaction'ı paylaşır, bu yüzden geri alma rakibin satırını da siler; Postgres'te
        // kazananın işaretçisi kalır. Burada doğrulanan: unique ihlali yutulur, çift işlenmez, yarım outbox kalmaz.
    }

    [Fact]
    public async Task Sweep_still_writes_events_with_empty_targets_when_auth_api_is_down()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("down"));

        (await SweepAsync()).ShouldBe(1);

        var evt = (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldHaveSingleItem();
        evt.ParentKeycloakId.ShouldBeEmpty();
        evt.StudentDisplayName.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sweep_honours_the_batch_size_and_finishes_the_rest_next_run()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(3)));
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)), worksheetId: await NewWorksheetAsync(w));
        await AssignAsync(w, Ago(TimeSpan.FromHours(1)), worksheetId: await NewWorksheetAsync(w));

        (await SweepAsync(batchSize: 2)).ShouldBe(2);
        (await SweepAsync(batchSize: 2)).ShouldBe(1);
        (await SweepAsync(batchSize: 2)).ShouldBe(0);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).Count.ShouldBe(3);
    }

    // ---- review fixes (#423) ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sweep_treats_a_test_finished_BEFORE_the_assignment_started_as_done()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2))); // StartAt = 5 gün önce
        await using (var ctx = _db.NewContext())
        {
            // #367: atamadan ÖNCE çözülmüş test ikinci kez başlatılamaz, atamayı karşılar.
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Completed,
                StartTime = Ago(TimeSpan.FromDays(20)), EndTime = Ago(TimeSpan.FromDays(20))
            });
            await ctx.SaveChangesAsync();
        }

        await SweepAsync();

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty();
        (await _db.NewContext().ParentHomeworkOverdueMarkers.CountAsync()).ShouldBe(1); // 0 veliyle işaretlenir, tekrar taranmaz
    }

    [Fact]
    public async Task Sweep_sends_ONE_notification_when_the_same_worksheet_is_assigned_twice()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(5)));
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)), toStudent: false, platformWide: true); // sınıf ataması, aynı test

        (await SweepAsync()).ShouldBe(1);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldHaveSingleItem();
        (await _db.NewContext().ParentHomeworkOverdueMarkers.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_does_not_notify_while_another_assignment_of_the_same_worksheet_is_still_open()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        await AssignAsync(w, _time.Now.UtcDateTime.AddDays(2), toStudent: false, platformWide: true); // hâlâ yapılabilir

        (await SweepAsync()).ShouldBe(0);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldBeEmpty();
        (await _db.NewContext().ParentHomeworkOverdueMarkers.CountAsync()).ShouldBe(0); // sonra yeniden değerlendirilir
    }

    [Fact]
    public async Task Sweep_flags_an_Expired_instance_so_the_text_says_time_ran_out_and_a_never_started_one_does_not()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var neverStarted = await NewWorksheetAsync(w);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)), worksheetId: neverStarted);
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Expired, StartTime = Ago(TimeSpan.FromDays(1))
            });
            await ctx.SaveChangesAsync();
        }

        (await SweepAsync()).ShouldBe(2);

        var events = await EventsAsync<ParentHomeworkOverdueEvent>();
        events.Single(e => e.WorksheetId == w.WorksheetId).InstanceExpired.ShouldBeTrue();
        events.Single(e => e.WorksheetId == neverStarted).InstanceExpired.ShouldBeFalse();
    }

    [Fact]
    public async Task Sweep_ignores_school_grade_assignments_when_the_students_school_is_not_verified()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await using (var ctx = _db.NewContext())
            await ctx.Students.Where(s => s.Id == w.StudentId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SchoolVerifiedAt, (DateTime?)null));
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)), toStudent: false, schoolId: w.SchoolId); // doğrulanmamış üyelik okul atamasını görmez
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)), toStudent: false, platformWide: true, worksheetId: await NewWorksheetAsync(w));

        (await SweepAsync()).ShouldBe(1); // yalnız platform geneli (AssignmentVisibleTo ile aynı)
    }

    [Fact]
    public async Task Sweep_does_not_write_to_a_parent_whose_link_was_revoked_between_plan_and_transaction()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, Ago(TimeSpan.FromHours(2)));
        RevokeDuringLookup(w.ParentA, w.StudentId);

        (await SweepAsync()).ShouldBe(1);

        (await EventsAsync<ParentHomeworkOverdueEvent>()).ShouldHaveSingleItem().ParentUserId.ShouldBe(ParentUserB);
    }

    [Fact]
    public async Task EndTest_does_not_write_to_a_parent_whose_link_was_revoked_between_plan_and_transaction()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Active);
        var instanceId = await StartAsync(w);
        RevokeDuringLookup(w.ParentA, w.StudentId);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldHaveSingleItem().ParentUserId.ShouldBe(ParentUserB);
    }

    [Fact]
    public async Task EndTest_sends_nothing_for_self_practice_without_a_visible_assignment()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        var instanceId = await StartAsync(w, assigned: false);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task EndTest_sends_nothing_when_the_only_assignment_is_outside_the_30_day_window()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId, ParentStudentLinkStatus.Active);
        await AssignAsync(w, _time.Now.UtcDateTime.AddDays(-40), startAt: _time.Now.UtcDateTime.AddDays(-60)); // 30 gün penceresi dışı
        var instanceId = await StartAsync(w, assigned: false);

        (await EndAsync(instanceId)).Success.ShouldBeTrue();

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    /// <summary>Plan okunduktan sonra, transaction öncesi auth-api çağrısı sırasında veli A bağlantıyı koparır (deterministik yarış).</summary>
    private void RevokeDuringLookup(int parentId, int studentId)
        => _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using (var ctx = _db.NewContext())
                {
                    ctx.ParentStudentLinks.Where(l => l.ParentId == parentId && l.StudentId == studentId)
                        .ExecuteUpdate(s => s.SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked));
                }
                return (IReadOnlyList<UserLookupResultDto>)ci.Arg<IEnumerable<int>>()
                    .Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = "X Y" }).ToList();
            });

    private async Task<int> NewWorksheetAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = $"Ek Test {Guid.NewGuid():N}", Description = "", GradeId = w.GradeId, MaxDurationSeconds = 600 };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task<int> AddSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        var s = new School { Name = "Başka Okul" };
        ctx.Schools.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    /// <summary>İlk SaveChanges'ten hemen önce (aynı transaction'ın içinde değil, hemen öncesinde) rakip örneğin işaretçisini yazar.</summary>
    private sealed class MarkerRaceInterceptor(TestDb db, int worksheetId, int studentId)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_fired)
            {
                _fired = true;
                await using var rival = db.NewContext();
                rival.ParentHomeworkOverdueMarkers.Add(new ParentHomeworkOverdueMarker
                {
                    WorksheetId = worksheetId, StudentId = studentId, ProcessedAt = DateTime.UtcNow
                });
                await rival.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
