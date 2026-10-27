---
name: csharp-code-structure
description: How production C# reads when its maintainer writes it by hand - where lines break (after `(`, `=>`, `=` or `??`, a wrapped parameter list on one line under the signature, pipelines one call per line, ternaries, booleans and switch arms that don't fit), how a member is shaped (public members as named steps, guards first, fetch-or-throw helpers), when to extract a named local, an Is/Has/Can predicate, a private or static method, a local function, an extension block or a type of its own (records, options, one class per alternative, interfaces, decorators, base classes), naming, member order and the comment policy. Use this whenever you write or change non-test C# code, review a diff for structure, split a long method or class, decide where a helper goes, or sweep a file to one style, even if the user only says "clean this up" or "make it read like the rest of the code". Not for tests (csharp-test-formatting covers them), algorithms or architecture choices.
---

# C# code structure

Production code should read as if one careful person wrote all of it: a public member reads as its steps, every
step and every condition that needs a sentence has a name, and lines break where the reader expects. The rules are
read off code the maintainer wrote by hand and off the maintainer's rework of agent-written code.

- [references/exemplars.md](references/exemplars.md): three of those implementations in full, with notes (a
  connection pool, a chat client factory with a tracking decorator, a search projection base). Read the one closest
  to your task before you write a new class, and whenever a rule below is unclear in a whole file.
- [references/before-after.md](references/before-after.md): twelve reworks, agent code before and the maintainer's
  version after. Read it before you restructure existing code or review a diff.

Assumed: C# 14, .NET 10 and 120 columns. **The project's own rules win.** Before you start, read its `AGENTS.md`
or `CLAUDE.md`, `.editorconfig` and convention tests, check the points in "Where projects differ" below, and name
every conflict you settle in the project's favor in your reply.

## The shape of a member

- **A public member reads as its steps.** Its body is a short list of calls to named steps, so a reader learns what
  happens before deciding whether to read how:

  ```csharp
  public async Task ApplyAsync(
      IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
  {
      ArgumentNullException.ThrowIfNull(EmbeddingProvider);
      var (deletions, updates) = GetRelevantMaps(events);
      DeleteSearchDocuments(deletions, operations);
      await UpdateSearchDocumentsAsync(updates, operations, cancellation);
  }
  ```

- **One level per body.** Once a body calls named steps, a loop body that builds an object or a condition that needs
  a sentence next to those calls becomes a step of its own. Length alone is not the rule (the pool's
  `LeaseClientAsync` is 45 lines of one level); mixed levels are.
- **Guards first, then a flat main path.** Guards are braceless one-liners stacked at the top
  (`ObjectDisposedException.ThrowIf(IsDisposed, this);`, `if (handlers is null) return;`); one that doesn't fit
  breaks after its condition and stays braceless. In a loop, `continue` rejects a case early. The main path never
  sits inside an `if`, because indentation is what a reader skims for the normal case.
- **Special cases return early, the default runs on.** The cheap exit comes first
  (`if (IsCachedAndRecent(state, out var cached)) return cached.Chat(model);`), and a special case returns from its
  own `if` block, so the default path continues unindented below it.
- **Fetch or throw.** A load that must succeed is a helper of its own that opens its own short-lived session and
  ends in `return value ?? throw ...` or a throwing ternary, so no caller checks for null. When the value is used
  further down the same method, guard it instead: `if (stored is null)` with the `throw` on the next line.
- **Await, then use.** An awaited value gets a named local before anything else touches it, and an awaited call
  takes names and plain values as its arguments, not other calls: `var builder = await CreateBuilderAsync(model);`
  then `return Build(builder);`, never `Build(await CreateBuilderAsync(model))`. This holds even when it turns an
  expression body into a block body, because a nested `await` hides when the work happens.
- **One statement, one effect.** No chained assignment (`var chat = _chats[id] = new(...)`): construct, then store.
  A blank line separates a state change from the notification about it.
- **Inputs first, then the construction.** Name the computed inputs (`var vectorText = GetVectorText(source);`),
  then build with a multi-line initializer whose values are names or single calls. Symmetric pairs name the same
  inputs, even where one of them uses a value only once.
- **Symmetric pairs look alike.** `CreateDocumentAsync`/`UpdateDocumentAsync`, `Initialize`/`Resume`,
  `Lock`/`Unlock`, `IndexEvent`/`DeleteEvent`: the same statements in the same order, so the one difference stands
  out.
- **A `try` holds the work its catch is about.** A catch either cleans up and ends in `throw;`, or swallows and
  carries one comment line (`// Best effort, the client is disposed either way`).
- **No guard for a state no caller can reach.** Every guard tells the reader the state happens; a null check no call
  path can trigger is misinformation.

## Where a line breaks

When a line doesn't fit:

