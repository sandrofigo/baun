using Baun;
using Baun.Cli;

return await CliApp.RunAsync(args, AbsolutePath.CurrentDirectory, BaunConsole.Create(), ProcessRunner.RunAsync);
