using System.Reflection;
using System.Runtime.CompilerServices;

namespace YourApp.Tests.Testing.Conventions;

// Adapt the namespace; FindProjectDirectory assumes this file sits two folders below the test project
public static class TestSources
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static string ProjectDirectory { get; } = FindProjectDirectory();

    // Every test file outside the shared Testing/ folder, as a path relative to the project
    public static IReadOnlyList<string> Files { get; } =
    [
        .. Directory.EnumerateFiles(ProjectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs") || path.EndsWith(".razor"))
            .Select(path => Path.GetRelativePath(ProjectDirectory, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("bin/") && !path.StartsWith("obj/") && !path.StartsWith("Testing/"))
            .Order()
    ];

    // Theory derives from Fact in xUnit, so this finds both
    public static IReadOnlyList<MethodInfo> TestMethods { get; } =
    [
        .. typeof(TestSources).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true })
            .SelectMany(type => type.GetMethods(Declared))
            .Where(method => method.GetCustomAttribute<FactAttribute>() is not null)
    ];

    public static string[] Lines(string file) => File.ReadAllLines(Path.Combine(ProjectDirectory, file));

    private static string FindProjectDirectory([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
}
