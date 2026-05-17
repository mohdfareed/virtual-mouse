using System.CommandLine;
using System.Linq;

namespace Cli.Tests;

/// <summary>Tests for client CLI helpers.</summary>
[TestClass]
public sealed class ClientCommandsTests
{
    /// <summary>Checks client command shape.</summary>
    [TestMethod]
    public void CreateClientCommandIncludesExpectedCommands()
    {
        string[] names = [.. ClientCommands.CreateClientCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "run");
        CollectionAssert.Contains(names, "emulation");
        CollectionAssert.Contains(names, "physical-motion");
    }

    /// <summary>Checks client route option wiring.</summary>
    [TestMethod]
    public void CreateClientCommandIncludesRouteOption()
    {
        Command client = ClientCommands.CreateClientCommand();
        Command run = client.Subcommands.Single(command => command.Name == "run");

        Assert.IsTrue(run.Options.Any(IsRouteOption));
    }

    /// <summary>Checks client run accepts a route-less session.</summary>
    [TestMethod]
    public void CreateClientCommandDoesNotRequireRouteOption()
    {
        Command client = ClientCommands.CreateClientCommand();
        Command run = client.Subcommands.Single(command => command.Name == "run");

        Assert.IsFalse(run.Options.Single(IsRouteOption).Required);
    }

    /// <summary>Checks host state command shape.</summary>
    [TestMethod]
    public void CreateClientCommandIncludesHostStateCommands()
    {
        Command client = ClientCommands.CreateClientCommand();
        Command emulation = client.Subcommands.Single(command => command.Name == "emulation");
        Command physicalMotion = client.Subcommands.Single(command => command.Name == "physical-motion");
        string[] emulationNames = [.. emulation.Subcommands.Select(command => command.Name)];
        string[] physicalMotionNames = [.. physicalMotion.Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(emulationNames, "enable");
        CollectionAssert.Contains(emulationNames, "disable");
        CollectionAssert.Contains(emulationNames, "toggle");
        CollectionAssert.Contains(physicalMotionNames, "enable");
        CollectionAssert.Contains(physicalMotionNames, "disable");
        CollectionAssert.Contains(physicalMotionNames, "toggle");
    }

    private static bool IsRouteOption(Option option)
    {
        return option.Name is "route" or "--route"
            || option.Aliases.Contains("route")
            || option.Aliases.Contains("--route");
    }
}
