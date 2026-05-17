using System.CommandLine;
using Inputs.Sdl;

namespace Cli.Tests;

/// <summary>Tests for shared CLI option helpers.</summary>
[TestClass]
public sealed class CliOptionsTests
{
    /// <summary>Checks positive integer option validation.</summary>
    [TestMethod]
    public void CountOptionRejectsZero()
    {
        Command command = new("test");
        Option<int?> option = CliOptions.CreateCountOption(100);
        command.Options.Add(option);

        ParseResult result = command.Parse("--count 0");

        Assert.AreNotEqual(0, result.Errors.Count);
    }

    /// <summary>Checks app id argument validation.</summary>
    [TestMethod]
    public void AppIdArgumentRejectsZero()
    {
        Command command = new("test");
        Argument<uint> argument = CliOptions.CreateAppIdArgument();
        command.Arguments.Add(argument);

        ParseResult result = command.Parse("0");

        Assert.AreNotEqual(0, result.Errors.Count);
    }

    /// <summary>Checks SDL option projection.</summary>
    [TestMethod]
    public void CreateSdlGamepadOptionsReadsValues()
    {
        Command command = new("test");
        Option<int?> deviceIndexOption = CliOptions.CreateDeviceIndexOption("--device-index", "device");
        Option<SdlGamepadInputMode?> modeOption = CliOptions.CreateSdlGamepadModeOption("--mode", "mode");
        Option<bool> physicalMotionOption = CliOptions.CreateSdlPhysicalMotionOption("--physical-motion", "physical motion");
        Option<int?> motionDeviceIndexOption = CliOptions.CreateDeviceIndexOption("--motion-device-index", "motion");
        command.Options.Add(deviceIndexOption);
        command.Options.Add(modeOption);
        command.Options.Add(physicalMotionOption);
        command.Options.Add(motionDeviceIndexOption);

        ParseResult result = command.Parse(
            "--device-index 2 --mode steam --physical-motion --motion-device-index 3");
        SdlGamepadOptions options = CliOptions.CreateSdlGamepadOptions(
            result,
            deviceIndexOption,
            modeOption,
            physicalMotionOption,
            motionDeviceIndexOption);

        Assert.AreEqual(2, options.DeviceIndex);
        Assert.AreEqual(SdlGamepadInputMode.Steam, options.Mode);
        Assert.IsTrue(options.UsePhysicalMotion);
        Assert.AreEqual(3, options.MotionDeviceIndex);
    }
}
