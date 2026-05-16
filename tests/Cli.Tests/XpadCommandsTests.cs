using System.Linq;
namespace Cli.Tests;

/// <summary>Tests for xpad CLI helpers.</summary>
[TestClass]
public sealed class XpadCommandsTests
{
    /// <summary>Checks xpad command shape.</summary>
    [TestMethod]
    public void CreateXpadCommandIncludesExpectedCommands()
    {
        string[] names = [.. XpadCommands.CreateXpadCommand().Subcommands.Select(command => command.Name)];

        CollectionAssert.Contains(names, "probe");
        CollectionAssert.Contains(names, "input");
        CollectionAssert.Contains(names, "test");
        CollectionAssert.Contains(names, "run");
        CollectionAssert.Contains(names, "forward");
    }

    /// <summary>Checks xpad command option wiring.</summary>
    [TestMethod]
    public void CreateXpadCommandIncludesExpectedOptions()
    {
        System.CommandLine.Command xpad = XpadCommands.CreateXpadCommand();
        System.CommandLine.Command input = xpad.Subcommands.Single(command => command.Name == "input");
        System.CommandLine.Command test = xpad.Subcommands.Single(command => command.Name == "test");
        string[] inputOptionNames = [.. input.Options.Select(option => option.Name)];

        CollectionAssert.Contains(inputOptionNames, "--device-index");
        CollectionAssert.Contains(inputOptionNames, "--poll-ms");
        CollectionAssert.Contains(test.Options.Select(option => option.Name).ToArray(), "--duration-ms");
    }

    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", XpadCommands.DisplayButtons(GamepadButtons.None));
    }
}
