using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using static MemoryMapFile.HybridHelper;

namespace MemoryMapFile
{
    public class HybridFileStream<T> : IHybridFileStream, IDisposable where T : struct
    {
        readonly static object GlobalSyncLock = new();
        private static long GlobalFlushing = 0;

        readonly FileStream InternalStream;
        readonly object SyncRoot = new();
        readonly int BlockSize = 0;
        static readonly IntPtr ResourceHandle;


        public long TotalSize { get; private set; }

        public static ulong GetUsedMemory()
        {

            ulong result = 0;

            foreach (var stream in Streams)
            {
                result += stream.Key.GetMemory();
            }

            return result;
        }

        static readonly ulong MaxMemory;
        static readonly ulong LowerOSMemoryThreshold;


        public readonly ConcurrentDictionary<int, Booklet<T>> InternalStore = new(Environment.ProcessorCount, 100);

        readonly static ConditionalWeakTable<IHybridFileStream, object> Streams = new();

        public static Stopwatch Watch = new Stopwatch();

        static HybridFileStream()
        {
            ResourceHandle = HybridHelper.CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification);
            MaxMemory = (ulong)(GetOSMemory().ullAvailPhys);
            LowerOSMemoryThreshold = (ulong)(MaxMemory * 0.9d);

            Console.WriteLine((MaxMemory - LowerOSMemoryThreshold) / (512 * 512 * 4));
        }

        public HybridFileStream(string fileName, int blockSize, int blocks)
        {
            TotalSize = (long)blockSize * blocks;

            InternalStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, blockSize, FileOptions.DeleteOnClose | FileOptions.RandomAccess);
            InternalStream.SetLength(TotalSize);
            BlockSize = blockSize;

            HybridHelper.SetFileValidData(InternalStream.SafeFileHandle, TotalSize);

            Streams.Add(this, null);
        }

        static ManualResetEventSlim GlobalFlushWaitHandle = new ManualResetEventSlim(true);

        private static void CheckResource()
        {
            bool state = false;
            var mem = GetOSMemory();

            if (mem.ullAvailPhys < LowerOSMemoryThreshold)
                state = true;

            GlobalFlushWaitHandle.Wait();

            if (state == true)
            {
                GlobalFlushWaitHandle.Reset();

                Console.WriteLine("GC Invoked");

                //     lock (GlobalSyncLock)
                {
                    var activeStreams = Streams.ToArray();

                    int streamNo = 1;

                    Parallel.For(0, activeStreams.Length, i =>
                    {
                        var stream = activeStreams[i].Key;

                        Console.WriteLine($"Flushing {streamNo++}");
                        stream.Flush();
                        Console.WriteLine($"Used Memory: {GetUsedMemory()}");
                    });

                    int ctr = 0;
                    foreach (var stream in activeStreams)
                    {
                        ctr += stream.Key.GetItemCount();
                    }

                    Console.WriteLine($"Active Items: {ctr}");


                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                GlobalFlushWaitHandle.Set();

                Console.WriteLine("GC Finished");
            }
        }

        public static MEMORYSTATUSEX GetOSMemory()
        {
            MEMORYSTATUSEX memstat = new()
            {
                dwLength = HybridHelper.SizeOfMemStat
            };

            if (!HybridHelper.GlobalMemoryStatusEx(ref memstat))
                throw new Win32Exception(Marshal.GetHRForLastWin32Error());

            return memstat;
        }

        public ReadOnlySpan<T> Read(int blockNo)
        {
            var booklet = InternalStore.GetOrAdd(blockNo, (blockNo) =>
            {
                CheckResource();

                lock (SyncRoot)
                {
                    InternalStream.Position = (long)BlockSize * blockNo;

                    var booklet = new Booklet<T>(GC.AllocateUninitializedArray<T>(BlockSize));
                    InternalStream.Read(MemoryMarshal.Cast<T, byte>(booklet.Data));

                    return booklet;
                }
            });

            return booklet.Data;
        }


        public void Write(int blockNo, T[] data)
        {
            InternalStore.AddOrUpdate(blockNo, no =>
            {
                return new Booklet<T>(data);
            }
            , (no, d) =>
            {
                CheckResource();
                return d.SetData(data);
            });
        }

        public void Flush()
        {
            Console.WriteLine("Entering Flush");
            try
            {
                lock (SyncRoot)
                {
                    foreach (var blockEntry in InternalStore.ToArray())
                    {
                        InternalStream.Position = (long)BlockSize * blockEntry.Key;
                        InternalStream.Write(MemoryMarshal.Cast<T, byte>(blockEntry.Value.Data));

                        InternalStore.TryRemove(blockEntry);
                    }

                    InternalStream.Flush();
                }
            }
            finally
            {
                Console.WriteLine("Exiting Flush");
            }
        }

        public ulong GetMemory()
        {
            ulong result = 0;

            lock (SyncRoot)
            {
                foreach (var data in InternalStore.ToArray())
                {
                    result += ((ulong)BlockSize * (ulong)Unsafe.SizeOf<T>());
                }

                return result;
            }
        }

        public void Dispose()
        {
            InternalStream?.Dispose();
            Streams.Remove(this);
        }

        public int GetItemCount()
        {
            return InternalStore.Count;
        }
    }

    public struct Booklet<T>
    {
        public int AccessCount;
        public DateTime LastAccess;
        public T[] Data;

        public Booklet(T[] data)
        {
            AccessCount = 1;
            LastAccess = DateTime.Now;
            Data = data;
        }

        public Booklet<T> SetData(T[] data)
        {
            Interlocked.Increment(ref AccessCount);
            LastAccess = DateTime.Now;
            Data = data;
            return this;
            
        }
    }
}
