using System;
using System.Collections.Generic;
using System.Threading;
using CA_DataUploaderLib;

namespace CommandHandlerTester
{
    class Program
    {
        static void Main(string[] args)
        {
            using var cmd = new CommandHandler();
            new PluginsLoader(cmd).LoadPlugins();
            Console.WriteLine("run help for available commands");
            int i = 0;
            while (cmd.IsRunning)
            {
                cmd.OnNewVectorReceived(new List<SensorSample> { new SensorSample("IterationSensor", i++) }, DateTime.UtcNow, null);
                Thread.Sleep(1000);
            }

            Console.WriteLine();
            Console.WriteLine("Stopped running... Press any key to exit");
            Console.ReadKey();
        }
    }
}
