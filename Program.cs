using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MappableFileStream
{
    class Program
    {

        static async Task Main(string[] args)
        {
            var process = Process.GetCurrentProcess();
            process.MaxWorkingSet = (nint)(HybridHelper.GetOSMemory().ullAvailPhys * 0.9d);
          //  process.MinWorkingSet = (nint)(HybridHelper.GetOSMemory().ullAvailPhys * 0.5d);

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            int SizeX, SizeY;

            SizeX = SizeY = 256;

            DataSource volumeStart = new Volume(SizeX, SizeY, 10000);

            List<DataSource> sources = new List<DataSource>();

            sources.Add(volumeStart);
            for (int i = 0; i < 20; i++)
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

                HybridHelper.GetProcessWorkingSetSize(Process.GetCurrentProcess().SafeHandle, out var minSet, out var maxSet);


                var headline = "{0,5} | {1,12} | {2,12} | {3,12} | {4,12} | {5,12} | {6,12} | {7,12} | {8,12}";
                var (left,top) = Console.GetCursorPosition();
                Console.SetCursorPosition(0, 0);
                Console.WriteLine(String.Format(headline,
                    "Count",
                    "WorkingSet",
                    "PeakSet",
                    "MinSet",
                    "MaxSet",
                    "Virtual",
                    "PeakVirtual",
                    "Private",
                    "NonPaged"
                    ));

                Console.SetCursorPosition(left, top);

                process.Refresh();
                Console.WriteLine(String.Format(headline,
                    nowCount,
                    process.WorkingSet64,
                    process.PeakWorkingSet64,
                    (long)process.MinWorkingSet,
                    (long)process.MaxWorkingSet,
                    process.VirtualMemorySize64,
                    process.PeakVirtualMemorySize64,
                    process.PrivateMemorySize64,
                    process.NonpagedSystemMemorySize64

                    ));

                //-Items per s: { diff / diffTime.TotalSeconds} ({ HybridHelper.GetOSMemory().ullAvailPhys}) 
            }

            timer.Change(0, 1000);

            await Task.Run(() =>
            {
                Parallel.For(0, volumeStart.SizeZ, i =>
               //    for (int i = 0; i < volumeStart.SizeZ; i++)
                {
                    var data = volumeStart.GetData(i);

                    Interlocked.Increment(ref currentCount);
                }
                   );
            });

            //Console.WriteLine($"{HybridFileStream<int>.Watch.Elapsed.TotalMilliseconds}ms");



            stopWatch.Stop();
            TimeSpan ts = stopWatch.Elapsed;
            Console.WriteLine("RunTime: " + ts.TotalMilliseconds + "ms");

        }
    }
}
