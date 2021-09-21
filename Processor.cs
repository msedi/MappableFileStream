using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace MappableFileStream
{
    unsafe class Processor : DataSource
    {
        DataSource InputVolume;
        Volume OutputVolume;

        public Processor(DataSource inputVolume)
        {
            InputVolume = inputVolume;
            OutputVolume = new Volume(inputVolume.SizeX, inputVolume.SizeY, inputVolume.SizeZ);
            SizeX = inputVolume.SizeX;
            SizeY = inputVolume.SizeY;
            SizeZ = inputVolume.SizeZ;
        }


        public override ReadOnlySpan<int> GetData(int sliceIndex)
        {
            var inputData = InputVolume.GetData(sliceIndex);
            var outputData = OutputVolume.GetWriteHandle(sliceIndex);

            for (int i = 0; i < inputData.Length; i++)
            {
                outputData[i] = inputData[i] + 10;
            }
            
            return outputData;
        }
    }
}