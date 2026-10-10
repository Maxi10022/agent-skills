namespace YourApp.Tests.Testing.Data;

// Adapt the namespace; the type has no dependencies
public class TestValue<T>(T @default)
{
    private readonly Dictionary<string, T> _values = [];

    public T Default { get; } = @default;

    public T Updated => _values[TestValueConstants.Updated];

    public T Invalid => _values[TestValueConstants.Invalid];

    public T Unchanged => Default;

    public static implicit operator T(TestValue<T> value) => value.Default;

    public static implicit operator TestValue<T>(T @default) => new(@default);

    public T this[string key] => _values[key];

    public TestValue<T> Add(string key, T value)
    {
        _values[key] = value;
        return this;
    }

    public TestValue<T> SetUpdated(T value)
    {
        _values[TestValueConstants.Updated] = value;
        return this;
    }

    public TestValue<T> SetInvalid(T value)
    {
        _values[TestValueConstants.Invalid] = value;
        return this;
    }
}
