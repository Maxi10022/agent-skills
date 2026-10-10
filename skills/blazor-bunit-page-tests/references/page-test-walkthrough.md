# Page test walkthrough (from a production codebase)

Annotated tests of one real page: a list of outgoing webhooks with an add/edit sheet, a row menu with a delete dialog,
a search box, optimistic rows and toasts. The page sends Wolverine commands; Marten projections and a notification
hub keep it current. The helpers come from the shared `RenderedComponentTestExtensions` and the slice's
`WebhooksPageExtensions` (see the templates).

## Contents

1. The class: fixture and wipe
2. Private render and seed helpers
3. A rendered row
4. An add with optimistic ordering
5. The delete dialog
6. A change made elsewhere while the pane is open
7. A search over batched seeds
8. A leaf component base
9. A `.razor` test with inline markup

## 1. The class: fixture and wipe

```csharp
public class WebhooksPageTests(WebhooksPageFixture fixture) :
    WebhookTestBase(fixture), IClassFixture<WebhooksPageFixture>, IAsyncLifetime
{
    // The page lists every webhook there is, so each test starts from a table only it has filled
    public async ValueTask InitializeAsync() => await WipeAsync<Webhook>();
```

- **A class fixture of its own.** The class runs long and serially, so taking it out of the slice collection
  shortens the run.
- **A wipe before every test.** The page lists everything, so a test can only count rows on a table it filled
  itself. `WipeAsync` waits for the daemon first.

## 2. Private render and seed helpers

```csharp
private async Task<IRenderedComponent<ContainerFragment>> RenderPageAsync(int rows)
{
    var cut = RenderPage<WebhooksPage>();
    await cut.WaitForStateAsync(() => cut.Names().Count == rows, Timeouts.Render);
    return cut;
}

private async Task<IRenderedComponent<ContainerFragment>> RenderEmptyPageAsync()
{
    var cut = RenderPage<WebhooksPage>();
    await cut.WaitForStateAsync(() => cut.Markup.Contains("No webhooks yet"), Timeouts.Render);
    return cut;
}

private Task GivenWebhooksAsync(params string[] names) =>
    GivenWebhooksAsync([.. names.Select(name => (name, "order.created"))]);

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

- **Every render waits for the loaded state** the test expects, read from the DOM.
- **Several seeds share one forwarded session.** That kept the paging tests at 1.5 s instead of 12 s.

## 3. A rendered row

```csharp
[Fact]
public async Task Given_A_Webhook_Its_Row_Shows_Name_Url_Two_Events_And_A_Count_For_The_Rest()
{
    // Arrange
    await GivenForwarded(
        WebhookAdded() with { Events = ["order.created", "order.paid", "order.shipped", "order.cancelled"] }
    );

    // Act
    var cut = await RenderPageAsync(rows: 1);
    var cells = cut.Cells(Webhook.Name.Default.Value);

    // Assert
    cut.Markup.Should().Contain("1 webhook");
    cut.Markup.Should().NotContain("matching");
    cells[0].Should().Be(Webhook.Name.Default.Value);
    cells[1].Should().Be(Webhook.Url.Default.Value);
    cut.EventChips(Webhook.Name.Default.Value).Should().Equal("order.created", "order.paid", "+2");
}
```

- The variant (four events) is a `with` on the one builder.
- The render is the act, because the test is about what the page shows.

## 4. An add with optimistic ordering

```csharp
[Fact]
public async Task Given_The_Add_Pane_Is_Submitted_The_New_Row_Waits_Last_Until_It_Settles()
{
    // Arrange
    await GivenWebhooksAsync("Someone else's webhook");
    var cut = await RenderPageAsync(rows: 1);

    await cut.ClickAsync("Add webhook");
    await cut.TypeAsync("webhook-name", Webhook.Name.Default.Value);
    await cut.TypeSecretAsync(Webhook.Secret);
    await cut.AddEventAsync("order.created");

    // Act
    await cut.SubmitAsync();
    var optimistic = cut.Names();
    var pending = !cut.Settled();

    await cut.WaitForStateAsync(() => cut.Settled() && cut.Names().Count == 2, Timeouts.Render);
    await WaitForNonStaleProjectionDataAsync();

    // Assert
    pending.Should().BeTrue();
    cut.PaneOpen().Should().BeFalse();
    optimistic.Should().Equal("Someone else's webhook", Webhook.Name.Default.Value);
    cut.Names().Should().Equal("Someone else's webhook", Webhook.Name.Default.Value);
}
```

- **The optimistic state is captured right after the submit,** because the page sets it synchronously before it
  sends.
- **The wait combines "settled" with the expected count.** A settled state alone could be the state before the
  command's answer.
- **The test ends with the non-stale wait,** so its projection lands before the next test's wipe.

## 5. The delete dialog

```csharp
[Fact]
public async Task Given_A_Webhook_The_Delete_Dialog_Names_It_And_Counts_Its_Events()
{
    // Arrange
    await GivenForwarded(
        WebhookAdded()
    );

    var cut = await RenderPageAsync(rows: 1);

    // Act
    var deleteTask = await cut.OpenDeleteDialogAsync(Webhook.Name.Default.Value);
    var dialog = cut.Markup;

    await cut.ClickAsync("Cancel");
    await deleteTask;

    // Assert
    dialog.Should().Contain("its 2 events");
    dialog.Should().Contain($"This removes “{Webhook.Name.Default.Value}” and its stored signing secret.");
}

