# Before and after (from a production codebase)

Every pair below comes from a real test-suite rework. **Before** is the test as it stood. **Maintainer** marks a version
the maintainer formatted by hand, which is the ground truth for the style. **After** is the swept test. Domain names
(Webhook, ShareLink, SecretStore) belong to that project; the base helpers (`Given`, `SendMessageAndWaitAsync`,
`FetchStreamAsync`) belong to its event-sourced test base.

## Contents

1. Names, `Given(`, one assertion per statement (RemoveWebhook)
2. Test data, variant commands, assertion helpers (UpdateWebhook)
3. Validators: theories, roads, `result.Assert` helpers
4. Notification probes: named arguments, the shared predicate
5. A super-test split into one test per rule
6. HTTP endpoints: the act stays inline
7. Page tests: helpers become extension members
8. Base hygiene: no regions, member order

---

## 1. Names, `Given(`, one assertion per statement

**Before**

```csharp
[Fact]
public async Task Given_A_Webhook_When_RemoveWebhook_Handled_Appends_WebhookRemoved_And_Drops_The_Webhook()
{
    // Arrange
    await GivenWebhookAsync();

    // Act
    await Host.InvokeMessageAndWaitAsync(RemoveWebhook());
    var stream = await FetchStreamAsync();
    var webhook = await FetchLatestAsync<Webhook>();

    // Assert
    stream.Should().HaveCount(2).And.ContainSingle(evt => evt.Data is WebhookRemoved);
    webhook.Should().BeNull();
}

[Fact]
public async Task Given_No_Webhook_When_RemoveWebhook_Is_Sent_It_Is_Dead_Lettered_And_Published_As_A_Fault()
{
    // Arrange
    var cmd = RemoveWebhook();

    // Act
    var tracked = await SendAsync(cmd);
    var stream = await FetchStreamAsync();

    // Assert
    var fault = tracked.AutoFaultsPublished.SingleMessage<Fault<RemoveWebhook>>();
    fault.Message.Should().Be(cmd);
    fault.Exception.Type.Should().Be(typeof(RequiredDataMissingException).FullName);
    tracked.MovedToErrorQueue.MessagesOf<RemoveWebhook>().Should().ContainSingle();
    stream.Should().BeEmpty();
}
```

**Maintainer (hand-formatted, names kept for the moment)**

