using System;
using System.Threading.Tasks;
using Outputs.Viiper;
using ViiperDeviceInfo = global::Viiper.Client.Types.Device;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace Outputs.Tests;

/// <summary>Tests for <see cref="ViiperMouseOutput" />.</summary>
[TestClass]
public sealed class ViiperMouseOutputTests
{
    /// <summary>Checks direct field mapping.</summary>
    [TestMethod]
    public void MapReportPreservesSupportedFields()
    {
        MouseReport report = new(
            MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward,
            123,
            -456,
            7);

        ViiperMouseInput input = ViiperMouseOutput.MapReport(report);

        Assert.AreEqual((byte)(MouseButtons.Left | MouseButtons.Right | MouseButtons.Forward), input.Buttons);
        Assert.AreEqual((short)123, input.Dx);
        Assert.AreEqual((short)-456, input.Dy);
        Assert.AreEqual((short)7, input.Wheel);
        Assert.AreEqual((short)0, input.Pan);
    }

    /// <summary>Checks overflow behavior.</summary>
    [TestMethod]
    public void MapReportThrowsWhenDeltaXOverflowsViiperRange()
    {
        MouseReport report = new(MouseButtons.None, short.MaxValue + 1, 0, 0);

        try
        {
            _ = ViiperMouseOutput.MapReport(report);
            Assert.Fail("Expected OverflowException.");
        }
        catch (OverflowException)
        {
        }
    }

    /// <summary>Checks constructor argument validation.</summary>
    [TestMethod]
    public void ConstructorThrowsWhenDeviceIsNull()
    {
        try
        {
#pragma warning disable CA2000
            _ = new ViiperMouseOutput(null!);
#pragma warning restore CA2000
            Assert.Fail("Expected ArgumentNullException.");
        }
        catch (ArgumentNullException)
        {
        }
    }

    /// <summary>Checks owned device detection.</summary>
    [TestMethod]
    public void IsOwnedDeviceReturnsTrueForOwnedMouse()
    {
        ViiperDeviceInfo device = new()
        {
            BusID = 1,
            DeviceSpecific = [],
            DevId = "1",
            Pid = ViiperMouseOutput.FormatUsbId(ViiperMouseOutput.OwnedProductId),
            Type = "mouse",
            Vid = ViiperMouseOutput.FormatUsbId(ViiperMouseOutput.OwnedVendorId),
        };

        Assert.IsTrue(ViiperMouseOutput.IsOwnedDevice(device));
    }

    /// <summary>Checks foreign device detection.</summary>
    [TestMethod]
    public void IsOwnedDeviceReturnsFalseForForeignMouse()
    {
        ViiperDeviceInfo device = new()
        {
            BusID = 1,
            DeviceSpecific = [],
            DevId = "1",
            Pid = "0x0001",
            Type = "mouse",
            Vid = ViiperMouseOutput.FormatUsbId(ViiperMouseOutput.OwnedVendorId),
        };

        Assert.IsFalse(ViiperMouseOutput.IsOwnedDevice(device));
    }

    /// <summary>Checks source identity matching for the VIIPER-owned output device.</summary>
    [TestMethod]
    public void IsOwnedDeviceNameReturnsTrueForOwnedDeviceName()
    {
        const string Owned = @"\\?\HID#VID_6969&PID_5050#1";
        const string Foreign = @"\\?\HID#VID_0001&PID_5050#1";

        Assert.IsTrue(ViiperMouseOutput.IsOwnedDeviceName(Owned));
        Assert.IsFalse(ViiperMouseOutput.IsOwnedDeviceName(Foreign));
    }

    /// <summary>Checks single-owner behavior.</summary>
    [TestMethod]
    public void TryAcquireOwnershipReturnsNullWhenAlreadyOwned()
    {
        string ownershipName = $@"Local\Outputs.Viiper.Mouse.Tests.{Guid.NewGuid():N}";
        ViiperOutputOwnership? first = ViiperMouseOutput.TryAcquireOwnership(ownershipName);
        Assert.IsNotNull(first);

        Task<bool> secondAcquireTask = Task.Run(() =>
        {
            using ViiperOutputOwnership? second = ViiperMouseOutput.TryAcquireOwnership(ownershipName);
            return second is not null;
        });

        bool secondAcquired;
        try
        {
            secondAcquired = secondAcquireTask.GetAwaiter().GetResult();
        }
        finally
        {
            first.Dispose();
        }

        Assert.IsFalse(secondAcquired);
    }

    /// <summary>Checks same-thread ownership rejection.</summary>
    [TestMethod]
    public void TryAcquireOwnershipReturnsNullWhenAlreadyOwnedOnSameThread()
    {
        string ownershipName = $@"Local\Outputs.Viiper.Mouse.Tests.{Guid.NewGuid():N}";
        using ViiperOutputOwnership? first = ViiperMouseOutput.TryAcquireOwnership(ownershipName);
        using ViiperOutputOwnership? second = ViiperMouseOutput.TryAcquireOwnership(ownershipName);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
    }

    /// <summary>Checks ownership can be released from another thread.</summary>
    [TestMethod]
    public void OwnershipCanReleaseFromAnotherThread()
    {
        string ownershipName = $@"Local\Outputs.Viiper.Mouse.Tests.{Guid.NewGuid():N}";
        ViiperOutputOwnership? first = ViiperMouseOutput.TryAcquireOwnership(ownershipName);
        Assert.IsNotNull(first);

        Task releaseTask = Task.Run(first.Dispose);
        releaseTask.GetAwaiter().GetResult();

        using ViiperOutputOwnership? second = ViiperMouseOutput.TryAcquireOwnership(ownershipName);
        Assert.IsNotNull(second);
    }
}
