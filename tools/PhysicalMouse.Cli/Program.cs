using System;
using System.CommandLine;
using System.Threading.Tasks;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static Task<int> Main(string[] args)
    {
        RootCommand root = new("Physical mouse transport CLI.");

        if (OperatingSystem.IsWindows())
        {
            root.Subcommands.Add(CliInputCommands.CreateInputCommand());
            root.Subcommands.Add(CliSteamCommands.CreateSteamCommand());
            root.Subcommands.Add(CliSteamCommands.CreateNullifyCommand());
        }
        root.Subcommands.Add(CliTestCommands.CreateBenchCommand());

        return root.Parse(args).InvokeAsync();
    }
}
