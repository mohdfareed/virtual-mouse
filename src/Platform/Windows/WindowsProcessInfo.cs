using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Platform.Windows;

/// <summary>Windows process and foreground-window helpers.</summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsProcessInfo
{
    /// <summary>Gets the foreground process name, including .exe.</summary>
    public static string GetForegroundProcessName()
    {
        int processId = GetForegroundProcessId();
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>Gets the foreground process id, or zero when unavailable.</summary>
    public static int GetForegroundProcessId()
    {
        nint window = GetForegroundWindow();
        if (window == nint.Zero)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId > int.MaxValue ? 0 : (int)processId;
    }

    /// <summary>Gets whether the process id is running.</summary>
    public static bool IsProcessRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Gets whether any process in the list is running.</summary>
    public static bool IsAnyProcessRunning(IReadOnlyList<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        foreach (string processName in processNames)
        {
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            try
            {
                if (processes.Length != 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    /// <summary>Gets whether the foreground process is in the list.</summary>
    public static bool IsForegroundProcess(IReadOnlyList<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        string foreground = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(foreground))
        {
            return false;
        }

        foreach (string processName in processNames)
        {
            if (string.Equals(
                Path.GetFileName(processName),
                foreground,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets whether a process belongs to the root process tree.</summary>
    public static bool IsProcessInTree(int rootProcessId, int processId)
    {
        return rootProcessId > 0 &&
            processId > 0 &&
            (rootProcessId == processId
            ? IsProcessRunning(rootProcessId)
            : IsProcessInTree(rootProcessId, processId, GetProcessParents()));
    }

    /// <summary>Gets whether any process in a root process tree is running.</summary>
    public static bool IsProcessTreeRunning(int rootProcessId)
    {
        if (IsProcessRunning(rootProcessId))
        {
            return true;
        }

        Dictionary<int, int> parents = GetProcessParents();
        foreach (int processId in parents.Keys)
        {
            if (IsProcessInTree(rootProcessId, processId, parents))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Stops all processes with names in the list.</summary>
    public static int StopProcessesByName(IReadOnlyList<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        int stoppedCount = 0;
        foreach (string processName in processNames)
        {
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
            try
            {
                foreach (Process process in processes)
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    stoppedCount++;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return stoppedCount;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    private static Dictionary<int, int> GetProcessParents()
    {
        Dictionary<int, int> parents = [];
        using Process currentProcess = Process.GetCurrentProcess();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                int processId = process.Id;
                if (processId > 0 && processId != currentProcess.Id)
                {
                    parents[processId] = GetParentProcessId(process);
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return parents;
    }

    private static bool IsProcessInTree(
        int rootProcessId,
        int processId,
        Dictionary<int, int> parents)
    {
        int current = processId;
        HashSet<int> visited = [];
        while (parents.TryGetValue(current, out int parentProcessId) && parentProcessId > 0)
        {
            if (!visited.Add(current))
            {
                return false;
            }

            if (parentProcessId == rootProcessId)
            {
                return true;
            }

            current = parentProcessId;
        }

        return false;
    }

    private static int GetParentProcessId(Process process)
    {
        try
        {
            return GetParentProcessIdCore(process.Id);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
        {
            return 0;
        }
    }

    private static int GetParentProcessIdCore(int processId)
    {
        using Process parentQuery = Process.GetProcessById(processId);
        return ParentProcessReader.GetParentProcessId(parentQuery);
    }

    private static partial class ParentProcessReader
    {
        public static int GetParentProcessId(Process process)
        {
            ProcessBasicInformation info = default;
            int status = NtQueryInformationProcess(
                process.Handle,
                processInformationClass: 0,
                ref info,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);
            return status == 0 && info.InheritedFromUniqueProcessId <= int.MaxValue
                ? (int)info.InheritedFromUniqueProcessId
                : 0;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("ntdll.dll")]
        private static partial int NtQueryInformationProcess(
            nint processHandle,
            int processInformationClass,
            ref ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessBasicInformation
        {
            public nint Reserved1;
            public nint PebBaseAddress;
            public nint Reserved2A;
            public nint Reserved2B;
            public nuint UniqueProcessId;
            public nuint InheritedFromUniqueProcessId;
        }
    }
}
