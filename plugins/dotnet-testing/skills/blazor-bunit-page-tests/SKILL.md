---
name: blazor-bunit-page-tests
description: bUnit tests for Blazor pages and components, rendered alone or against a real host (Alba with Marten/Wolverine) - a component test base that registers services before the first render and wraps every render in a portal host, RenderPage with a SectionOutlet and dialog provider, page helpers as C# 14 extension members, DOM-based waits instead of one-off samples, held tasks for confirm and prompt dialogs, a stubbed message bus for command failure paths, wiping and forwarded seeding for pages that list everything. Use this whenever you write, fix or review a bUnit test for a Blazor page, dialog, sheet, table or form, register services for a bUnit render, test optimistic UI or toasts, or chase a bUnit test that hangs, misses a control, or flakes under full-suite load ("cannot register services after the first was retrieved", rows from an earlier test, late notifications, a dialog await that never returns). Not for Playwright or browser end-to-end tests.
---

# Blazor bUnit page tests

bUnit renders components in-process. Leaf components render alone. Pages render against the real host, so their
commands, projections and notifications are the real ones. Most failures come from five places:

- services registered in the wrong order;
- layout pieces missing from a render that has no layout: section outlets, dialog providers, portal hosts;
- reading the DOM before the render landed;
- awaiting an action that waits for a dialog;
- data from another test, or the test's own seed, arriving late.

Assumed stack: bUnit 2 (`BunitContext`, `Render`, `WaitForStateAsync`), Blazor Server interactive rendering, a
component library with portals, dialogs and toasts (BlazorBlueprint in the examples, so substitute your library's
types), xUnit v3, FluentAssertions, NSubstitute. For the host side (fixtures, `Given`, tracked sessions, daemon
waits) use the `event-sourced-dotnet-integration-tests` skill if it is installed. Snippets with domain names (the
Webhooks page, `WebhookAdded`, `UpdateWebhook`) are examples from a production codebase and illustrate the pattern
only.

## The bases

```
BunitContext
  ComponentTestBase              Data, Ct, Toasts, the services hook, portal host, renderer setup
    <Slice>ComponentTestBase     leaf components: read models built in the test, its own notifications instance
    LoopbackBase                 a real HTTP server on 127.0.0.1:0, for code that only talks to a URL
    TestBase(fixture)            the host: auth stub, document session, message bus or its stub, RenderPage<TPage>
      <Slice>TestBase            the slice's host singletons, test-data shortcut, builders
```

Templates: [templates/ComponentTestBase.cs](templates/ComponentTestBase.cs),
[templates/TestBase.Pages.cs](templates/TestBase.Pages.cs) (the page half of the host base),
[templates/RenderedComponentTestExtensions.cs](templates/RenderedComponentTestExtensions.cs),
[templates/PageExtensions.example.cs](templates/PageExtensions.example.cs),
[templates/LoopbackBase.cs](templates/LoopbackBase.cs). The templates assume the test project imports the shared `Testing` namespaces as global usings
(`<Using Include="YourApp.Tests.Testing.Bases" />` and so on in the `.csproj`).

## Setup order

- **`InitializeComponentServices()` runs once, right before the first render.** The base overrides both `Render`
  overloads to call it. A test can therefore still register services in Arrange; `StubBus()` relies on that.
- **The renderer setup runs after every registration:** `JSInterop.Mode = JSRuntimeMode.Loose` and
  `SetRendererInfo(new RendererInfo(rendererName: "Server", isInteractive: true))`. Both resolve services, and bUnit
  throws "New services/implementations cannot be registered ... after the first services has been retrieved" for
  any registration after that.
- **Every render is wrapped in the library's portal host,** so dialogs, sheets, dropdowns and toasts have somewhere
  to render. A leaf test never adds its own; a second host duplicates portal content.
- **One render per test.** The page or component under test is the only thing a test renders.

## Pages against the host

The host base's `InitializeComponentServices()` registers what a page resolves:

