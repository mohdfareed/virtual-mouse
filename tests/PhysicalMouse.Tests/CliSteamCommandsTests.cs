using VirtualMouse;

namespace PhysicalMouse.Tests;

/// <summary>Tests for Steam CLI helpers.</summary>
[TestClass]
public sealed class CliSteamCommandsTests
{
    /// <summary>Checks movement inversion.</summary>
    [TestMethod]
    public void NullifyFlipsMovementOnly()
    {
        MouseReport report = new(MouseButtons.Left | MouseButtons.Right, 12, -34, 5);

        MouseReport inverted = CliSteamCommands.Nullify(report);

        Assert.AreEqual(MouseButtons.None, inverted.Buttons);
        Assert.AreEqual(-12, inverted.DeltaX);
        Assert.AreEqual(34, inverted.DeltaY);
        Assert.AreEqual(0, inverted.WheelDelta);
    }

    /// <summary>Checks forward mode.</summary>
    [TestMethod]
    public void ApplyModePreservesReportInForwardMode()
    {
        MouseReport report = new(MouseButtons.Left, 12, -34, 5);

        MouseReport forwarded = CliSteamCommands.ApplyMode(report, SteamMouseMode.Forward);

        Assert.AreEqual(report, forwarded);
    }

    /// <summary>Checks owned VIIPER device filtering.</summary>
    [TestMethod]
    public void TryCreateOutputSkipsOwnedViiperDevice()
    {
        MouseReport report = new(MouseButtons.None, 1, 0, 0);
        VirtualMouseInput input = new(report, @"\\?\HID#VID_6969&PID_5050#1");

        bool hasOutput = CliSteamCommands.TryCreateOutput(
            in input,
            SteamMouseMode.Forward,
            out MouseReport output);

        Assert.IsFalse(hasOutput);
        Assert.AreEqual(MouseReport.Empty, output);
    }

    /// <summary>Checks empty input filtering.</summary>
    [TestMethod]
    public void TryCreateOutputSkipsEmptyReport()
    {
        VirtualMouseInput input = new(MouseReport.Empty, string.Empty);

        bool hasOutput = CliSteamCommands.TryCreateOutput(
            in input,
            SteamMouseMode.Forward,
            out MouseReport output);

        Assert.IsFalse(hasOutput);
        Assert.AreEqual(MouseReport.Empty, output);
    }
}
