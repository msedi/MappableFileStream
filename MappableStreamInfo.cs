using System;
using System.Buffers;
using System.Collections.Generic;

namespace MappableFileStream
{
    public readonly struct MappableStreamInfo
    {
        private readonly Booklet[] InternalBooklets;

        public ReadOnlySpan<Booklet> GetBooklets() => InternalBooklets.AsSpan(0, ItemCount);

        /// <summary>
        /// Gets the amount of currently mapped memory.
        /// </summary>
        /// <returns></returns>
        public ulong MappedMemory { get; init; }

        /// <summary>
        /// Gets the amount of items that are currently mapped.
        /// </summary>
        /// <returns></returns>
        public int ItemCount { get; init; }

        public int BlockSize { get; init; }

        public int BlockSizeInBytes { get; init; }

        public MappableStreamInfo(ICollection<Booklet> booklets, int blockSize, int blockSizeInBytes)
        {
            ItemCount = booklets.Count;
            InternalBooklets = ArrayPool<Booklet>.Shared.Rent(ItemCount);
            booklets.CopyTo(InternalBooklets, 0);

            BlockSize = blockSize;
            BlockSizeInBytes = blockSizeInBytes;

            MappedMemory = (ulong)ItemCount * (ulong)BlockSizeInBytes;
        }


        public void Dispose()
        {
            ArrayPool<Booklet>.Shared.Return(InternalBooklets);
        }
    }
}
