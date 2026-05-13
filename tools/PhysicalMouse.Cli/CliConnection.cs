using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PhysicalMouse.Viiper;

internal static class CliConnection
{
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3242;
    private const string DefaultPassword = "";
    private const int DefaultSettleMs = 750;

    // MARK: Connection
    // ========================================================================

    internal static async Task<int> ExecuteAsync(
        Func<ViiperPhysicalMouse, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        ViiperOptions viiperOptions = CreateViiperOptions();
        ViiperPhysicalMouse mouse = await ViiperPhysicalMouse.ConnectAsync(viiperOptions, cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(DefaultSettleMs, cancellationToken).ConfigureAwait(false);

            return await action(mouse, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await mouse.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal static async Task PrintConnectionAsync(ViiperPhysicalMouse mouse)
    {
        await Console.Out.WriteLineAsync($"Connected: {mouse.IsConnected}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"BusId: {mouse.BusId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"DeviceId: {mouse.DeviceId ?? "<unknown>"}").ConfigureAwait(false);
    }

    // MARK: Helpers
    // ========================================================================

    private static ViiperOptions CreateViiperOptions()
    {
        return new ViiperOptions
        {
            Host = DefaultHost,
            Port = DefaultPort,
            Password = DefaultPassword,
        };
    }
}
