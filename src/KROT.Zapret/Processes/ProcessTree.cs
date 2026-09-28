using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KROT.Zapret.Processes;

internal static class ProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int ErrorNoMoreFiles = 18;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static IReadOnlyList<int> DescendantsOf(int rootProcessId)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Cannot enumerate the KROT runtime process tree.");
        }

        try
        {
            var entries = new List<ProcessEntry>();
            var entry = new ProcessEntry
            {
                Size = (uint)Marshal.SizeOf(typeof(ProcessEntry))
            };
            if (!Process32First(snapshot, ref entry))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreFiles)
                {
                    return Array.Empty<int>();
                }

                throw new Win32Exception(
                    error,
                    "Cannot read the KROT runtime process tree.");
            }

            do
            {
                entries.Add(entry);
                entry.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
            }
            while (Process32Next(snapshot, ref entry));

            var descendants = new List<int>();
            var parents = new Queue<int>();
            var visited = new HashSet<int> { rootProcessId };
            parents.Enqueue(rootProcessId);
            while (parents.Count > 0)
            {
                var parent = parents.Dequeue();
                foreach (var candidate in entries)
                {
                    if (candidate.ParentProcessId != (uint)parent
                        || !visited.Add((int)candidate.ProcessId))
                    {
                        continue;
                    }

                    var processId = (int)candidate.ProcessId;
                    descendants.Add(processId);
                    parents.Enqueue(processId);
                }
            }

            return descendants;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(
        IntPtr snapshot,
        ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(
        IntPtr snapshot,
        ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
