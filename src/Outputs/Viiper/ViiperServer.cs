using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Outputs.Viiper;

/// <summary>Starts and probes the local VIIPER server.</summary>
public static class ViiperServer
{
    private const string ExecutableEnvironmentVariable = "VIIPER_PATH";
    private const string ProcessName = "viiper";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Starts the local VIIPER server when it is not already accepting connections.</summary>
    /// <param name="options">VIIPER connection options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureRunningAsync(
        ViiperOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!IsLocalHost(options.Host) || await CanConnectAsync(options, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!IsViiperProcessRunning())
        {
            string executablePath = FindExecutablePath(
                Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ??
                throw new FileNotFoundException(
                    $"VIIPER is not running and viiper.exe was not found. Set {ExecutableEnvironmentVariable} or start VIIPER.");

            Start(executablePath);
        }

        await WaitForServerAsync(options, StartupTimeout, cancellationToken).ConfigureAwait(false);
    }

    internal static string? FindExecutablePath(string? configuredPath, string? localApplicationData)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
            return File.Exists(path) ? path : null;
        }

        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            return null;
        }

        string defaultPath = Path.Combine(localApplicationData, "VIIPER", "viiper.exe");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    internal static bool IsLocalHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));
    }

    private static async Task WaitForServerAsync(
        ViiperOptions options,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (await CanConnectAsync(options, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(StartupPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Timed out waiting for VIIPER at {options.Host}:{options.Port.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static async Task<bool> CanConnectAsync(
        ViiperOptions options,
        CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(options.Host, options.Port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsViiperProcessRunning()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void Start(string executablePath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start VIIPER.");
    }
}
