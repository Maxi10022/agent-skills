# Before and after (from production codebases)

Every pair below is a real rework. *Before* is the code as an agent committed it. *After* is the maintainer's
next commit on that code, made by hand. Names are translated to the fictional catalog domain (`Customer`, `Book`,
`Webhook`, `Loan`); library calls (Marten, Wolverine, Microsoft.Extensions.AI, EF Core) are real. Where the
maintainer's commit also changed behavior, that part is left out and named under the pair. Where a rule settled later
changes the after, a "Since then" block shows the code as it is written today. Read the pair whose rule you are about
to apply, or all of them before restructuring existing code.

## Contents

1. A signature past the limit wraps its parameters onto one line
2. A construction that overflows a call becomes a named local
3. An `await` never sits inside another call
4. One statement, one effect
5. Pipelines leave argument lists and `foreach` headers
6. A helper left with one caller goes back into it
7. A local that only renames a construction is inlined
8. A comment sits on the statement it justifies
9. Every chained builder call on its own line, where the project says so
10. Independent statements go shortest first
11. A lazily created property uses `field`
12. A feature registers itself

---

## 1. A signature past the limit wraps its parameters onto one line

Inside its class, the second signature ran to 123 columns and wrapped; the 113-column line above it stayed as it
was. `=>` stays at the end of the wrapped parameter line.

### Before

```csharp
public static StartStream<Customer> Handle(CreateCustomer cmd) =>
    MartenOps.StartStream<Customer>(streamId: cmd.Id, new CustomerCreated(cmd.Name, cmd.Email, cmd.Country));

public static async Task OnExceptionAsync(ExistingStreamIdCollisionException _, CreateCustomer cmd, IMessageBus bus) =>
    await bus.PublishAsync(new CustomerAlreadyExists(cmd.Id));
```

### After

```csharp
public static StartStream<Customer> Handle(CreateCustomer cmd) =>
    MartenOps.StartStream<Customer>(streamId: cmd.Id, new CustomerCreated(cmd.Name, cmd.Email, cmd.Country));

public static async Task OnExceptionAsync(
    ExistingStreamIdCollisionException _, CreateCustomer cmd, IMessageBus bus) =>
    await bus.PublishAsync(new CustomerAlreadyExists(cmd.Id));
```

## 2. A construction that overflows a call becomes a named local

The registration ran to 137 columns inside its class; its construction became a named local, used once, with a
blank line after it. The selector chains
(`ForMessagesOfType<T>().Encrypt()`) stay on one line.

### Before

```csharp
public void Configure(WolverineOptions options)
{
    options.RegisterEncryptionSerializer(new InMemoryKeyProvider(KeyId, new Dictionary<string, byte[]> { [KeyId] = keys.Messages }));
    options.Policies.ForMessagesOfType<AddWebhook>().Encrypt();
    options.Policies.ForMessagesOfType<UpdateWebhook>().Encrypt();
}
```

### After

```csharp
public void Configure(WolverineOptions options)
{
    var provider = new InMemoryKeyProvider(KeyId, new Dictionary<string, byte[]> { [KeyId] = keys.Messages });

    options.RegisterEncryptionSerializer(provider);
    options.Policies.ForMessagesOfType<AddWebhook>().Encrypt();
    options.Policies.ForMessagesOfType<UpdateWebhook>().Encrypt();
}
```

### Since then

This pair predates a rule the maintainer settled later: initializer braces never sit on one line or inside a call.
The dictionary's `{ [KeyId] = keys.Messages }` still does, so today the same code builds it as a local of its own:

```csharp
public void Configure(WolverineOptions options)
{
    var messageKeys = new Dictionary<string, byte[]>
    {
        [KeyId] = keys.Messages
    };

    var provider = new InMemoryKeyProvider(KeyId, messageKeys);

    options.RegisterEncryptionSerializer(provider);
    options.Policies.ForMessagesOfType<AddWebhook>().Encrypt();
    options.Policies.ForMessagesOfType<UpdateWebhook>().Encrypt();
}
```

## 3. An `await` never sits inside another call

The maintainer had written the named local; an agent folded it into expression bodies around a nested `await`; the
maintainer's review put it back, block bodies and all.

### Before

```csharp
public async ValueTask<IChatClient> GetClientAsync(ProviderModel providerModel) =>
    Build(await CreateChatBuilderAsync(providerModel));

public async ValueTask<IChatClient> GetClientAsync(ActiveChat chat) =>
    Build((await CreateChatBuilderAsync(chat.Model)).UseRecording(chat));
```

### After

