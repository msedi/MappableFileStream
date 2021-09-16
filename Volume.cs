using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Microsoft.Win32;
using System.Threading;

namespace MemoryMapFile
{
    unsafe class Volume : DataSource, IDisposable
    {
        //MemoryMappedFile mmf;
        //MemoryMappedViewAccessor va;
        //FileStream stream;

        HybridFileStream<int> hs;

        readonly nint StartOffset;
        readonly long SliceSize;
        readonly int N;
        readonly nint FileSize;
        int count = 100;


        string TempPath = "C:\\MMF";


        [DllImport("kernel32.dll")]
        static extern bool FlushViewOfFile(IntPtr lpBaseAddress, nint dwNumberOfBytesToFlush);

        
        enum MemoryResourceNotificationType : int
        {
            LowMemoryResourceNotification = 0,
            HighMemoryResourceNotification = 1,
        }

        enum FileMapAccessType : uint
        {
            Copy = 0x01,
            Write = 0x02,
            Read = 0x04,
            AllAccess = 0x08,
            Execute = 0x20,
        }





        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateMemoryResourceNotification(MemoryResourceNotificationType notificationType);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool QueryMemoryResourceNotification(IntPtr resourceNotificationHandle, out bool resourceState);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll")]
        static extern IntPtr MapViewOfFileEx(IntPtr hFileMappingObject, FileMapAccessType dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap, IntPtr lpBaseAddress);

        [DllImport("NtosKrnl.exe")]
        static extern void CcFlushCache(IntPtr SectionObjectPointer, long FileOffset, long Length);



        private static long Flushing = 0;
        static readonly IntPtr ResourceHandle;

        static Volume()
        {
            ResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification);
        }

        public Volume(int sizex, int sizey, int sizez, string fileName = null)
        {
            SizeX = sizex;
            SizeY = sizey;
            SizeZ = sizez;

            N = (SizeX * SizeY);
            SliceSize = (N) * sizeof(int);
            FileSize = (nint)SizeX * SizeY * SizeZ * (nint)sizeof(int);

            if (!Directory.Exists(TempPath))
                Directory.CreateDirectory(TempPath);

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Path.Combine(TempPath, Guid.NewGuid().ToString()) + ".tmp";
                //stream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, (int)SliceSize, FileOptions.DeleteOnClose |FileOptions.RandomAccess);

                hs = new HybridFileStream<int>(fileName, N, SizeZ);
            }
            else
            {
                //stream = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, (int)SliceSize, FileOptions.RandomAccess);
            }
            
            //mmf = //MemoryMappedFile.CreateNew(Guid.NewGuid().ToString(), (long)sizex * sizey * sizez * (long)sizeof(int), MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.DelayAllocatePages, HandleInheritability.None);//   CreateFromFile(stream, null, (long)sizex * sizey * sizez * (long)sizeof(int), MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
            //mmf = MemoryMappedFile.CreateFromFile(stream, null, FileSize, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
            //va = mmf.CreateViewAccessor();

            //bool success = false;
            //va.SafeMemoryMappedViewHandle.DangerousAddRef(ref success);

            //StartOffset = va.SafeMemoryMappedViewHandle.DangerousGetHandle();            
        }

        public void Dispose()
        {
            //va.SafeMemoryMappedViewHandle.ReleasePointer();

            //stream.Dispose();
            //mmf.Dispose();

            CloseHandle(ResourceHandle);
        }

        public override unsafe ReadOnlySpan<int> getVolume(int sliceIndex)
        {
            if (Interlocked.Read(ref Flushing) > 0)
                SpinWait.SpinUntil(() => Interlocked.Read(ref Flushing) > 0);

            //QueryMemoryResourceNotification(CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification), out bool state);
            //if (state == true)
            //    Flush();
            //if (count == 0)
            //{
            //    Flush();
            //    count = 50;
            //}
            //count--;
            //Flush();

            //long Offset = (long)sliceIndex * N * sizeof(int);
            //using (var view = mmf.CreateViewAccessor(Offset, SliceSize, MemoryMappedFileAccess.ReadWrite))
            //{
            //    var startOffset = view.SafeMemoryMappedViewHandle.DangerousGetHandle();
            //    return new Span<int>((int*)(startOffset), N).ToArray();
            //}

            //long Offset = (long)sliceIndex * N;
            //return new ReadOnlySpan<int>((int*)(StartOffset + Offset), N).ToArray();


            return hs.Read(sliceIndex);

            //long Offset = (long)sliceIndex * N;
            //return new ReadOnlySpan<int>((int*)(StartOffset + Offset), N);
        }


        public unsafe void setVolume(int sliceIndex, int[] data)
        {
            if (Interlocked.Read(ref Flushing) > 0)
                SpinWait.SpinUntil(() => Interlocked.Read(ref Flushing) > 0);

            //long Offset = (long)sliceIndex * N * sizeof(int);

            //using (var view = mmf.CreateViewAccessor(Offset, SliceSize, MemoryMappedFileAccess.ReadWrite))
            //{
            //    var startOffset = view.SafeMemoryMappedViewHandle.DangerousGetHandle();
            //    var span = new Span<int>((int*)(startOffset), N);
            //    data.CopyTo(span);
            //}

            //long Offset = (long)sliceIndex * N;
            //var span = new Span<int>((int*)(StartOffset + Offset), N);
            //data.CopyTo(span);

            hs.Write(sliceIndex, data);
        }
    }
}