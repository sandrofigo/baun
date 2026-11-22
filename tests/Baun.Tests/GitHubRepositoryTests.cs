using Baun.CI.GitHub;

namespace Baun.Tests;

public sealed class GitHubRepositoryTests
{
    [Fact]
    public void Parses_owner_and_name()
    {
        GitHubRepository repository = GitHubRepository.Parse("octocat/Hello-World");

        Assert.Equal("octocat", repository.Owner);
        Assert.Equal("Hello-World", repository.Name);
        Assert.Equal("octocat/Hello-World", repository.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noslash")]
    [InlineData("octocat/")]
    [InlineData("/Hello-World")]
    [InlineData("a/b/c")]
    public void Doesnt_parse_an_invalid_repository(string? value)
    {
        Assert.False(GitHubRepository.TryParse(value, out GitHubRepository? repository));
        Assert.Null(repository);
    }

    [Fact]
    public void Parse_throws_for_an_invalid_repository()
    {
        Assert.Throws<FormatException>(() => GitHubRepository.Parse("noslash"));
    }
}
