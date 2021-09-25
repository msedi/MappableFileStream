using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

namespace MappableFileStream
{
    [DebuggerDisplay("{InternalStream.Name}")]
    public abstract class MappableFileStream : IDisposable
    {
        readonly FileStream InternalStream;
        readonly MemoryMappedFile InternalMemoryMap;
        readonly MemoryMappedViewAccessor ViewAccessor;

        protected readonly nint StartAddress;

        readonly object SyncRoot = new();

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
        protected readonly int BlockSizeInBytes = 0;

        internal readonly ConcurrentDictionary<int, Booklet> InternalStore = new(Environment.ProcessorCount, 100);

        /// <summary>
        /// Gets the amount of currently mapped memory.
        /// </summary>
        /// <returns></returns>
        internal MappableStreamInfo GetMemoryInfo() => new MappableStreamInfo(InternalStore.Values, BlockSize, BlockSizeInBytes);

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
        public abstract void Unmap(int blockNo);

        /// <summary>
        /// Suggests the memory manager that the memory area can be paged if necessary.
        /// </summary>
        /// <param name="blockNo"></param>
        public abstract void UnmapRange(int startBlock, int noOfBlocks);

        public void Flush()
        {
            ViewAccessor.Flush();
        }

        #region IDisposable

        public bool IsDisposed {  get; private set; }   

        /// <summary>
        /// Disposes allocated unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            GC.SuppressFinalize(this);

            InternalMemoryMap?.Dispose();
            ViewAccessor?.Dispose();
            InternalStream?.Dispose();

            MappableFileStreamManager.RemoveStream(this);
        }

        #endregion

        #region Factory Methods 

        private static (int blockSizeInBytes, long totalSizeInBytes) GetSize<T>(int blockSize, int noOfBlocks) where T : unmanaged
        {
            unsafe
            {
                int blockSizeInBytes = blockSize * (int)sizeof(T);
                var totalSize = checked(noOfBlocks * (long)blockSizeInBytes);

                return (blockSizeInBytes, totalSize);
            }
        }

        /// <summary>
        /// Creates a 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <param name="blockSize"></param>
        /// <param name="noOfBlocks"></param>
        /// <returns></returns>
        public static IMappableFileStream<T> CreateNew<T>(string fileName, int blockSize, int noOfBlocks) where T : unmanaged
        {
            var (blockSizeInBytes, totalSizeInBytes) = GetSize<T>(blockSize, noOfBlocks);

            var fileStream = new FileStream(fileName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite, blockSize, FileOptions.DeleteOnClose | FileOptions.RandomAccess);
            fileStream.SetLength(totalSizeInBytes);
            HybridHelper.SetValidFileRegion(fileStream.SafeFileHandle, totalSizeInBytes);

            var memoryMappedFile = MemoryMappedFile.CreateFromFile(fileStream, null, totalSizeInBytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);

            MappableFileStream<T> mfs = new(fileStream, memoryMappedFile, blockSize, blockSizeInBytes, noOfBlocks);
            MappableFileStreamManager.AddStream(mfs);

            return mfs;
        }

        /// <summary>
        /// Creates a 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <param name="blockSize"></param>
        /// <param name="noOfBlocks"></param>
        /// <returns></returns>
        public static IReadOnlyMappableFileStream<T> OpenRead<T>(string fileName, int blockSize, int noOfBlocks) where T : unmanaged
        {
            var (blockSizeInBytes, _) = GetSize<T>(blockSize, noOfBlocks);

            var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, FileOptions.RandomAccess);

            var memoryMappedFile = MemoryMappedFile.CreateFromFile(fileStream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

            MappableFileStream<T> mfs = new(fileStream, memoryMappedFile, blockSize, blockSizeInBytes, noOfBlocks);
            MappableFileStreamManager.AddStream(mfs);

            return mfs;
        }

        #endregion
    }
}
