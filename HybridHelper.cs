using Microsoft.Win32.SafeHandles;

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace MemoryMapFile
{
    [Flags]
    public enum MemoryMappedSyncFlags
    {
        MS_ASYNC = 0x1,
        MS_SYNC = 0x2,
        MS_INVALIDATE = 0x10,
    }

    static class HybridHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateMemoryResourceNotification(MemoryResourceNotificationType notificationType);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool QueryMemoryResourceNotification(IntPtr resourceNotificationHandle, out bool resourceState);

        internal enum MemoryResourceNotificationType : int
        {
            LowMemoryResourceNotification = 0,
            HighMemoryResourceNotification = 1,
        }

        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32")] internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static readonly uint SizeOfMemStat = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));


        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32")] internal static extern bool SetFileValidData(SafeFileHandle handle, long validDataLength);
    }
}
