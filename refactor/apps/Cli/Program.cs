using System.CommandLine;
using VirtualMouse.Cli;

RootCommand root = new("Virtual Mouse");
root.Subcommands.Add(Commands.CreateServerCommand());
root.Subcommands.Add(Commands.CreateClientCommand());
root.Subcommands.Add(SteamCommands.Create());

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
