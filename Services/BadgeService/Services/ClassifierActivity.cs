namespace BadgeService.Services;

/// <summary>
/// Issue #897: what the question classifier did recently, for the admin status card. In memory only: counts start
/// again when the service restarts (<see cref="StartedAt"/> says since when). Only the exception type of the last
/// failure is kept — messages can carry provider responses.
/// </summary>
public sealed class ClassifierActivity(TimeProvider time)
{
    public enum Outcome
    {
        Classified,
        Skipped,
        Failed,
    }

    internal static readonly TimeSpan Window = TimeSpan.FromHours(24);
    // A burst of imports is a few thousand questions; older entries fall out of the window anyway.
    internal const int MaxEntries = 20_000;

    private readonly object _gate = new();
    private readonly Queue<(DateTime At, Outcome Outcome)> _recent = new();
    private DateTime? _lastErrorAt;
    private string? _lastErrorKind;

    public DateTime StartedAt { get; } = time.GetUtcNow().UtcDateTime;

    public void Record(Outcome outcome, Exception? error = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            _recent.Enqueue((now, outcome));
            Trim(now);
            if (outcome == Outcome.Failed)
            {
                _lastErrorAt = now;
                _lastErrorKind = error?.GetType().Name;
            }
        }
    }

    public ClassifierActivitySnapshot Snapshot()
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            Trim(now);
            return new ClassifierActivitySnapshot(
                StartedAt,
                _recent.Count(e => e.Outcome == Outcome.Classified),
                _recent.Count(e => e.Outcome == Outcome.Skipped),
                _recent.Count(e => e.Outcome == Outcome.Failed),
                _lastErrorAt,
                _lastErrorKind);
        }
    }

    private void Trim(DateTime now)
    {
        while (_recent.Count > 0 && (_recent.Count > MaxEntries || now - _recent.Peek().At > Window))
            _recent.Dequeue();
    }
}

public sealed record ClassifierActivitySnapshot(
    DateTime StartedAt,
    int Classified,
    int Skipped,
    int Failed,
    DateTime? LastErrorAt,
    string? LastErrorKind);
