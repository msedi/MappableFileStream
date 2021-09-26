
using System;

namespace MappableFileStream
{

    public interface IReadOnlyMappableFileStream : IDisposable
    {
        /// <summary>
        /// Indicates if the stream has been disposed and cannot be used anymore.
        /// </summary>
        bool IsDisposed { get; }

        int BlockSize { get; }

        /// <summary>
        /// Returns a pointer to the first element of the first block.
        /// </summary>
        /// <returns></returns>
        IntPtr DangerousGetHandle();

        /// <summary>
        /// Advises the memory manager that the given block has been in use and shall not be preferred in paging when necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        void TouchBlock(int blockNo);

        /// <summary>
        /// Suggests the memory manager that the memory area is invalid and can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        void Invalidate(int blockNo);

        /// <summary>
        /// Suggests the memory manager that the memory area is invalid and can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        void Invalidate(int startBlock, int noOfBlocks);

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        void Unmap(int blockNo);

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        void UnmapRange(int startBlock, int noOfBlocks);

        void Flush();
    }



    public interface IMappableFileStream : IReadOnlyMappableFileStream
    {

    }

    public unsafe interface IReadOnlyMappableFileStream<T> : IReadOnlyMappableFileStream where T : unmanaged
    {
        unsafe T* DangerousGetHandle(int blockNo);
    }

    public interface IMappableFileStream<T> : IReadOnlyMappableFileStream<T> where T : unmanaged
    {
    }
}
