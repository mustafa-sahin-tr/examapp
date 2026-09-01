using MassTransit;

namespace OutboxPublisher.Tests;

/// <summary>Minimal <see cref="IPublishEndpoint"/> that records what OutboxProcessor publishes.</summary>
public sealed class RecordingPublishEndpoint : IPublishEndpoint
{
    public List<object> Published { get; } = new();

    /// <summary>When true, the next Publish call throws (then resets).</summary>
    public bool FailNext { get; set; }

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
    {
        if (FailNext)
        {
            FailNext = false;
            return Task.FromException(new InvalidOperationException("broker down"));
        }

        Published.Add(message);
        return Task.CompletedTask;
    }

    // ---- unused surface ----
    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        => Publish(message, typeof(T), cancellationToken);
    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish(message, typeof(T), cancellationToken);
    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => Publish(message, typeof(T), cancellationToken);
    public Task Publish(object message, CancellationToken cancellationToken = default)
        => Publish(message, message.GetType(), cancellationToken);
    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Publish(message, message.GetType(), cancellationToken);
    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        => Publish(message, messageType, cancellationToken);
    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        => throw new NotSupportedException();
    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotSupportedException();
    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        => throw new NotSupportedException();

    public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => new NoopHandle();

    private sealed class NoopHandle : ConnectHandle
    {
        public void Disconnect() { }
        public void Dispose() { }
    }
}
