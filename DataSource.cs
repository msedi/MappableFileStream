using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;

namespace MappableFileStream
{
    abstract class DataSource
    {
        public int SizeX;
        public int SizeY;
        public int SizeZ;

        public DataSource()
        {
        }

        abstract public ReadOnlySpan<int> GetData(int sliceIndex);

        public void SaveToFile(string fileName)
        {
            using var stream = File.Create(fileName);
            stream.SetLength((long)SizeX * SizeY * SizeZ * sizeof(int));

            for (int z = 0; z < SizeZ; z++)
            {
                var span = GetData(z);

                var spantowrite = MemoryMarshal.Cast<int, byte>(span);
                stream.Write(spantowrite);
            }
        }
    }
}