```csharp
public async ValueTask<IChatClient> GetClientAsync(ProviderModel providerModel)
{
    var builder = await CreateChatBuilderAsync(providerModel);
    return Build(builder);
}

public async ValueTask<IChatClient> GetClientAsync(ActiveChat chat)
{
    var builder = await CreateChatBuilderAsync(chat.Model);
    return Build(builder.UseRecording(chat));
}
```

## 4. One statement, one effect

The chained assignment is split into the construction and the store, and a blank line separates the state change
from the notification.

### Before

```csharp
public async Task<ActiveChat> StartAsync(Guid id, string task, ProviderModel model)
{
    var chat = _chats[id] = new ActiveChat(this, id, task, model);
    await Raise(OnChatActivated, chat);
    return chat;
}
```

### After

```csharp
public async Task<ActiveChat> StartAsync(Guid id, string task, ProviderModel model)
{
    var chat = new ActiveChat(this, id, task, model);
    _chats[id] = chat;

    await Raise(OnChatActivated, chat);
    return chat;
}
```

## 5. Pipelines leave argument lists and `foreach` headers

Each pipeline becomes a named local, one call per line, even though every line fitted. A long single chain starts
with its receiver alone. The braceless `foreach` gets braces. The same commit also materialized the incoming
messages once; that change is left out.

### Before

```csharp
var turn = new Turn([.. messages.Skip(chat.Messages.Count)], cancellationToken, chat.StopToken);
chat.BeginTurn();
```

```csharp
await using var stream = base.GetStreamingResponseAsync(messages, options, turn.Token).GetAsyncEnumerator(turn.Token);
```

```csharp
foreach (var content in update.Contents.Where(c => c is not TextContent and not UsageContent))
    message.Contents.Add(content);
```

### After

```csharp
var remaining = messages
    .Skip(chat.Messages.Count)
    .ToArray();

var turn = new Turn(remaining, cancellationToken, chat.StopToken);

chat.BeginTurn();
```

```csharp
await using var stream = base
    .GetStreamingResponseAsync(messages, options, turn.Token)
    .GetAsyncEnumerator(turn.Token);
```

```csharp
var filteredContents = update.Contents.Where(c => c is not TextContent and not UsageContent);

foreach (var content in filteredContents)
{
    message.Contents.Add(content);
}
```

## 6. A helper left with one caller goes back into it

Once only one overload was left, `Build` said nothing its caller didn't, so its chain moved into the caller and is
returned after a blank line. The commit also renamed `CreateChatBuilderAsync` and added the tracking middleware (the
`UseConversationTracking` line).

### Before

```csharp
public async ValueTask<IChatClient> GetClientAsync(ProviderModel providerModel)
{
    // TODO wrap all clients with tracking middleware
    var builder = await CreateChatBuilderAsync(providerModel);
    return Build(builder);
}

private IChatClient Build(ChatClientBuilder builder) =>
    builder
        .UseFunctionInvocation(configure: ConfigureFunctionInvocation)
        .UseOpenTelemetry()
        .UseLogging()
        .Build(sp);
```

### After

```csharp
public async ValueTask<IChatClient> GetClientAsync(ProviderModel providerModel)
{
    var builder = await CreateBuilderAsync(providerModel);

    return builder
        .UseConversationTracking(providerModel)
        .UseFunctionInvocation(configure: ConfigureFunctionInvocation)
        .UseOpenTelemetry()
        .UseLogging()
        .Build(sp);
}
```

## 7. A local that only renames a construction is inlined

A block body of a single-use local and a `return` becomes an expression body that wraps after `=>`. A returned tuple
lists one element per line, its `(` and `);` at the member's own indent. The construction stays inside the call
because the line fits. The maintainer's commit also moved a normalized value onto the command as a property (see
"A member of the type it reads" in `SKILL.md`); that part is left out.

### Before

```csharp
public static (IMartenOp, WebhookCreated) Handle(CreateWebhook cmd)
{
    var webhook = new Webhook(cmd.Url, cmd.Events);
    return (
        MartenOps.Insert(webhook),
        new WebhookCreated(cmd.Url, cmd.Events)
    );
}
```

### After

```csharp
public static (IMartenOp, WebhookCreated) Handle(CreateWebhook cmd) =>
(
    MartenOps.Insert(new Webhook(cmd.Url, cmd.Events)),
    new WebhookCreated(cmd.Url, cmd.Events)
);
```

## 8. A comment sits on the statement it justifies

One comment sat on the enclosing call and another on a type; both moved onto the line they explain, with a blank
line between the two commented statements.

### Before

```csharp
// The history page lists conversations newest first, from a column rather than out of the JSON
services.ConfigureMarten(options =>
{
    options.Schema.For<Conversation>().Duplicate(conversation => conversation.LastActivity);
    options.Projections.Add<ConversationProjection>(ProjectionLifecycle.Inline);
});
```