[Fact]
public async Task Given_The_Delete_Dialog_Is_Confirmed_The_Webhook_And_Its_Secret_Are_Gone()
{
    // Arrange
    await GivenForwarded(
        WebhookAdded()
    );

    await Secrets.SetSecretAsync(AggregateId, Webhook.Secret);
    await WaitForNonStaleProjectionDataAsync();
    var cut = await RenderPageAsync(rows: 1);

    // Act
    var deleteTask = await cut.OpenDeleteDialogAsync(Webhook.Name.Default.Value);
    await cut.ClickAsync("Delete webhook");
    await deleteTask;

    await cut.WaitForStateAsync(() => cut.Markup.Contains("No webhooks yet"), Timeouts.Render);
    await WaitForNonStaleProjectionDataAsync();
    var webhook = await LoadAsync<Webhook>(AggregateId);

    // Assert
    webhook.Should().BeNull();
    await Secrets.Assert.NoSecretAsync(AggregateId);
}
```

- **The dialog's text and the confirmed deletion are two rules, so two tests.**
- **The held task (`Task<Task>`) is awaited only after the dialog was answered,** even on Cancel.
- **The confirmed test asserts the stored state,** not just the page. The page could show an optimistic removal
  that never reached the store.

## 6. A change made elsewhere while the pane is open

```csharp
[Fact]
public async Task Given_An_Open_Edit_Pane_A_Url_Change_Elsewhere_Refreshes_The_Url_And_Keeps_The_Typed_Name()
{
    // Arrange
    await GivenForwarded(
        WebhookAdded()
    );

    var cut = await RenderPageAsync(rows: 1);

    await cut.RowActionAsync(Webhook.Name.Default.Value, "Edit webhook");
    await cut.TypeAsync("webhook-name", Webhook.Name.Updated.Value);

    var updated = WebhookUpdated() with
    {
        Name = Optional.Unchanged,
        Events = Optional.Unchanged
    };

    // Act
    await GivenForwarded(
        updated
    );

    await cut.WaitForStateAsync(() => cut.Input("webhook-url").Value == Webhook.Url.Updated.Value, Timeouts.Render);
    await WaitForNonStaleProjectionDataAsync();

    // Assert
    cut.PaneOpen().Should().BeTrue();
    cut.Names().Should().Equal(Webhook.Name.Default.Value);
    cut.Cells(Webhook.Name.Default.Value)[1].Should().Be(Webhook.Url.Updated.Value);
    cut.Input("webhook-name").Value.Should().Be(Webhook.Name.Updated.Value);
    cut.Input("webhook-url").Value.Should().Be(Webhook.Url.Updated.Value);
}
```

- **"Another user" is a forwarded append in the Act.** The wait reads the input's value, which is the state the
  assertions depend on.

## 7. A search over batched seeds

```csharp
[Fact]
public async Task Given_A_Search_For_An_Event_Type_Only_Its_Webhook_Stays_Listed()
{
    // Arrange
    await GivenWebhooksAsync(("Alpha", "order.created"), ("Bravo", "refund.issued"), ("Charlie", "order.shipped"));
    var cut = await RenderPageAsync(rows: 3);

    // Act
    await cut.SearchAsync("refund");

    // Assert
    cut.Names().Should().Equal("Bravo");
    cut.Markup.Should().Contain("1 of 3 webhooks matching");
}
```

The search by event type, by name prefix and with no match are three tests: three rules, one each.

## 8. A leaf component base

```csharp
// Share-link components rendered alone, the rows they get are read models built here instead of a host's projection
public abstract class ShareLinkComponentTestBase : ComponentTestBase
{
    protected const string BaseUrl = "https://links.example.com";

    protected ShareLinkTestData ShareLink => Data.ShareLink;

    protected ShareLinkNotifications Notifications { get; } = new();

    // One instant per test, so an assertion can name the very date a row renders
    protected DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    protected override void InitializeComponentServices()
    {
        base.InitializeComponentServices();

        Services.AddSingleton(Notifications);
        Services.AddSingleton(Options.Create(new ShareLinkOptions { BaseUrl = BaseUrl }));
    }

    // Rows of one table need ids of their own, a seed keeps each id readable in a failure message
    protected static Guid IdFor(int seed) => new($"00000000-0000-0000-0000-{seed:D12}");

    protected ShareLink Link(string note, TimeSpan expiresIn, int seed) =>
        new(
            Id: IdFor(seed),
            Note: note,
            Token: ShareToken.Generate(IdFor(seed)),
            CreatedAt: Now - 30.Days(),
            ExpiresAt: Now + expiresIn
        );
}
```

`Now` reads the wall clock once per test. A `TimeProvider` seam in the component is the cleaner fix, and the source
project deferred it, keeping the read as a convention exemption.

## 9. A `.razor` test with inline markup

Only a test that writes Razor markup inline is a `.razor` file. This one also runs against a loopback server that
plays the event-type list endpoint:

```razor
@inherits EventTypesLoopbackBase

@code {

    [Fact]
    public async Task Given_Fetched_Event_Types_The_Mismatches_Are_Tagged()
    {
        // Arrange
        Reply = EventTypeListResult(EventType("order.created", 100), EventType("order.cancelled", 200));

        var form = new EventsForm { Events = ["order.shipped"] };
        var cut = Render(
            @<EditForm Model="form">
                <EventSelector @bind-Value="form.Events" Url="@EventTypesUrl" Secret="@Secret"/>
            </EditForm>);

        // Act
        await cut.ClickAsync("Fetch from URL");

        // Assert
        cut.Markup.Should().Contain("Not listed");
        cut.Markup.Should().Contain("Not reported");
        cut.Markup.Should().Contain("1 listed · 2 reported");
    }
}
```

The loopback base maps `GET /v1/event-types` to a settable `Reply` and records the `Authorization` header, so the
request side is its own test.