- an authentication state stub for a fixed user and username;
- a scoped `IDocumentSession` from the host's store;
- the host's `IMessageBus`, unless the test called `StubBus()`.

A slice hook then adds the slice's singletons. They must be the very instances the host uses, so the page receives
what the host forwards: `Services.AddSingleton(Host.Services.GetRequiredService<WebhookNotifications>())`.

`RenderPage<TPage>()` renders three things side by side:

- a `SectionOutlet` named like the layout's header section. Header controls a page projects through
  `SectionContent` do not exist without it.
- the dialog provider. Without it, confirm and prompt dialogs never appear.
- the page.

## Helpers are extension members

- **A helper that takes the rendered component first is a C# 14 extension member** on `IRenderedComponent<T>`:
  `cut.ClickAsync("Add webhook")`, `cut.Names()`, `cut.Settled()`.
- **Generic helpers go in one shared class.** bUnit already has a `RenderedComponentExtensions`, so name yours
  `RenderedComponentTestExtensions`. It holds:
  - `Settled()` (no spinner);
  - `Button(text)`, `ClickAsync(text)`;
  - `Input(id)`, `TypeAsync(id, value)`;
  - `SubmitAsync()`, `CloseDialogAsync()`;
  - `Row(firstCell)`, `RowActionAsync(firstCell, action)`.
- **Page helpers go in `<Slice>/Support/<Page>Extensions`.** All helpers of a slice extend the same type, so their
  names stay unique within the slice.
- **Rendering, seeding and the bus stub stay class or base members:** `RenderPageAsync(rows)`, `GivenWebhooksAsync(...)`,
  `StubBus()`.
- **Drive library inputs through their parameters** (`cut.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value))`)
  and menu items through `item.Instance.OnClick`. Drive native inputs through `InputAsync` and `KeyDownAsync`.
- **Scope selectors.** "The first button with this text" also hits table headers, select triggers and tabs with the
  same label, so use `thead button` or `[role=dialog] button`.

## Waiting reads the DOM

- **Wait with `cut.WaitForStateAsync(() => <DOM predicate>, Timeouts.Render)`.** The predicate reads rendered
  markup: `cut.Names().Count == 2`, `cut.Markup.Contains("No webhooks")`, `cut.Settled()`. A child component's
  parameters update before the markup does, so a wait on `FindComponent<X>().Instance.Rows` passes while
  `FindAll("tbody tr")` still returns the previous render.
- **Wait for a state instead of sampling it once after an action.** Under load the render has not landed yet. A fault
  test waits for the row to go pending before it reads what the bus received.
- **Capture an optimistic state right after the action, when the page updates synchronously**
  (`var optimistic = cut.Names(); var pending = !cut.Settled();`). Then wait for the settled state.
- **End every test that sends a command by waiting for that command's visible result,** then for non-stale
  projections. Assert the stored document as well, which is the stronger assertion. A command still in flight when
  the test ends lands in the next test.
- **Every timeout comes from a shared `Timeouts.Render`.** A page waits for the command it sent, so a render wait is
  as long as a tracked one.

## Seeding and wiping

- **Wipe in `InitializeAsync` when the page lists everything:**
  `public async ValueTask InitializeAsync() => await WipeAsync<Webhook>();`. The wipe catches the daemon up first,
  or a projection still in flight brings a deleted row back. Such a page class usually has a class fixture of its
  own, because it runs long and serially.
- **Seed through `GivenForwarded(...)`, never `Given(...)` plus a projection wait.** Otherwise the page receives the
  late notification of its own seed data, and a row appears twice or an edit gets overwritten.
- **Batch seeds into one call.** A private helper builds `(Guid stream, object event)` pairs:

  ```csharp
  // A fixed url keeps a search from matching anything but the name and event each webhook was given
  private async Task GivenWebhooksAsync(params (string Name, string Event)[] webhooks)
  {
      var seeds = webhooks.Select(webhook => (Guid.CreateVersion7(), (object)new WebhookAdded(
          Name: new WebhookName(webhook.Name),
          Url: new WebhookUrl("https://hooks.example.com/orders"),
          Events: [webhook.Event]
      )));

      await GivenForwarded([.. seeds]);
  }
  ```

