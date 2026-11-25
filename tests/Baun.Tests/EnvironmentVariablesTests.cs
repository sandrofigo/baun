namespace Baun.Tests;

/// <summary>
/// Uses a variable name unique to each test, so it can set environment variables while other tests run.
/// </summary>
public sealed class EnvironmentVariablesTests : IDisposable
{
    private readonly string name = $"BAUN_TEST_{Guid.NewGuid():N}";

    public void Dispose() => System.Environment.SetEnvironmentVariable(name, null);

    [Fact]
    public void Returns_the_value_of_a_variable()
    {
        System.Environment.SetEnvironmentVariable(name, "value");

        Assert.Equal("value", EnvironmentVariables.Get(name));
    }

    [Fact]
    public void Returns_null_for_a_variable_that_is_not_set()
    {
        Assert.Null(EnvironmentVariables.Get(name));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", false)]
    [InlineData("Yes", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("y", false)]
    public void Reads_a_variable_as_true(string value, bool expected)
    {
        System.Environment.SetEnvironmentVariable(name, value);

        Assert.Equal(expected, EnvironmentVariables.IsTrue(name));
    }

    [Fact]
    public void A_variable_that_is_not_set_is_not_true()
    {
        Assert.False(EnvironmentVariables.IsTrue(name));
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("0", 0L)]
    [InlineData("-1", -1L)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("1.5", null)]
    [InlineData("1,000", null)]
    [InlineData(" 1", null)]
    [InlineData("many", null)]
    [InlineData("9223372036854775808", null)]
    public void Reads_a_variable_as_a_number(string value, long? expected)
    {
        System.Environment.SetEnvironmentVariable(name, value);

        Assert.Equal(expected, EnvironmentVariables.GetNumber(name));
    }

    [Fact]
    public void A_variable_that_is_not_set_is_no_number()
    {
        Assert.Null(EnvironmentVariables.GetNumber(name));
    }

    [Fact]
    public void Reads_a_variable_as_an_absolute_path()
    {
        string path = Path.GetTempPath();
        System.Environment.SetEnvironmentVariable(name, path);

        Assert.Equal((AbsolutePath)path, EnvironmentVariables.GetPath(name));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("./relative")]
    public void A_relative_path_is_no_absolute_path(string value)
    {
        System.Environment.SetEnvironmentVariable(name, value);

        Assert.Null(EnvironmentVariables.GetPath(name));
    }

    [Fact]
    public void A_variable_that_is_not_set_is_no_path()
    {
        Assert.Null(EnvironmentVariables.GetPath(name));
    }
}
