using System;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse.Viiper;
using ViiperDeviceInfo = global::Viiper.Client.Types.Device;
using ViiperMouseInput = global::Viiper.Client.Devices.Mouse.MouseInput;

namespace PhysicalMouse.Tests;

/// <summary>Tests for <see cref="ViiperPhysicalMouse" />.</summary>
[TestClass]
public sealed class ViiperPhysicalMouseTests
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

        ViiperMouseInput input = ViiperPhysicalMouse.MapReport(report);

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
            _ = ViiperPhysicalMouse.MapReport(report);
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
            _ = new ViiperPhysicalMouse(null!);
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
            Pid = ViiperPhysicalMouse.FormatUsbId(ViiperPhysicalMouse.OwnedProductId),
            Type = "mouse",
            Vid = ViiperPhysicalMouse.FormatUsbId(ViiperPhysicalMouse.OwnedVendorId),
        };

        Assert.IsTrue(ViiperPhysicalMouse.IsOwnedDevice(device));
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
            Vid = ViiperPhysicalMouse.FormatUsbId(ViiperPhysicalMouse.OwnedVendorId),
        };

        Assert.IsFalse(ViiperPhysicalMouse.IsOwnedDevice(device));
    }

    /// <summary>Checks single-owner mutex behavior.</summary>
    [TestMethod]
    public void TryAcquireOwnershipMutexReturnsNullWhenAlreadyOwned()
    {
        string mutexName = $@"Local\PhysicalMouse.Viiper.Tests.{Guid.NewGuid():N}";
        Mutex? first = ViiperPhysicalMouse.TryAcquireOwnershipMutex(mutexName);
        Assert.IsNotNull(first);

        Task<bool> secondAcquireTask = Task.Run(() =>
        {
            Mutex? second = ViiperPhysicalMouse.TryAcquireOwnershipMutex(mutexName);
            if (second is null)
            {
                return false;
            }

            second.ReleaseMutex();
            second.Dispose();
            return true;
        });

        bool secondAcquired;
        try
        {
            secondAcquired = secondAcquireTask.GetAwaiter().GetResult();
        }
        finally
        {
            first.ReleaseMutex();
            first.Dispose();
        }

        Assert.IsFalse(secondAcquired);
    }

}
