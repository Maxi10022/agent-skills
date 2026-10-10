# The test base, helper by helper

The template is [templates/TestBase.Host.cs](../templates/TestBase.Host.cs). This file explains each member and the
reason it looks the way it does.

## Contents

1. Identity and seeding
2. Reads
3. Waiting on the daemon
4. Tracked Wolverine sessions
5. Faults the test publishes itself
6. A slice base on top
7. What not to add

## 1. Identity and seeding

```csharp
public Guid AggregateId { get; } = Guid.CreateVersion7();

protected async Task Given(params object[] events) => await Given(AggregateId, events);

protected async Task Given(Guid aggregateId, params object[] events)
{
    await using var session = NewSession();
    session.Events.Append(aggregateId, events);
    await session.SaveChangesAsync();
}
```

- **Each test owns one stream.** Commands target `AggregateId`, and reads filter by it. That is what lets a whole
  collection share one database.
- **Seed read models through events, never by storing documents a projection owns.** Inline projections build them
  in the same transaction; async projections build them after a daemon wait. A stored document is what the test
  author thinks the projection builds, and the daemon may overwrite it.
- **A test that needs several streams** appends with `Given(otherId, ...)` or `GivenForwarded((stream, evt), ...)`.

```csharp
protected Task GivenForwarded(params object[] events) =>
    GivenForwarded([.. events.Select(evt => (AggregateId, evt))]);

protected Task GivenForwarded(Guid stream, params object[] events) =>
    GivenForwarded([.. events.Select(evt => (stream, evt))]);

protected Task GivenForwarded(params (Guid Stream, object Event)[] events) =>
    TrackActivity()
        .WaitForNonStaleDaemonDataAfterExecution(Timeouts.Projection)
        .ExecuteAndWaitAsync((IMessageContext _) => AppendAndCatchUpAsync(events));

// Catching up inside the session makes it track what the forwarding subscription publishes meanwhile
private async Task AppendAndCatchUpAsync((Guid Stream, object Event)[] events)
{
    foreach (var (stream, evt) in events)
    {
        await Given(stream, evt);
    }

    await WaitForNonStaleProjectionDataAsync();
}
```

- **Why it exists.** Marten's event subscription (`PublishEventsToWolverine`) hands events to Wolverine, and the
  handlers that forward them to an in-process hub run after the projection wait returns. A page or subscriber that
  starts right after `Given(...)` plus a projection wait then receives a "live" notification for its own seed data.
  - A note edit in flight was overwritten by the stored note.
  - A paging test listed rows twice.
  - A tray toasted its seed rows.
- **Why the catch-up is inside the session.** Only then does the tracked session see, and wait for, the messages
  the forwarding publishes while the daemon catches up.
- **Batch seeds.** Several seeds in one call share one session.

## 2. Reads

```csharp
protected async Task<T?> FetchLatestAsync<T>() where T : class => await FetchLatestAsync<T>(AggregateId);

protected async Task<IReadOnlyList<IEvent>> FetchStreamAsync() => await FetchStreamAsync(AggregateId);

protected async Task<T?> LoadAsync<T>(Guid id) where T : class
{
    await using var session = NewSession();
    return await session.LoadAsync<T>(id);
}

protected async Task StoreAsync<T>(T document) where T : notnull { ... }

protected IDocumentSession NewSession() => Fixture.Host.Services
    .GetRequiredService<IDocumentStore>()
    .LightweightSession();
```

- **`FetchLatestAsync<T>`** aggregates the stream live (or reads an inline projection). It needs no daemon wait.
- **`LoadAsync<T>`** reads the stored document, which an async projection writes later. Wait for the daemon first.
- **Reuse these helpers.** A test class never re-implements store, load or query plumbing. Store, then use the id
  the store assigned; there is no need to query the document back.

## 3. Waiting on the daemon

```csharp
// Marten throws on a store without events instead of returning, an empty store has nothing to catch up on
protected async Task WaitForNonStaleProjectionDataAsync()
{
    var statistics = await Host.DocumentStore().Advanced.FetchEventStoreStatistics();
    if (statistics.EventCount == 0) return;

    await Host.WaitForNonStaleProjectionDataAsync(Timeouts.Projection);
}

protected async Task<T?> WaitForDocumentAsync<T>() where T : class
{
    await WaitForNonStaleProjectionDataAsync();
    return await LoadAsync<T>(AggregateId);
}

// The daemon catches up first, or a projection still in flight brings a deleted row back
protected async Task WipeAsync<T>() where T : notnull
{
    await WaitForNonStaleProjectionDataAsync();

    await using var session = NewSession();
    session.DeleteWhere<T>(_ => true);
    await session.SaveChangesAsync();
}
```

