---
name: event-sourced-dotnet-integration-tests
description: Integration tests for event-sourced .NET apps on Marten and Wolverine (the Critter Stack), hosted with Alba against Postgres in Testcontainers under xUnit v3 - one Postgres container with a database per fixture, the collection-versus-class-fixture decision, a test base with Given(events) per test stream, tracked Wolverine sessions, waits for the async projection daemon, Fault<T> and dead-letter tests, notification probes and recorders, shared timeouts, and a catalogue of flake root causes. Use this whenever you set up or change test fixtures or test bases in a Marten/Wolverine project, seed streams or projections in a test, wait for commands, projections or forwarded events, test a failing command, speed up a slow integration suite, or chase flaky, hanging or slow-to-shut-down host tests (timeouts, stray rows, "No event activity was detected", tracked sessions that never finish), even if the user only says "the tests are flaky". Not for bUnit rendering details or test formatting.
---

# Event-sourced .NET integration tests

These tests run the real host against a real Postgres:

- commands go through Wolverine;
- events land in Marten;
- projections and event subscriptions run in the async daemon.

Most of what goes wrong is timing: a test reads before the daemon caught up, waits on a message that never finishes,
or receives another test's leftovers. This skill describes a fixture topology and a test base that make the waits
explicit, and lists the flake root causes a large suite turned up.

Assumed stack: .NET 10, Marten 9 and Wolverine 6 (JasperFx), Alba 8, Testcontainers.PostgreSql 4, xUnit v3
(assembly fixtures, `TestContext.Current`), FluentAssertions 7, NSubstitute. Check member names against your versions
before copying a template. Snippets with domain names (Webhook, SecretStore, notification hubs) are examples from a
production codebase and illustrate the pattern only.

## Topology: one server, a database per fixture

```
test run
  PostgresServer          xUnit v3 assembly fixture, one container for the whole run
    WebhookFixture        its own database + its own Alba host   (collection fixture)
    CustomerFixture       its own database + its own Alba host   (collection fixture)
    WebhooksPageFixture   its own database + its own Alba host   (class fixture)
```

- **One container for the whole run.** It is registered with `[assembly: AssemblyFixture(typeof(PostgresServer))]`.
  The server is throwaway, so durability is pointless: `fsync=off`, `synchronous_commit=off`,
  `full_page_writes=off`. Every host keeps its own connection pool, so set `max_connections=1000`.
- **Each fixture creates its own database on that server,** named after its type plus a v7 Guid, lower-cased, and
  connects with `IncludeErrorDetail = true`. `CREATE DATABASE` copies `template1` under a lock, so fixtures that
  start together create their databases one at a time behind a `SemaphoreSlim`. The databases are never dropped;
  they go away with the container.
- **`FixtureBase` boots one Alba host** (`AlbaHost.For<Program>(...)`, with `JasperFxEnvironment.AutoStartHost = true`)
  on that connection string. It also:
  - runs Wolverine in solo mode (`services.RunWolverineInSoloMode()`), so there is no leader election;
  - sets `Durability.DrainTimeout` to 2 s;
  - swaps external services for stubs and dummy keys, so no test reaches the cloud;
  - calls a virtual `Configure(builder)` last, so a derived fixture's settings win.

  A derived fixture that adds its own resources calls `base.InitializeAsync()` and `base.DisposeAsync()`.

Templates: [templates/PostgresServer.cs](templates/PostgresServer.cs), [templates/FixtureBase.cs](templates/FixtureBase.cs).
The templates assume the test project imports the shared `Testing` namespaces as global usings
(`<Using Include="YourApp.Tests.Testing.Bases" />` and so on in the `.csproj`).
Details and the measurements behind the rules: [references/fixtures.md](references/fixtures.md).

## Choosing a fixture

First measure **B**, the boot time of one fixture (database, host, Wolverine codegen; about 3 s in the source
project). Collections run in parallel and the tests inside one collection run serially, so the run's wall time is
roughly the slowest collection's serial time plus B.

1. **A test that needs no host gets no fixture.** It uses a host-free unit base or a component base.
2. **A host test joins its slice's collection:** `[Collection(nameof(WebhookTestCollection))]`.
3. **A class gets a class fixture of its own in two cases only.** Either the host needs a different configuration
   (a fake HTTP endpoint, a real external service), or the class runs at least 2B serially and sits on the critical
   path, as page test classes often do.
