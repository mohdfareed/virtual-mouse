using System.Linq;

namespace Cli.Tests;

#pragma warning disable CA1416

/// <summary>Tests for host CLI helpers.</summary>
[TestClass]
public sealed class HostCommandsTests
{
    /// <summary>Checks CLI host command shape.</summary>
    [TestMethod]
    public void CreateHostCommandIncludesControlCommands()
    {
        string[] names = [.. HostCommands.CreateHostCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "run");
        CollectionAssert.Contains(names, "enable");
        CollectionAssert.Contains(names, "status");
    }

    /// <summary>Checks route options.</summary>
    [TestMethod]
    public void CreateHostCommandIncludesRouteOptions()
    {
        System.CommandLine.Command host = HostCommands.CreateHostCommand();
        System.CommandLine.Command run = host.Subcommands.Single(command => command.Name == "run");
        System.CommandLine.Command enable = host.Subcommands.Single(command => command.Name == "enable");
        System.CommandLine.Command status = host.Subcommands.Single(command => command.Name == "status");

        CollectionAssert.Contains(run.Options.Select(option => option.Name).ToArray(), "--route");
        CollectionAssert.Contains(enable.Options.Select(option => option.Name).ToArray(), "--route");
        CollectionAssert.Contains(status.Options.Select(option => option.Name).ToArray(), "--route");
    }

    /// <summary>Checks host run xpad input options.</summary>
    [TestMethod]
    public void CreateHostCommandRunIncludesSdlOptions()
    {
        System.CommandLine.Command host = HostCommands.CreateHostCommand();
        System.CommandLine.Command run = host.Subcommands.Single(command => command.Name == "run");
        string[] names = [.. run.Options.Select(option => option.Name)];

        CollectionAssert.Contains(names, "--device-index");
        CollectionAssert.Contains(names, "--poll-ms");
    }
}

#pragma warning restore CA1416
