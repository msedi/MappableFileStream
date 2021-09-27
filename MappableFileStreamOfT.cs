
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
        /// <summary>
        /// Creates a new instance of <see cref="MappableFileStream{T}"/>.
        /// </summary>
        /// <param name="blockSize"></param>
        /// <param name="noOfBlocks"></param>
        internal MappableFileStream(FileStream stream, MemoryMappedFile memoryMappedFile, int blockSize, int blockSizeInBytes, int noOfBlocks) : base(stream, memoryMappedFile, blockSize, blockSizeInBytes, noOfBlocks)
        {
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        private T* NonBlockingDangerousGetHandle(int blockNo)
        {
            var blockAddress = (T*)StartAddress + checked(blockNo * (long)BlockSize);
          //  HybridHelper.VirtualUnlock((nint)blockAddress, BlockSizeInBytes);

            return blockAddress;
        }

        /// <summary>
        /// Return a pointer to the first element of the <paramref name="blockNo"/> block.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public  unsafe T* DangerousGetHandle(int blockNo) 
        {
            var blockAddress = (T*)DangerousGetHandle() + checked(blockNo * (long)BlockSize);
            //HybridHelper.VirtualUnlock((nint)blockAddress, BlockSizeInBytes);

            return blockAddress;        
        }

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public override void Unmap(int blockNo)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(blockNo);

            // Unlock the address range.
            var unlockResult = HybridHelper.VirtualUnlock(blockAddress, BlockSizeInBytes);

            // Remove it from the booklet tracking.
            InternalStore.TryRemove(blockNo, out _);
        }

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public override void UnmapRange(int startBlock, int noOfBlocks)
        {
            // Get the address of the block.
            var blockAddress = NonBlockingDangerousGetHandle(startBlock);

            // Unlock the address range.
            HybridHelper.VirtualUnlock((nint)blockAddress, BlockSizeInBytes * noOfBlocks);

            HybridHelper.FlushViewOfFile((nint)blockAddress, BlockSizeInBytes * noOfBlocks);

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
    }
}
