
using System;
using System.Runtime.CompilerServices;

namespace MappableFileStream
{
    public static class MappableFileStreamExtensions
    {

        /// <summary>
        /// Writes the <paramref name="data"/>.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <param name="data"></param>
        public static void Write<T>(this IMappableFileStream<T> stream, int blockNo, ReadOnlySpan<T> data) where T : unmanaged
        {
            data.CopyTo(stream.GetWriteHandle(blockNo));
        }


        /// <summary>
        /// Request a writeable structure for the given <paramref name="blockNo"/>.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static ReadOnlySpan<T> DangerousGetReadHandle<T>(this IReadOnlyMappableFileStream<T> stream, int blockNo) where T : unmanaged
        {
            unsafe { return new(stream.DangerousGetHandle(blockNo), stream.BlockSize); }
        }

        /// <summary>
        /// Request a writeable structure for the given <paramref name="blockNo"/>.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Span<T> DangerousGetWriteHandle<T>(this IMappableFileStream<T> stream, int blockNo) where T : unmanaged
        {
            unsafe { return new(stream.DangerousGetHandle(blockNo), stream.BlockSize); }
        }

        /// <summary>
        /// Request a readable structure for the given <paramref name="blockNo"/>.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static ReadOnlySpan<T> GetReadHandle<T>(this IReadOnlyMappableFileStream<T> stream, int blockNo) where T : unmanaged
        {
            stream.TouchBlock(blockNo);
            return stream.DangerousGetReadHandle(blockNo);
        }

        /// <summary>
        /// Request a writeable structure for the given <paramref name="blockNo"/>.
        /// </summary>
        /// <param name="blockNo"></param>
        /// <returns></returns>
        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static Span<T> GetWriteHandle<T>(this IMappableFileStream<T> stream, int blockNo) where T : unmanaged
        {
            stream.TouchBlock(blockNo);
            return stream.DangerousGetWriteHandle(blockNo);
        }
    }
}
