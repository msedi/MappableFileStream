using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MappableFileStream
{
    class Program
    {
        static bool Cancel = false;

        static async Task Main(string[] args)
        {
            int SizeX, SizeY;

            SizeX = SizeY = 2048;

            var process = Process.GetCurrentProcess();
            var max= (nint)(HybridHelper.GetOSMemory().ullAvailPhys * 0.5d);
            process.MaxWorkingSet = (nint)(HybridHelper.GetOSMemory().ullAvailPhys * 0.5d);
            // process.MinWorkingSet = (nint)(HybridHelper.GetOSMemory().ullAvailPhys * 0.5d);

           MappableFileStreamManager.SetMaxMemory((ulong)(HybridHelper.GetOSMemory().ullAvailPhys * 0.3d));

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();


            DataSource volumeStart = new Volume(SizeX, SizeY, 1000);

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

            var headline = "{0,5} | {1,12} | {2,12}";

            Timer timer = new Timer(callback);

             Console.WriteLine(String.Format(headline,
                "Count",
                "WorkingSet",
                "Avail. Phys"
                ));


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

                process.Refresh();
                using (MappableFileStreamManager.WaitForDataAccess())
                {
                    Console.WriteLine(String.Format(headline,
                        nowCount,
                        process.WorkingSet64,
                        (long)process.MaxWorkingSet
                        ));
                }

                //-Items per s: { diff / diffTime.TotalSeconds} ({ HybridHelper.GetOSMemory().ullAvailPhys}) 
            }

            timer.Change(0, 1000);


            Console.CancelKeyPress += ConsoleCancelEventHandler;
            await Task.Run(() =>
            {
                Parallel.For(0, volumeStart.SizeZ, (i, loopState) =>
                //    for (int i = 0; i < volumeStart.SizeZ; i++)
                {
                    var data = volumeStart.GetData(i);

                    Interlocked.Increment(ref currentCount);

                    if (Cancel)
                    {
                        loopState.Break();
                        return;
                    }
                }
                   );
            });

            //Console.WriteLine($"{HybridFileStream<int>.Watch.Elapsed.TotalMilliseconds}ms");


            Stopwatch disposeWatch = Stopwatch.StartNew();
            volumeStart.Dispose();
            foreach (var processor in sources)
                processor.Dispose();
            Console.WriteLine("Dispose: " + disposeWatch.Elapsed.TotalMilliseconds + "ms");

            TimeSpan ts = stopWatch.Elapsed;
            Console.WriteLine("RunTime: " + ts.TotalMilliseconds + "ms");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Finished");
        }

        public static void ConsoleCancelEventHandler(object? sender, ConsoleCancelEventArgs e)
        {
            Cancel = true;
        }
    }
}
