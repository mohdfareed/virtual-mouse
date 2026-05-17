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

    /// <summary>Checks mixed mode motion selection prefers a matching physical device.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexPrefersMatchingPhysicalDeviceName()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);
        SdlGamepadInfo otherPhysical = new(0, 1, "DualSense", 0, 0x054c, 0x0df2, null);
        SdlGamepadInfo matchingPhysical = new(2, 3, "steam controller", 0, 0x28de, 0x1304, null);

        int index = SdlGamepadSource.ResolveMotionDeviceIndex(
            [otherPhysical, primary, matchingPhysical],
            primary,
            new SdlGamepadOptions
            {
                Mode = SdlGamepadInputMode.Steam,
                UsePhysicalMotion = true,
            });

        Assert.AreEqual(2, index);
    }

    /// <summary>Checks mixed mode motion selection falls back to the first physical device.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexFallsBackToFirstPhysicalDevice()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);
        SdlGamepadInfo physical = new(0, 1, "DualSense", 0, 0x054c, 0x0df2, null);

        int index = SdlGamepadSource.ResolveMotionDeviceIndex(
            [physical, primary],
            primary,
            new SdlGamepadOptions
            {
                Mode = SdlGamepadInputMode.Steam,
                UsePhysicalMotion = true,
            });

        Assert.AreEqual(0, index);
    }

    /// <summary>Checks explicit mixed mode motion index wins over automatic selection.</summary>
    [TestMethod]
    public void ResolveMotionDeviceIndexUsesExplicitIndex()
    {
        SdlGamepadInfo primary = new(1, 2, "Steam Controller", 0x1234, 0x045e, 0x028e, null);

        int index = SdlGamepadSource.ResolveMotionDeviceIndex(
            [primary],
            primary,
            new SdlGamepadOptions
            {
                Mode = SdlGamepadInputMode.Steam,
                UsePhysicalMotion = true,
                MotionDeviceIndex = 5,
            });

        Assert.AreEqual(5, index);
    }

    /// <summary>Checks motion events come from the configured motion source.</summary>
    [TestMethod]
    public void IsMotionEventUsesConfiguredMotionInstance()
    {
        Assert.IsTrue(SdlGamepadSource.IsMotionEvent(1, primaryInstanceId: 1, motionInstanceId: null));
        Assert.IsFalse(SdlGamepadSource.IsMotionEvent(2, primaryInstanceId: 1, motionInstanceId: null));
        Assert.IsTrue(SdlGamepadSource.IsMotionEvent(2, primaryInstanceId: 1, motionInstanceId: 2));
        Assert.IsFalse(SdlGamepadSource.IsMotionEvent(1, primaryInstanceId: 1, motionInstanceId: 2));
    }

    /// <summary>Checks motion construction uses sensor capability flags.</summary>
    [TestMethod]
    public void CreateMotionUsesAvailableSensors()
    {
        GamepadMotion motion = SdlGamepadSource.CreateMotion(
            hasGyro: true,
            [1, 2, 3],
            hasAccelerometer: false,
            [4, 5, 6]);

        Assert.IsTrue(motion.HasGyro);
        Assert.AreEqual(1, motion.GyroX);
        Assert.AreEqual(2, motion.GyroY);
        Assert.AreEqual(3, motion.GyroZ);
        Assert.IsFalse(motion.HasAccelerometer);
        Assert.AreEqual(0, motion.AccelX);
        Assert.AreEqual(0, motion.AccelY);
        Assert.AreEqual(0, motion.AccelZ);
    }
}