- **A live change "from elsewhere" is a `GivenForwarded(...)` in the Act**, after the render. Then wait for the DOM
  to show it.

## Dialogs: hold the task

A page handler that awaits a confirm or prompt dialog completes only once the dialog is answered. Awaiting the click
that opens it deadlocks the test. Hold the task instead:

```csharp
// Held, not awaited, an awaited confirm deadlocks the test before the dialog can be clicked
public async Task<Task> OpenDeleteDialogAsync(string name)
{
    var deleteTask = cut.RowActionAsync(name, "Delete webhook");
    await cut.WaitForStateAsync(() => cut.Markup.Contains("Delete webhook?"), Timeouts.Render);
    return deleteTask;
}

// in the test
var deleteTask = await cut.OpenDeleteDialogAsync(Webhook.Name.Default.Value);
await cut.ClickAsync("Delete webhook");
await deleteTask;
```

A page test run that takes several times its usual duration is such a deadlock, not slowness. Run slices under
`timeout 300` with `longRunningTestSeconds` set, so the stuck test is named.

## Command failure paths

```csharp
// Arrange
var bus = StubBus();

await GivenForwarded(
    WebhookAdded()
);

var cut = await RenderPageAsync(rows: 1);

await cut.RowActionAsync(Webhook.Name.Default.Value, "Edit webhook");
await cut.TypeAsync("webhook-name", Webhook.Name.Updated.Value);
await cut.SubmitAsync();
await cut.WaitForStateAsync(() => !cut.Settled(), Timeouts.Render);

var optimistic = cut.Names();
var cmd = Sent<UpdateWebhook>(bus);

// Act
await Notifications.PublishAsync(Faulted(cmd));
await cut.WaitForStateAsync(cut.Settled, Timeouts.Render);

// Assert
optimistic.Should().Equal(Webhook.Name.Updated.Value);
cut.Names().Should().Equal(Webhook.Name.Default.Value);

Toasts.Toasts.Should().ContainSingle();
Toasts.Toasts[0].Title.Should().Be("Saving failed");
```

- **`StubBus()` runs before the render.** The page resolves its bus at render time, and the substitute holds the
  command back, so the fault the test publishes is the only answer the page gets.
- **`Sent<TCommand>(bus)` reads the one command the page sent.** `Faulted(cmd)` builds the fault, which goes through
  the slice's notification hub, exactly as the host would forward it.
- **Assert both states:** the optimistic state captured before the fault, and the reverted state after it. The
  toast is its own assert group.

## Leaf components and URL clients

- **Leaf components use `ComponentTestBase` or a slice component base.** They render with read models built in the
  test, not with projections. A fixed `Now` per test lets an assertion name the exact date a row renders. Readable
  ids such as `IdFor(seed)` make failure messages easy to trace.
- **Code that only talks to a URL** (fetching a model list, calling a web API) runs against `LoopbackBase`. It is a
  real server on `127.0.0.1:0`, set up in `ConfigureLoopback(app)`, with a settable `Reply` and the recorded
  request headers.
- **Test files are `.cs`.** A `.razor` test file only for a test that writes markup inline, such as
  `Render(@<EditForm Model="form"><EventSelector @bind-Value="form.Events" /></EditForm>)`.

## More

- [references/page-test-walkthrough.md](references/page-test-walkthrough.md): annotated page tests (a rendered row,
  an add with optimistic ordering, the delete dialog, a live update from elsewhere, a search over batched seeds) and
  a leaf component base. Read it when writing the first page test of a slice.
- [references/gotchas.md](references/gotchas.md): symptom → cause → fix for every bUnit trap found so far. Read it
  when a page test fails in a way the error message does not explain.
