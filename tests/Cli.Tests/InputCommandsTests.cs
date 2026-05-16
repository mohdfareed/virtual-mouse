namespace Cli.Tests;

/// <summary>Tests for input CLI helpers.</summary>
[TestClass]
public sealed class InputCommandsTests
{
    /// <summary>Checks empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNoneForEmptyButtons()
    {
        Assert.AreEqual("none", InputCommands.DisplayButtons(MouseButtons.None));
    }

    /// <summary>Checks non-empty button formatting.</summary>
    [TestMethod]
    public void DisplayButtonsReturnsNamedButtons()
    {
        Assert.AreEqual(
            "Left, Right",
            InputCommands.DisplayButtons(MouseButtons.Left | MouseButtons.Right));
    }
}
