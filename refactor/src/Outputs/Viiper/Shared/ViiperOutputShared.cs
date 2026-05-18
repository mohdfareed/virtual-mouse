using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;
using global::Viiper.Client.Types;
using Microsoft.Extensions.Logging;

namespace VirtualMouse.Outputs.Viiper;

internal sealed record ViiperOutputDeviceDefinition(
    string DeviceType,
    ushort VendorId,
    ushort ProductId,
    string OwnershipName,
    string DisplayName)
{
    public bool IsOwnedDevice(Device device)
    {
        return string.Equals(device.Type, DeviceType, StringComparison.Ordinal) &&
            string.Equals(device.Vid, FormatUsbId(VendorId), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.Pid, FormatUsbId(ProductId), StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatUsbId(ushort value)
    {
        return $"0x{value.ToString("x4", CultureInfo.InvariantCulture)}";
    }
}

internal static class ViiperOutputLog
{
    private static readonly Action<ILogger, string, uint, Exception?> CreatedDeviceMessage =
        LoggerMessage.Define<string, uint>(
            LogLevel.Information,
            new EventId(1, nameof(CreatedDevice)),
            "Created VIIPER {Name} device on bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> ConnectedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Information,
            new EventId(2, nameof(ConnectedDevice)),
            "Connected VIIPER {Name} device {DeviceId} on bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> RemovedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Information,
            new EventId(3, nameof(RemovedDevice)),
            "Removed VIIPER {Name} device {DeviceId} from bus {BusId}.");

    private static readonly Action<ILogger, string, string, uint, Exception?> DisconnectedDeviceMessage =
        LoggerMessage.Define<string, string, uint>(
            LogLevel.Warning,
            new EventId(4, nameof(DisconnectedDevice)),
            "VIIPER {Name} device {DeviceId} disconnected from bus {BusId}.");

    public static void CreatedDevice(ILogger? logger, string name, uint busId)
    {
        if (logger is not null)
        {
            CreatedDeviceMessage(logger, name, busId, null);
        }
    }

    public static void ConnectedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            ConnectedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }

    public static void RemovedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            RemovedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }

    public static void DisconnectedDevice(ILogger? logger, string name, uint busId, string deviceId)
    {
        if (logger is not null)
        {
            DisconnectedDeviceMessage(logger, name, deviceId, busId, null);
        }
    }
}

internal sealed class ViiperOutputDevice : IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly Action<uint, string>? _removed;
    private readonly Action<uint, string>? _disconnected;
    private ViiperDevice? _device;
    private int _isConnected;

    public ViiperOutputDevice(
        ViiperClient client,
        ViiperDevice device,
        uint busId,
        string deviceId,
        Action<uint, string>? removed = null,
        Action<uint, string>? disconnected = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _removed = removed;
        _disconnected = disconnected;
        _isConnected = 1;
        BusId = busId;
        DeviceId = deviceId;
        HookDisconnect(device);
    }

    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    public uint BusId { get; }

    public string DeviceId { get; }

    public ViiperDevice GetDeviceOrThrow(string message)
    {
        ViiperDevice? device = _device;
        return !IsConnected || device is null
            ? throw new InvalidOperationException(message)
            : device;
    }

    public IDisposable ListenOutput(Func<Stream, Task> handler, string message)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ViiperDevice device = GetDeviceOrThrow(message);
        if (device.OnOutput is not null)
        {
            throw new InvalidOperationException("VIIPER output feedback is already being handled.");
        }

        device.OnOutput = handler;
        return new OutputSubscription(() =>
        {
            if (ReferenceEquals(device.OnOutput, handler))
            {
                device.OnOutput = null;
            }
        });
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        _ = Interlocked.Exchange(ref _isConnected, 0);
        ViiperDevice? device = Interlocked.Exchange(ref _device, null);
        if (device is null)
        {
            return;
        }

        try
        {
            try
            {
                _ = await _client!
                    .BusDeviceRemoveAsync(BusId, DeviceId, CancellationToken.None)
                    .ConfigureAwait(false);
                _removed?.Invoke(BusId, DeviceId);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                await RemoveBusAsync().ConfigureAwait(false);
            }

            await DisposeDeviceStreamAsync(device).ConfigureAwait(false);
        }
        finally
        {
            _client?.Dispose();
        }
    }

    private async Task RemoveBusAsync()
    {
        try
        {
            _ = await _client!.BusRemoveAsync(BusId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task DisposeDeviceStreamAsync(ViiperDevice device)
    {
        try
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            _disconnected?.Invoke(BusId, DeviceId);
            onDisconnect?.Invoke();
        };
    }

    private sealed class OutputSubscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
