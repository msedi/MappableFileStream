using System;
using System.Buffers;
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
        readonly static ConditionalWeakTable<IReadOnlyMappableFileStream, object> Streams = new();

        static IntPtr LoMemResourceHandle;
        static IntPtr HiMemResourceHandle;
        static ulong MaxAvailableMemory;

        internal static readonly ManualResetEventSlim FlushWaitHandle = new(true);

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
        static float LowerOSMemoryPercentage = 0.2f;

        static Thread MemoryThread;

        public static void FlushWait() => FlushWaitHandle.Wait();

        /// <summary>
        /// Adds a stream being managed by the <see cref="MappableFileStreamManager"/>.
        /// </summary>
        /// <param name="stream"></param>
        internal static void AddStream(IReadOnlyMappableFileStream stream)
        {
            lock (Streams)
            {
                Streams.Add(stream, null);

                // We need to check if the thread is running.
                if (LoMemResourceHandle == IntPtr.Zero)
                {
                    AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

                    LoMemResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification);
                    HiMemResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.HighMemoryResourceNotification);

                    // Get the current memory situation. This reflects the current situation and is seen as an upper boundary
                    // when the management starts. 
                    MaxAvailableMemory = GetOSMemory().ullAvailPhys;

                    // This is the threshold that is used to get active with paging
                    // and unmapping when the current memory siutation reaches this threshold.
                    LowerOSMemoryThreshold = (ulong)(MaxAvailableMemory * LowerOSMemoryPercentage);

                    Console.WriteLine((MaxAvailableMemory - LowerOSMemoryThreshold) / (512 * 512 * 4));
                }

                AdjustMemoryLimits();

                if (MemoryThread is null)
                {
                    MemoryThread = new Thread(MemoryManagerThread);
                    MemoryThread.Name = "IPIPE Memory Manager";
                    MemoryThread.Start();
                }
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Continously adjust the memory limits according to the current situation.
        /// </summary>
        private static void AdjustMemoryLimits()
        {
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
        }

        /// <summary>
        /// Removes a stream from being managed by the <see cref="MappableFileStreamManager"/>.
        /// </summary>
        /// <param name="stream"></param>
        internal static void RemoveStream(IReadOnlyMappableFileStream stream)
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
                try
                {
                    // It only makes sense to perform the cleanup thread if there are streams registered.
                    SpinWait.SpinUntil(() => Streams.Any());
                    // Query for low resources.
                    CleanupMappableStreams();
                }
                finally
                {
                }
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
            if (QueryMemoryResourceNotification(LoMemResourceHandle, out var state))
            {
                return state;
            }

            return false;
        }

        private static StreamInfo GetStreamInfo()
        {
            var streamInfo = ArrayPool<(IReadOnlyMappableFileStream, MappableStreamInfo)>.Shared.Rent(Streams.Count());

            int totalBookletCount = 0;
            int streamCount = 0;
            foreach (var stream in Streams.ToArray())
            {
                // Remove already disposed streams.
                if (stream.Key.IsDisposed)
                {
                    Streams.Remove(stream.Key);
                    continue;
                }

                var memoryInfo = stream.Key.GetMemoryInfo();
                streamInfo[streamCount++] = (stream.Key, memoryInfo);

                totalBookletCount += memoryInfo.ItemCount;
            }

            return new StreamInfo(streamInfo, streamCount, totalBookletCount);

        }

        private static void CleanupMappableStreams()
        {
            try
            {
                do
                {
                    // Escape the cleanup if memory resources are enough.
                    if (!IsMemoryLo() && IsMemoryHi()) return;

                    // Block the access to streams so that noone can currently allocate new memory.
                    FlushWaitHandle.Reset();

                    using var streamInfo = GetStreamInfo();

                    var booklets = ArrayPool<(IReadOnlyMappableFileStream stream, Booklet booklet)>.Shared.Rent(streamInfo.TotalBookletCount);
                    try
                    {
                        int bookletIndex = 0;
                        foreach (var (stream, memInfo) in streamInfo.Get)
                        {
                            foreach (var booklet in memInfo.GetBooklets())
                            {
                                booklets[bookletIndex++] = (stream, booklet);
                            }
                        }

                        var moreThanMaxAllowedItems = (streamInfo.TotalBookletCount > MaxAllowedItems);
                        var lowerMemoryThresholdReached = GetOSMemory().ullAvailPhys < LowerOSMemoryThreshold;

                        if (moreThanMaxAllowedItems || lowerMemoryThresholdReached)
                        {
                            var items = booklets.Take(totalBookletCount).OrderByDescending(x => x.booklet.LastAccess).ThenByDescending(x => x.booklet.AccessCount);

                            Stopwatch unmapWatch = Stopwatch.StartNew();
                            Console.Write("Unmapping: ");
                            foreach (var (stream, booklet) in items)
                            {
                                stream.Unmap(booklet.Index);
                            }
                            Console.WriteLine($"{unmapWatch.Elapsed.TotalSeconds}s");


                            Stopwatch flushWatch = Stopwatch.StartNew();
                            Console.Write("Flushing: ");
                            Parallel.ForEach(streamInfo, stream =>
                            {
                                stream.Item1.Flush();
                            });
                            Console.WriteLine($"{flushWatch.Elapsed.TotalSeconds}s");
                        }

                        if (!HybridHelper.K32EmptyWorkingSet(Process.GetCurrentProcess().SafeHandle))
                        {
                            Console.WriteLine($"EmptyWorkingSet filed an error: {Marshal.GetLastWin32Error()}");
                        }
                    }
                    finally
                    {
                        ArrayPool<(IReadOnlyMappableFileStream stream, Booklet booklet)>.Shared.Return(booklets);

                        foreach (var s in streamInfo)
                            s.Item2.Dispose();
                    }
                }
                while (true);
            }
            catch (Exception ex)
            {

            }
            finally
            {
                // Allow access to streams again.
                FlushWaitHandle.Set();
            }
        }
    }

    readonly ref struct StreamInfo
    {

        readonly (IReadOnlyMappableFileStream stream, MappableStreamInfo memInfo)[] InternalStreams;

        public readonly int TotalBookletCount;

         readonly int StreamCount;

        public StreamInfo((IReadOnlyMappableFileStream stream, MappableStreamInfo memInfo)[] streams, int streamCount, int bookletCount)
        {
            InternalStreams = streams;
            StreamCount = streamCount;
            TotalBookletCount = bookletCount;
        }

        public void Dispose()
        {
            ArrayPool<(IReadOnlyMappableFileStream stream, MappableStreamInfo memInfo)>.Shared.Return(InternalStreams);
        }
    }
}
