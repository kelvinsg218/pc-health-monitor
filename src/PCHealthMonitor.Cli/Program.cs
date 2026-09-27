using System.Text;
using PCHealthMonitor.Cli;

Console.OutputEncoding = Encoding.UTF8;
return await CliApplication.RunAsync(args);
