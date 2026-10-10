# Example (from a production codebase)

Everything below comes from a production test suite, a .NET 10 app on Marten, Wolverine and Blazor. It shows the
catalog in context; domain names (Webhook, Customer, Chat) belong to that project. The slice bases derive the
project's `TestBase`, which exposes `Data` and `AggregateId`.

## Contents

1. Before the catalog
2. A slice data class with value objects and disjoint halves
3. Curated lists for a richer domain
4. Fixed reference objects and builder methods for mutable ones
5. Shortcut and builders on the slice base
6. Tests reading the catalog

## 1. Before the catalog

This is the pre-sweep base, excerpted. Each problem it shows has a rule in SKILL.md:

```csharp
protected string CategoryName { get; } = fixture.Faker.Commerce.Department();
protected string UpdatedCategoryName { get; } = fixture.Faker.Commerce.Department() + fixture.Faker.UniqueIndex;

protected CreateCategory CreateCategory(Guid? id = null) => ...
protected DateTimeOffset FutureExpiry() => DateTimeOffset.UtcNow.Add(Faker.Random.Int(1, 30 * 24).Hours());
```

- The faker is shared by the fixture and unseeded, so values change from run to run.
- `UniqueIndex` gives the same number to every test in the collection.
- The builder has an optional parameter.
- `FutureExpiry()` draws a new value and reads the wall clock on every call.

The tests called builders such as `UpdateWebhook(name: NewName().Value, url: NewUrl().Value, events: NewEvents())`,
and the "givens" mixed events with side state: `var (added, secret) = await GivenWebhookAsync();`.

## 2. A slice data class with value objects and disjoint halves

```csharp
public class WebhookTestData(Faker faker)
{
    private static readonly string[] Systems =
        ["Billing", "Shop", "CRM sync", "Warehouse", "Newsletter", "Accounting", "Support desk", "Analytics"];

    // Two disjoint halves, so an update never repeats an event the webhook was added with
    private static readonly string[] AddedEvents = ["order.created", "order.paid", "order.shipped", "order.cancelled"];

    private static readonly string[] UpdatedEvents =
        ["refund.issued", "customer.created", "customer.updated", "invoice.sent"];

    // A suffix keeps the name unique among the webhooks other tests left in the database
    public TestValue<WebhookName> Name { get; } =
        new TestValue<WebhookName>(new WebhookName($"{faker.PickRandom(Systems)} {faker.Random.AlphaNumeric(6)}"))
            .SetUpdated(new WebhookName($"{faker.PickRandom(Systems)} {faker.Random.AlphaNumeric(6)}"))
            .SetInvalid(new WebhookName(""));

    public TestValue<WebhookUrl> Url { get; } =
        new TestValue<WebhookUrl>(new WebhookUrl($"https://{faker.Internet.DomainName()}/hooks"))
            .SetUpdated(new WebhookUrl($"https://{faker.Internet.DomainName()}/hooks"))
            .SetInvalid(new WebhookUrl("hooks.example.com/orders"));

    public TestValue<ImmutableArray<string>> Events { get; } =
        new TestValue<ImmutableArray<string>>([.. faker.PickRandom(AddedEvents, 2)])
            .SetUpdated([.. faker.PickRandom(UpdatedEvents, 2)])
            .SetInvalid([]);

    public TestValue<string> Secret { get; } =
        new TestValue<string>(faker.Random.AlphaNumeric(32))
            .SetUpdated(faker.Random.AlphaNumeric(32))
            .SetInvalid("");
}
```

## 3. Curated lists for a richer domain

Realistic company names and addresses read well in a failure message, and a few non-ASCII names and second address
lines exercise the edge cases on the side:

```csharp
public class CustomerTestData(Faker faker)
{
    // Two disjoint halves, so an update never repeats a value the customer was created with
    private static readonly CustomerName[] CreatedNames =
    [
        new("Northwind Traders Ltd", ["Northwind"]),
        new("Contoso Pharmaceuticals", ["Contoso"]),
        new("Fabrikam Fibers Inc", ["Fabrikam"]),
        new("Müller & Söhne GmbH", ["Müller"])
    ];

    private static readonly CustomerName[] UpdatedNames =
    [
        new("Adventure Works Cycles", ["Adventure Works"]),
        new("Wide World Importers", ["Wide World"]),
        new("Tailspin Toys", ["Tailspin"]),
        new("Œuvre Atelier SARL", ["Œuvre"])
    ];

    // CreatedAddresses / UpdatedAddresses, FirstNames, LastNames and Roles follow the same shape

    public TestValue<CustomerName> Name { get; } =
        new TestValue<CustomerName>(faker.PickRandom(CreatedNames))
            .SetUpdated(faker.PickRandom(UpdatedNames))
            .SetInvalid(new CustomerName("", []));

    public TestValue<Address> Office { get; } =
        new TestValue<Address>(faker.PickRandom(CreatedAddresses))
            .SetUpdated(faker.PickRandom(UpdatedAddresses))
            .SetInvalid(faker.PickRandom(CreatedAddresses) with { Country = new CountryCode("XX") });

    public TestValue<Contact[]> Contacts { get; } =
        new TestValue<Contact[]>([NewContact(faker)])
            .SetUpdated([NewContact(faker), NewContact(faker)]);

    // The stamp a fabricated customer document carries, where the projection would stamp the event's own
    public DateTimeOffset CreatedAt { get; } = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero)
        .AddMinutes(faker.Random.Int(0, 60 * 24 * 30));

    private static Contact NewContact(Faker faker) =>
        new(
            Email: $"office@{faker.Internet.DomainWord()}.example",
            Phone: faker.Phone.PhoneNumber("+1 555 ### ####"),
            Website: $"https://www.{faker.Internet.DomainWord()}.example"
        );
}
```

