using FluentAssertions;

namespace YourApp.Tests.Testing.Assertions;

// Replace Subject with the asserted type; one method per recurring check, each a road of single assertions
public sealed class SubjectAssertions(Subject subject)
{
    public Subject HasName(string expected)
    {
        subject.Name.Should().NotBeNull();
        subject.Name.Should().Be(expected);
        return subject;
    }

    public void IsEmpty() => subject.Items.Should().BeEmpty();
}

public static class SubjectAssertionExtensions
{
    extension(Subject subject)
    {
        public SubjectAssertions Assert => new(subject);
    }
}
