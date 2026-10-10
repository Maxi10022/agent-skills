---
name: test-data-catalog
description: Build and use a test-data catalog for C#/.NET tests - TestValue<T> with Default/Updated/Invalid variants, one data class per feature slice drawn once per test from a Bogus Faker seeded by the test's identity, reached through the test base as `Data`, plus one parameterless builder per command or event. Use this whenever you add, review or refactor test data, test builders or Faker calls in a .NET test project, when a value must stay the same for the whole test and repeat on every run, when tests share a database and need unique names, or when an assertion against test data fails although both sides print the same value. Not for fixtures, database setup or test formatting.
---

# Test-data catalog

Scattered literals and ad-hoc `Faker` calls cause the same few problems in every test suite:

- a value read twice comes back different;
- an "update" sets the value the entity already had;
- two tests collide on a "unique" name in a shared database;
- a failure does not reproduce on the next run.

The catalog fixes these by giving every test one fixed, named set of values. The set differs per test and repeats on
every run.

Assumed stack: C# 12+, Bogus, xUnit v3 (the test identity comes from `TestContext.Current`), FluentAssertions. The
section on seeding names what to change for other frameworks.

## The pieces

```
TestBase / UnitTestBase / ComponentTestBase
  Data : TestDataCollection                 one per test, from TestFaker.ForCurrentTest()
    Customer : CustomerTestData             one class per slice or feature
      Name  : TestValue<CustomerName>       Default / Updated / Invalid
      Email : TestValue<string>
    Order : OrderTestData ...
Slice base   Customer => Data.Customer      the shortcut
             CustomerRegistered(), RegisterCustomer()   one parameterless builder each
Test         Customer.Name.Updated, RegisterCustomer() with { Email = Customer.Email.Invalid }
```

Templates to copy and adapt: [templates/TestValue.cs](templates/TestValue.cs),
[templates/TestValueConstants.cs](templates/TestValueConstants.cs), [templates/TestFaker.cs](templates/TestFaker.cs),
[templates/TestDataCollection.cs](templates/TestDataCollection.cs) and
[templates/SliceTestData.example.cs](templates/SliceTestData.example.cs). The last one is a slice data class plus
its base with builders. The templates assume the test project imports the shared `Testing` namespaces as global usings
(`<Using Include="YourApp.Tests.Testing.Bases" />` and so on in the `.csproj`).

## TestValue<T>

`TestValue<T>` wraps the value a test uses by default and the variants a test may need instead.

- `Default` converts implicitly in both directions. A data class can therefore write
  `new TestValue<string>("x")`, and a builder can pass `Customer.Name` to a parameter of type `CustomerName`.
- `Updated` is what an update sets. It must differ from `Default`, see the section on disjoint halves below.
- `Invalid` is a value the validator rejects.
- `Unchanged` returns `Default`. It makes "stays as it was" assertions read as what they mean.
- `Add(key, value)` and the `[key]` indexer hold a third named variant, which is rarely needed. Prefer a separate
  property over many keys.

Set only the variants some test reads. If a test reads a variant that was never set, it gets a
`KeyNotFoundException`. That means the catalog is missing the variant: add it there instead of catching the exception.

## One data class per slice

```csharp
public class CustomerTestData(Faker faker)
{
    // Two disjoint halves, so an update never repeats the value the customer was registered with
    private static readonly string[] RegisteredNames = ["Anna Gruber", "Lukas Huber", "Sophie Wagner"];

    private static readonly string[] UpdatedNames = ["Florian Pichler", "Katharina Steiner", "Max Moser"];

    public TestValue<CustomerName> Name { get; } =
        new TestValue<CustomerName>(Named(faker, RegisteredNames))
            .SetUpdated(Named(faker, UpdatedNames))
            .SetInvalid(new CustomerName(""));

    public TestValue<string> Email { get; } =
        new TestValue<string>(faker.Internet.Email())
            .SetUpdated(faker.Internet.Email())
            .SetInvalid("not-an-email");

    public DateTimeOffset RegisteredAt { get; } =
        new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero).AddMinutes(faker.Random.Int(0, 60 * 24 * 30));

    // A suffix keeps the name unique among the rows other tests left in the shared database
    private static CustomerName Named(Faker faker, string[] names) =>
        new($"{faker.PickRandom(names)} {faker.Random.AlphaNumeric(6)}");
}
```

