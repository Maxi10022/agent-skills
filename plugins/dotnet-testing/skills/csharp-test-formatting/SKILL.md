---
name: csharp-test-formatting
description: Naming, layout and assertion style for C# tests (xUnit, NUnit or MSTest with FluentAssertions) - Given_<state>_<outcome> names, Arrange/Act/Assert sections and blank lines, statements ordered by length within the causal flow, `Given(` event blocks with one event per line, one assertion per statement instead of .And/.Which/.Subject chains, x.Assert.Y() assertion helpers as C# 14 extension blocks, named-argument constructor calls, the comment policy, and convention tests with an exemption ratchet that enforce it all. Use this whenever you write a new C# test, reformat or review existing tests, sweep a test suite to one style, split a test that checks too much, or add an assertion helper, even if the user only says "clean up these tests". Not for choosing fixtures, waiting strategies or test-data design.
---

# C# test formatting

Every test should read the same way. The name states one rule, `Given(` lists the state, the act is visible, and the
asserts walk to their subject in short lines. These rules come from a test-suite rework in which the maintainer
reformatted sample tests by hand. [references/before-after.md](references/before-after.md) holds those samples as
before/after pairs. Read it when a rule below is unclear in practice.

Assumed: C# 14 for `extension` blocks, xUnit attributes in the examples, FluentAssertions 7. On older C#, write the
helpers as classic extension methods (`fault.Assert().ThrownException<T>()`); the rest applies unchanged.

The snippets are examples from a production codebase, an event-sourced .NET app. Its domain names (Webhook,
SecretStore, ShareLink) and base helpers (`Given`, `SendMessageAndWaitAsync`) illustrate the rules; they are not requirements.

## What a test is

- **One test class per subject:** a command, projection, component, page or value object.
- **A test asserts exactly what its name says, and arranges only what that needs.** An assertion the name does not
  promise belongs in another test or nowhere. A removal test that asserts nothing about the secret does not store
  one.
- **Every effect the name promises is asserted.** "Setting every field" also asserts the new secret. "Changes
  nothing else" asserts that the other fields stayed unchanged.
- **One business rule per test.** A test that checks several rules becomes one test per rule. A 20-assert
  "super-test" is hard to read and hides which rule broke.
- **Fewer tests are fine when no assertion is lost.** A command test and the projection it alone touches can share
  one test while the logic stays simple.
- **A bug is a red test.** Never wrap a bug in a green test, and never write "expected" bugs. When a suite is reworked,
  known reds stay red under their new names.

## Names

- **`Given_<state>_<outcome>`, or only `<outcome>` for the base case.** The class names the subject, so the test
  name does not repeat it:
  - `UpdateWebhookTests.Given_A_Webhook_Setting_Only_The_Name_Changes_Nothing_Else`
  - `AddWebhookTests.Stores_The_Secret_Under_The_Webhook_Id`
- **No `When_` and no subject prefix.** `When_UpdateWebhook_Is_Handled` only repeats the class.
- **The declaration line fits 120 columns,** which keeps names to the state and the outcome.

## Comments

- **Inside a test body,** only `// Arrange`, `// Act`, `// Assert` and `// Act & Assert` are allowed. A test that
  needs any other comment needs a different shape.
- **`// Act & Assert` is for an act that is itself the assertion,** such as a throws or not-throws check, or a
  status-only HTTP scenario.
- **Outside test bodies,** comments are one line at most and say why. Helpers get no XML docs, there are no section
  dividers and no `#region`. A comment about a value sits on the test data.

## Arrange, Act, Assert

- **No `// Arrange` when there is nothing to arrange.**
- **Arrange** runs in this order: the `Given(` block, then side state such as a stored secret or a setting, then the
  command or request when it is a variant.
- **Act** holds the action, then only the reads the asserts need, in the order the asserts use them. A read that
  exists only to be asserted, such as a stored secret, becomes an assertion helper in Assert:
  `await Secrets.Assert.SecretAsync(id, key);`.
- **Assert** has one group per subject, with a blank line between groups: the secrets, then the event; the rows, then
  the toasts.

