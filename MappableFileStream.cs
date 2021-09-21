
using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MappableFileStream
{
    public abstract class MappableFileStream : IDisposable
    {
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

        public abstract void Dispose();
    }
}
