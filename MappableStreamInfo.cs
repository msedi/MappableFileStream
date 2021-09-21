using System.Collections.Generic;

namespace MappableFileStream
{
    public readonly struct MappableStreamInfo
    {
        public IReadOnlyList<Booklet> Booklets { get; init; }

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
    }
}
