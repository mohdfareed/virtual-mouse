using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace PhysicalMouse.Viiper;

public sealed partial class ViiperPhysicalMouse
{
    internal const ushort OwnedVendorId = 0x6969;
    internal const ushort OwnedProductId = 0x5050;
    internal const string OwnershipMutexName = @"Local\PhysicalMouse.Viiper";
    private const string OwnedDeviceNameFragment = "VID_6969&PID_5050";

    // MARK: Ownership
    // ========================================================================

    internal static bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, "mouse", StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(OwnedVendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(OwnedProductId), StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatUsbId(ushort value)
    {
        return $"0x{value:x4}";
    }

    internal static Mutex? TryAcquireOwnershipMutex(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Mutex mutex = new(initiallyOwned: false, name);

        try
        {
            return mutex.WaitOne(0) ? mutex : DisposeMutex(mutex);
        }
        catch (AbandonedMutexException)
        {
            return mutex;
        }
    }

    // MARK: Recovery
    // ========================================================================

    private static async Task<uint> ReclaimOwnedDevicesAsync(
        ViiperClient client,
        ILogger? logger,
        uint[] buses,
        CancellationToken cancellationToken)
    {
        if (buses.Length == 0)
        {
            BusCreateResponse created = await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false);
            if (logger is not null)
            {
                Log.CreatingDevice(logger, created.BusID, null);
            }

            return created.BusID;
        }

        foreach (uint busId in buses)
        {
            DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken).ConfigureAwait(false);
            foreach (Device device in devices.Devices)
            {
                if (!IsOwnedDevice(device))
                {
                    continue;
                }

                _ = await client.BusDeviceRemoveAsync(device.BusID, device.DevId, cancellationToken).ConfigureAwait(false);
                if (logger is not null)
                {
                    Log.RemovedDevice(logger, device.BusID, device.DevId, null);
                }
            }
        }

        return buses[0];
    }

    // MARK: Helpers
    // ========================================================================

    private static Mutex AcquireOwnershipMutexOrThrow()
    {
        return TryAcquireOwnershipMutex(OwnershipMutexName) ??
            throw new InvalidOperationException("Another PhysicalMouse VIIPER session is already active.");
    }

    private static void ReleaseOwnershipMutex(Mutex? mutex)
    {
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        finally
        {
            mutex.Dispose();
        }
    }

    private static Mutex? DisposeMutex(Mutex mutex)
    {
        mutex.Dispose();
        return null;
    }
}