```csharp
[Fact]
public async Task Given_No_Webhook_RemoveWebhook_Is_Dead_Lettered_And_Published_As_A_Fault()
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
```

## Layout

- **`Given(` lists one event per line, even a single one, and `);` closes on its own line.** The given reads as a
  list, and adding an event is a one-line diff:

  ```csharp
  await Given(
      WebhookAdded(),
      WebhookRemoved()
  );
  ```

- **A constructor call with several arguments uses named arguments, one per line.** Inside a test it closes on the
  last argument. An expression-bodied builder (`=> new(`) closes `);` on its own line, like `Given(`:

  ```csharp
  using var probe = new NotificationProbe<WebhookAdded>(
      notifications: Notifications,
      eventName: nameof(Notifications.OnWebhookAdded),
      predicate: OwnStream);
  ```

- **A multi-line statement gets a blank line on each side.**
- **Order inside a section:** statements with fewer lines first, which gets the short ones out of the way. Among
  statements of the same height, the shorter line comes first. The causal order always wins. Inside a single block,
  character length may run either way.
- **A chain that does not fit** puts the receiver on its own line, then one call per line, indented once.
- **The command goes inline into the act:** `await SendMessageAndWaitAsync(RemoveWebhook());`. A `var cmd` exists
  only when something else reads it. A variant, such as a partial update, is a multi-line initializer in Arrange.
- **Act variables name their role:** `firstSubscriber`, `publishTask`, `promptTask`, `optimistic`, `forwarded`. A
  blank line separates the phases of an act.

## Assertions

- **One assertion per statement, and no `.And`.** `HaveCount(2).And.ContainSingle(...)` becomes two statements.
- **No `.Which`, no `.Subject`, and no variable that only carries an intermediate subject.** Assert the "road" to the
  final assertion in short lines, or use a helper:

  ```csharp
  // Negative example
  result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(CreateKey.ExpiresAt));

  // The road
  stored.Sessions.Should().ContainSingle();
  stored.Sessions[0].ModelRef.Should().Be(Model);

  // A recurring check
  result.Assert.SingleError(nameof(CreateKey.ExpiresAt));
  ```

  The maintainer's samples keep one exception, in an `// Act & Assert` whose act returns the value under test:
  `validate.Should().NotThrow().Which.Assert.ErrorOn(nameof(Address.Country));`.

- **A recurring check becomes an assertions class exposed as `.Assert`** through a C# 14 `extension` block. The class
  is `sealed`, holds the subject, runs the road inside and returns the subject (or the next one), so a chain can
  continue. Shared helpers live in one shared assertions folder:

  ```csharp
  public sealed class FaultAssertions<T>(Fault<T> fault) where T : class
  {
      public Fault<T> ThrownException<TException>() where TException : Exception
      {
          fault.Exception.Type.Should().Be(typeof(TException).FullName);
          return fault;
      }
  }

  public static class FaultAssertionExtensions
  {
      extension<T>(Fault<T> fault) where T : class
      {
          public FaultAssertions<T> Assert => new(fault);
      }
  }
  ```

  [references/assertion-helpers.md](references/assertion-helpers.md) shows more helpers (validation results, event
  streams, optional fields, async secret reads), and [templates/AssertionsTemplate.cs](templates/AssertionsTemplate.cs)
  is a starting point.
- **A library's own assertion stays as it is.** Wolverine's `SingleMessage<T>()` is the assertion, with no
  `.Should()` around it. Alba's `scenario.StatusCodeShouldBe(...)` stays inside the scenario.
- **Two FluentAssertions traps:**
  - `Should().Be(object)` never applies an implicit conversion, so a wrapper (a test-data `TestValue<T>`, an
    `Optional<T>`) never equals the raw value. Name the variant or write a helper.
  - `BeOfType<I>()` checks the exact runtime type, so for an interface use `BeAssignableTo<I>()`.

## Helpers and test bases

- **Base helpers mirror the library's names:** `SendMessageAndWaitAsync`, `InvokeMessageAndWaitAsync`, not
  `SendAsync`.
- **The act stays inline.** `await Host.Scenario(scenario => { ... });` appears in every endpoint test, because a
  helper around the act hides it. Helpers that arrange or read (`IssueKeyAsync`, `FindStoredAsync`) are fine.
