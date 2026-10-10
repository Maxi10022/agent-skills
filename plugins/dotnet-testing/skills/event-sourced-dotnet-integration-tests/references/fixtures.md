# Fixtures: one server, a database per fixture

## Contents

1. Why one server
2. `PostgresServer`
3. `FixtureBase`
4. Slice fixtures and collections
5. Choosing between a collection and a class fixture
6. Example numbers (from a production codebase)

## 1. Why one server

Starting a Postgres container costs seconds, and a suite that gives every fixture its own container pays that once
per fixture. One container per run, with a fresh database per fixture, keeps the isolation (no fixture sees another
fixture's tables) at a fraction of the cost. Within a fixture, tests isolate themselves by their own ids, not by
separate databases.

## 2. `PostgresServer`

[templates/PostgresServer.cs](../templates/PostgresServer.cs) is an xUnit v3 assembly fixture:

```csharp
[assembly: AssemblyFixture(typeof(YourApp.Tests.Testing.Fixtures.PostgresServer))]
```

- **The image is the one production uses.** It must include the extensions the app needs, for example
  `pgvector/pgvector:pg18` when Marten stores vectors.
- **Throwaway settings:** `fsync=off`, `synchronous_commit=off`, `full_page_writes=off`. A crash loses nothing that
  matters.
- **`max_connections=1000`.** Every Alba host keeps its own Npgsql pool, and Marten's daemon and Wolverine's durability
  agent hold connections too.
- **`CreateDatabaseAsync(prefix)`:**
  - The name is the prefix (the fixture's type name, cut to 24 characters) plus a v7 Guid, lower-cased, which stays
    well under Postgres' 63-byte identifier limit.
  - The `CREATE DATABASE` runs behind a `SemaphoreSlim`, because Postgres copies `template1` under a lock and fixtures
    starting in parallel would contend for it.
  - It returns a connection string with `IncludeErrorDetail = true`, so a constraint failure names its values.
- **Nothing is dropped.** The databases go away with the container at the end of the run.

## 3. `FixtureBase`

[templates/FixtureBase.cs](../templates/FixtureBase.cs) creates its database, then boots the app in an Alba host:

- **`JasperFxEnvironment.AutoStartHost = true`.** Wolverine and Marten start with the host.
- **`builder.UseSetting("ConnectionStrings:<name>", connectionString)`.** The connection string reaches the app the
  way production configuration does. An extension member such as `builder.UseConnectionString(...)` reads well.
- **`services.RunWolverineInSoloMode()`.** It skips leader election and node assignment, so durability agents start
  at once.
- **`options.Durability.DrainTimeout = 2.Seconds()`.** A paused or requeuing local queue otherwise holds every host's
  shutdown for the default 30 s.
- **External services are replaced**: a deterministic stub for an embedding generator, a dummy API key for a
  third-party API client. No test reaches the cloud.
- **`new AuthenticationStub()`** is passed to `AlbaHost.For`, so protected endpoints accept the test's calls.
- **`Configure(IWebHostBuilder)`** is virtual and runs last, so a derived fixture's settings win over the defaults.

A derived fixture that owns extra resources starts them before `base.InitializeAsync()` when the host needs them,
and disposes them after `base.DisposeAsync()`.

## 4. Slice fixtures and collections

A slice usually has one fixture type and one collection:

```csharp
public class WebhookFixture(PostgresServer server) : FixtureBase(server);

[CollectionDefinition(nameof(WebhookTestCollection))]
public class WebhookTestCollection : ICollectionFixture<WebhookFixture>;

[Collection(nameof(WebhookTestCollection))]
public class AddWebhookTests(WebhookFixture fixture) : WebhookTestBase(fixture) { ... }
```

A page class with a fixture of its own derives a named fixture type, so its database is named after it:

```csharp
public class WebhooksPageFixture(PostgresServer server) : WebhookFixture(server);

public class WebhooksPageTests(WebhooksPageFixture fixture) :
    WebhookTestBase(fixture), IClassFixture<WebhooksPageFixture>, IAsyncLifetime { ... }
```

The fixture, the collection, the slice base and the slice's test data live in `<Slice>/Support/`. The server, the
fixture base and the shared base live in one shared `Testing/` folder.

## 5. Choosing between a collection and a class fixture

xUnit runs collections in parallel, and the classes inside one collection serially. A class with an
`IClassFixture` and no `[Collection]` is a collection of its own. So:

- **B** is the boot time of one fixture: database, host start, the first Wolverine codegen. Measure it from the
  first test's duration in a fresh fixture minus a later test's.
- **S** is the serial time of a class or collection: the sum of its test durations in the test report.
- **The wall time** is about the largest S among the collections that run at once, plus B. That collection is the
  critical path.

The rules:

1. No host, no fixture.
2. A host test joins its slice's collection.
3. A class gets its own fixture for a different host configuration, or when S ≥ 2B and the class is on the critical
   path. Taking it out of the collection then shortens the path by more than the extra boot costs.
4. A collection that is the critical path splits along slices, when each part runs at least 2B.
5. Never for isolation. A test that needs a clean table filters by its own ids or wipes (a page listing
   everything). A flake that a fresh fixture "fixes" is a test leaking state into the next one; find the leak.

## 6. Example numbers (from a production codebase)

- **One container:** 16 containers became 1 assembly fixture. The wall time stayed at 63–68 s, because one
  default collection (218 tests, 55 s serial) was the critical path.
- **Splitting the default collection by the rule:** three slices (85, 46 and 14 tests) got
  collections of their own, each past twice the 3 s boot. The wall time went from 73 s to 38–40 s.
- **Fixtures moved back into a collection:** four import handler classes with class fixtures, each running less
  than B, joined their slice's collection. Their reads now filter by the test's own key id.
- **Fixtures kept:** two page classes with S ≥ 2B kept class fixtures and wipe their table before each test.
- **The drain timeout:** at 2 s, one host's teardown went from 31 s to 3 s.
- **What the split exposed:** a message that retried forever in a shared host had been hidden by test order.
