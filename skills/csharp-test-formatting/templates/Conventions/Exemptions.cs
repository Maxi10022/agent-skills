namespace YourApp.Tests.Testing.Conventions;

// Today's violators, a ratchet: an entry goes when its slice is swept and nothing new is ever added
public static class Exemptions
{
    // Class.Method
    public static IReadOnlySet<string> Names { get; } = new HashSet<string>
    {
    };

    // file: Method
    public static IReadOnlySet<string> LongDeclarations { get; } = new HashSet<string>
    {
    };

    // Namespace.Class
    public static IReadOnlySet<string> Misplaced { get; } = new HashSet<string>
    {
    };

    // Namespace.Class
    public static IReadOnlySet<string> WithoutFixture { get; } = new HashSet<string>
    {
    };

    // file: Method
    public static IReadOnlySet<string> Comments { get; } = new HashSet<string>
    {
    };

    // file: Type
    public static IReadOnlySet<string> SupportOutsideSupport { get; } = new HashSet<string>
    {
    };

    // file: banned call, for example "Orders/Support/OrderTestData.cs: wall clock"
    public static IReadOnlySet<string> BannedCalls { get; } = new HashSet<string>
    {
    };

    public static IEnumerable<string> All =>
        Names.Concat(LongDeclarations)
            .Concat(Misplaced)
            .Concat(WithoutFixture)
            .Concat(Comments)
            .Concat(SupportOutsideSupport)
            .Concat(BannedCalls);
}