4. **Never add a fixture to get a clean table or to cure a flake.** A test filters by its own ids (`AggregateId`, a
   unique name from the test data). A page that lists everything wipes its table in `InitializeAsync`.

## The test base

`TestBase(FixtureBase fixture)` gives every test the following. Code and notes are in
[references/test-base-helpers.md](references/test-base-helpers.md) and
[templates/TestBase.Host.cs](templates/TestBase.Host.cs).

- **`AggregateId = Guid.CreateVersion7()`.** Every test owns its stream, so tests share a database without seeing
  each other.
- **`Given(params object[] events)` and `Given(Guid stream, params object[] events)`.** They append in a fresh
  session. Read models are seeded from events: let the projections build them, and never `StoreAsync` a document a
  projection owns. A stored document diverges from what the projection would build, and the daemon may overwrite
  it. `StoreAsync` is only for documents no projection owns.
- **`GivenForwarded(...)`.** Use it when something listens to the seeded events (see Waiting).
- **Reads:**
  - `FetchStreamAsync()`;
  - `FetchLatestAsync<T>()` aggregates live, without a daemon wait;
  - `LoadAsync<T>(id)` reads the stored document, so wait for the daemon first;
  - `NewSession()`.
- **`OwnStream(IEvent evt)`.** A predicate that matches only the test's own stream.
- **`Ct`, `Data`, the slice shortcuts and one parameterless builder per event and command.** For the data side, use
  the `test-data-catalog` skill if it is installed.

## Waiting

| Waiting for | Use | Why |
|---|---|---|
| A command handled inline | `InvokeMessageAndWaitAsync(cmd)` | Runs the handler and rethrows; never dead-letters, so it never yields a `Fault<T>` |
| A command that may fail and fault | `SendMessageAndWaitAsync(cmd)` (with `DoNotAssertOnExceptionsDetected()`) | Goes through the queue, retries, dead-letters, publishes the fault |
| Any other tracked work (an HTTP call, a cascade) | `TrackActivity().ExecuteAndWaitAsync(...)`, `TrackedHttpCall(scenario)` | Waits for every cascaded message |
| Async projections | `WaitForNonStaleProjectionDataAsync()` | Returns at once on an empty store, where Marten's own wait throws |
| One projected document | `WaitForDocumentAsync<T>()` | Daemon wait, then `LoadAsync` |
| Seed data that subscribers react to (pages, notifications) | `GivenForwarded(events)` / `GivenForwarded((stream, evt), ...)` | Appends inside a tracked session that waits for the daemon and for Wolverine's forwarding |
| Emptying a table | `WipeAsync<T>()` | Catches the daemon up first, or a projection in flight brings a deleted row back |

- **`TrackActivity()` is the one entry point** to Wolverine's tracking:
  `Host.TrackActivity().Timeout(Timeouts.Tracked).IgnoreMessageType<T>()` for every message type that retries
  forever in a test host (a job that needs an external API key no test configures). Without the ignore, every later
  tracked session in that host waits on the requeued envelope until it times out. Never call `Host.TrackActivity()` or
  `Host.InvokeMessageAndWaitAsync()` directly.
- **`GivenForwarded` catches the daemon up inside the tracked session.** That way the session also tracks what the
  forwarding subscription publishes meanwhile. Batch the seeds into one call: one session covering 12 seeds took
  1.5 s, twelve sessions took 12 s.
- **Every timeout comes from one `Timeouts` class** ([templates/Timeouts.cs](templates/Timeouts.cs)), never a number
  in a test. Be generous: the first command of a run pays for Wolverine's handler codegen while every host boots at
  once. A passing test never waits for its timeout, and `longRunningTestSeconds` reports a real hang.
- **Never wait with `Task.Delay`.** Wait for a state, a tracked session or a probe.

## Faults and dead letters

- **Wolverine publishes a `Fault<T>` only when something routes that exact `Fault<T>`.** Even with
  `PublishFaultEvents(...)` it otherwise logs "No routes" and skips it. If production has a handler for the fault
  (for example one that forwards it to a notification hub), the tests get the fault for free. If not, give it a route
  in the test with `Host.WolverineStubs(stubs => stubs.Stub<Fault<T>>((_, _, _, _) => Task.CompletedTask))`, and
  call `Host.ClearAllWolverineStubs()` afterwards (Wolverine 6, `Wolverine.Tracking`).
