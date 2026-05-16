using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace Outputs.Viiper;

internal static class ViiperOutputConnector
{
    public static async Task<TOutput> ConnectAsync<TOutput>(
        ViiperOptions options,
        ViiperOutputDeviceDefinition definition,
        string ownershipErrorMessage,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createOutput);

        ViiperOutputOwnership? ownership = ViiperOutputOwnership.AcquireOrThrow(
            definition.OwnershipName,
            ownershipErrorMessage);
        ViiperClient? client = new(options.Host, options.Port, options.Password);

        try
        {
            Device createdDevice = await CreateDeviceAsync(
                client,
                definition,
                options.Logger,
                cancellationToken).ConfigureAwait(false);

            try
            {
                TOutput output = await ConnectDeviceAsync(
                    client,
                    createdDevice,
                    ownership,
                    definition,
                    options.Logger,
                    createOutput,
                    cancellationToken).ConfigureAwait(false);

                ownership = null;
                client = null;
                return output;
            }
            catch
            {
                if (client is not null)
                {
                    _ = await client.BusDeviceRemoveAsync(
                        createdDevice.BusID,
                        createdDevice.DevId,
                        cancellationToken).ConfigureAwait(false);
                }

                throw;
            }
        }
        catch
        {
            client?.Dispose();
            ownership?.Dispose();
            throw;
        }
    }

    private static async Task<Device> CreateDeviceAsync(
        ViiperClient client,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        BusListResponse buses = await client.BusListAsync(cancellationToken).ConfigureAwait(false);
        uint resolvedBusId = await ReclaimOwnedDevicesAsync(
            client,
            definition,
            logger,
            buses.Buses,
            cancellationToken).ConfigureAwait(false);

        Device device = await client.BusDeviceAddAsync(
            resolvedBusId,
            new DeviceCreateRequest
            {
                Type = definition.DeviceType,
                IdVendor = definition.VendorId,
                IdProduct = definition.ProductId,
            },
            cancellationToken).ConfigureAwait(false);

        ViiperOutputLog.CreatedDevice(logger, definition.DisplayName, device.BusID);
        return device;
    }

    private static async Task<uint> ReclaimOwnedDevicesAsync(
        ViiperClient client,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        uint[] buses,
        CancellationToken cancellationToken)
    {
        if (buses.Length == 0)
        {
            return (await client.BusCreateAsync(null, cancellationToken).ConfigureAwait(false)).BusID;
        }

        foreach (uint busId in buses)
        {
            DevicesListResponse devices = await client.BusDevicesListAsync(busId, cancellationToken).ConfigureAwait(false);
            foreach (Device device in devices.Devices)
            {
                if (!definition.IsOwnedDevice(device))
                {
                    continue;
                }

                _ = await client.BusDeviceRemoveAsync(device.BusID, device.DevId, cancellationToken).ConfigureAwait(false);
                ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, device.BusID, device.DevId);
            }
        }

        return buses[0];
    }

    private static async Task<TOutput> ConnectDeviceAsync<TOutput>(
        ViiperClient client,
        Device createdDevice,
        ViiperOutputOwnership ownership,
        ViiperOutputDeviceDefinition definition,
        ILogger? logger,
        Func<ViiperOutputSession, TOutput> createOutput,
        CancellationToken cancellationToken)
    {
        ViiperDevice device = await client.ConnectDeviceAsync(
            createdDevice.BusID,
            createdDevice.DevId,
            cancellationToken).ConfigureAwait(false);

        ViiperOutputLog.ConnectedDevice(logger, definition.DisplayName, createdDevice.BusID, createdDevice.DevId);

#pragma warning disable CA2000 // Ownership transfers to the output instance created below.
        ViiperOutputSession session = new(
            client,
            device,
            createdDevice.BusID,
            createdDevice.DevId,
            ownership,
            (busId, deviceId) => ViiperOutputLog.RemovedDevice(logger, definition.DisplayName, busId, deviceId),
            (busId, deviceId) => ViiperOutputLog.DisconnectedDevice(logger, definition.DisplayName, busId, deviceId));
#pragma warning restore CA2000

        return createOutput(session);
    }
}