```csharp
[Fact]
public async Task Given_A_Webhook_When_RemoveWebhook_Handled_Appends_WebhookRemoved_And_Drops_The_Webhook()
{
    // Arrange
    await Given(
        WebhookAdded()
    );

    // Act
    await Host.InvokeMessageAndWaitAsync(RemoveWebhook());
    var webhook = await FetchLatestAsync<Webhook>();
    var stream = await FetchStreamAsync();

    // Assert
    webhook.Should().BeNull();
    stream.Should().HaveCount(2);
    stream.Should().ContainSingle(evt => evt.Data is WebhookRemoved);
}

[Fact]
public async Task Given_No_Webhook_When_RemoveWebhook_Is_Sent_It_Is_Dead_Lettered_And_Published_As_A_Fault()
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

**After**

```csharp
[Fact]
public async Task Given_A_Webhook_Appends_WebhookRemoved_And_Drops_The_Webhook()
{
    // Arrange
    await Given(
        WebhookAdded()
    );

    // Act
    await InvokeMessageAndWaitAsync(RemoveWebhook());
    var webhook = await FetchLatestAsync<Webhook>();
    var stream = await FetchStreamAsync();

    // Assert
    webhook.Should().BeNull();
    stream.Should().HaveCount(2);
    stream.Should().ContainSingle(evt => evt.Data is WebhookRemoved);
}
```

What changed:

- `GivenWebhookAsync()` also stored a signing secret that this test never asserts. `Given(` lists only the event, one
  per line.
- The reads follow the order the asserts use them (`webhook`, then `stream`), and the asserts go shortest first.
- `.And` became two statements.
- The command is inline and `var cmd` is gone. `fault.Message.Should().Be(cmd)` was dropped, because the name does
  not promise it.
- Wolverine's `SingleMessage<T>()` is the assertion itself. The exception check is `.Assert.ThrownException<T>()`,
  and the multi-line chain goes last.
- The helper is named after the library (`SendMessageAndWaitAsync`, not `SendAsync`).
- `When_..._Handled` repeated the class name and was dropped.

## 2. Test data, variant commands, assertion helpers

**Before**

```csharp
[Fact]
public async Task Given_A_Webhook_When_UpdateWebhook_Sets_Only_The_Name_Nothing_Else_Changes()
{
    // Arrange
    var (_, secret) = await GivenWebhookAsync();
    var cmd = UpdateWebhook(name: NewName().Value);

    // Act
    await Host.InvokeMessageAndWaitAsync(cmd);
    var stream = await FetchStreamAsync();
    var storedSecret = await Secrets.GetSecretAsync(AggregateId);

    // Assert
    stream[1].Data.Should().Be(WebhookUpdated(name: new WebhookName(cmd.Name.Value)));
    storedSecret.Should().Be(secret);
}
```

**Maintainer (intermediate sample)**

```csharp
// Arrange
await Given(
    WebhookAdded()
);

await Secrets.SetSecretAsync(AggregateId, Webhook.Secret);

var cmd = new UpdateWebhook
{
    Id = AggregateId,
    Name = Webhook.Name.Updated.Value
};

// Act
await Host.InvokeMessageAndWaitAsync(cmd);
var stream = await FetchStreamAsync();

// Assert
await Secrets.AssertSecretAsync(AggregateId, Webhook.Secret);

var evt = stream.AssertEventAt<WebhookUpdated>(position: 1);
evt.Data.Name.Should().Be(Webhook.Name.Updated);
evt.Data.Events.Should().Be(Optional.Unchanged);
evt.Data.Url.Should().Be(Optional.Unchanged);
```

**After**

```csharp
[Fact]
public async Task Given_A_Webhook_Setting_Only_The_Name_Changes_Nothing_Else()
{
    // Arrange
    await Given(
        WebhookAdded()
    );

    await Secrets.SetSecretAsync(AggregateId, Webhook.Secret);

    var cmd = new UpdateWebhook
    {
        Id = AggregateId,
        Name = Webhook.Name.Updated.Value
    };

    // Act
    await InvokeMessageAndWaitAsync(cmd);
    var stream = await FetchStreamAsync();

    // Assert
    await Secrets.Assert.SecretAsync(AggregateId, Webhook.Secret);

    var evt = stream.Assert.EventAt<WebhookUpdated>(1);
    evt.Data.Url.Assert.Unchanged();
    evt.Data.Events.Assert.Unchanged();
    evt.Data.Name.Assert.SetTo(Webhook.Name.Updated);
}
```

What changed:

- Arrange runs in the order given, side state, command. The partial command is a multi-line initializer, not an
  optional-parameter builder.
- The secret store read moved out of Act into an assertion helper.
- Assert has two groups (the secret store, the event), separated by a blank line.
- The maintainer's `AssertEventAt` / `AssertSecretAsync` became the `x.Assert.Y()` form.
- Two bugs in the intermediate version were fixed. `Optional<T>.Should().Be(Optional.Unchanged)` never passes, and
  `BeOfType<IEvent<T>>` never matched Marten's `Event<T>`. Both now go through helpers.

## 3. Validators: theories, roads, `result.Assert` helpers

**Before**

```csharp
private static CreateShareLink Command(string note = "Acme pilot", DateTimeOffset? expiresAt = null) => ...

public static TheoryData<string, string> RejectedNotes =>
    new()
    {
        { "", "A note is required." },
        { "  ", "A note is required." },
        { new string('a', 256), "The note must be 255 characters or fewer." }
    };

[Theory]
[MemberData(nameof(RejectedNotes))]
public void A_Note_Without_Text_Or_Past_255_Characters_Is_Rejected(string note, string message)
{
    // Act
    var result = _validator.Validate(Command(note));

    // Assert
    var error = result.Errors.Should().ContainSingle().Subject;
    error.PropertyName.Should().Be(nameof(CreateShareLink.Note));
    error.ErrorMessage.Should().Be(message);
}

[Fact]
public void An_Expiry_That_Already_Passed_Is_Rejected()
{
    // Act
    var result = _validator.Validate(Command(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

    // Assert
    result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(CreateShareLink.ExpiresAt));
}
```

**After**

```csharp
public class CreateShareLinkValidatorTests : UnitTestBase
{
    private readonly CreateShareLinkValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Given_A_Note_Without_Text_The_Note_Is_Rejected(string note)
    {
        // Arrange
        var cmd = new CreateShareLink
        {
            Id = Guid.CreateVersion7(),
            Note = note,
            ExpiresAt = Data.ShareLink.ExpiresAt
        };

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.Assert.SingleError(nameof(CreateShareLink.Note), "A note is required.");
    }

    [Fact]
    public void Given_A_Note_Past_255_Characters_The_Note_Is_Rejected()
    {
        // Arrange
        var cmd = new CreateShareLink
        {
            Id = Guid.CreateVersion7(),
            Note = Data.ShareLink.Note.Invalid,
            ExpiresAt = Data.ShareLink.ExpiresAt
        };

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.Assert.SingleError(nameof(CreateShareLink.Note), "The note must be 255 characters or fewer.");
    }
}
```

What changed:

- `InlineData` holds the constants. The 256-character boundary is not a constant, so it is its own Fact, reading
  the invalid value from the test data.
- The command is built inline. A host-free test needs no builder machinery.
- The `.Subject` variable and the `.Which` chain became `result.Assert.SingleError(property[, message])`.
- Host-free tests derive from `UnitTestBase` for `Data` and `Ct`.

An act that is itself the assertion keeps `// Act & Assert`, and this is the one `.Which` the maintainer kept:

```csharp
[Fact]
public void Given_No_Country_The_Country_Is_Reported_Instead_Of_Throwing()
{
    // Arrange
    var validate = () => Address.Validator.Validate(Data.Customer.Office.Default with { Country = null! });

    // Act & Assert
    validate.Should().NotThrow().Which.Assert.ErrorOn(nameof(Address.Country));
}
```

## 4. Notification probes: named arguments, the shared predicate

**Before**

```csharp
[Fact]
public async Task Appended_WebhookAdded_Is_Forwarded_Through_Wolverine_To_The_Registered_Singleton_Subscriber()
{
    // Arrange
    var notifications = Host.Services.GetRequiredService<WebhookNotifications>();
    var added = WebhookAdded();
    using var probe = new NotificationProbe<WebhookAdded>(
        subscribe: handler => notifications.OnWebhookAdded += handler,
        unsubscribe: handler => notifications.OnWebhookAdded -= handler,
        predicate: evt => evt.StreamId == AggregateId
    );

    // Act
    await Given(added);
    var forwarded = await probe.WaitAsync(10.Seconds());

    // Assert
    forwarded.StreamId.Should().Be(AggregateId);
    forwarded.Data.Should().BeEquivalentTo(added);
}
```

**After**

```csharp
[Fact]
public async Task Given_An_Appended_WebhookAdded_The_Subscriber_Receives_It()
{
    // Arrange
    var added = WebhookAdded();

    using var probe = new NotificationProbe<WebhookAdded>(
        notifications: Notifications,
        eventName: nameof(Notifications.OnWebhookAdded),
        predicate: OwnStream);

    // Act
    await Given(
        added
    );

    var forwarded = await probe.WaitAsync(Timeouts.Probe);

    // Assert
    forwarded.Data.Should().BeEquivalentTo(added);
}
```

What changed:

- The probe takes the notifications, the event's `nameof` and a predicate as named arguments, one per line, closing
  on the last one. C# cannot pass an event as a value, hence the `nameof`.
- The stream predicate is the base's `OwnStream`.
- The `StreamId` assertion was dropped: the probe only completes on the test's own stream, so it was a precondition.
- The timeout comes from the shared `Timeouts`, and the multi-line `Given(` gets blank lines around it.

## 5. A super-test split into one test per rule

**Before (one test, about 20 assertions)**

```csharp
[Fact]
public async Task Given_No_Conversation_Id_When_GetResponseAsync_A_Conversation_Is_Started_And_Stored()
{
    // Arrange
    var stub = new ScriptedChatClient().Reply([.. AnswerUpdates]);
    using var client = Tracked(stub);
    using var probe = ConversationsProbe();
    var prompt = Prompt;
    var options = Options();

    // Act
    var response = await client.GetResponseAsync(prompt, options, Ct);
    var started = probe.Snapshots[0];
    var stored = await LoadAsync<StoredConversation>(started!.ConversationId);

    // Assert
    stub.Requests.Should().ContainSingle().Which.Should().Equal(prompt);
    stub.Options.Should().ContainSingle().Which.Should().BeSameAs(options);
    response.Text.Should().Be(Answer);
    response.FinishReason.Should().Be(ChatFinishReason.Stop);
    probe.Snapshots.Should().HaveCount(2);
    started.Context.Should().Equal(prompt);
    started.Resumed.Should().BeFalse();
    started.Should().BeOfType<RecordedSession>();
    probe.Snapshots[1].Should().BeNull();
    Listed.Should().BeNull();
    stored.Should().NotBeNull();
    stored.ExecutionContext.Should().Be(Context);
    stored.CreatedAt.Should().Be(started.StartedAt);

    var session = stored.Sessions.Should().ContainSingle().Subject;
    session.ModelRef.Should().Be(Model);
    session.UsageDetails.InputTokenCount.Should().Be(InputTokens);
    session.UsageDetails.OutputTokenCount.Should().Be(OutputTokens);
    session.FinishReason.Should().Be(ChatFinishReason.Stop);
    stored.LastActivity.Should().Be(session.FinishedAt);
}
```

**Maintainer (three tests)**

```csharp
[Fact]
public async Task Given_No_Conversation_Id_The_Inner_Client_Answers_The_Prompt()
{
    // Arrange
    var options = Options();
    var stub = new ScriptedChatClient().Reply([.. AnswerUpdates]);
    using var client = Tracked(stub);

    // Act
    var response = await client.GetResponseAsync(Prompt, options, Ct);

    // Assert
    response.Text.Should().Be(Answer);
    response.FinishReason.Should().Be(ChatFinishReason.Stop);

    stub.Requests.Should().ContainSingle();
    stub.Requests[0].Should().Equal(Prompt);
    stub.Options.Should().ContainSingle();
    stub.Options[0].Should().BeSameAs(options);
}

[Fact]
public async Task Given_No_Conversation_Id_A_Session_Is_Listed_Until_The_Answer_Arrives()
{
    // Arrange
    var stub = new ScriptedChatClient().Reply([.. AnswerUpdates]);
    using var client = Tracked(stub);
    using var probe = ConversationsProbe();

    // Act
    await client.GetResponseAsync(Prompt, Options(), Ct);

    // Assert
    Listed.Should().BeNull();
    probe.Snapshots.Should().HaveCount(2);
    probe.Snapshots[1].Should().BeNull();
    probe.Snapshots[0].Should().BeOfType<RecordedSession>();
    probe.Snapshots[0]!.Resumed.Should().BeFalse();
    probe.Snapshots[0]!.Context.Should().Equal(Prompt);
}

[Fact]
public async Task Given_No_Conversation_Id_The_Conversation_Is_Stored_With_Its_Session()
{
    // Arrange
    var stub = new ScriptedChatClient().Reply([.. AnswerUpdates]);
    using var client = Tracked(stub);
    using var probe = ConversationsProbe();

    // Act
    await client.GetResponseAsync(Prompt, Options(), Ct);
    var started = probe.Snapshots[0]!;
    var stored = await LoadAsync<StoredConversation>(started.ConversationId);

    // Assert
    stored.Should().NotBeNull();
    stored.ExecutionContext.Should().Be(Context);
    stored.CreatedAt.Should().Be(started.StartedAt);

    stored.Sessions.Should().ContainSingle();
    stored.Sessions[0].ModelRef.Should().Be(Model);
    stored.Sessions[0].FinishReason.Should().Be(ChatFinishReason.Stop);
    stored.Sessions[0].UsageDetails.InputTokenCount.Should().Be(InputTokens);
    stored.Sessions[0].UsageDetails.OutputTokenCount.Should().Be(OutputTokens);
    stored.LastActivity.Should().Be(stored.Sessions[0].FinishedAt);
}
```

What changed:

- Three rules became three tests: the inner client answers, the session is listed until the answer arrives, the
  conversation is stored. Each test arranges only what its rule needs.
- `.Which` and `.Subject` became roads (`ContainSingle();` then `[0]`).
- `var prompt = Prompt;` is gone, because the test data returns the same instance on every read. `Ct` comes from
  the base instead of a private copy.

## 6. HTTP endpoints: the act stays inline

**Before**

```csharp
#region Import key authentication

[Fact]
public async Task When_The_Import_Key_Header_Is_Missing_Returns_Unauthorized()
{
    // Act & Assert
    await ImportAsync(ImportCustomer(), key: null, StatusCodes.Status401Unauthorized);
}
```

**After**

```csharp
[Fact]
public async Task Given_No_Import_Key_Header_Returns_Unauthorized()
{
    // Act & Assert
    await Host.Scenario(scenario =>
    {
        scenario.Post.Json(ImportCustomer()).ToUrl(Url);
        scenario.StatusCodeShouldBe(StatusCodes.Status401Unauthorized);
    });
}

[Fact]
public async Task Given_A_Valid_Key_The_Submission_Is_Stored_As_An_Imported_Record()
{
    // Arrange
    var apiKey = await IssueKeyAsync();

    // Act
    await Host.Scenario(scenario =>
    {
        scenario.Post.Json(ImportCustomer()).ToUrl(Url);
        scenario.WithImportKey(apiKey);
        scenario.StatusCodeShouldBe(StatusCodes.Status202Accepted);
    });

    var stored = await FindStoredAsync();

    // Assert
    stored.Should().ContainSingle();
    stored[0].Id.Should().NotBe(Guid.Empty);
    stored[0].Country.Should().Be(Import.Country.Default);

    stored[0].Source.Url.Should().Be(Import.Source.Default.Url);
    stored[0].Source.Name.Should().Be(Import.Source.Default.Name);
    stored[0].Source.Reliability.Should().Be(Import.Source.Default.Reliability);
    stored[0].Source.FetchedAt.Should().BeCloseTo(Import.Source.Default.FetchedAt, 1.Seconds());
}
```

What changed:

- `ImportAsync(request, key, status)` hid the act; the scenario is now inline in every test. `IssueKeyAsync` (an
  arrange helper) and `FindStoredAsync` (a read helper) stay.
- Alba's status check stays inside the scenario. A status-only test is therefore `// Act & Assert`, and a test that
  also reads the result checks the status in Act.
- `#region` and `When_` are gone. Assert groups by subject: the record, then its source. `ContainSingle();` then
  `stored[0]` is the road to the record.

## 7. Page tests: helpers become extension members

**Before (private static helpers taking `cut`)**

```csharp
private static IReadOnlyList<string> Notes(IRenderedComponent<ContainerFragment> cut) =>
    [.. cut.FindAll("tbody tr td:first-child:not([colspan])").Select(cell => cell.TextContent.Trim())];

private static bool Settled(IRenderedComponent<ContainerFragment> cut) =>
    cut.FindComponents<BbSpinner>().Count == 0;

// in the test
await CreateAsync(cut, "Acme pilot");
var waiting = Notes(cut);
var cmd = Sent<CreateShareLink>(bus);

// Act
await Notifications.PublishAsync(Faulted(cmd));
await cut.WaitForStateAsync(() => Settled(cut), 15.Seconds());

// Assert
waiting.Should().Equal("Acme pilot", "Someone else's link");
Notes(cut).Should().Equal("Someone else's link");
var toast = Toasts.Toasts.Should().ContainSingle().Subject;
toast.Title.Should().Be("Creation failed");
```

**After**

```csharp
// ShareLinks/Support/ShareLinksPageExtensions.cs
public static class ShareLinksPageExtensions
{
    extension(IRenderedComponent<ContainerFragment> cut)
    {
        public IReadOnlyList<string> Notes() =>
        [
            .. cut.FindAll("tbody tr td:first-child:not([colspan])")
                .Select(cell => cell.TextContent.Trim())
        ];

        public async Task CreateAsync(string note)
        {
            await cut.ClickAsync("Create link");
            await cut.TypeAsync("link-note", note);
            await cut.SubmitAsync();
        }
    }
}

// the test
[Fact]
public async Task Given_The_Create_Command_Faults_The_Waiting_Row_Is_Dropped_And_The_Page_Toasts()
{
    // Arrange
    var bus = StubBus();
    await GivenLinksAsync(("Someone else's link", 30.Days()));
    var cut = await RenderPageAsync(rows: 1);

    await cut.CreateAsync(ShareLink.Note);
    var waiting = cut.Notes();
    var cmd = Sent<CreateShareLink>(bus);

    // Act
    await Notifications.PublishAsync(Faulted(cmd));
    await cut.WaitForStateAsync(cut.Settled, Timeouts.Render);

    // Assert
    cut.Notes().Should().Equal("Someone else's link");
    waiting.Should().Equal(ShareLink.Note.Default, "Someone else's link");

    Toasts.Toasts.Should().ContainSingle();
    Toasts.Toasts[0].Title.Should().Be("Creation failed");
    Toasts.Toasts[0].Variant.Should().Be(ToastVariant.Destructive);
    Toasts.Toasts[0].Description.Should().Contain(ShareLink.Note.Default);
}
```

What changed:

- Helpers that take the rendered component first are extension members. The generic ones (`Settled`, `ClickAsync`,
  `TypeAsync`, `SubmitAsync`) live in a shared class, the page ones in `<Slice>/Support/<Page>Extensions`.
- `StubBus`, `Sent` and `RenderPage` moved to the base. `RenderPageAsync(rows)` and the seed helper stay class
  members.
- Literals came out of the test data, the timeout from `Timeouts`, and the toast `.Subject` became a road.
- The page test became a `.cs` file instead of `.razor` + `.razor.cs`.

## 8. Base hygiene: no regions, member order

**Before (excerpt)**

```csharp
public class CustomerTestBase(FixtureBase fixture) : TestBase(fixture)
{
    #region Blazor component tests
    protected override void InitializeComponentServices(AsyncServiceScope scope) { ... }
    #endregion

    #region Categories
    protected CategoryCreated CategoryCreated() => ...
    protected CreateCategory CreateCategory(Guid? id = null) => ...
    protected string CategoryName { get; } = fixture.Faker.Commerce.Department();
    protected string UpdatedCategoryName { get; } = fixture.Faker.Commerce.Department() + fixture.Faker.UniqueIndex;
    #endregion

    #region Import submissions
    /// <summary>
    /// ...
    /// </summary>
    protected ImportCustomer ImportCustomer(...)
    #endregion
}
```

**Maintainer**

```csharp
public abstract class CustomerTestBase(FixtureBase fixture) : TestBase(fixture)
{
    protected CategoryTestData Category => Data.Category;

    protected ImportTestData Import => Data.Import;

    protected override void InitializeComponentServices(AsyncServiceScope scope)
    {
        Services.AddSingleton(Host.Services.GetRequiredService<CustomerNotifications>());
        Services.AddSingleton(Host.Services.GetRequiredService<ImportKeyNotifications>());
    }

    protected CategoryCreated CategoryCreated() =>
        new(
            Name: Category.Name,
            Description: Category.Description
        );

    protected CategoryUpdated CategoryUpdated() =>
        new(
            Name: Category.Name.Updated,
            Description: Category.Description.Updated
        );

    protected CategoryDeleted CategoryDeleted() => new();

    protected CreateCategory CreateCategory() =>
        new()
        {
            Id = AggregateId,
            Name = Category.Name,
            Description = Category.Description
        };

    protected ImportCustomer ImportCustomer() =>
        new(
            Country: Import.Country,
            Source: Import.Source,
            Properties: Import.Properties
        );
}
```

What changed:

- The members come in a fixed order: data shortcuts, the service override, then per area the events, the commands
  and the other builders.
- `#region` and the XML summaries are gone.
- Values moved into the per-test catalog. The optional `Guid? id` parameter is gone, because a test that needs
  another id writes `CreateCategory() with { Id = ... }`.
