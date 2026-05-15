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

    // MARK: Connection
    // ========================================================================

    /// <summary>
    /// Creates and connects a VIIPER mouse device.
    /// </summary>
    /// <param name="options">Connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected transport.</returns>
    public static async Task<ViiperPhysicalMouse> ConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Mutex? ownershipMutex = AcquireOwnershipMutexOrThrow();
        ViiperClient? client = new(options.Host, options.Port, options.Password);

        try
        {
            Device createdDevice = await CreateDeviceAsync(client, options.Logger, cancellationToken).ConfigureAwait(false);

            try
            {
                ViiperPhysicalMouse mouse = await ConnectDeviceAsync(
                    client,
                    createdDevice,
                    options.Logger,
                    ownershipMutex,
                    cancellationToken).ConfigureAwait(false);

                ownershipMutex = null;
                client = null;
                return mouse;
            }
            catch
            {
                if (client is null)
                {
                    throw;
                }

                _ = await client.BusDeviceRemoveAsync(
                    createdDevice.BusID,
                    createdDevice.DevId,
                    cancellationToken).ConfigureAwait(false);

                throw;
            }
        }
        catch
        {
            client?.Dispose();
            ReleaseOwnershipMutex(ownershipMutex);
            throw;
        }
    }

    // MARK: Ownership
    // ========================================================================

    internal static bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, "mouse", StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(OwnedVendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(OwnedProductId), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOwnedDeviceName(string? deviceName)
    {
        return deviceName?.Contains(OwnedDeviceNameFragment, StringComparison.OrdinalIgnoreCase) == true;
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

    private static async Task<Device> CreateDeviceAsync(
        ViiperClient client,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);
        uint resolvedBusId = await ReclaimOwnedDevicesAsync(
            client,
            logger,
            buses.Buses,
            cancellationToken).ConfigureAwait(false);

        return await client.BusDeviceAddAsync(
            resolvedBusId,
            new DeviceCreateRequest
            {
                Type = "mouse",
                IdVendor = OwnedVendorId,
                IdProduct = OwnedProductId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ViiperPhysicalMouse> ConnectDeviceAsync(
        ViiperClient client,
        Device createdDevice,
        ILogger? logger,
        Mutex ownershipMutex,
        CancellationToken cancellationToken)
    {
        ViiperDevice device = await client.ConnectDeviceAsync(
            createdDevice.BusID,
            createdDevice.DevId,
            cancellationToken).ConfigureAwait(false);

        if (logger is not null)
        {
            Log.ConnectedKnownDevice(logger, createdDevice.BusID, createdDevice.DevId, null);
        }

        return new ViiperPhysicalMouse(
            client,
            device,
            createdDevice.BusID,
            createdDevice.DevId,
            logger,
            ownershipMutex);
    }

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
