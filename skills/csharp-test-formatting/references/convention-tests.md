# Convention tests with an exemption ratchet

Style rules that only live in a document drift. Convention tests read the test project's own sources and reflection
data, and fail when a rule is broken. An exemption list lets them land in a suite that does not comply yet.

## How the ratchet works

- **`TestSources`** lists every `.cs` and `.razor` file of the test project outside `bin/`, `obj/` and the shared
  `Testing/` folder, plus every `[Fact]` method found by reflection. A theory counts too, because `TheoryAttribute`
  derives from `FactAttribute`.
- **`ConventionViolations`** returns one string per violation, in a stable format (`file: Method`,
  `Class.Method`, `Namespace.Class`, `file: banned call`). The format is what an exemption entry matches.
- **`Exemptions`** holds today's violators per rule. When the convention tests are introduced, run them once,
  paste every reported entry into its set, and commit.
- **`TestConventionTests`** asserts `violations.Except(exemptions)` is empty for each rule. A new violation fails
  at once.
- **`Every_Exemption_Still_Applies`** fails when an exemption no longer matches anything. Fixing a violation
  therefore forces its entry out of the list, and the list only shrinks.
- **A convention you break is fixed, not exempted.** The only legitimate new entries are those made the day the
  conventions are introduced. Later a deferred refactor may add some, such as wall-clock reads waiting on a
  `TimeProvider` seam, but only when the commit says why.

A failure message lists every entry, one per line, so it can be pasted into the exemptions or worked off directly.

## The rules and their regexes

| Rule | Check |
|---|---|
| Names | `^(Given_)?[A-Z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$`, does not start with `When_` or `<Subject>_` (class name minus `Tests`) |
| Long declarations | a line longer than 120 that matches `^\s*public (async )?(Task\|void\|ValueTask) (?<name>\w+)\(` |
| Misplaced | the class is not in `<namespace minus root, as folders>/<Class>.cs` or `.razor` |
| Without fixture | a class deriving the host test base has neither a `[Collection]` attribute nor `IClassFixture<>` |
| Comments | a `//` line inside a test body other than `// Arrange`, `// Act`, `// Assert`, `// Act & Assert` |
| Support outside Support | a top-level type whose name does not end in `Tests`, in a file outside `/Support/` |
| Banned calls | one regex per call, matched per line, reported per file |

The banned calls of the source project, each with the reason it is banned. Keep those that fit the new project and
add its own:

| Banned | Why | Instead |
|---|---|---|
| `Task.Delay(`, `Thread.Sleep(` | timing guesses flake under load | `TaskCompletionSource` gates, state waits |
| `#region` | hides structure in bases | member order |
| `new Faker(` | bypasses the seeded per-test catalog | the base's `Data` |
| `TestContext.Current.CancellationToken` | private copies of the token | the base's `Ct` |
| `Host.InvokeMessageAndWaitAsync(` | runs on the library's 5 s default timeout and tracks every background message | the base's tracked helper |
| `Host.WaitForNonStaleProjectionDataAsync(` | throws on an empty event store | the base's wait |
| `(WaitAsync\|WaitForStateAsync\|Timeout)(... N.Seconds())` | magic timeouts drift apart | a shared `Timeouts` class |
| `DateTime(Offset)?.(Now\|UtcNow\|Today)` | wall-clock reads fail across zones and midnight | a fixed instant in test data, `TimeProvider` |

The body scan is line-based. It finds a test attribute, then the declaration, and counts braces until they close.
It is not a C# parser. A brace inside a string literal can mislead it, which in practice has not mattered.

## Adopting

1. Copy `templates/Conventions/*.cs` into the shared test folder and set `RootNamespace` and `HostTestBase`.
2. Check the `..` count in `TestSources.FindProjectDirectory`, which depends on where the file sits.
3. Drop rules the project does not want.
4. Run the tests, paste the reported entries into `Exemptions`, and commit that alone.
5. Sweep the suite slice by slice, deleting each slice's exemptions in the same commit as its fix.
6. Add the convention test class to the before-you-finish checklist, so it runs with every slice.

## Example (from a production codebase)

When the convention tests were introduced, the exemptions held 98 names, 99 long declarations, 115 banned calls,
3 comments and 1 misplaced class. A later rule (helpers outside `Support/`) started with 51. After the sweep only
24 wall-clock reads were left, all waiting on a deferred `TimeProvider` seam. One commit moved every wait onto the
shared `Timeouts`. That made 69 numeric-timeout exemptions stale at once, and `Every_Exemption_Still_Applies`
forced them out in that same commit.
