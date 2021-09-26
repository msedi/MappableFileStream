using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using static MappableFileStream.HybridHelper;

namespace MappableFileStream
{
    /// <summary>
    /// Manager that manages all created <see cref="MappableFileStream"/>s.
    /// </summary>
    public static class MappableFileStreamManager
    {
        /// <summary>
        /// Holds a list of all created <see cref="MappableFileStream"/>.
        /// </summary>
        readonly static ConditionalWeakTable<MappableFileStream, object> Streams = new();

        private static ulong MaxAvailableMemoryOnStartup;


        static IntPtr LoMemResourceHandle;
        static IntPtr HiMemResourceHandle;
        static ulong MaxAvailableMemory;

    //    private static readonly ManualResetEventSlim FlushWaitHandle = new(true);

        internal static readonly ReaderWriterLockSlim FlushLock = new(LockRecursionPolicy.SupportsRecursion);

        /// <summary>
        /// Gets a value of how many items are allowed at maximum to be stored concurrently in memory.
        /// This values is calculated from the average blocksize of the stream.
        /// </summary>
        static int MaxAllowedItems;

        /// <summary>
        /// This is the threshold that is used to get start paging
        /// and unmapping data when the current memory siutation reaches this threshold.
        /// It is calculated from the MaxMemory and the <see cref="LowerOSMemoryPercentage"/>.
        /// </summary>
        static ulong LowerOSMemoryThreshold;

        /// <summary>
        /// This is the percentage of the <see cref="MaxAvailableMemory"/>. The <see cref="LowerOSMemoryThreshold"/> is calculated by
        /// multiplying the <see cref="MaxAvailableMemory"/> with the <see cref="LowerOSMemoryPercentage"/>.
        /// </summary>
        static double LowerOSMemoryPercentage = 0.1d;

        static Thread MemoryThread;

        /// <summary>
        /// Wait until the memory manager allows access to the data.
        /// </summary>
        /// <returns></returns>
        public static IDisposable WaitForDataAccess() => FlushLock.EnterRead();

        static MappableFileStreamManager()
        {
            // Get the current memory situation. This reflects the current situation and is seen as an upper boundary
            // when the management starts. 
            MaxAvailableMemoryOnStartup = MaxAvailableMemory = GetOSMemory().ullAvailPhys;

            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            LoMemResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification);
            HiMemResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.HighMemoryResourceNotification);

