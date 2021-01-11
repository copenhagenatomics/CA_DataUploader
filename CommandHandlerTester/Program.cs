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
            Console.WriteLine(typeof(CommandHandlerTesterLib.TestClass1).AssemblyQualifiedName);
            Console.WriteLine("run help for available commands");
            int i = 0;
            while (cmd.IsRunning)
            {
                cmd.OnNewVectorReceived(new List<SensorSample> { new SensorSample("IterationSensor", i++) });
                Thread.Sleep(1000);
            }

            Console.WriteLine();
            Console.WriteLine("Stopped running... Press any key to exit");
            Console.ReadKey();
        }
    }
}
