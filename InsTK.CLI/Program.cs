using InsTK.Core;

var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();

if (string.IsNullOrWhiteSpace(command) || command is "help" or "--help" or "-h")
{
    Console.WriteLine(HelpText.Build());
    return 0;
}

var request = CommandRequestParser.Parse(command, args.Skip(1).ToArray());
var host = new ConsoleCommandHost();
var app = InsTkApplication.CreateDefault();

try
{
    return await app.ExecuteAsync(request, host);
}
catch (Exception ex)
{
    host.Error.WriteLine(ex.Message);
    return 1;
}

internal sealed class ConsoleCommandHost : ICommandHost
{
    public TextWriter Out => Console.Out;
    public TextWriter Error => Console.Error;
    public TextReader In => Console.In;
}