- **Break after `(`, `=>`, `=` or `??`, and continue one indent deeper.** The open token at the end of the line tells
  the reader the statement goes on.
- **A wrapped parameter list stays on one line under the signature.** Only when that line doesn't fit either, every
  parameter gets its own line and `)` follows the last one. A base list or `where` clause stays on the parameter line
  when it fits, else takes a line of its own, one indent deeper:

  ```csharp
  public async Task<ShortConnectionLease<TClient>> LeaseClientAsync(
      LeaseClientOptions<TClient> options, CancellationToken ct = default)

  public class ClientConnectionPool<TClient>(GenerationTracker tracker, int capacity) : IAsyncDisposable
      where TClient : IMailService
  ```

- **A positional record** stays on one line with one or two parameters (`public record ProviderModel(Guid Id, string
  Model);`). With more, its parameters are its members and read as a list: one per line, then `);` on its own line,
  or `) where T : ...` when a constraint follows.
- **An expression body stays an expression body when it wraps** (unless an `await` must be named, see above). Break
  after `=>`; a body that is `new()` or `new T(...)` starts on the next line. After a short signature (a property, a
  parameterless local function) the receiver of a chain stays on the `=>` line (`=> conversation.Sessions`); after a
  long one, break after `=>` and start with the receiver.

These shapes break whether or not they would fit, because each part is something the reader checks on its own:

- **Pipelines go one call per line.** A LINQ query or builder chain of two or more calls, a materializer such as
  `ToList()` or `Build()` included, starts with its receiver and puts each call on its own line, one indent deeper.
  Member access (`update.Contents`) is not a call. Two shapes are not pipelines and stay on one line: a factory call
  and its adapter (`openAi.GetChatClient(model).AsIChatClient()`), and a selector with one configuring call
  (`options.Schema.For<TDocument>().NgramIndex(...)`, `options.Policies.ForMessagesOfType<T>().Encrypt()`).
- **No pipeline inside an argument list or a `foreach` header.** It becomes a named local first, one call per line,
  even when used once (`var remaining = messages` ... then `new Turn(remaining, ...)`). A construction inside a call
  stays there while the line fits (`MartenOps.Insert(new Webhook(...))`); once it overflows it becomes a named local,
  and a multi-line named-argument construction becomes a local function or a small private method
  (`NewConversation()`).
- **Initializer braces stand only at statement level, and always multi-line.** An object, collection or dictionary
  initializer, or a `with` expression, opens its braces only as the right side of a declaration or assignment, after
  `return`, as a whole expression body or as a switch arm's result, with one member per line. Never on one line
  (`new Dictionary<string, byte[]> { [KeyId] = keys.Messages }`), and never inside a call's arguments, another
  initializer, a collection expression or a lambda, multi-line or not: braces buried in an expression hide a
  construction the reader should see on its own. Build it first, as a named local in a block body, or as a helper
  (a local function or a private method) where no statement can stand (inside an expression body, a field
  initializer or a lambda) or where the construction deserves a name (`WithValue(error)`). A collection takes a collection expression
  (`[a, b]`) instead of an initializer. Not covered: property patterns (`is { } x`), accessors, and anonymous types
  inside a lambda (`HasKey(book => new { book.Shelf, book.Id })`).

  ```csharp
  var messageKeys = new Dictionary<string, byte[]>
  {
      [KeyId] = keys.Messages
  };

  var provider = new InMemoryKeyProvider(KeyId, messageKeys);
  ```

- **A ternary takes three lines** in a return, an assignment or an expression body: the condition, then `? ...` and
  `: ...`, one indent deeper. A ternary nested in another becomes a switch expression.

  ```csharp
  public TClient Client =>
      IsDisposed
          ? throw new ObjectDisposedException(nameof(ShortConnectionLease<>))
          : client;
  ```

- **A call of parts lists one part per line.** A variadic call (`Join(...)` of several formatted parts) and a tuple of
  results put each element on its own line, `)` or `);` on a line of its own, after the `=>` break. A fixed-arity
  call stays on one line while it fits (`MapEventTypesToStreams(events, _deleteEvents)`).
- **Switch arms:** when one arm's result breaks after its `=>`, all of them do, with a blank line between arms; a
  final `_ => throw ...` may stay on one line.

And these only when they don't fit:

- **A boolean** breaks after `=>` or `=` first; if it still doesn't fit, or its clauses are comparisons a reader
  checks one by one (`shouldKeep`), it breaks before each top-level `&&` or `||`, operators leading and lined up
  under the first operand. A run of short member checks stays on one line (`CanUseConnection`).
- **A call:** when its last argument is a lambda, the earlier arguments stay on the call line and the lambda opens
  there (`Parallel.ForEachAsync(updates, cancellation, async (documentId, ct) =>`). A single long call keeps its
  receiver on the `=` line and breaks before the `.`
  (`var vectorDocs = await session` / `.VectorSearchWithScoresAsync<TDocument>(...)`).