- **Marten's `WaitForNonStaleProjectionDataAsync` throws on an empty store.** It throws
  `TimeoutException: No event activity was detected within the timeout span` at once on a store without events.
  That happens to a fresh page fixture wiping before its first test.
- **`WipeAsync` belongs in `InitializeAsync`** of a class whose subject lists everything. Wiping only helps if no
  command of the previous test is still in flight; see the flake catalogue.

## 4. Tracked Wolverine sessions

```csharp
// A message that needs configuration no test provides retries in the background for good
protected TrackedSessionConfiguration TrackActivity() =>
    Host.TrackActivity()
        .Timeout(Timeouts.Tracked)
        .IgnoreMessageType<RetriesForeverInTests>();

protected Task<ITrackedSession> SendMessageAndWaitAsync<TCommand>(TCommand cmd) where TCommand : class =>
    TrackActivity()
        .DoNotAssertOnExceptionsDetected()
        .SendMessageAndWaitAsync(cmd);

protected Task<ITrackedSession> InvokeMessageAndWaitAsync<TCommand>(TCommand cmd) where TCommand : class =>
    TrackActivity().InvokeMessageAndWaitAsync(cmd);

protected async Task<(ITrackedSession Tracked, IScenarioResult Result)> TrackedHttpCall(Action<Scenario> configure)
{
    IScenarioResult result = null!;
    Func<IMessageContext, Task> execute = async _ => result = await Host.Scenario(configure);
    var tracked = await TrackActivity().ExecuteAndWaitAsync(execute);
    return (tracked, result);
}
```

- **The names mirror Wolverine's**, so a reader who knows the library knows the helpers.
- **Invoke** runs the handler inline in the test's call and rethrows its exception. Use it for the happy path.
- **Send** goes through the local queue with its retry policy, so a failure is dead-lettered and becomes a `Fault<T>`.
  `DoNotAssertOnExceptionsDetected()` keeps the session from throwing, so the test can assert the failure.
- **`TrackActivity()` is the only door** to tracking. It carries the shared timeout and the ignore list. A raw
  `Host.InvokeMessageAndWaitAsync` runs on Wolverine's 5 s default and waits on every background message.
- **`TrackedHttpCall`** tracks what an Alba scenario cascades. The scenario itself stays inline in tests that only
  need the response.

## 5. Faults the test publishes itself

```csharp
protected Fault<T> Faulted<T>(T message) where T : class =>
    new(
        Message: message,
        Exception: new ExceptionInfo(
            Type: nameof(InvalidOperationException),
            Message: "Handling failed",
            StackTrace: "",
            InnerExceptions: []),
        Attempts: 1,
        FailedAt: DateTimeOffset.UtcNow,
        CorrelationId: Guid.CreateVersion7().ToString(),
        ConversationId: Guid.CreateVersion7(),
        TenantId: StorageConstants.DefaultTenantId,
        Source: "tests",
        Headers: new Dictionary<string, string?>()
    );

protected bool OwnStream(IEvent evt) => evt.StreamId == AggregateId;
```

`Faulted(cmd)` feeds a notification handler or a page under test a fault without running a failing command. The
`Fault<T>` constructor's shape depends on the Wolverine version.

## 6. A slice base on top (example from a production codebase)

```csharp
public class WebhookTestBase(WebhookFixture fixture) : TestBase(fixture)
{
    protected WebhookTestData Webhook => Data.Webhook;

    protected SecretStore Secrets => Host.Services.GetRequiredService<SecretStore>();

    protected WebhookNotifications Notifications => Host.Services.GetRequiredService<WebhookNotifications>();

    protected override void InitializeComponentServices(AsyncServiceScope scope)
    {
        Services.AddSingleton(Notifications);
        Services.AddSingleton(Secrets);
    }

    protected WebhookAdded WebhookAdded() => ...

    protected AddWebhook AddWebhook() => ...

    protected async Task<Webhook?> FindWebhookAsync(string name)
    {
        await WaitForNonStaleProjectionDataAsync();
        await using var session = NewSession();

        return await session.Query<Webhook>()
            .Where(webhook => webhook.Name.Value == name)
            .SingleOrDefaultAsync();
    }
}
```

- The service properties resolve host singletons.
- The component-services override registers them for bUnit renders; see the `blazor-bunit-page-tests` skill if
  installed.
- A read helper such as `FindWebhookAsync` waits for the daemon itself.

## 7. What not to add

- **A composite given** (`GivenWebhookAsync()` that appends and stores a secret). The test lists its own events and
  side state.
- **A helper around the act** (`ImportAsync(request, key, status)`). The act stays visible in the test.
- **Optional-parameter builders.** Use one parameterless builder and `with { ... }` in the test.
- **A private copy of `Ct`, of a timeout or of a tracking chain** in a test class.
