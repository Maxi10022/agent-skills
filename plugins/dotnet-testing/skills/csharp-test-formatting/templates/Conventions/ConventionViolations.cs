using System.Reflection;
using System.Text.RegularExpressions;

namespace YourApp.Tests.Testing.Conventions;

// Adapt RootNamespace, HostTestBase and the banned calls to the project; drop a rule that does not apply
public static partial class ConventionViolations
{
    private const string RootNamespace = "YourApp.Tests";

    // The base every test with a host derives from, it must declare a collection or class fixture
    private static readonly Type HostTestBase = typeof(TestBase);

    public static IReadOnlyDictionary<string, Regex> BannedCalls { get; } = new Dictionary<string, Regex>
    {
        ["Task.Delay"] = new(@"\bTask\.Delay\("),
        ["Thread.Sleep"] = new(@"\bThread\.Sleep\("),
        ["#region"] = new(@"^\s*#(end)?region\b"),
        ["new Faker"] = new(@"\bnew Faker\("),
        ["raw cancellation token"] = new(@"TestContext\.Current\.CancellationToken"),
        ["raw tracked invoke"] = new(@"\bHost\.InvokeMessageAndWaitAsync\("),
        ["raw projection wait"] = new(@"\bHost\.WaitForNonStaleProjectionDataAsync\("),
        ["numeric wait timeout"] = new(@"\b(WaitAsync|WaitForStateAsync|Timeout)\([^;]*\d+\.(Seconds|Minutes)\(\)"),
        ["wall clock"] = new(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b")
    };

    // Class.Method of every test whose name is neither Given_<state>_<outcome> nor the outcome alone
    public static IEnumerable<string> Names() =>
        TestSources.TestMethods
            .Where(method => !IsWellNamed(method))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Distinct();

    // file: Method of every test declaration line past 120 columns
    public static IEnumerable<string> LongDeclarations() =>
        TestSources.Files.SelectMany(file => TestSources.Lines(file)
            .Where(line => line.Length > 120)
            .Select(line => TestDeclaration().Match(line))
            .Where(match => match.Success)
            .Select(match => $"{file}: {match.Groups["name"].Value}"));

    // Class names of tests that do not live in <Namespace folder>/<Class>.cs or .razor
    public static IEnumerable<string> Misplaced() =>
        TestSources.TestMethods
            .Select(method => method.DeclaringType!)
            .Distinct()
            .Where(type => !IsInItsOwnFile(type))
            .Select(type => type.FullName!);

    // Class names of host tests that declare neither a collection nor a class fixture
    public static IEnumerable<string> WithoutFixture() =>
        TestSources.TestMethods
            .Select(method => method.DeclaringType!)
            .Distinct()
            .Where(type => type.IsAssignableTo(HostTestBase) && !HasFixture(type))
            .Select(type => type.FullName!);

    // file: banned call
    public static IEnumerable<string> Banned(string call) =>
        TestSources.Files
            .Where(file => TestSources.Lines(file).Any(line => BannedCalls[call].IsMatch(line)))
            .Select(file => $"{file}: {call}");

    // file: Method of every test body holding a comment other than Arrange, Act, Assert or Act & Assert
    public static IEnumerable<string> Comments() =>
        TestSources.Files.SelectMany(file => TestBodies(TestSources.Lines(file))
            .Where(body => body.Lines.Any(IsForeignComment))
            .Select(body => $"{file}: {body.Name}"));

    // file: Type of everything but a test class declared outside Testing/ and the slices' Support/ folders
    public static IEnumerable<string> SupportOutsideSupport() =>
        TestSources.Files
            .Where(file => file.EndsWith(".cs") && !file.Contains("/Support/"))
            .SelectMany(file => TestSources.Lines(file)
                .Select(line => TopLevelType().Match(line))
                .Where(match => match.Success && !match.Groups["name"].Value.EndsWith("Tests"))
                .Select(match => $"{file}: {match.Groups["name"].Value}"));

    public static IEnumerable<string> All() =>
        Names()
            .Concat(LongDeclarations())
            .Concat(Misplaced())
            .Concat(WithoutFixture())
            .Concat(Comments())
            .Concat(SupportOutsideSupport())
            .Concat(BannedCalls.Keys.SelectMany(Banned));

    private static bool IsWellNamed(MethodInfo method)
    {
        var subject = method.DeclaringType!.Name.Replace("Tests", "");

        return WellFormedName().IsMatch(method.Name)
            && !method.Name.StartsWith("When_")
            && !method.Name.StartsWith($"{subject}_");
    }

    private static bool IsInItsOwnFile(Type type)
    {
        var folder = type.Namespace!.Replace($"{RootNamespace}.", "").Replace('.', '/');

        return TestSources.Files.Contains($"{folder}/{type.Name}.cs")
            || TestSources.Files.Contains($"{folder}/{type.Name}.razor")
            || type.Namespace.StartsWith($"{RootNamespace}.Testing");
    }

    private static bool HasFixture(Type type) =>
        type.GetCustomAttributes().Any(attribute => attribute.GetType().Name.StartsWith("CollectionAttribute"))
        || type.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IClassFixture<>));

    private static bool IsForeignComment(string line)
    {
        var trimmed = line.Trim();

        return trimmed.StartsWith("//")
            && trimmed is not ("// Arrange" or "// Act" or "// Assert" or "// Act & Assert");
    }

    // The lines between a [Fact] or [Theory] method's braces, found by counting them
    private static IEnumerable<(string Name, List<string> Lines)> TestBodies(string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            if (!TestAttribute().IsMatch(lines[index])) continue;

            var declaration = Array.FindIndex(lines, index, line => TestDeclaration().IsMatch(line));
            if (declaration < 0) yield break;

            var name = TestDeclaration().Match(lines[declaration]).Groups["name"].Value;
            var body = new List<string>();
            var depth = 0;
            var opened = false;

            for (index = declaration + 1; index < lines.Length; index++)
            {
                depth += lines[index].Count(character => character == '{');
                depth -= lines[index].Count(character => character == '}');
                opened |= depth > 0;
                body.Add(lines[index]);

                if (opened && depth == 0) break;
            }

            yield return (name, body);
        }
    }

    [GeneratedRegex(@"^(Given_)?[A-Z][A-Za-z0-9]*(_[A-Za-z0-9]+)*$")]
    private static partial Regex WellFormedName();

    [GeneratedRegex(@"^\s*public (async )?(Task|void|ValueTask) (?<name>\w+)\(")]
    private static partial Regex TestDeclaration();

    // Top-level types start in the first column, nested ones are indented
    [GeneratedRegex(
        @"^(public |internal |file )?((sealed|static|abstract|partial) )*"
        + @"(class|record|struct|interface|enum) (?<name>\w+)")]
    private static partial Regex TopLevelType();

    [GeneratedRegex(@"^\s*\[(Fact|Theory)\b")]
    private static partial Regex TestAttribute();
}
