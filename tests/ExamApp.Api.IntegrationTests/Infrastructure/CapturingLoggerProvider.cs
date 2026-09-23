using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// issue #156: host'un TÜM log çıktısını (Trace dahil, mesaj + exception metni) bellekte toplar — "geçici şifre
/// hiçbir loga yazılmıyor" kontrolü için. Sınırlı: en son <see cref="Capacity"/> kayıt tutulur.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public const int Capacity = 50_000;

    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose() { }

    private void Add(string entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => owner.Add($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
