using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace PhysicalMouse.Viiper;

public sealed partial class ViiperPhysicalMouse
{
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
}
