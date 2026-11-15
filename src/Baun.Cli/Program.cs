using Baun;
using Baun.Cli;

return await CliApp.RunAsync(args, (AbsolutePath)Directory.GetCurrentDirectory(), BaunConsole.Create(), ProcessRunner.RunAsync);
