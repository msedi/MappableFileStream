using System;
using System.Diagnostics;
using System.IO;

namespace MappableFileStream
{
    [DebuggerDisplay("{hs}")]
    unsafe class Volume : DataSource
    {
        readonly IMappableFileStream<int> hs;

        readonly int N;
        string TempPath = "D:\\MMF";

       static  int Number=0;
    
        public Volume(int sizex, int sizey, int sizez, string fileName = null)
        {
            SizeX = sizex;
            SizeY = sizey;
            SizeZ = sizez;

            N = SizeX * SizeY;

            if (!Directory.Exists(TempPath))
                Directory.CreateDirectory(TempPath);

            
            if (string.IsNullOrEmpty(fileName))
            {
                //Guid.NewGuid().ToString()
                fileName = Path.Combine(TempPath, (Number++).ToString()) + ".tmp";
                hs = MappableFileStream.CreateNew<int>(fileName, N, SizeZ);
            }
            else
            {
                //stream = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, (int)SliceSize, FileOptions.RandomAccess);
            }
        }

        public override void Dispose()
        {
            hs.Dispose();
        }

        public override ReadOnlySpan<int> GetData(int sliceIndex)
        {
            return hs.GetReadHandle(sliceIndex);
        }


        public void SetData(int sliceIndex, int[] data)
        {
            hs.Write(sliceIndex, data);
        }

        public Span<int> GetWriteHandle(int sliceIndex)
        {
            return hs.GetWriteHandle(sliceIndex);  
        }
    }
}