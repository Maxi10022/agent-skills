using JasperFx.Events;

namespace YourApp.Tests.Testing.Probes;

// Completes on the first event the predicate accepts; build it before the act and dispose it with `using`
public sealed class NotificationProbe<TEvent> : IDisposable where TEvent : notnull
{
    private readonly TaskCompletionSource<IEvent<TEvent>> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Action<Func<IEvent<TEvent>, Task>> _unsubscribe;
    private readonly Func<IEvent<TEvent>, bool> _predicate;
    private readonly Func<IEvent<TEvent>, Task> _handler;

    public NotificationProbe(
        Action<Func<IEvent<TEvent>, Task>> subscribe,
        Action<Func<IEvent<TEvent>, Task>> unsubscribe,
        Func<IEvent<TEvent>, bool> predicate)
    {
        _unsubscribe = unsubscribe;
        _predicate = predicate;
        _handler = Handle;
        subscribe(_handler);
    }

    public NotificationProbe(object notifications, string eventName, Func<IEvent<TEvent>, bool> predicate) :
        this(
            EventSubscription.Subscriber<Func<IEvent<TEvent>, Task>>(notifications, eventName),
            EventSubscription.Unsubscriber<Func<IEvent<TEvent>, Task>>(notifications, eventName),
            predicate)
    {
    }

    public Task<IEvent<TEvent>> WaitAsync(TimeSpan timeout) =>
        _received.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

    public void Dispose() => _unsubscribe(_handler);

    private Task Handle(IEvent<TEvent> evt)
    {
        if (_predicate(evt)) _received.TrySetResult(evt);
        return Task.CompletedTask;
    }
}
