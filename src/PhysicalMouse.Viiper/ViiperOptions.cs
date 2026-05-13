using Microsoft.Extensions.Logging;

namespace PhysicalMouse.Viiper;

/// <summary>VIIPER connection options.</summary>
public sealed class ViiperOptions
{
    /// <summary>Host name or IP address.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    public int Port { get; init; } = 3242;

    /// <summary>Server password.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Bus to use, if known.</summary>
    public uint? BusId { get; init; }

    /// <summary>Device to use, if known.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Gets whether a created device should be removed on dispose.</summary>
    public bool RemoveCreatedDeviceOnDispose { get; init; }

    /// <summary>Logger for transport lifecycle events.</summary>
    public ILogger? Logger { get; init; }
}
