using JasperFx.Core;

namespace YourApp.Tests.Testing;

// Every wait in the suite reads one of these; a passing test never waits for its timeout, so generous is cheap
public static class Timeouts
{
    // Generous, the first command of a run also pays for Wolverine's handler codegen
    public static TimeSpan Tracked { get; } = 60.Seconds();

    public static TimeSpan Projection { get; } = 30.Seconds();

    public static TimeSpan Probe { get; } = 30.Seconds();

    // A page waits for the command it sent, so a render wait is as long as a tracked one
    public static TimeSpan Render { get; } = 60.Seconds();
}