            // This is the threshold that is used to get active with paging
            // and unmapping when the current memory siutation reaches this threshold.
            LowerOSMemoryThreshold = (ulong)(MaxAvailableMemory * LowerOSMemoryPercentage);
        }

        /// <summary>
        /// Adds a stream being managed by the <see cref="MappableFileStreamManager"/>.
        /// </summary>
        /// <param name="stream"></param>
        internal static void AddStream(MappableFileStream stream)
        {
            lock (Streams)
            {
                Streams.Add(stream, null);

                AdjustMemoryLimits();

                if (MemoryThread is null)
                {
                    MemoryThread = new Thread(MemoryManagerThread);
                    MemoryThread.Name = "IPIPE Memory Manager";
                    MemoryThread.Start();
                }
            }
        }

        /// <summary>
        /// Sets the upper boundary of memory that is allowed to be used by all <see cref="MappableFileStream"/>s.
        /// </summary>
        /// <param name="allowedMaximumMemory">Sets the allowed memory in bytes.</param>
        public static void SetMaxMemory(ulong allowedMaximumMemory)
        {
            if (allowedMaximumMemory != default)
                MaxAvailableMemory = Math.Min(MaxAvailableMemoryOnStartup, allowedMaximumMemory);
            else
                MaxAvailableMemory = MaxAvailableMemoryOnStartup;

            Console.WriteLine($"Maximum Allowed Memory: {MaxAvailableMemory}");

            AdjustMemoryLimits();
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Continously adjust the memory limits according to the current situation.
        /// </summary>
        private static void AdjustMemoryLimits()
        {
            if (!Streams.Any())
                return;

            var memoryInfo = Streams.Select(x => x.Key.GetMemoryInfo()).ToArray();
            var possibleMemory = MaxAvailableMemory - LowerOSMemoryThreshold;

            var averageBlockSize = memoryInfo.Average(x => x.BlockSizeInBytes);
            MaxAllowedItems = (int)Math.Floor(possibleMemory / averageBlockSize);

            // Calculate how much memory the MaxAllowedItems would use.
            var memoryOccupiedByMaxItems = MaxAllowedItems * averageBlockSize;

            // It could happen the average blocksize is too high so we need to increase to the max blocksize.
            if (memoryOccupiedByMaxItems > possibleMemory)
            {
                var maxBlockSize = memoryInfo.Max(x => x.BlockSizeInBytes);
                MaxAllowedItems = (int)Math.Floor(possibleMemory / (double)maxBlockSize);
            }

            Console.WriteLine($"Max Items: {MaxAllowedItems}");
        }

        /// <summary>
        /// Removes a stream from being managed by the <see cref="MappableFileStreamManager"/>.
        /// </summary>
        /// <param name="stream"></param>
        internal static void RemoveStream(MappableFileStream stream)
        {
            lock (Streams)
            {
                Streams.Remove(stream);
            }
        }

        private static void MemoryManagerThread()
        {
            while (true)
            {
                // It only makes sense to perform the cleanup thread if there are streams registered.
                SpinWait.SpinUntil(() => Streams.Any());

                // Query for low resources.
                CleanupMappableStreams();
            }
        }

        /// <summary>
        /// Check if we have hi memory resources.
        /// </summary>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static bool IsMemoryHi()
        {
            if (QueryMemoryResourceNotification(HiMemResourceHandle, out var state))
            {
                return state;
            }

            return false;
        }

        /// <summary>
        /// Check if we have lo memory resources.
        /// </summary>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static bool IsMemoryLo()
        {
            [SkipLocalsInit]
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            static bool TooManyBooklets()
            {
                var (activeStreams, totalBookletCount) = GetStreams();

                return totalBookletCount > MaxAllowedItems;
            }

            if (QueryMemoryResourceNotification(LoMemResourceHandle, out var state))
            {
                if ((!state && (GetOSMemory().ullAvailPhys < LowerOSMemoryThreshold)) || TooManyBooklets())
                    return true;

                return state;
            }

            return false;
        }

        /// <summary>
        /// Returns the total amount of currently allocated items.
        /// </summary>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static (MappableFileStream[] activeStreams, int totalBookletCount) GetStreams()
        {
            int totalBookletCount = 0;

            lock (Streams)
            {
                List<MappableFileStream> result = new(Streams.Count());

                foreach (var stream in Streams)
                {
                    // Remove already disposed streams.
                    if (stream.Key.IsDisposed)
                    {
                        Streams.Remove(stream.Key);
                        continue;
                    }


                    totalBookletCount += stream.Key.InternalStore.Count;
                    result.Add(stream.Key);
                }


                return (result.ToArray(), totalBookletCount);
            }
        }

        private static void CleanupMappableStreams()
        {
            try
            {
                do
                {
                    // Escape the cleanup if memory resources are enough.
                    if (!IsMemoryLo() && IsMemoryHi()) return;

                    // Get the total amount of currently allocated items.
                    (MappableFileStream[] activeStreams, int totalBookletCount) = GetStreams();

                    // Block the access to streams so that noone can currently allocate new memory.
                    using var disallowedProcessingForOtherThreads = FlushLock.EnterWrite();

                    var booklets = ArrayPool<(MappableFileStream stream, Booklet booklet)>.Shared.Rent(totalBookletCount);
                    try
                    {
                        int bookletIndex = 0;
                        foreach (var stream in activeStreams)
                        {
                            foreach (var booklet in stream.InternalStore.Values)
                            {
                                booklets[bookletIndex++] = (stream, booklet);
                            }
                        }

                        var items = booklets.Take(totalBookletCount).OrderByDescending(x => x.booklet.LastAccess).ThenByDescending(x => x.booklet.AccessCount);

                        Stopwatch unmapWatch = Stopwatch.StartNew();
                        Console.Write("Unmapping: ");
                        foreach (var (stream, booklet) in items)
                        {
                            stream.Unmap(booklet.Index);
                        }
                        Console.WriteLine($"{unmapWatch.Elapsed.TotalSeconds}s");


                        //Stopwatch flushWatch = Stopwatch.StartNew();
                        //Console.Write("Flushing: ");
                        //Parallel.ForEach(activeStreams, stream =>
                        //{
                        //    stream.Flush();
                        //});

                        //Console.WriteLine($"{flushWatch.Elapsed.TotalSeconds}s");

                        if (!HybridHelper.K32EmptyWorkingSet(Process.GetCurrentProcess().SafeHandle))
                        {
                            Console.WriteLine($"EmptyWorkingSet filed an error: {Marshal.GetLastWin32Error()}");
                        }
                    }
                    finally
                    {
                        ArrayPool<(MappableFileStream stream, Booklet booklet)>.Shared.Return(booklets, true);
                    }
                }
                while (true);
            }
            catch (Exception ex)
            {

            }
        }
    }

    static class ReaderWriterLockExtensions
    {
        public static IDisposable EnterWrite(this ReaderWriterLockSlim rw) => new ReaderWriterLock_WriteLock_Disposable(rw);

        public static IDisposable EnterRead(this ReaderWriterLockSlim rw) => new ReaderWriterLock_ReadLock_Disposable(rw);
    }

    struct ReaderWriterLock_WriteLock_Disposable : IDisposable
    {
        private readonly ReaderWriterLockSlim RWLock;

        public ReaderWriterLock_WriteLock_Disposable(ReaderWriterLockSlim rwLock)
        {
            RWLock = rwLock;
            RWLock.EnterWriteLock();
        }

        public void Dispose()
        {
            RWLock.ExitWriteLock();
        }
    }

    struct ReaderWriterLock_ReadLock_Disposable : IDisposable
    {
        private readonly ReaderWriterLockSlim RWLock;

        public ReaderWriterLock_ReadLock_Disposable(ReaderWriterLockSlim rwLock)
        {
            RWLock = rwLock;
            RWLock.EnterReadLock();
        }

        public void Dispose()
        {
            RWLock.ExitReadLock();
        }
    }
}
