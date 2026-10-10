using System.Collections.Immutable;
using Bogus;

namespace YourApp.Tests.Customers.Support;

// An example slice: replace Customer, its value objects, events and commands with the slice's own

public class CustomerTestData(Faker faker)
{
    // Two disjoint halves, so an update never repeats the value the customer was registered with
    private static readonly string[] RegisteredNames = ["Anna Gruber", "Lukas Huber", "Sophie Wagner", "Paul Bauer"];

    private static readonly string[] UpdatedNames = ["Florian Pichler", "Katharina Steiner", "Max Moser", "Lena Hofer"];

    private static readonly string[] Tags = ["wholesale", "retail", "export", "priority", "net-30", "net-60"];

    public TestValue<CustomerName> Name { get; } =
        new TestValue<CustomerName>(Named(faker, RegisteredNames))
            .SetUpdated(Named(faker, UpdatedNames))
            .SetInvalid(new CustomerName(""));

    public TestValue<string> Email { get; } =
        new TestValue<string>(faker.Internet.Email())
            .SetUpdated(faker.Internet.Email())
            .SetInvalid("not-an-email");

    // Drawn from disjoint thirds of the list for the same reason as the names
    public TestValue<ImmutableArray<string>> TagList { get; } =
        new TestValue<ImmutableArray<string>>([.. faker.PickRandom(Tags[..3], 2)])
            .SetUpdated([.. faker.PickRandom(Tags[3..], 2)])
            .SetInvalid([]);

    // A fixed instant per test instead of the wall clock
    public DateTimeOffset RegisteredAt { get; } =
        new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero).AddMinutes(faker.Random.Int(0, 60 * 24 * 30));

    // A suffix keeps the name unique among the rows other tests left in the shared database
    private static CustomerName Named(Faker faker, string[] names) =>
        new($"{faker.PickRandom(names)} {faker.Random.AlphaNumeric(6)}");
}

public abstract class CustomerTestBase(FixtureBase fixture) : TestBase(fixture)
{
    protected CustomerTestData Customer => Data.Customer;

    protected CustomerRegistered CustomerRegistered() =>
        new(
            Name: Customer.Name,
            Email: Customer.Email,
            Tags: Customer.TagList
        );

    protected CustomerUpdated CustomerUpdated() =>
        new(
            Name: Customer.Name.Updated,
            Email: Customer.Email.Updated,
            Tags: Customer.TagList.Updated
        );

    protected RegisterCustomer RegisterCustomer() =>
        new()
        {
            Id = AggregateId,
            Name = Customer.Name.Default.Value,
            Email = Customer.Email,
            Tags = Customer.TagList
        };

    protected UpdateCustomer UpdateCustomer() =>
        new()
        {
            Id = AggregateId,
            Name = Customer.Name.Updated.Value,
            Email = Customer.Email.Updated,
            Tags = Customer.TagList.Updated
        };
}
