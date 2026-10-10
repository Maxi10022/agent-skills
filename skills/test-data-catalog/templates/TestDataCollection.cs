using Bogus;
using YourApp.Tests.Customers.Support;
using YourApp.Tests.Orders.Support;

namespace YourApp.Tests.Testing.Data;

// One property per slice; every test base exposes it as `Data { get; } = new(TestFaker.ForCurrentTest())`
public class TestDataCollection(Faker faker)
{
    public CustomerTestData Customer { get; } = new(faker);

    public OrderTestData Order { get; } = new(faker);
}
