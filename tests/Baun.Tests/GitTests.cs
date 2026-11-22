using Baun.Logging;
using Baun.Tools.Git;
using Spectre.Console.Testing;

namespace Baun.Tests;

public sealed class GitTests : IDisposable
{
    private readonly AbsolutePath repository = (AbsolutePath)Path.GetTempPath() / Path.GetRandomFileName();

    // Outside a build, messages go to the real console, where they would end up in the output of dotnet test.
    private readonly SerilogBackend backend = SerilogBackend.ForConsole(new TestConsole(), LogLevel.Debug);

    private readonly IDisposable scope;

    public GitTests()
    {
        scope = Log.Use(backend);
        repository.CreateDirectory();
    }

    public void Dispose()
    {
        scope.Dispose();
        backend.Dispose();
        repository.Delete();
    }

    [Fact]
    public async Task Returns_the_tag_with_the_highest_version_of_the_current_commit()
    {
        await InitAsync();
        await GitAsync("tag", "v1.0.0");
        await GitAsync("tag", "v1.10.0");
        await GitAsync("tag", "v1.9.0");

        Assert.Equal("v1.10.0", await Git.GetCurrentTagAsync(repository, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Returns_no_tag_for_a_commit_without_one()
    {
        await InitAsync();
        await GitAsync("tag", "v1.0.0");
        await CommitAsync();

        Assert.Null(await Git.GetCurrentTagAsync(repository, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Returns_the_current_branch_or_none_when_detached()
    {
        await InitAsync();

        Assert.Equal("release", await Git.GetCurrentBranchAsync(repository, TestContext.Current.CancellationToken));

        await GitAsync("checkout", "--detach");

        Assert.Null(await Git.GetCurrentBranchAsync(repository, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Throws_outside_a_repository()
    {
        await Assert.ThrowsAsync<ToolException>(() => Git.GetCurrentTagAsync(repository, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(ToolStream.StandardError, "fatal: not a git repository (or any of the parent directories): .git", LogLevel.Error)]
    [InlineData(ToolStream.StandardError, "error: pathspec 'x' did not match any file(s) known to git", LogLevel.Error)]
    [InlineData(ToolStream.StandardError, "warning: in the working copy of 'a.cs', LF will be replaced by CRLF", LogLevel.Warning)]
    [InlineData(ToolStream.StandardError, "Switched to a new branch 'main'", LogLevel.Debug)]
    [InlineData(ToolStream.StandardError, "hint: Using 'master' as the name for the initial branch.", LogLevel.Debug)]
    [InlineData(ToolStream.StandardOutput, "error: only on the standard error", LogLevel.Debug)]
    public void Logs_errors_and_warnings_of_git_as_such_and_progress_as_debug(ToolStream stream, string text, LogLevel level)
    {
        Assert.Equal(level, new GitLogger().GetLevel(new ToolOutputLine(stream, text)));
    }

    private async Task InitAsync()
    {
        await GitAsync("init", "--initial-branch", "release");
        await CommitAsync();
    }

    private Task CommitAsync() =>
        GitAsync("-c", "user.name=baun", "-c", "user.email=baun@example.com", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "--message", "Commit");

    private Task GitAsync(params string[] arguments) =>
        Git.RunAsync(arguments, repository, TestContext.Current.CancellationToken);
}
