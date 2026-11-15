using Baun;
using Baun.Cli;

return await CliApp.RunAsync(args, Directory.GetCurrentDirectory(), BaunConsole.Create(), ProcessRunner.RunAsync);