- **A construction** stays positional while it fits (`new(capacity, capacity)`). Once it wraps, it takes named
  arguments, one per line, `)` on its own line.

Braces and blank lines:

- **Guards** (`return`, `throw`, `continue`, `break`) are always braceless. Any other `if` is braceless only when its
  condition and statement share one line well inside the limit; an `if`/`else` is braceless only when both branches
  are one-liners. Once one branch needs more, both get braces. Loops always get braces.
- **Blank lines separate phases** (setup, the work, the result) and stand before a multi-line statement, except where
  the line above prepares it: the wait its `try` releases, the declaration its loop fills. A body of up to three
  one-line statements has none.
- **Independent statements go shortest first.** Registrations, assignments or declarations whose order doesn't
  matter are sorted by line length, so the eye runs down a straight edge. Causal order always wins.

## Extract, inline or promote

Take the first rung that fits:

1. **Inline** a local that only renames a plain construction handed straight on
   (`var evt = new X(...); return Op(evt);` is one statement). Keep it named when it is awaited, when it carries a
   pipeline or construction out of an argument list, or when it gives the next line its meaning
   (`var state = await ...; return state ?? throw ...;`).
2. **A named local** when a value is used twice, or an expression needs a name to be read (`searchDepth`,
   `shouldKeep`).
3. **A named predicate** when a condition has more than one clause, is used in two places, or names a concept:
   `CanUseConnection(client, generation)`, `HasIntervalElapsed()`. A property when it takes no argument
   (`HeartbeatFailed`, `PublishIndication`). A Try-shaped method with a `[MaybeNullWhen(false)] out` parameter when
   the check also yields what it found (`IsCachedAndRecent(state, out var cached)`).
4. **A private method** for a step whose name tells the caller's reader something. Builders and formatters that read
   no instance state are `static` (`CachedRequest`, `CreateError`, `MapEventTypesToStreams`). A private helper whose
   only caller already says what it does goes back into that caller.
5. **A local function** when the helper closes over the method's locals and nothing else would call it (`ReturnLease`,
   `NewConversation`, `PatchConversation`). It sits after the method's last statement, so the decision comes first
   and reads as a choice between two names (`? ResumeConversationOptions()` / `: InitializeConversationOptions()`).
6. **A member of the type it reads.** A helper that takes one of your own types and reads only its members becomes
   a member of that type (`endTime.ToUtc()`, not `ToUtc(endTime)`). A value derived from an input becomes a property
   on the input instead of a local in every handler.
7. **An extension block** for behavior on a type you don't own or want to keep plain: a library interface
   (`IMailService.ReleaseClientAsync`), a query session, store options, a stored record
   (`StoredConversation.AggregatedUsage`). Extensions of your own type sit in that type's file; extensions of a
   library type get `<Type>Extensions.cs`.
8. **One shared home** when a helper, literal or computation appears twice: one `CreateError` for the failed
   response and the failed stream; two sibling files' helper moves to the folder they share.
9. **A generic overload over `typeof`**: `IndexEvent<T>()`, not `IndexEvent(typeof(T))`.
10. **A type of its own**, when one of the cases below applies.

## Types of their own

A type earns its place by owning a rule, not by its size:

- **State with its own rules:** a number changed with `Interlocked` (`GenerationTracker`, two members), a lease
  disposed once, a pooled entry with its own heartbeat. The rule lives in one place instead of at every caller.
- **A value with a meaning:** a record instead of a raw string or a pair that travels (`ProviderModel`), with named
  instances for special values (`ChatExecutionContext.Unknown`). A private method may still return a named tuple its
  caller deconstructs at once (`var (deletions, updates) = GetRelevantMaps(events);`).
- **Inputs that will grow:** an options type with `required`, `init`-only properties instead of a bare delegate or
  flag parameter (`LeaseClientOptions<TClient>.Factory`), so the next setting is a property, not a new parameter.
- **Alternatives:** an abstract base and one type per case, dispatched with a switch expression
  (`InitializeConversationOptions`, `ResumeConversationOptions`), instead of a bool and fields null in one case.
- **A failure a caller can act on:** an exception type whose primary constructor builds the message.
- **A capability two types share:** a small interface (`ILockableMailService` over the IMAP and SMTP clients).
- **A cross-cutting wrapper:** a decorator (a `DelegatingChatClient` subclass) plugged in by a builder hook in the
  same file, instead of the same calls around every call site.
- **Classes that differ only in data:** an abstract base that owns the algorithm, abstract hooks, protected
  registration methods the derived constructor calls, and a `static abstract` interface member for configuration
  (`SearchProjection<TDocument, TSource>`).
- **A private pair:** a private nested record names a cache entry without leaking it (`VersionedClient`).
- **A decision made once:** return the configured function (`Func<string, IChatClient>` per provider) instead of
  deciding again on every call.