- **Reuse what the base offers.** Do not rebuild a store, load or fetch helper in a test class.
- **A helper that takes the subject first is an extension member:** `cut.ClickAsync("Cancel")`, `cut.Notes()`.
  Generic ones go in a shared extensions class, slice-specific ones in `<Slice>/Support/<Subject>Extensions`. Within
  one slice their names stay unique, because they all extend the same type.
- **Order tasks with `TaskCompletionSource` gates, never with `Task.Delay` or `Thread.Sleep`:**

  ```csharp
  // Act
  var publishTask = notifications.PublishAsync(created);
  firstSubscriber.SetResult();

  var completedEarly = publishTask.IsCompleted;
  secondSubscriber.SetResult();
  await publishTask;
  ```

- **Cancellation tokens come from the base's `Ct`.** No private copies.
- **Base member order:**
  1. the test-data shortcuts;
  2. the service-registration override;
  3. per area of the slice: its events, its commands, its other builders.

  Each event and command has one parameterless builder. There is no `#region`. Builders shared by several test
  classes live on the slice base.
- **A test without a host derives from a host-free unit base,** unless its subject already has a class of host
  tests. Then it joins that class.

## Theories

- `InlineData` comes first. Use `TheoryData` only for large or generated sets.
- A boundary that is not a constant, such as a 256-character note, cannot go into an attribute. It gets its own
  `[Fact]` and reads the invalid value from the test data.

## Files

- A test class lives in a file named after it, in the folder its namespace names.
- A slice folder mirrors the production folders and holds test classes only. Everything else the slice's tests share
  goes into `<Slice>/Support/`: the base, the test data, the fixtures, helpers and fakes. Shared infrastructure goes
  into one `Testing/` folder.
- Test files are `.cs`. Use a `.razor` test file only for a test that writes Razor markup inline.

## Enforce it with convention tests

A small set of tests reads the test sources and fails on:

- names that are not `Given_<state>_<outcome>`;
- declarations past 120 columns;
- a class outside its own file;
- a host test without a fixture;
- a foreign comment in a test body;
- a helper outside `Support/`;
- banned calls: `Task.Delay`, `Thread.Sleep`, `#region`, `new Faker`, a raw cancellation token, raw waits, numeric
  timeouts, the wall clock.

Today's violators go into an exemption list that only shrinks. A test fails when an exemption no longer applies, so
the list cannot rot. Read [references/convention-tests.md](references/convention-tests.md) and copy
`templates/Conventions/` when the project has no such tests yet. The templates assume the test project imports the shared `Testing` namespaces as global usings
(`<Using Include="YourApp.Tests.Testing.Bases" />` and so on in the `.csproj`).

## Before you finish

- Build without warnings in the files you touched.
- Run the touched test classes three times; a test that passes once can still be flaky.
- Run the convention tests. Fix a convention you broke; never exempt it.
- Write a commit message that lists every renamed, split, merged or dropped test.

## Sweeping an existing suite

Work one slice per commit. Moving the support files into `Support/` is its own "moves only" commit, before the
formatting commit. Record what happened to each test in the commit message:

```
Swept the <Slice> tests to the agreed formatting: <data, builders, helpers, files that changed>

Splits (one business rule per test):
- <Subject>: <rule a> / <rule b>

Merges: <tests folded together, and why no assertion is lost>

Dropped assertions, each covered elsewhere: <assertion> (<the test that covers it>)

Added: <new tests or assertions>

Known reds stay red under new names: <Class.Test>

Test methods <before> -> <after> in the slice; green three times in a row with the conventions.
```

## References

- [references/before-after.md](references/before-after.md): eight before/after pairs from the maintainer's samples (names,
  givens, assertion chains, probes, a split super-test, an inline HTTP act, page helpers, base hygiene). Read it
  before reformatting existing tests.
- [references/assertion-helpers.md](references/assertion-helpers.md): the helper pattern with several real helpers.
- [references/convention-tests.md](references/convention-tests.md): how the convention ratchet works, its regexes,
  and how to adopt it.