What each choice protects against:

- **Get-only properties with an initializer (`{ get; } =`).** The value is drawn once, when the test starts, and a
  second read returns the same value. A `=>` property that calls the faker draws a new value on every read. That is
  the classic cause of a builder and an assertion that disagree.
- **Domain types.** Values are the domain's value objects (`TestValue<CustomerName>`), not raw strings, wherever the
  domain has them. A command that takes primitives reads `.Value`.
- **Curated lists over Lorem.** Realistic values read well in failure messages and catch format bugs. The faker
  picks among them, so each test still gets its own value.
- **Disjoint halves for Default and Updated.** An update test can never pass because the new value happened to
  equal the old one. The "nothing changed" path stays its own test.
- **A random suffix (`faker.Random.AlphaNumeric(6)`) or a per-test Guid.** Use one on anything a test looks up by
  value in a shared database. Never use `Faker.UniqueIndex`: Bogus fixes it when the `Faker` instance is created, so
  every test using a shared faker gets the same "unique" value.
- **A plain property for a value without variants.** `RegisteredAt` above is one.
- **A fixed instant drawn from a range, not `DateTime.UtcNow`.** Where production code reads the clock, inject a
  `TimeProvider` (`FakeTimeProvider` in tests) instead of reading the wall clock in test data.
- **Derived values that only read.** `Answer => string.Concat(AnswerDeltas)` is fine, because it builds nothing new
  from the faker.
- **Reference-equality objects built once.** A message object that compares by reference goes in an initializer,
  so a second read returns the same instance.
- **Mutable objects as builder methods.** An object that production code mutates is never shared. It becomes a
  builder method on the slice base (`Usage()`) that returns a fresh instance per call.
- **`IAsyncLifetime.InitializeAsync` for values that need async work.** Assign the value once there.

## Wiring

- `TestDataCollection(Faker faker)` has one get-only property per slice, each `new(faker)`. A slice whose data needs
  no randomness gets `new()`.
- Every base exposes `public TestDataCollection Data { get; } = new(TestFaker.ForCurrentTest());`. That includes the
  host-free unit base and the component base, so tests reach their data the same way everywhere.
- The slice base adds a shortcut: `protected CustomerTestData Customer => Data.Customer;`. A shortcut returns the
  same object on every read, so `=>` is fine here. A shortcut named like a projection type is fine too.
- Tests read through the shortcut (`Customer.Name.Updated`). Host-free tests without a slice base read
  `Data.Customer.Name`.

## Seeding from the test's identity

`TestFaker.ForCurrentTest()` seeds a `Faker` from the running test's unique id:

- Values differ per test, which exposes tests that only pass because of one particular constant.
- Values repeat on every run and machine, so a failure reproduces.
- The seed is hashed with FNV-1a by hand, because `string.GetHashCode` is randomized per process.
- The seed goes into the faker's own `Randomizer`. Never use Bogus' static `Randomizer.Seed`: it is shared by
  every faker, so parallel tests would interleave on it.

Notes:

- Properties draw in declaration order. Adding a property changes the values drawn after it. That is harmless,
  because no test depends on a concrete drawn value.
- Values that do not come from the faker, such as `Guid.CreateVersion7()` for ids, are unique but not reproducible.
  That is fine for ids.
- The identity source depends on the framework:
  - **xUnit v3:** `TestContext.Current.Test?.UniqueID`, which differs per theory row.
  - **NUnit:** `TestContext.CurrentContext.Test.ID` or `FullName`.
  - **MSTest:** `TestContext.FullyQualifiedTestClassName + "." + TestContext.TestName`.
  - **xUnit v2:** there is no ambient test context, so pass a name into the base or move to v3.
- Ban `new Faker(` in test files, for example with a convention test, so nobody bypasses the catalog.

