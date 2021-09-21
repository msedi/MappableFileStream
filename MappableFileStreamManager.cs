﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

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

        static IntPtr LowMemResourceHandle;
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
                if (LowMemResourceHandle == IntPtr.Zero)
                {
                    AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

                    LowMemResourceHandle = CreateMemoryResourceNotification(MemoryResourceNotificationType.LowMemoryResourceNotification);

                    // Get the current memory situation. This reflects the current situation and is seen as an upper boundary
                    // when the management starts. 
                    MaxAvailableMemory = GetOSMemory().ullAvailPhys;

                    // This is the threshold that is used to get active with paging
                    // and unmapping when the current memory siutation reaches this threshold.
                    LowerOSMemoryThreshold = (ulong)(MaxAvailableMemory * LowerOSMemoryPercentage);

                    // Also watch for the GC.
                    GC.RegisterForFullGCNotification(90, 90);

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

                    var gcStatus = GC.WaitForFullGCApproach(500);
                    switch (gcStatus)
                    {
                        case GCNotificationStatus.NotApplicable:
                            break;

                        case GCNotificationStatus.Failed:
                            break;

                        case GCNotificationStatus.Succeeded:
                            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            break;

                        case GCNotificationStatus.Canceled:
                            break;

                        case GCNotificationStatus.Timeout:
                            break;
                    }


                    var gcStatus2 = GC.WaitForFullGCComplete(500);
                    switch (gcStatus2)
                    {
                        case GCNotificationStatus.NotApplicable:
                            break;

                        case GCNotificationStatus.Failed:
                            break;

                        case GCNotificationStatus.Succeeded:
                            break;

                        case GCNotificationStatus.Canceled:
                            break;

                        case GCNotificationStatus.Timeout:
                            break;
                    }

                    // Query for low resources.
                    //if (QueryMemoryResourceNotification(LowMemResourceHandle, out var state))
                    //{
                    //    var memory = GetOSMemory();
                    //    if (state)
                    //    {
                    //        FlushWaitHandle.Reset();
                    //        CleanupMappableStreams();
                    //    }
                    //}
                    //else
                    //{
                    //    // Log the win32 error.
                    //}
                }
                finally
                {
                    FlushWaitHandle.Set();
                }
            }
        }

        private static void CleanupMappableStreams()
        {
            try
            {
                List<(IReadOnlyMappableFileStream, MappableStreamInfo)> streamInfo = new(Streams.Count());

                int totalBookletCount = 0;
                foreach(var stream in Streams.ToArray())
                {
                    if (stream.Key.IsDisposed)
                    {
                        Streams.Remove(stream.Key);
                        continue;
                    }

                    var memoryInfo = stream.Key.GetMemoryInfo();
                    streamInfo.Add((stream.Key, memoryInfo));

                    totalBookletCount += memoryInfo.ItemCount;
                }

                var booklets = ArrayPool<(IReadOnlyMappableFileStream stream, Booklet booklet)>.Shared.Rent(totalBookletCount);
                try
                {
                    int bookletIndex = 0;
                    foreach (var (stream, memInfo) in streamInfo)
                    {
                        foreach (var booklet in memInfo.Booklets)
                        {
                            booklets[bookletIndex++] = (stream, booklet);
                        }
                    }

                    var moreThanMaxAllowedItems = (totalBookletCount > MaxAllowedItems);
                    var lowerMemoryThresholdReached = GetOSMemory().ullAvailPhys < LowerOSMemoryThreshold;

                    if (moreThanMaxAllowedItems || lowerMemoryThresholdReached)
                    {
                        var items = booklets.Take(totalBookletCount).OrderByDescending(x => x.booklet.LastAccess).ThenByDescending(x => x.booklet.AccessCount);
                        foreach (var (stream, booklet) in items)
                        {
                            stream.Unmap(booklet.Index);
                        }

                        foreach(var stream in streamInfo)
                        {

                            stream.Item1.Flush();
                        }
                    }

                    if (!HybridHelper.K32EmptyWorkingSet(Process.GetCurrentProcess().SafeHandle))
                    {
                        Console.WriteLine($"EmptyWorkingSet filed an error: {Marshal.GetLastWin32Error()}");
                    }
                }
                finally
                {
                    ArrayPool<(IReadOnlyMappableFileStream stream, Booklet booklet)>.Shared.Return(booklets);
                }
            }
            catch (Exception ex)
            {

            }
        }
    }
}
