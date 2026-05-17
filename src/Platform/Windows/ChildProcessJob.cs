using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Platform.Windows;

/// <summary>Windows job object that closes child processes with the owner.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class ChildProcessJob : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>Creates a child process job.</summary>
    public ChildProcessJob()
    {
        _handle = CreateJobObject(nint.Zero, null);
        if (_handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create child process job.");
        }

        JobObjectExtendedLimitInformation info = new()
        {
            BasicLimitInformation =
            {
                LimitFlags = JobObjectLimitFlags.KillOnJobClose,
            },
        };

        if (!SetInformationJobObject(
            _handle,
            JobObjectInfoClass.ExtendedLimitInformation,
            ref info,
            Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            int error = Marshal.GetLastPInvokeError();
            _ = CloseHandle(_handle);
            _handle = nint.Zero;
            throw new Win32Exception(error, "Could not configure child process job.");
        }
    }

    /// <summary>Adds a process to the job.</summary>
    public bool TryAdd(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return !_disposed && !process.HasExited && AssignProcessToJobObject(_handle, process.Handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != nint.Zero)
        {
            _ = CloseHandle(_handle);
            _handle = nint.Zero;
        }

        _disposed = true;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObject(nint jobAttributes, string? name);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        nint job,
        JobObjectInfoClass infoClass,
        ref JobObjectExtendedLimitInformation info,
        int infoLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9,
    }

    [Flags]
    private enum JobObjectLimitFlags : uint
    {
        KillOnJobClose = 0x00002000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimitFlags LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
