# bUnit gotchas: symptom → cause → fix

Most entries cost at least one failed run whose error message did not name the cause. The rest explain why the
shared helpers look the way they do. Entries marked **(host)** only happen in page tests that run against a real
Marten/Wolverine host.

## Setup

| Symptom | Cause | Fix |
|---|---|---|
| `New services/implementations cannot be registered ... after the first services has been retrieved` | A registration ran after `SetRendererInfo`, the JSInterop setup or any `Services.GetRequiredService` | Register everything in the `InitializeComponentServices` hook, which runs right before the first render; renderer setup last |
| A header button or search box "does not exist" | The page projects it into the layout through `SectionContent`, and the test has no layout | Render a `SectionOutlet` with the layout's section name beside the page (`RenderPage<TPage>()`) |
| A confirm or prompt dialog never appears | No dialog provider in the render tree | Render the library's dialog provider beside the page |
| Dropdown items, sheets or toasts missing, or rendered twice | No portal host, or a test added its own on top of the base's | The base wraps every render in exactly one portal host; leaf tests never add one |
| A second `Render` in one test threw | An earlier base registered services on every render, and bUnit refuses registrations after the first resolution | One render per test; the base now registers once, before the first render |
| The page uses a different notification hub than the host forwards to | The bUnit container built its own instance | Register the host's instance: `Services.AddSingleton(Host.Services.GetRequiredService<THub>())` |

## Driving the UI

| Symptom | Cause | Fix |
|---|---|---|
| Typing into a library input changes nothing | The component listens to its `ValueChanged` parameter, not to DOM change events | `cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value))` |
| A menu item click does nothing | The item renders in a portal, and its handler is the component's `OnClick` | Open the menu, find the item component by its markup, invoke `item.Instance.OnClick` inside `cut.InvokeAsync` |
| A click hits the wrong control | "The first button with this text" also matches table headers, select triggers or tabs | Scope the selector: `thead button`, `[role=dialog] button`, `aside nav button` |
| An `aria-expanded` check fails when collapsed | Blazor drops a `bool` attribute that is `false` | In the component, write `aria-expanded="@(Expanded ? "true" : "false")"` |
| A typed edit is lost and Save is a no-op (only under full-suite load) **(host)** | The test typed before the pane showed the clicked item; the load then replaced the form | The select helper waits until the pane shows the clicked item before returning |

## Waiting

| Symptom | Cause | Fix |
|---|---|---|
| A wait passes, then `FindAll(...)` finds nothing (only after other tests ran) | The predicate read a child component's parameters, which update before the markup | Wait on rendered elements: `cut.FindAll("tbody tr")`, `cut.Markup.Contains(...)` |
| A one-off read right after an action is wrong under load | The render had not landed yet | `cut.WaitForStateAsync(() => <state>, Timeouts.Render)` |
| The test hangs with no output | It awaited an action whose handler awaits a confirm or prompt dialog | Hold the task, wait for the dialog markup, answer it, then await the task |
| A slice that runs in a minute is still running after five | One of the above deadlocks | Run under `timeout 300` with `longRunningTestSeconds`, and look for an awaited dialog action |

## Data from elsewhere **(host)**

| Symptom | Cause | Fix |
|---|---|---|
| A row is listed twice, an edit is overwritten by the stored value, the page toasts its own seed rows | The forwarded notification of the test's own seed arrived after the render | Seed with `GivenForwarded(...)`, batched into one call |
| A stray row from an earlier test, or a `NullReferenceException` loading a half-built row in the next test | The previous test ended with its command in flight; it landed after the wipe and a projection recreated the row with default fields | Wait for the command's visible result, then for non-stale projections, before the test ends |
| `No event activity was detected within the timeout span` in `InitializeAsync` | Marten's daemon wait throws on an empty event store | The base's wait (and `WipeAsync`) return early when the store has no events |
| Counts include rows of other tests | The page lists everything and shares a database | Wipe in `InitializeAsync`, or assert only on the test's own rows |
| Seeded rows appear in an unexpected order | v7 Guids from one millisecond do not keep their order | Derive seed ids from a fixed instant that counts up |

## Fault paths **(host)**

| Symptom | Cause | Fix |
|---|---|---|
| The page shows the real result instead of the fault | The real bus handled the command before the test published its fault | Call `StubBus()` before the render; the substitute swallows the command |
| `Sent<T>(bus)` finds no call | The test read it before the page sent | Wait for the pending state (`!cut.Settled()`) first |
