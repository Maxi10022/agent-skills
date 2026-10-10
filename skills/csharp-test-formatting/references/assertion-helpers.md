# Assertion helpers in the `x.Assert.Y()` form

A recurring check, or a road that would otherwise need `.Which`, `.Subject` or a throwaway variable, becomes a small
assertions class. A C# 14 `extension` block exposes it as a property named `Assert` on the subject type. The call
site then reads like the rule it checks:

```csharp
tracked.AutoFaultsPublished
    .SingleMessage<Fault<RemoveWebhook>>()
    .Assert.ThrownException<RequiredDataMissingException>();

result.Assert.SingleError(nameof(CreateShareLink.Note), "A note is required.");
await Secrets.Assert.SecretAsync(AggregateId, Webhook.Secret);
var evt = stream.Assert.EventAt<WebhookUpdated>(1);
evt.Data.Name.Assert.SetTo(Webhook.Name.Updated);
```

## The shape

- A `sealed` class named `<Subject>Assertions` takes the subject in its primary constructor.
- Each method runs the road inside, as plain FluentAssertions statements, one assertion per line. It returns the
  subject, or the next subject, so a caller can go on. A helper that checks an absence returns `void`.
- A static class named `<Subject>AssertionExtensions` holds the `extension(Subject x)` block with
  `public <Subject>Assertions Assert => new(x);`.
- A helper that must read something, such as a secret store, is `async` and named `...Async`. The read moves out of
  the test's Act into the helper.
- All helpers live in one shared folder (`Testing/Assertions/`), so every slice uses the same vocabulary.
- On C# 13 or older, use `public static FaultAssertions<T> Assert<T>(this Fault<T> fault) => new(fault);` and call
  `fault.Assert().ThrownException<T>()`.

## Example (from a production codebase)

### A Wolverine fault

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

### A FluentValidation result

`ErrorOn` compares the property names as a list, so a failure prints the names instead of a lambda:

```csharp
public sealed class ValidationResultAssertions(ValidationResult result)
{
    public ValidationResult ErrorOn(string property)
    {
        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(property);
        return result;
    }

    public ValidationResult SingleError(string property)
    {
        result.Errors.Should().ContainSingle();
        result.Errors[0].PropertyName.Should().Be(property);
        return result;
    }

    public ValidationResult SingleError(string property, string message)
    {
        SingleError(property);
        result.Errors[0].ErrorMessage.Should().Be(message);
        return result;
    }
}

public static class ValidationResultAssertionExtensions
{
    extension(ValidationResult result)
    {
        public ValidationResultAssertions Assert => new(result);
    }
}
```

### An event stream (Marten)

Marten hands back its own `Event<T>`, so the type check must test assignability. `BeOfType<IEvent<T>>()` always
fails.

```csharp
public sealed class StreamAssertions(IReadOnlyList<IEvent> stream)
{
    public IEvent<TEvent> EventAt<TEvent>(int position) where TEvent : notnull
    {
        position.Should().BeGreaterThanOrEqualTo(0);
        position.Should().BeLessThan(stream.Count, "the stream has not enough events");

        // Marten hands back its own Event<T>, so the type check is an assignability check
        return stream[position].Should().BeAssignableTo<IEvent<TEvent>>().Subject;
    }
}

public static class StreamAssertionExtensions
{
    extension(IReadOnlyList<IEvent> stream)
    {
        public StreamAssertions Assert => new(stream);
    }
}
```

The `.Subject` here is inside the helper, which is the one place a road may hand its end on.

### An optional field

`Optional<T>.Should().Be(value)` never passes, because the expected side would need to be an `Optional<T>` too. A
helper states the two cases a test means:

```csharp
public sealed class OptionalAssertions<T>(Optional<T> optional)
{
    public T SetTo(T expected)
    {
        optional.IsSet.Should().BeTrue();
        optional.Value.Should().BeEquivalentTo(expected);
        return optional.Value;
    }

    public void Unchanged() => optional.IsSet.Should().BeFalse();
}

public static class OptionalAssertionExtensions
{
    extension<T>(Optional<T> optional)
    {
        public OptionalAssertions<T> Assert => new(optional);
    }
}
```

### An async read

```csharp
public sealed class SecretStoreAssertions(SecretStore store)
{
    public async Task SecretAsync(Guid id, string secret)
    {
        var stored = await store.GetSecretAsync(id);
        stored.Should().Be(secret);
    }

    public async Task NoSecretAsync(Guid id)
    {
        var stored = await store.GetSecretAsync(id);
        stored.Should().BeNull();
    }
}

public static class SecretStoreAssertionExtensions
{
    extension(SecretStore store)
    {
        public SecretStoreAssertions Assert => new(store);
    }
}
```

## When not to write a helper

- **A one-off check.** Write the road inline in short lines.
- **A check the library already has.** Wolverine's `SingleMessage<T>()` and Alba's `StatusCodeShouldBe` stay as
  they are.
- **A helper that would hide the act or arrange state.** An assertion helper asserts; it does not act or arrange.
