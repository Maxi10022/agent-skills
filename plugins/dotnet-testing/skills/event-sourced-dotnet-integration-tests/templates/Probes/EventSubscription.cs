using System.Reflection;

namespace YourApp.Tests.Testing.Probes;

// C# cannot pass an event as a value (CS0070), so probes subscribe by the event's nameof
public static class EventSubscription
{
    public static Action<THandler> Subscriber<THandler>(object target, string eventName) where THandler : Delegate =>
        handler => Find(target, eventName).AddEventHandler(target, handler);

    public static Action<THandler> Unsubscriber<THandler>(object target, string eventName) where THandler : Delegate =>
        handler => Find(target, eventName).RemoveEventHandler(target, handler);

    private static EventInfo Find(object target, string eventName) =>
        target.GetType().GetEvent(eventName)
        ?? throw new ArgumentException($"{target.GetType().Name} has no event {eventName}.", nameof(eventName));
}
