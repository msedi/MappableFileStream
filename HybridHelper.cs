
using Microsoft.Win32.SafeHandles;

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;

namespace MappableFileStream
{
    [Flags]
    public enum MemoryMappedSyncFlags
    {
        MS_ASYNC = 0x1,
        MS_SYNC = 0x2,
        MS_INVALIDATE = 0x10,
    }

    public enum QUOTA_LIMITS_HARDWS : uint
    {
        /// <summary>
        /// Enable the maximum size limit.
        /// <para>The FILE_CACHE_MAX_HARD_DISABLE and FILE_CACHE_MAX_HARD_ENABLE flags are mutually exclusive.</para>
        /// </summary>
        QUOTA_LIMITS_HARDWS_MIN_ENABLE = 0x00000001,

        /// <summary>
        /// Disable the maximum size limit.
        /// <para>The FILE_CACHE_MAX_HARD_DISABLE and FILE_CACHE_MAX_HARD_ENABLE flags are mutually exclusive.</para>
        /// </summary>
        QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002,

        /// <summary>
        /// Enable the minimum size limit.
        /// <para>The FILE_CACHE_MIN_HARD_DISABLE and FILE_CACHE_MIN_HARD_ENABLE flags are mutually exclusive.</para>
        /// </summary>
        QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x00000004,

        /// <summary>
        /// Disable the minimum size limit.
        /// <para>The FILE_CACHE_MIN_HARD_DISABLE and FILE_CACHE_MIN_HARD_ENABLE flags are mutually exclusive.</para>
        /// </summary>
        QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x00000008,
    }

    static class HybridHelper
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool  SetProcessWorkingSetSizeEx(
  SafeProcessHandle hProcess,
  nint dwMinimumWorkingSetSize,
  nint dwMaximumWorkingSetSize,
  QUOTA_LIMITS_HARDWS Flags
);

        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool K32EmptyWorkingSet(SafeProcessHandle hProcess);


        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetProcessWorkingSetSize(SafeProcessHandle hProcess, out nint lpMinimumWorkingSetSize, out nint lpMaximumWorkingSetSize);

        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr CreateMemoryResourceNotification(MemoryResourceNotificationType notificationType);

        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool QueryMemoryResourceNotification(IntPtr resourceNotificationHandle, out bool resourceState);

        internal enum MemoryResourceNotificationType : int
        {
            LowMemoryResourceNotification = 0,
            HighMemoryResourceNotification = 1,
        }



        [DllImport("kernel32.dll")]
        internal static extern bool FlushViewOfFile(IntPtr lpBaseAddress, nint dwNumberOfBytesToFlush);



        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32")] 
        internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);


        public static readonly uint SizeOfMemStat = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));


        [SuppressUnmanagedCodeSecurity]
        [SuppressGCTransition]
        [DllImport("kernel32")] 
        private static extern bool SetFileValidData(SafeFileHandle handle, long validDataLength);


        #region P/Invokes

        /// <summary>
        /// Unlocks a specified range of pages in the virtual address space of a process, 
        /// enabling the system to swap the pages out to the paging file if necessary.
        /// </summary>
        /// <param name="lpAddress"></param>
        /// <param name="dwSize"></param>
        /// <returns></returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [SuppressGCTransition]
        [SuppressUnmanagedCodeSecurity]
        public static extern bool VirtualUnlock(nint lpAddress, nint dwSize);

        /// <summary>
        /// Unlocks a specified range of pages in the virtual address space of a process, 
        /// enabling the system to swap the pages out to the paging file if necessary.
        /// </summary>
        /// <param name="lpAddress"></param>
        /// <param name="dwSize"></param>
        /// <returns></returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [SuppressGCTransition]
        [SuppressUnmanagedCodeSecurity]
        public static extern bool DiscardVirtualMemory(nint lpAddress, nint dwSize);

        #endregion

        [SupportedOSPlatform("win-x64")]
        public static void SetValidFileRegion(SafeFileHandle handle, long validDataLength)
        {
            SetFileValidData(handle, validDataLength);
        }


        internal static MEMORYSTATUSEX GetOSMemory()
        {
            MEMORYSTATUSEX memstat = new()
            {
                dwLength = SizeOfMemStat
            };

            if (!GlobalMemoryStatusEx(ref memstat))
                throw new Win32Exception(Marshal.GetHRForLastWin32Error());

            return memstat;
        }
    }


    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullwAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
