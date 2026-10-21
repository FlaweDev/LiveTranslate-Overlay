/**
 * Windows Job Object wrapper ensuring child Python processes are terminated
 * immediately if the parent .NET process exits or crashes.
 */

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LiveTranslateOverlay.Core.ProcessManagement;

public static class ChildProcessGuard
{
    private const uint JobObjectLimitKillOnJobClose = 0x2000;
    private static readonly IntPtr JobHandle;

    static ChildProcessGuard()
    {
        if (!OperatingSystem.IsWindows()) return;

        JobHandle = CreateJobObject(IntPtr.Zero, null);
        if (JobHandle == IntPtr.Zero) return;

        var extendedInfo = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(extendedInfo, ptr, false);
            if (!SetInformationJobObject(JobHandle, JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static void Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (OperatingSystem.IsWindows() && JobHandle != IntPtr.Zero)
        {
            AssignProcessToJobObject(JobHandle, process.Handle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
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
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}