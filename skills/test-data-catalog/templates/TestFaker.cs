using Bogus;

namespace YourApp.Tests.Testing.Data;

// xUnit v3; for NUnit seed from TestContext.CurrentContext.Test.ID, for MSTest from the class and test name
public static class TestFaker
{
    // The published starting value and multiplier of 32-bit FNV-1a, a small and widely used string hash
    private const uint FnvOffsetBasis = 2166136261;

    private const uint FnvPrime = 16777619;

    // Seeded from the running test's identity, so a test draws the same values on every run
    public static Faker ForCurrentTest() =>
        new()
        {
            Random = new Randomizer(Seed(TestContext.Current.Test?.UniqueID ?? ""))
        };

    // string.GetHashCode differs between processes, FNV-1a gives a test the same seed on every run and machine
    private static int Seed(string identity)
    {
        var hash = FnvOffsetBasis;

        // Each character is folded into the hash, the multiplication spreads it across all bits
        foreach (var character in identity)
        {
            hash = (hash ^ character) * FnvPrime;
        }

        // Randomizer takes an int, a seed past int.MaxValue simply turns negative
        return unchecked((int)hash);
    }
}
