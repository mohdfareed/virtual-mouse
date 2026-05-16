namespace Inputs.Tests;

/// <summary>Tests for mouse input contracts.</summary>
[TestClass]
public sealed class MouseInputTests
{
    /// <summary>Checks input value storage.</summary>
    [TestMethod]
    public void MouseInputStoresReportAndDeviceName()
    {
        MouseReport report = new(MouseButtons.Left, 1, -2, 0);

        MouseInput input = new(report, "device");

        Assert.AreEqual(report, input.Report);
        Assert.AreEqual("device", input.DeviceName);
    }
}
