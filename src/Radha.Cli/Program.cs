using Radha.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var exitCode = await CliApp.RunAsync(args, Console.Out, Console.Error);
return exitCode;
