using System;
using System.Threading;
using System.Threading.Tasks;
using global::Viiper.Client;

namespace Outputs.Viiper;

internal sealed class ViiperOutputSession : IDisposable, IAsyncDisposable
{
    private readonly ViiperClient? _client;
    private readonly ViiperOutputOwnership? _ownership;
    private readonly Action<uint, string>? _removed;
    private readonly Action<uint, string>? _disconnected;
    private ViiperDevice? _device;
    private int _isConnected;

    public ViiperOutputSession(
        ViiperDevice device,
        Action<uint, string>? disconnected = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _disconnected = disconnected;
        _isConnected = 1;
        HookDisconnect(device);
    }

    public ViiperOutputSession(
        ViiperClient client,
        ViiperDevice device,
        uint busId,
        string deviceId,
        ViiperOutputOwnership ownership,
        Action<uint, string>? removed = null,
        Action<uint, string>? disconnected = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _removed = removed;
        _disconnected = disconnected;
        _isConnected = 1;
        BusId = busId;
        DeviceId = deviceId;

        HookDisconnect(device);
    }

    public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

    public uint? BusId { get; }

    public string? DeviceId { get; }

    public ViiperDevice GetDeviceOrThrow(string message)
    {
        ViiperDevice? device = _device;
        return !IsConnected || device is null
            ? throw new InvalidOperationException(message)
            : device;
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
            if (_client is not null && BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                _ = await _client
                    .BusDeviceRemoveAsync(BusId.Value, DeviceId, CancellationToken.None)
                    .ConfigureAwait(false);
                _removed?.Invoke(BusId.Value, DeviceId);
            }
        }
        finally
        {
            await device.DisposeAsync().ConfigureAwait(false);
            _client?.Dispose();
            _ownership?.Dispose();
        }
    }

    private void HookDisconnect(ViiperDevice device)
    {
        Action? onDisconnect = device.OnDisconnect;
        device.OnDisconnect = () =>
        {
            _ = Interlocked.Exchange(ref _isConnected, 0);
            if (BusId.HasValue && !string.IsNullOrWhiteSpace(DeviceId))
            {
                _disconnected?.Invoke(BusId.Value, DeviceId);
            }

            onDisconnect?.Invoke();
        };
    }
}
