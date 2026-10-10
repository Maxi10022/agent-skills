using FluentAssertions;

namespace YourApp.Tests.Testing.Conventions;

// Derive from the project's host-free unit base if it has one; the InlineData rows mirror BannedCalls' keys
public class TestConventionTests
{
    [Fact]
    public void Every_Test_Is_Named_Given_State_And_Outcome_Or_The_Outcome_Alone()
    {
        // Act
        var violations = ConventionViolations.Names().Except(Exemptions.Names).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Every_Test_Declaration_Fits_120_Columns()
    {
        // Act
        var violations = ConventionViolations.LongDeclarations().Except(Exemptions.LongDeclarations).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Every_Test_Class_Lives_In_A_File_Named_After_It_In_Its_Namespace_Folder()
    {
        // Act
        var violations = ConventionViolations.Misplaced().Except(Exemptions.Misplaced).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Every_Host_Test_Class_Declares_A_Collection_Or_Class_Fixture()
    {
        // Act
        var violations = ConventionViolations.WithoutFixture().Except(Exemptions.WithoutFixture).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Test_Bodies_Carry_No_Comments_But_Arrange_Act_And_Assert()
    {
        // Act
        var violations = ConventionViolations.Comments().Except(Exemptions.Comments).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Every_Helper_Lives_In_Testing_Or_In_Its_Slices_Support_Folder()
    {
        // Act
        var violations = ConventionViolations.SupportOutsideSupport().Except(Exemptions.SupportOutsideSupport).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Theory]
    [InlineData("Task.Delay")]
    [InlineData("Thread.Sleep")]
    [InlineData("#region")]
    [InlineData("new Faker")]
    [InlineData("raw cancellation token")]
    [InlineData("raw tracked invoke")]
    [InlineData("raw projection wait")]
    [InlineData("numeric wait timeout")]
    [InlineData("wall clock")]
    public void A_Banned_Call_Stays_Out_Of_The_Tests(string call)
    {
        // Act
        var violations = ConventionViolations.Banned(call).Except(Exemptions.BannedCalls).ToList();

        // Assert
        violations.Should().BeEmpty(Listing(violations));
    }

    [Fact]
    public void Every_Exemption_Still_Applies()
    {
        // Act
        var stale = Exemptions.All.Except(ConventionViolations.All()).ToList();

        // Assert
        stale.Should().BeEmpty(Listing(stale));
    }

    private static string Listing(IEnumerable<string> entries) =>
        $"every entry breaks the convention or is a stale exemption:{Environment.NewLine}"
        + string.Join(Environment.NewLine, entries);
}
