using PhysicalMouse;

namespace VirtualMouse.Tests;

/// <summary>Tests for virtual mouse contracts.</summary>
[TestClass]
public sealed class VirtualMouseTests
{
    /// <summary>Checks input value storage.</summary>
    [TestMethod]
    public void VirtualMouseInputStoresReportAndDeviceName()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);

        VirtualMouseInput input = new(report, "device");

        Assert.AreEqual(report, input.Report);
        Assert.AreEqual("device", input.DeviceName);
    }
}
