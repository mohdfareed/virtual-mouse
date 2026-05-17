using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

namespace Platform.Windows;

/// <summary>Launches and owns one game process tree.</summary>
[SupportedOSPlatform("windows")]
public sealed class GameProcessHost : IDisposable
{
    private ChildProcessJob? _job;
    private Process? _process;

    /// <summary>Gets the launched process id.</summary>
    public int? ProcessId => _process?.Id;

    /// <summary>Gets whether the launched process has exited.</summary>
    public bool HasExited => _process is null || _process.HasExited;

    /// <summary>Gets whether the launched process tree is still running.</summary>
    public bool IsTreeRunning => _process is { } process &&
        WindowsProcessInfo.IsProcessTreeRunning(process.Id);

    /// <summary>Launches one process.</summary>
    public int Launch(string executable, string arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException($"Executable not found: {executable}", executable);
        }

        if (_process is { HasExited: false })
        {
            throw new InvalidOperationException("A launched process is already running.");
        }

        string resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            : workingDirectory;

        try
        {
            Process process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = resolvedWorkingDirectory,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Launch failed.");

            Track(process);
            return process.Id;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw new InvalidOperationException($"Launch failed: {exception.Message}", exception);
        }
    }

    /// <summary>Stops the launched process tree.</summary>
    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _job?.Dispose();
            _job = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    private void Track(Process process)
    {
        _process?.Dispose();
        _process = process;
        process.EnableRaisingEvents = true;
        _job ??= new ChildProcessJob();
        _ = _job.TryAdd(process);
    }
}
