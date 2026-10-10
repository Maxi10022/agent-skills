# Faults, dead letters and notification tests

## Contents

1. How a fault comes to exist
2. Fault tests
3. The notification hub pattern
4. Probes
5. The recorder
6. Ordering with gates
7. Examples (from a production codebase)

## 1. How a fault comes to exist

A command handler throws, then:

- Wolverine applies the error policy: retries, then a move to the error queue (dead letter), or a discard.
- With `options.PublishFaultEvents(includeDiscarded: true)`, Wolverine then publishes a `Fault<T>` carrying the
  message, `ExceptionInfo` (`Type` is the exception's full name), attempts and timestamps.
- Before publishing, the fault publisher routes the `Fault<T>`. **When nothing handles or subscribes to that exact
  `Fault<T>`, it is skipped**, with only a debug log line ("No routes configured").

What this means for tests:

- **Only a sent message can fault.** `InvokeMessageAndWaitAsync` runs the handler inline and rethrows; it never
  reaches the error queue.
- **A fault needs a route.** If production forwards the fault (a handler taking `Fault<AddWebhook>`), the test gets
  it. If not, stub a route for the duration of the test:

  ```csharp
  Host.WolverineStubs(stubs => stubs.Stub<Fault<AddWebhook>>((_, _, _, _) => Task.CompletedTask));
  // ... send and assert ...
  Host.ClearAllWolverineStubs();
  ```

- **A missing aggregate guard decides whether there is a fault at all.** A bare `[WriteAggregate]` / `[Entity]`
  stops the handler silently, so nothing is appended and nothing is faulted. With
  `OnMissing = OnMissing.ThrowException` the message is dead-lettered with `RequiredDataMissingException`. Name and
  assert the test after what the handler actually does.

## 2. Fault tests

```csharp
[Fact]
public async Task Given_No_Webhook_It_Is_Dead_Lettered_And_Published_As_A_Fault()
{
    // Act
    var tracked = await SendMessageAndWaitAsync(RemoveWebhook());
    var stream = await FetchStreamAsync();

    // Assert
    stream.Should().BeEmpty();
    tracked.MovedToErrorQueue.SingleMessage<RemoveWebhook>();
    tracked.AutoFaultsPublished
        .SingleMessage<Fault<RemoveWebhook>>()
        .Assert.ThrownException<RequiredDataMissingException>();
}

[Fact]
public async Task Given_No_Webhook_Nothing_Is_Appended_Stored_Or_Faulted()
{
    // Act
    var tracked = await SendMessageAndWaitAsync(UpdateWebhook());
    var stream = await FetchStreamAsync();

    // Assert
    stream.Should().BeEmpty();
    await Secrets.Assert.NoSecretAsync(AggregateId);
    tracked.AutoFaultsPublished.MessagesOf<Fault<UpdateWebhook>>().Should().BeEmpty();
}
```

- **`SingleMessage<T>()` is the assertion itself.** No `.Should()` goes around it.
- **Do not assert `fault.Message.Should().Be(cmd)`** unless the test's name promises it. The fault's message is
  Wolverine's business.
- **A validation failure** (a FluentValidation middleware) faults with `ValidationException`. Test it with the
  invalid catalog value, for example `AddWebhook() with { Secret = Webhook.Secret.Invalid }`.

## 3. The notification hub pattern

The source app has one singleton hub per slice:

```csharp
public class WebhookNotifications : NotificationBase
{
    public event Func<IEvent<WebhookAdded>, Task>? OnWebhookAdded;
    public event Func<Fault<AddWebhook>, Task>? OnAddWebhookFailed;

    public Task PublishAsync(object @event) => @event switch
    {
        IEvent<WebhookAdded> evt => Raise(OnWebhookAdded, evt),
        Fault<AddWebhook> evt => Raise(OnAddWebhookFailed, evt),
        _ => Task.CompletedTask
    };
}

public static class WebhookNotificationHandler
{
    public static Task Handle(IEvent<WebhookAdded> evt, WebhookNotifications notifications) =>
        notifications.PublishAsync(evt);

    public static Task Handle(Fault<AddWebhook> evt, WebhookNotifications notifications) =>
        notifications.PublishAsync(evt);
}
```

Marten publishes events to Wolverine through `PublishEventsToWolverine(...)`, and the handlers raise the hub's
events, which Blazor pages subscribe to. Three kinds of tests cover it:

- **Forwarding:** an appended event reaches the subscriber. This needs a host and a probe.
- **Faults:** a failing command's fault reaches the subscriber. This needs a host and a fault probe.
- **Dispatch:** `PublishAsync(x)` raises only x's event. This needs no host, only a recorder.

## 4. Probes

```csharp
using var probe = new NotificationProbe<WebhookAdded>(
    notifications: Notifications,
    eventName: nameof(Notifications.OnWebhookAdded),
    predicate: OwnStream);

await Given(
    added
);

var forwarded = await probe.WaitAsync(Timeouts.Probe);
```

- **Build the probe before the act.** It subscribes in its constructor; `using` unsubscribes it.
- **`eventName: nameof(...)`.** C# does not let code outside the declaring class pass an event as a value (CS0070),
  so `EventSubscription` adds and removes the handler through reflection.
- **The predicate filters other tests' traffic.** For events it is the base's `OwnStream`. For faults it is a
  lambda on the command's id (`fault => fault.Message.Id == AggregateId`), because commands share no id interface.
  Since the probe only completes on the test's own stream, the test does not assert the `StreamId` again.
- **`RunContinuationsAsynchronously`.** The `TaskCompletionSource` is created with it, so the publisher's thread
  never runs the test's continuation, which could deadlock or reorder the publish.
- **Variants:**
  - `FaultProbe<TMessage>` for `Func<Fault<T>, Task>` events;
  - `MessageProbe<T>` for plain payloads;
  - `SignalProbe<T>` for `Func<CancellationToken, Task>` events, which records a snapshot on every raise.

Templates: [templates/Probes/](../templates/Probes/).

## 5. The recorder

```csharp
[Fact]
public async Task Given_A_Faulted_AddWebhook_Only_OnAddWebhookFailed_Is_Invoked()
{
    // Arrange
    var fault = Faulted(AddWebhook());
    var notifications = new WebhookNotifications();
    var recorder = Recorder.SubscribeToAll(notifications);

    // Act
    await WebhookNotificationHandler.Handle(fault, notifications);

    // Assert
    recorder.Received.Should().ContainSingle();
    recorder.Received[0].Event.Should().Be(fault);
    recorder.Received[0].Subscriber.Should().Be(nameof(WebhookNotifications.OnAddWebhookFailed));
}
```

- **`Recorder.SubscribeToAll(hub)`** attaches one handler per public event, each recording `(eventName, payload)`.
  "Only X fired" is then `ContainSingle()` plus the subscriber's name.
- **`Recorder.RecordAsync<THub>(act)`** builds a fresh hub, subscribes and runs the act, so several dispatches in one
  test do not share a recorder:

  ```csharp
  var afterCreated = await RecordAsync(notifications => notifications.PublishAsync(created));
  var afterUpdated = await RecordAsync(notifications => notifications.PublishAsync(updated));
  ```

- **A theory over `nameof(...)` rows** covers every dispatch branch of a hub in one test method.

## 6. Ordering with gates

"`PublishAsync` completes only after every subscriber" is tested with `TaskCompletionSource` gates. A delay would
only guess at the timing:

```csharp
[Fact]
public async Task Given_Two_Subscribers_PublishAsync_Completes_Only_After_Both()
{
    // Arrange
    var notifications = new FormNotifications();
    var firstSubscriber = new TaskCompletionSource();
    var secondSubscriber = new TaskCompletionSource();
    var created = new Event<FormCreated>(new FormCreated(Data.Form.Name));

    notifications.OnFormCreated += _ => firstSubscriber.Task;
    notifications.OnFormCreated += _ => secondSubscriber.Task;

    // Act
    var publishTask = notifications.PublishAsync(created);
    firstSubscriber.SetResult();

    var completedEarly = publishTask.IsCompleted;
    secondSubscriber.SetResult();
    await publishTask;

    // Assert
    completedEarly.Should().BeFalse();
    publishTask.IsCompletedSuccessfully.Should().BeTrue();
}
```

## 7. Examples (from a production codebase)

A fault reaching the hub through the real pipeline:

```csharp
[Fact]
public async Task Given_An_Existing_Webhook_A_Faulted_AddWebhook_Reaches_The_Subscriber()
{
    // Arrange
    await Given(
        WebhookAdded()
    );

    using var probe = new FaultProbe<AddWebhook>(
        notifications: Notifications,
        eventName: nameof(Notifications.OnAddWebhookFailed),
        predicate: fault => fault.Message.Id == AggregateId);

    // Act
    await SendMessageAndWaitAsync(AddWebhook());
    var forwarded = await probe.WaitAsync(Timeouts.Probe);

    // Assert
    forwarded.Assert.ThrownException<ExistingStreamIdCollisionException>();
}
```

The hub is a singleton:

```csharp
[Fact]
public void Is_Registered_As_A_Singleton()
{
    // Arrange
    using var firstScope = Host.Services.CreateScope();
    using var secondScope = Host.Services.CreateScope();

    // Act
    var first = firstScope.ServiceProvider.GetRequiredService<WebhookNotifications>();
    var second = secondScope.ServiceProvider.GetRequiredService<WebhookNotifications>();

    // Assert
    first.Should().BeSameAs(second);
}
```