`NewContact` is a static helper that only runs while the initializers run, so each contact is still drawn exactly
once per test.

## 4. Fixed reference objects and builder methods for mutable ones

The chat slice has two kinds of values that need care. In the data class:

```csharp
public class ChatTestData(Faker faker)
{
    // Unique per test, so a test finds its own sessions and replies among the ones other tests left behind
    public ModelRef Model { get; } =
        new(Guid.CreateVersion7(), $"{faker.Hacker.Abbreviation()}-{faker.Random.AlphaNumeric(6)}".ToLowerInvariant());

    // Built once, a ChatMessage compares by reference, so a second read must return the same instances
    public IReadOnlyList<ChatMessage> Prompt { get; } =
    [
        new(ChatRole.System, faker.Lorem.Sentence()),
        new(ChatRole.User, faker.Lorem.Sentence())
    ];

    public string Instructions => Prompt[0].Text;

    // Four characters per streamed delta, joined they are the answer
    public IReadOnlyList<string> AnswerDeltas { get; } =
        [.. faker.Lorem.Sentence().Chunk(4).Select(characters => new string(characters))];

    public string Answer => string.Concat(AnswerDeltas);

    public int InputTokens { get; } = faker.Random.Int(100, 999);
}
```

Production code mutates some of these objects, so they are builder methods on the slice base instead:

```csharp
// A fresh instance per call, composing a response sums usage into the first instance it sees
protected UsageDetails Usage() =>
    new()
    {
        InputTokenCount = Chat.InputTokens,
        OutputTokenCount = Chat.OutputTokens
    };

// Fresh per call, the function loop marks a FunctionCallContent InformationalOnly once its result exists
protected IReadOnlyList<ChatMessage> Reply() => ...
```

## 5. Shortcut and builders on the slice base

The members come in this order: shortcuts, the service hook, then per area the events, the commands and the other
builders.

```csharp
public class WebhookTestBase(WebhookFixture fixture) : TestBase(fixture)
{
    protected WebhookTestData Webhook => Data.Webhook;

    protected WebhookAdded WebhookAdded() =>
        new(
            Name: Webhook.Name,
            Url: Webhook.Url,
            Events: Webhook.Events
        );

    protected WebhookUpdated WebhookUpdated() =>
        new(
            Name: Webhook.Name.Updated,
            Url: Webhook.Url.Updated,
            Events: Webhook.Events.Updated
        );

    protected WebhookRemoved WebhookRemoved() => new();

    protected AddWebhook AddWebhook() =>
        new()
        {
            Id = AggregateId,
            Secret = Webhook.Secret,
            Name = Webhook.Name.Default.Value,
            Url = Webhook.Url.Default.Value,
            Events = Webhook.Events
        };

    protected UpdateWebhook UpdateWebhook() =>
        new()
        {
            Id = AggregateId,
            Secret = Webhook.Secret.Updated,
            Name = Webhook.Name.Updated.Value,
            Url = Webhook.Url.Updated.Value,
            Events = Webhook.Events.Updated
        };
}
```

The event takes the value objects through the implicit conversion. The command takes primitives, so it reads
`.Default.Value` / `.Updated.Value`.

## 6. Tests reading the catalog

A variant written as a `with` expression in the act:

```csharp
var tracked = await SendMessageAndWaitAsync(AddWebhook() with { Secret = Webhook.Secret.Invalid });
```

A partial update as a multi-line initializer in Arrange:

```csharp
var cmd = new UpdateWebhook
{
    Id = AggregateId,
    Name = Webhook.Name.Updated.Value
};
```

Assertions that name the variant:

```csharp
webhook.Url.Should().Be(Webhook.Url.Default);
webhook.Name.Should().Be(Webhook.Name.Updated);
webhook.Events.Should().Equal(Webhook.Events.Default);
```

`Optional<T>` fields through a helper:

```csharp
var evt = stream.Assert.EventAt<WebhookUpdated>(1);
evt.Data.Url.Assert.Unchanged();
evt.Data.Name.Assert.SetTo(Webhook.Name.Updated);
```

A non-constant boundary as its own Fact, where a validator test with no slice base reads `Data` directly:

```csharp
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
```