A feature registers itself in its own `Add<Feature>()`, never in a central configuration. Inside a feature, one
folder per concept (`Clients/`, `Exceptions/`, `Extensions/`, `Pooling/Lease/`). One concept per file, named after its
type; satellites share the file: a document and its projection, an options hierarchy, a decorator and its hook.

## Names

- **The verb says the kind of work:** `Get` computes, `Create` builds, `Resolve` picks between alternatives, `Map`
  turns one set into another, `Ensure` checks, `Release` frees, `Perform` runs an action, `Raise` notifies. A helper
  over a store follows the API it wraps (`FetchProviderAsync` over `FetchLatest`, `LoadDocumentsAsync` over
  `LoadManyAsync`). Public async methods end in `Async`.
- **Predicates:** `Is`/`Has`/`Can` for checks, a past participle for a state flag (`HeartbeatFailed`, `Resumed`),
  `should...` for a local.
- **Locals and lambda parameters name the role:** `pooled`, `keep`, `deletions`, `cached`, `completed`. `x` only in a
  one-line lambda that reads one member (`.Select(x => x.StreamId)`).
- **Tokens:** overrides and interface implementations keep the base's parameter name; own code uses the project's
  (`ct` in the exemplars).
- Events are `On<What happened>`; constants and static readonly fields PascalCase; private fields `_camelCase`. A
  lazily created property uses `field` (`public BookService Books => field ??= new(this);`), not a declared field.

## Order in a file

1. Constants, each with its one-line why when the value isn't self-evident; then static readonly, readonly and
   mutable fields.
2. In a base class the abstract hooks; then properties and events.
3. Public members, each followed by its helpers: the step that does the most work first, with its own helpers below
   it, then the small predicates, then helpers of one kind grouped (the fetchers together).
4. A helper shared by several members sits after its last caller; disposal comes last.

A reader goes top-down, so the file reads like an outline: what a member does, then how.

## Comments and docs

- **A comment is one line, directly on the statement it justifies**, and says what the code cannot: a race, an order
  that must not change, why a catch swallows, why a constant has its value, why a deliberate no-op is right. Two
  commented statements get a blank line between them. In a configuration block a short label of a setting's purpose
  is enough (`// Support correlation IDs`).
- **Never a section comment, history, or the call restated.** A block that needs a heading needs a name.
- **A public member's `<summary>` is one sentence of its contract**, including the edge a caller must know ("never
  throwing", "throws an `ObjectDisposedException` once the lease is disposed").

## Lifetime and concurrency idioms

- A flag is a `byte` or `int` set with `Interlocked.Exchange`; dispose once with `if (Interlocked.Exchange(ref
  _disposed, 1) != 0) return;`, and guard entry points with `ObjectDisposedException.ThrowIf`.
- A `SemaphoreSlim` guarding a block is released in a `finally`; one counting capacity is released where the capacity
  comes back. A short critical section is a `lock` on a `Lock` field. Time comes from an injected `TimeProvider`.

## Where projects differ

Check each against the project before writing; the exemplars took one side, the project may take the other:

- the column limit (120 here; some projects use 100), `sealed`, and records or classes for settings and documents;
- which XML doc tags a public member needs (a summary only, or every `param`, `typeparam`, `returns`);
- extension blocks everywhere, or classic `this` methods for `IServiceCollection` and builder hooks;
- whether every chained builder call starts its own line, even a single one (`WithChatOptions()` /
  `.WithModel("fast")`), which overrides the two one-line exceptions under "Pipelines";
- the token name, the summary's line shape, and marker comments for known limits (`shortcut: ...`).

## Restructuring existing code

Reshape only what your change touches, unless the task is a sweep: a drive-by reformat hides the real change. A
sweep goes one file or feature per commit, moves and renames first, behavior unchanged and never with a feature.

## Enforce what a pattern can check

With the exemption ratchet of `csharp-test-formatting`'s convention tests (today's violators are listed, the list
only shrinks): lines past the limit; `//` comment blocks of two or more lines outside XML docs; a method's wrapped
parameter list split one per line although it fits on one continuation line (a Roslyn syntax walk); an `await`
inside another call's argument list; an initializer or `with` whose `{` shares a line with a member, or that sits
inside an argument list. The rest needs a reader: review a diff with this file and the closest exemplar open, and
report each deviation as file:line with a before and after.

## Before you finish

- Reread every member you changed from the top: named steps, named conditions, no nested `await`, no initializer
  braces inside an expression, breaks at the points above, helpers below their callers.
- Hold the file next to the closest exemplar; a shape the exemplar would not have needs a reason.
- No comment a name could replace, no guard for an unreachable state, nothing computed twice.
- Build without warnings and run the tests and convention tests the project names.
