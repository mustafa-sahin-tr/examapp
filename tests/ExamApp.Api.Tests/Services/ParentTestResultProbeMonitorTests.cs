using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #421 review: velinin test sonucu ucunda 10 dakikada 20'yi AŞAN 404'ü (olası id taraması) bir kez Warning olarak
/// görünür olur; pencere dolunca sayaç sıfırlanır; veliler birbirini etkilemez.
/// </summary>
public class ParentTestResultProbeMonitorTests
{
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly ILogger<ParentTestResultProbeMonitor> _logger = Substitute.For<ILogger<ParentTestResultProbeMonitor>>();

    private int Warnings() => _logger.ReceivedCalls().Count(c =>
        c.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)c.GetArguments()[0]! == LogLevel.Warning);

    [Fact]
    public void Warns_once_when_more_than_twenty_not_found_in_ten_minutes()
    {
        var monitor = new ParentTestResultProbeMonitor(_logger, _time);

        for (var i = 0; i < ParentTestResultProbeMonitor.Threshold; i++)
        {
            monitor.RecordNotFound(1, 10).ShouldBeFalse();
            _time.Now = _time.Now.AddSeconds(20);
        }

        Warnings().ShouldBe(0);
        monitor.RecordNotFound(1, 10).ShouldBeTrue(); // 21.
        monitor.RecordNotFound(1, 10).ShouldBeFalse(); // aynı pencerede tekrar uyarmaz
        Warnings().ShouldBe(1);
    }

    [Fact]
    public void Window_resets_and_parents_are_counted_separately()
    {
        var monitor = new ParentTestResultProbeMonitor(_logger, _time);
        for (var i = 0; i < ParentTestResultProbeMonitor.Threshold; i++)
            monitor.RecordNotFound(1, 10);

        monitor.RecordNotFound(2, 20).ShouldBeFalse(); // başka veli
        _time.Now = _time.Now.Add(ParentTestResultProbeMonitor.Window);
        monitor.RecordNotFound(1, 10).ShouldBeFalse(); // yeni pencere: 1.

        Warnings().ShouldBe(0);
    }
}
