using Alba;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;

namespace YourApp.Tests.Testing.Bases;

// The host half of the shared test base; a bUnit page half derives the component base, then drop Ct here
public abstract partial class TestBase(FixtureBase fixture)
{
    public Guid AggregateId { get; } = Guid.CreateVersion7();

    public FixtureBase Fixture { get; } = fixture;

    public IAlbaHost Host => Fixture.Host;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected async Task Given(params object[] events) => await Given(AggregateId, events);

    protected async Task Given(Guid aggregateId, params object[] events)
    {
        await using var session = NewSession();
        session.Events.Append(aggregateId, events);
        await session.SaveChangesAsync();
    }

    // Also waits until the daemon projected the events and Wolverine forwarded them, so a subscriber started next
    // never sees a late notification for its own seed data
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

    protected async Task<T?> FetchLatestAsync<T>() where T : class => await FetchLatestAsync<T>(AggregateId);

    protected async Task<T?> FetchLatestAsync<T>(Guid id) where T : class
    {
        await using var session = NewSession();
        return await session.Events.FetchLatest<T>(id);
    }

    protected async Task<IReadOnlyList<IEvent>> FetchStreamAsync() => await FetchStreamAsync(AggregateId);

    protected async Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid id)
    {
        await using var session = NewSession();
        return await session.Events.FetchStreamAsync(id);
    }

    // Only for documents no projection owns, a projected read model is seeded through its events
    protected async Task StoreAsync<T>(T document) where T : notnull
    {
        await using var session = NewSession();
        session.Store(document);
        await session.SaveChangesAsync();
    }

    protected async Task<T?> LoadAsync<T>(Guid id) where T : class
    {
        await using var session = NewSession();
        return await session.LoadAsync<T>(id);
    }

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

    // Add .IgnoreMessageType<T>() for every message that retries forever without configuration no test provides
    protected TrackedSessionConfiguration TrackActivity() =>
        Host.TrackActivity()
            .Timeout(Timeouts.Tracked);

    protected Task<ITrackedSession> SendMessageAndWaitAsync<TCommand>(TCommand cmd) where TCommand : class =>
        TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(cmd);

    protected Task<ITrackedSession> InvokeMessageAndWaitAsync<TCommand>(TCommand cmd) where TCommand : class =>
        TrackActivity().InvokeMessageAndWaitAsync(cmd);

    protected bool OwnStream(IEvent evt) => evt.StreamId == AggregateId;

    protected async Task<(ITrackedSession Tracked, IScenarioResult Result)> TrackedHttpCall(Action<Scenario> configure)
    {
        IScenarioResult result = null!;
        Func<IMessageContext, Task> execute = async _ => result = await Host.Scenario(configure);
        var tracked = await TrackActivity().ExecuteAndWaitAsync(execute);
        return (tracked, result);
    }

    // A fault the test publishes itself, for notification handlers and pages; the shape follows Wolverine 6
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

    protected IDocumentSession NewSession() => Fixture.Host.Services
        .GetRequiredService<IDocumentStore>()
        .LightweightSession();
}
