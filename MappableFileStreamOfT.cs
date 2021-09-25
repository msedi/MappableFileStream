
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace MappableFileStream
{
    /// <summary>
    /// Class that handles access to block-based data partially residing on disk and memory.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    sealed unsafe class MappableFileStream<T> : MappableFileStream, IMappableFileStream<T>, IDisposable where T : unmanaged
    {
        readonly FileStream InternalStream;
        readonly MemoryMappedFile InternalMemoryMap;
        readonly MemoryMappedViewAccessor ViewAccessor;
        readonly nint StartAddress;

        readonly object SyncRoot = new();

        /// <summary>
        /// Gets the total number of blocks.
        /// </summary>
        readonly int NoOfBlocks = 0;

        /// <summary>
        /// Gets the size of a block in number of elements.
        /// </summary>
        public int BlockSize { get; } = 0;

        /// <summary>
        /// Gets the size of a block in bytes.
        /// </summary>
        readonly int BlockSizeInBytes = 0;

        public readonly ConcurrentDictionary<int, Booklet> InternalStore = new(Environment.ProcessorCount, 100);

        /// <summary>
        /// Creates a new instance of <see cref="MappableFileStream{T}"/>.
        /// </summary>
        /// <param name="blockSize"></param>
        /// <param name="noOfBlocks"></param>
        internal MappableFileStream(FileStream stream, MemoryMappedFile memoryMappedFile, int blockSize, int blockSizeInBytes, int noOfBlocks)
        {
            InternalStream = stream;
            InternalMemoryMap = memoryMappedFile;

            NoOfBlocks = noOfBlocks;
            BlockSize = blockSize;
            BlockSizeInBytes = blockSizeInBytes;

            ViewAccessor = InternalMemoryMap.CreateViewAccessor();
            StartAddress = ViewAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
        }

        /// <summary>
        /// Returns a pointer to the first element of the first block.
        /// </summary>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public IntPtr DangerousGetHandle()
        {
            MappableFileStreamManager.FlushWaitHandle.Wait();
            return StartAddress;
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        private T* NonBlockingDangerousGetHandle(int blockNo) => (T*)StartAddress + checked(blockNo * (long)BlockSize);

        /// <summary>
        /// Method that updates the booklet.
        /// </summary>
        /// <param name="blockNo"></param>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        public void TouchBlock(int blockNo)
        {
            InternalStore.AddOrUpdate(blockNo, (x) => new Booklet(blockNo), (x, b) => b.Touch());
        }

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public void Unmap(int blockNo)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(blockNo);

            // Unlock the address range.
            var unlockResult = HybridHelper.VirtualUnlock((nint)blockAddress, BlockSizeInBytes);

            // Remove it from the booklet tracking.
            InternalStore.TryRemove(blockNo, out _);
        }

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public void UnmapRange(int startBlock, int noOfBlocks)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(startBlock);

            // Unlock the address range.
            HybridHelper.VirtualUnlock((nint)blockAddress, BlockSizeInBytes * noOfBlocks);

            // Remove it from the booklet tracking.
            for (int i = startBlock; i < noOfBlocks; i++)
                InternalStore.TryRemove(i, out _);
        }

        /// <summary>
        /// Suggests the memory manager that the memory area is invalid and can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public void Invalidate(int blockNo)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(blockNo);

            // Unlock the address range.
            HybridHelper.DiscardVirtualMemory((nint)blockAddress, BlockSizeInBytes);

            // Remove it from the booklet tracking.
            InternalStore.TryRemove(blockNo, out _);
        }

        /// <summary>
        /// Suggests the memory manager that the memory area is invalid and can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public void Invalidate(int startBlock, int noOfBlocks)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(startBlock);

            // Unlock the address range.
            HybridHelper.DiscardVirtualMemory((nint)blockAddress, BlockSizeInBytes * noOfBlocks);

            // Remove it from the booklet tracking.
            for (int i = startBlock; i < noOfBlocks; i++)
                InternalStore.TryRemove(i, out _);
        }

        /// <summary>
        /// Gets the amount of currently mapped memory.
        /// </summary>
        /// <returns></returns>
        public MappableStreamInfo GetMemoryInfo()
        {
            return new MappableStreamInfo(InternalStore.Values, BlockSize, BlockSizeInBytes);
        }

        public void Flush()
        {
            ViewAccessor.Flush();
        }

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Disposes allocated unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            GC.SuppressFinalize(this);

            ViewAccessor?.Dispose();
            InternalMemoryMap?.Dispose();
            InternalStream?.Dispose();

            MappableFileStreamManager.RemoveStream(this);
        }

    }
}