- **Send, never invoke, to see a fault.** An invoke runs inline and rethrows.

  ```csharp
  var tracked = await SendMessageAndWaitAsync(RemoveWebhook());

  tracked.MovedToErrorQueue.SingleMessage<RemoveWebhook>();
  tracked.AutoFaultsPublished
      .SingleMessage<Fault<RemoveWebhook>>()
      .Assert.ThrownException<RequiredDataMissingException>();
  ```

- **`Fault<T>.Exception.Type` holds the full type name.** A small `fault.Assert.ThrownException<T>()` helper compares
  it.
- **"No fault" is an assertion of its own:**
  `tracked.AutoFaultsPublished.MessagesOf<Fault<T>>().Should().BeEmpty()`.
- **A missing aggregate is not automatically a fault.** A bare `[WriteAggregate]` / `[Entity]` guard stops
  silently. Only `OnMissing = OnMissing.ThrowException` dead-letters, with `RequiredDataMissingException`. Know
  which one the handler uses before writing the test's name.
- **`Faulted<T>(message)` on the base** builds a fault, so a notification handler or a page can be tested with a
  fault the test publishes itself.

## Notifications and probes

The source app forwards Marten events through Wolverine (`PublishEventsToWolverine`) to handlers that raise
`Func<IEvent<T>, Task>` / `Func<Fault<T>, Task>` events on a singleton hub. Tests observe that hub:

- **A probe** subscribes in its constructor and unsubscribes in `Dispose`. It completes a `TaskCompletionSource`
  created with `RunContinuationsAsynchronously` when its predicate matches, so the publisher never runs the test's
  continuation. Build it before the act with `using var probe = new NotificationProbe<T>(notifications:,
  eventName: nameof(...), predicate: OwnStream);`. C# cannot pass an event as a value (CS0070), so the probe
  subscribes by name through reflection.
- **The predicate `OwnStream` keeps other tests' events out.** The test therefore does not assert the `StreamId`
  again.
- **A recorder** (`Recorder.SubscribeToAll(notifications)`, or `Recorder.RecordAsync<THub>(act)` on a fresh hub)
  records every event raised. It answers "only `OnAddWebhookFailed` fired".
- **Ordering is tested with `TaskCompletionSource` gates,** never delays.
- **"The hub is a singleton" is its own small test.**

Code: [references/faults-and-notifications.md](references/faults-and-notifications.md), [templates/Probes/](templates/Probes/).

## Flake root causes (summary)

The full list, with symptom, cause and fix for each entry, is in
[references/flake-catalog.md](references/flake-catalog.md). Read it before calling a failure "random".

- A message that retries forever (missing configuration) blocks every later tracked session in its host. Ignore its
  type in `TrackActivity()`.
- A paused queue holds each host's shutdown for Wolverine's 30 s drain timeout. Set `DrainTimeout` to 2 s.
- The 5 s default tracking timeout fails when all hosts boot at once. Use the shared generous timeout.
- `WaitForNonStaleProjectionDataAsync` throws "No event activity was detected" on an empty store. Check the event
  count first.
- A page or subscriber receives the late forwarded notification of its own seed. Use `GivenForwarded`.
- A command still in flight when a test ends lands after the next test's wipe, and a projection recreates the row
  with default fields. Wait for the command's visible result before the test ends.
- A collection-shared faker's `UniqueIndex` gives every test the same "unique" value. Use a per-test seeded faker
  with a random suffix.
- v7 Guids created in the same millisecond do not keep their creation order. Count seeded ids up from a fixed
  instant.
- A test built a date in UTC and failed under UTC+14. Build in the zone the code under test uses.
- A fake's canned replies were shared across tests, so one test's unused reply answered the next. Key fake state by
  a per-test value.
- A big shared collection hid ordering bugs. Splitting it surfaced them, so keep the fixes, not the big collection.

## Running and finishing

- **Fast loops** run the built test dll with xUnit v3 filters:
  `dotnet bin/Debug/net10.0/YourApp.Tests.dll -class 'YourApp.Tests.Webhooks.*'`.
- **`xunit.runner.json`** sets `"longRunningTestSeconds": 60`, so a hang names its test. Wrap runs in
  `timeout 300`: a slice that normally takes a minute and runs past five is a deadlock, not slowness.
- **Containers.** Testcontainers' containers can be invisible to a sandboxed shell's `docker ps`. Check fixture state
  from inside a test.
- **Before you finish:** run the slice three times, then several full-suite runs in a row. Some flakes appear only
  under full-suite load (18 of 18 full runs failed on one of them, while the slice alone was green).