## Builders on the slice base

- **One parameterless builder per event and per command.** Each builder sets every field from the catalog.
  Creating ones read `Default`, updating ones read `Updated`:

  ```csharp
  protected CustomerRenamed CustomerRenamed() =>
      new(
          Name: Customer.Name.Updated
      );

  protected RegisterCustomer RegisterCustomer() =>
      new()
      {
          Id = AggregateId,
          Name = Customer.Name.Default.Value,
          Email = Customer.Email
      };
  ```

- **Variants are written in the test.** Use `RegisterCustomer() with { Email = Customer.Email.Invalid }`, or a
  multi-line object initializer in Arrange when the test sends only some fields.
- **No optional-parameter builders** like `UpdateCustomer(name: ..., email: ...)`. They hide which fields a test
  cares about, and they grow a parameter for every new test.
- **No composite givens** like `GivenCustomerAsync()` that appends events and stores side data. The test lists its
  events and side state itself, so it arranges only what its name needs.
- **Inline construction in host-free tests.** A validator test that needs a command or two builds it inline
  (`new RegisterCustomer { ... }`). No builder machinery for a few tests.

## Asserting against catalog values

- **Name the variant in assertions.** FluentAssertions' `Should().Be(object expected)` takes `object`, so the
  implicit conversion never runs. `customer.Name.Should().Be(Customer.Name)` compares against the `TestValue` wrapper
  and fails, even though both values look identical in the message. Write `.Default` or `.Updated`. Builders and
  method arguments convert implicitly; only assertions need the variant spelled out.
- **Collections as well.** `Equal(...)` and `BeEquivalentTo(...)` against a catalog collection take
  `Customer.Tags.Default`.
- **Wrapper types need a helper.** Wrapper types without value equality against `T`, such as an `Optional<T>` field
  on an event, need an assertion helper (`evt.Data.Name.Assert.SetTo(Customer.Name.Updated)`,
  `.Assert.Unchanged()`), because `.Should().Be(value)` never passes.

## Boundaries and theories

- Use `[InlineData]` for constants (`""`, `"  "`, `null`).
- A boundary value that is not a constant, such as a 256-character note, cannot go into an attribute. It lives in
  the catalog as `Invalid` and gets its own `[Fact]`.
- Use `TheoryData` only for large or generated sets, never to smuggle catalog values into a theory.

## Symptom → cause → fix

| Symptom | Cause | Fix |
|---|---|---|
| Assertion fails, expected and actual print the same | `TestValue<T>` compared to `T` | Name `.Default` / `.Updated` |
| Builder and assertion disagree | `=>` property draws on every read | `{ get; } =` initializer |
| Two tests collide on a "unique" value | `Faker.UniqueIndex` or a shared, unseeded faker | `Random.AlphaNumeric(n)` suffix or a Guid |
| Update test passes without changing anything | `Updated` equals `Default` by chance | Draw from disjoint lists |
| A failure does not reproduce | Unseeded faker, or a `GetHashCode` seed | `TestFaker.ForCurrentTest()` |
| A later read sees a mutated object | Shared object that production mutates | Builder method returning a fresh instance |
| `KeyNotFoundException` from `.Updated` | Variant never set | Set it in the data class |

## Introducing the catalog into an existing suite

1. Add `TestValue<T>`, `TestValueConstants`, `TestFaker` and an empty `TestDataCollection`, and expose `Data` on
   every base.
2. Work through one slice at a time:
   - add a data class, the shortcut and one builder per event and command;
   - replace literals and faker calls in that slice's tests;
   - delete optional-parameter builders and composite givens;
   - add `.Default` to assertions.
3. Ban `new Faker(` in test files. If the project has convention tests with an exemption list, exempt the unswept
   files and remove each entry as its slice is swept.

## Worked examples

[references/catalog-examples.md](references/catalog-examples.md) holds an example from a real project: data classes
with curated lists and value objects, a chat slice where reference-equality objects are fixed and mutable ones are
builders, the slice base's builders, and tests reading the catalog. Read it to see the pattern in context.