```csharp
// Inline, so a run's row is final by the time the tracker reports it ended
public partial class ConversationProjection : SingleStreamProjection<Conversation, Guid>
```

### After

```csharp
services.ConfigureMarten(options =>
{
    // The history page lists conversations newest first, from a column rather than out of the JSON
    options.Schema.For<Conversation>().Duplicate(conversation => conversation.LastActivity);

    // Inline, so a run's row is final by the time the tracker reports it ended
    options.Projections.Add<ConversationProjection>(ProjectionLifecycle.Inline);
});
```

```csharp
public partial class ConversationProjection : SingleStreamProjection<Conversation, Guid>
```

## 9. Every chained builder call on its own line, where the project says so

From a project whose rules say "fluent chains one call per line". A first commit broke the long chain; a second one
broke every chain of the project's builders and of the EF Core model builder, a single call included, "as the longer
chains already did". In a project without that rule, the selector exception in `SKILL.md` keeps
`model.Entity<BookRow>().HasKey(...)` on one line.

### Before

```csharp
UseTools<CatalogTools>().Include(t => t.FindBook).Include(t => t.ListLoans).Include(t => t.SearchCustomers);
```

```csharp
model.Entity<BookRow>().HasKey(book => new { book.Shelf, book.Id });
model.Entity<BookRow>().Property(book => book.Revision).IsConcurrencyToken();
model.Entity<LoanRow>().HasKey(loan => loan.Sequence);
model.Entity<LoanRow>().HasIndex(loan => loan.Customer);
```

### After

```csharp
UseTools<CatalogTools>()
    .Include(t => t.FindBook)
    .Include(t => t.ListLoans)
    .Include(t => t.SearchCustomers);
```

```csharp
model.Entity<BookRow>()
    .HasKey(book => new { book.Shelf, book.Id });

model.Entity<BookRow>()
    .Property(book => book.Revision)
    .IsConcurrencyToken();

model.Entity<LoanRow>()
    .HasKey(loan => loan.Sequence);

model.Entity<LoanRow>()
    .HasIndex(loan => loan.Customer);
```

## 10. Independent statements go shortest first

A block of registrations whose order doesn't matter, re-sorted by line length after an agent appended one. Lines of
equal length keep their earlier order.

### Before

```csharp
builder.Services.AddSecurity();
builder.Services.AddWebhooks();
builder.Services.AddLoans();
builder.Services.AddCustomers();
builder.Services.AddCatalog();
builder.Services.AddShareLinks();
builder.Services.AddBooks();
builder.Services.AddNotifications();
builder.Services.ConfigureForwardedHeaders();
builder.Services.AddCascadingAuthenticationState();
```

### After

```csharp
builder.Services.AddLoans();
builder.Services.AddBooks();
builder.Services.AddCatalog();
builder.Services.AddSecurity();
builder.Services.AddWebhooks();
builder.Services.AddCustomers();
builder.Services.AddShareLinks();
builder.Services.AddNotifications();
builder.Services.ConfigureForwardedHeaders();
builder.Services.AddCascadingAuthenticationState();
```

## 11. A lazily created property uses `field`

The maintainer's commit removed eight declared backing fields (three are shown), and each property creates its
service through `field`. The summaries stayed.

### Before

```csharp
private BookService? _books;
private LoanService? _loans;
private CustomerService? _customers;

/// <summary>
/// The books of a catalog - every title it lends, with its copies and shelves.
/// </summary>
public BookService Books => _books ??= new(this);
```

### After

```csharp
/// <summary>
/// The books of a catalog - every title it lends, with its copies and shelves.
/// </summary>
public BookService Books => field ??= new(this);
```

## 12. A feature registers itself

The feature's projections moved out of the central Marten configuration, labels and all, into the feature's own
`AddCustomers()`. Behavior is unchanged.

### Before

The central configuration, and the feature's registration without a Marten call:

```csharp
// Share link projections
options.Projections.Add<ShareLinkProjection>(ProjectionLifecycle.Async);
options.Projections.Add<ExpiredShareLinkProjection>(ProjectionLifecycle.Async);

// Customer projection
options.Projections.Add<CustomerProjection>(ProjectionLifecycle.Async);
```

```csharp
public static IServiceCollection AddCustomers(this IServiceCollection services)
{
    services.AddImports();
    services.AddSingleton<CustomerNotifications>();
    return services;
}
```

### After

```csharp
public static IServiceCollection AddCustomers(this IServiceCollection services)
{
    services.AddImports();
    services.AddSingleton<CustomerNotifications>();
    services.ConfigureMarten(options =>
    {
        options.Projections.Add<CustomerProjection>(ProjectionLifecycle.Async);
    });

    return services;
}
```
