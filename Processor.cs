using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace MemoryMapFile
{
    unsafe class Processor: DataSource
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

        
        public override ReadOnlySpan<int> getVolume(int sliceIndex)
        {
            int[] data = new int[InputVolume.SizeX* InputVolume.SizeY];
            var inputData = InputVolume.getVolume(sliceIndex);

            for (int i = 0; i < data.Length; i++)
            {

                //data[i] = Add(inputData, i);
                
                data[i] = inputData[i] + 10;

                //IntPtr hglobal = Marshal.AllocHGlobal(2048);
                //Marshal.FreeHGlobal(hglobal);

            }

            OutputVolume.setVolume(sliceIndex, data);
            return data;
        }



        //public IntPtr* Add (ReadOnlySpan<int> input, int i)
        //{
        //    int sizea = (Marshal.SizeOf(typeof(IntPtr)));
        //    IntPtr* result = Marshal.AllocHGlobal(sizea);
        //    int x = input[i] + 10;
        //    Marshal.WriteIntPtr(result, (IntPtr)x);
        //    Console.WriteLine(result);
        //    return *result;
        //}

        //public int Add(ReadOnlySpan<int> input, int i)
        //{
        //    int* p = null;
        //    int x = 0;
        //    x = new int;
        //    x = input[i] + 10;
        //    p = &x;
        //    return *p;
        //}
    }
}