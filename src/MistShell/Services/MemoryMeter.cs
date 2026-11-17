using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MistShell.Services;

public static class MemoryMeter
{
    private const uint Th32csSnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static long GetProcessTreeWorkingSet()
    {
        try
        {
            var root = Environment.ProcessId;
            var parentByPid = new Dictionary<uint, uint>();
            var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return Process.GetCurrentProcess().WorkingSet64;
            try
            {
                var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
                if (Process32First(snapshot, ref entry))
                {
                    do
                    {
                        parentByPid[entry.th32ProcessID] = entry.th32ParentProcessID;
                    } while (Process32Next(snapshot, ref entry));
                }
            }
            finally
            {
                CloseHandle(snapshot);
            }

            var ids = new HashSet<uint> { (uint)root };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var pair in parentByPid)
                {
                    if (ids.Contains(pair.Value) && ids.Add(pair.Key)) changed = true;
                }
            }

            long total = 0;
            foreach (var id in ids)
            {
                try
                {
                    using var process = Process.GetProcessById((int)id);
                    total += process.WorkingSet64;
                }
                catch
                {
                    // Process ended while measuring.
                }
            }
            return total;
        }
        catch
        {
            return Process.GetCurrentProcess().WorkingSet64;
        }
    }
}
