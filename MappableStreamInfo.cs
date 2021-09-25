using System;
using System.Buffers;
using System.Collections.Generic;

namespace MappableFileStream
{
    public readonly struct MappableStreamInfo
    {
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

            BlockSize = blockSize;
            BlockSizeInBytes = blockSizeInBytes;

            MappedMemory = (ulong)ItemCount * (ulong)BlockSizeInBytes;
        }
    }
}
