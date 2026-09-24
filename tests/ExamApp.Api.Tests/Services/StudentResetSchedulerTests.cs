using ExamApp.Api.Services.StudentReset;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #243: aynı kullanıcı için bekleyen sıfırlama işi varsa yenisi kuyruğa alınmaz.
/// Hangfire storage'ı (bağlantı, hash, iş durumu, dağıtık kilit) sözlük destekli sahte ile simüle edilir.
/// </summary>
public class StudentResetSchedulerTests
{
    private const int UserId = 42;
    private const int StudentId = 420;
    private const string KeycloakId = "kc-42";

    private readonly Dictionary<string, Dictionary<string, string>> _hashes = new();
    private readonly Dictionary<string, string?> _jobStates = new();
    private readonly List<string> _locks = new();
    private readonly IStorageConnection _connection = Substitute.For<IStorageConnection>();
    private readonly JobStorage _storage = Substitute.For<JobStorage>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private int _nextJobId = 100;

    public StudentResetSchedulerTests()
    {
        _storage.GetConnection().Returns(_connection);

        _connection.AcquireDistributedLock(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(call => { _locks.Add(call.ArgAt<string>(0)); return Substitute.For<IDisposable>(); });

        _connection.GetAllEntriesFromHash(Arg.Any<string>())
            .Returns(call => _hashes.TryGetValue(call.ArgAt<string>(0), out var h) ? new Dictionary<string, string>(h) : null);

        _connection.GetStateData(Arg.Any<string>())
            .Returns(call => _jobStates.TryGetValue(call.ArgAt<string>(0), out var state) && state != null
                ? new StateData { Name = state }
                : null);

        _connection.CreateWriteTransaction().Returns(_ =>
        {
            var tx = Substitute.For<IWriteOnlyTransaction>();
            var pending = new List<(string Key, KeyValuePair<string, string>[] Pairs)>();
            tx.When(t => t.SetRangeInHash(Arg.Any<string>(), Arg.Any<IEnumerable<KeyValuePair<string, string>>>()))
                .Do(c => pending.Add((c.ArgAt<string>(0), c.ArgAt<IEnumerable<KeyValuePair<string, string>>>(1).ToArray())));
            tx.When(t => t.Commit()).Do(_ =>
            {
                foreach (var (key, pairs) in pending)
                {
                    if (!_hashes.TryGetValue(key, out var hash))
                        _hashes[key] = hash = new Dictionary<string, string>();
                    foreach (var pair in pairs)
                        hash[pair.Key] = pair.Value;
                }
            });
            return tx;
        });

        // Enqueue<T> uzantısı IBackgroundJobClient.Create'e iner.
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns(_ =>
        {
            var id = (_nextJobId++).ToString();
            _jobStates[id] = EnqueuedState.StateName;
            return id;
        });
    }

    private StudentResetScheduler NewScheduler() =>
        new(_storage, _jobs, Substitute.For<ILogger<StudentResetScheduler>>());

    [Fact]
    public void First_request_enqueues_the_reset_job_and_records_it_under_a_user_lock()
    {
        var result = NewScheduler().Enqueue(UserId, StudentId, KeycloakId);

        result.AlreadyPending.ShouldBeFalse();
        result.JobId.ShouldBe("100");
        _jobs.Received(1).Create(
            Arg.Is<Job>(j => j.Type == typeof(StudentResetJob)
                             && j.Method.Name == nameof(StudentResetJob.RunAsync)
                             && (int)j.Args[0]! == UserId && (int)j.Args[1]! == StudentId && (string)j.Args[2]! == KeycloakId),
            Arg.Any<EnqueuedState>());
        _hashes[StudentResetScheduler.MarkerKey(UserId)][StudentResetScheduler.JobIdField].ShouldBe("100");
        _locks.ShouldBe(new[] { StudentResetScheduler.LockResource(UserId) });
    }

    [Theory]
    [InlineData("Enqueued")]
    [InlineData("Scheduled")]  // otomatik retry beklemesi
    [InlineData("Processing")]
    [InlineData("Awaiting")]
    public void Pending_job_is_returned_and_no_new_job_is_enqueued(string state)
    {
        var scheduler = NewScheduler();
        var first = scheduler.Enqueue(UserId, StudentId, KeycloakId);
        _jobStates[first.JobId] = state;

        var second = scheduler.Enqueue(UserId, StudentId, KeycloakId);

        second.AlreadyPending.ShouldBeTrue();
        second.JobId.ShouldBe(first.JobId);
        _jobs.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Deleted")]
    [InlineData(null)] // iş storage'dan süpürülmüş
    public void Finished_or_missing_job_allows_a_new_reset(string? state)
    {
        var scheduler = NewScheduler();
        var first = scheduler.Enqueue(UserId, StudentId, KeycloakId);
        _jobStates[first.JobId] = state;

        var second = scheduler.Enqueue(UserId, StudentId, KeycloakId);

        second.AlreadyPending.ShouldBeFalse();
        second.JobId.ShouldNotBe(first.JobId);
        _jobs.Received(2).Create(Arg.Any<Job>(), Arg.Any<IState>());
        _hashes[StudentResetScheduler.MarkerKey(UserId)][StudentResetScheduler.JobIdField].ShouldBe(second.JobId);
    }

    [Fact]
    public void Pending_job_of_another_user_does_not_block()
    {
        var scheduler = NewScheduler();
        scheduler.Enqueue(UserId, StudentId, KeycloakId);

        var other = scheduler.Enqueue(UserId + 1, StudentId + 1, "kc-43");

        other.AlreadyPending.ShouldBeFalse();
        _jobs.Received(2).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public void Lock_timeout_is_mapped_to_already_pending_and_nothing_is_enqueued()
    {
        // review: eskiden exception controller'a çıkıp 500 dönüyordu.
        _connection.AcquireDistributedLock(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(_ => throw new DistributedLockTimeoutException(StudentResetScheduler.LockResource(UserId)));

        var result = NewScheduler().Enqueue(UserId, StudentId, KeycloakId);

        result.AlreadyPending.ShouldBeTrue();
        result.JobId.ShouldBe(string.Empty); // eşzamanlı istek işareti henüz yazmadı
        _jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public void Lock_timeout_returns_the_pending_job_id_when_the_marker_is_readable()
    {
        var scheduler = NewScheduler();
        var first = scheduler.Enqueue(UserId, StudentId, KeycloakId);
        _connection.AcquireDistributedLock(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(_ => throw new DistributedLockTimeoutException(StudentResetScheduler.LockResource(UserId)));

        var second = scheduler.Enqueue(UserId, StudentId, KeycloakId);

        second.AlreadyPending.ShouldBeTrue();
        second.JobId.ShouldBe(first.JobId);
        _jobs.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public void Lock_timeout_does_not_report_a_finished_job_as_pending_id()
    {
        var scheduler = NewScheduler();
        var first = scheduler.Enqueue(UserId, StudentId, KeycloakId);
        _jobStates[first.JobId] = SucceededState.StateName;
        _connection.AcquireDistributedLock(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(_ => throw new DistributedLockTimeoutException(StudentResetScheduler.LockResource(UserId)));

        var second = scheduler.Enqueue(UserId, StudentId, KeycloakId);

        second.AlreadyPending.ShouldBeTrue();
        second.JobId.ShouldBe(string.Empty);
        _jobs.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }
}
