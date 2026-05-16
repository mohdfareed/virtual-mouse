using Inputs.Sdl;

namespace Inputs.Tests;

/// <summary>Tests for SDL gamepad helpers.</summary>
[TestClass]
public sealed class SdlGamepadSourceTests
{
    /// <summary>Checks SDL trigger conversion clamps negative values.</summary>
    [TestMethod]
    public void ToTriggerClampsNegativeValuesToZero()
    {
        Assert.AreEqual((ushort)0, SdlGamepadSource.ToTrigger(-1));
        Assert.AreEqual((ushort)32767, SdlGamepadSource.ToTrigger(32767));
    }
}
