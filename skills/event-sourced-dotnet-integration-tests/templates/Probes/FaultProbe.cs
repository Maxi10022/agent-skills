using Wolverine;

namespace YourApp.Tests.Testing.Probes;

// The predicate is a lambda on the command's id, commands share no id interface
public sealed class FaultProbe<TMessage> : IDisposable where TMessage : class
{
    private readonly TaskCompletionSource<Fault<TMessage>> _received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Action<Func<Fault<TMessage>, Task>> _unsubscribe;
    private readonly Func<Fault<TMessage>, bool> _predicate;
    private readonly Func<Fault<TMessage>, Task> _handler;

    public FaultProbe(
        Action<Func<Fault<TMessage>, Task>> subscribe,
        Action<Func<Fault<TMessage>, Task>> unsubscribe,
        Func<Fault<TMessage>, bool> predicate)
    {
        _unsubscribe = unsubscribe;
        _predicate = predicate;
        _handler = Handle;
        subscribe(_handler);
    }

    public FaultProbe(object notifications, string eventName, Func<Fault<TMessage>, bool> predicate) :
        this(
            EventSubscription.Subscriber<Func<Fault<TMessage>, Task>>(notifications, eventName),
            EventSubscription.Unsubscriber<Func<Fault<TMessage>, Task>>(notifications, eventName),
            predicate)
    {
    }

    public Task<Fault<TMessage>> WaitAsync(TimeSpan timeout) =>
        _received.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);

    public void Dispose() => _unsubscribe(_handler);

    private Task Handle(Fault<TMessage> fault)
    {
        if (_predicate(fault)) _received.TrySetResult(fault);
        return Task.CompletedTask;
    }
}
