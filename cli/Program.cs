using System;
using System.CommandLine;
using System.Threading.Tasks;

internal static class Program
{
    // MARK: Entry
    // ========================================================================

    private static Task<int> Main(string[] args)
    {
        RootCommand root = new("Local input forwarding CLI.");

        if (OperatingSystem.IsWindows())
        {
            root.Subcommands.Add(HostCommands.CreateHostCommand());
            root.Subcommands.Add(InputCommands.CreateInputCommand());
            root.Subcommands.Add(MouseCommands.CreateMouseCommand());
            root.Subcommands.Add(SteamCommands.CreateSteamCommand());
            root.Subcommands.Add(MouseCommands.CreateLegacyNullifyCommand());
        }
        root.Subcommands.Add(BenchCommands.CreateBenchCommand());
        root.Subcommands.Add(XpadCommands.CreateXpadCommand());

        return root.Parse(args).InvokeAsync();
    }
}
