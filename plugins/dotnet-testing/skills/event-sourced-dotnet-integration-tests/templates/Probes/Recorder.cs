using System.Reflection;

namespace YourApp.Tests.Testing.Probes;

// Records which event of a notification hub fired with which payload; every hub event is a Func<TPayload, Task>
public sealed class Recorder
{
    private static readonly MethodInfo HandlerMethod =
        typeof(Recorder).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public List<(string Subscriber, object Event)> Received { get; } = [];

    public static Recorder SubscribeToAll(object notifications)
    {
        var recorder = new Recorder();

        foreach (var info in notifications.GetType().GetEvents())
        {
            var payload = info.EventHandlerType!.GetGenericArguments()[0];
            var handler = HandlerMethod.MakeGenericMethod(payload).Invoke(recorder, [info.Name]);
            info.AddEventHandler(notifications, (Delegate)handler!);
        }

        return recorder;
    }

    // A fresh notifications object with every event recorded, handed to the act
    public static async Task<Recorder> RecordAsync<TNotifications>(Func<TNotifications, Task> act)
        where TNotifications : new()
    {
        var notifications = new TNotifications();
        var recorder = SubscribeToAll(notifications!);

        await act(notifications);
        return recorder;
    }

    private Func<TPayload, Task> Handler<TPayload>(string subscriber) => payload =>
    {
        Received.Add((subscriber, payload!));
        return Task.CompletedTask;
    };
}
