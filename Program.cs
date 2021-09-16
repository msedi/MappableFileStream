using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Windows.Win32;
using System.Security;
using System.ComponentModel;

namespace MemoryMapFile
{
    class Program
    {
     
        static async Task Main(string[] args)
        {
            var TotalMemory = GetMemory().ullAvailPhys;
            var MemoryThreshold = (ulong)(TotalMemory * 0.2);





        Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            int SizeX, SizeY;

            SizeX = SizeY = 512;

            DataSource volumeStart = new Volume(SizeX, SizeY, 10000);//, "C:\\MMF\\SourceVolume.tmp");

            List<DataSource> sources = new List<DataSource>();

            sources.Add(volumeStart);
            for (int i = 0; i < 10; i++)
            {
                Processor p = new Processor(volumeStart);
                volumeStart = p;

                sources.Add(p);
            }


            int currentCount = 0;
            int lastCount = 0;

            DateTime lastTime = DateTime.Now;

            Timer timer = new Timer(callback);

            void callback(object state)
            {
                if (lastCount == currentCount)
                    return;

                int nowCount = currentCount;
                var nowTime = DateTime.Now;

                int diff = nowCount - lastCount;
                TimeSpan diffTime = nowTime - lastTime;


                lastCount = currentCount;
                lastTime = nowTime;

                Console.WriteLine($"{nowCount} - Items per s: {diff / diffTime.TotalSeconds} ({GetMemory().ullAvailPhys})");
            }

            timer.Change(0, 1000);

            await Task.Run( () =>
             {
               Parallel.For(0, volumeStart.SizeZ, i =>
              //   for (int i = 0; i < volumeStart.SizeZ; i++)
                 {
                     var data = volumeStart.getVolume(i);

                     Interlocked.Increment(ref currentCount);
                 }
                 );
             });

            Console.WriteLine($"{HybridFileStream<int>.Watch.Elapsed.TotalMilliseconds}ms");



            stopWatch.Stop();
            TimeSpan ts = stopWatch.Elapsed;
            Console.WriteLine("RunTime: " + ts.TotalMilliseconds + "ms");

        }

        private static readonly uint SizeOfMemStat = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));

        public static MEMORYSTATUSEX GetMemory()
        {
            MEMORYSTATUSEX memstat = new()
            {
                dwLength = SizeOfMemStat
            };

            if (!GlobalMemoryStatusEx(ref memstat))
                throw new Win32Exception(Marshal.GetHRForLastWin32Error());

            return memstat;
        }

        private static readonly List<byte[]> AllocatedMemory = new();

        [SuppressUnmanagedCodeSecurity]
        [DllImport("kernel32")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }

    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullwAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
