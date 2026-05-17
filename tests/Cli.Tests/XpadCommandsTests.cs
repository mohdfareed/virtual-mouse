using System.Linq;
namespace Cli.Tests;

/// <summary>Tests for xpad CLI helpers.</summary>
[TestClass]
public sealed class XpadCommandsTests
{
    /// <summary>Checks xpad command shape.</summary>
    [TestMethod]
    public void XpadHelperCommandsIncludeExpectedOptions()
    {
        System.CommandLine.Command input = XpadCommands.CreateInputCommand();
        System.CommandLine.Command press = XpadCommands.CreatePressCommand();
        string[] inputOptionNames = [.. input.Options.Select(option => option.Name)];

        CollectionAssert.Contains(inputOptionNames, "--device-index");
        CollectionAssert.Contains(inputOptionNames, "--mode");
        CollectionAssert.Contains(inputOptionNames, "--physical-motion");
        CollectionAssert.Contains(inputOptionNames, "--motion-device-index");
        CollectionAssert.Contains(inputOptionNames, "--wait-ms");
        CollectionAssert.Contains(inputOptionNames, "--pause");
        string[] probeOptionNames = [.. XpadCommands.CreateProbeCommand().Options.Select(option => option.Name)];
        CollectionAssert.Contains(probeOptionNames, "--wait-ms");
        CollectionAssert.Contains(probeOptionNames, "--pause");
        CollectionAssert.Contains(press.Options.Select(option => option.Name).ToArray(), "--duration-ms");
    }

    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", XpadCommands.DisplayButtons(GamepadButtons.None));
    }

    /// <summary>Checks motion formatting.</summary>
    [TestMethod]
    public void DisplayMotionShowsGyroAndAccelerometer()
    {
        GamepadMotion motion = new(true, 1.25f, -2.5f, 3, true, 4, 5.125f, -6);

        Assert.AreEqual(
            "gyro=1.25,-2.5,3 accel=4,5.125,-6",
            XpadCommands.DisplayMotion(motion));
    }
}
