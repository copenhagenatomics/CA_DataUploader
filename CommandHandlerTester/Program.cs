using System;
using System.Collections.Generic;
using System.Threading;
using CA_DataUploaderLib;

namespace CommandHandlerTester
{
    public class TestClass1
    {
        public TestClass1(CommandHandler cmd)
        {
            cmd.AddCommand("metA", MethodA);
            cmd.AddCommand("help", HelpMenu);
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, "metA [arg1]               - will print the first value after metA, try lower case too");
            return true;
        }

        private bool MethodA(List<string> args)
        {
            Console.WriteLine();
            Console.WriteLine(args[1]);
            return true;
        }

    }

    public class TestClass2
    {
        public TestClass2(CommandHandler cmd)
        {
            cmd.AddCommand("metB", MethodB);
        }

        private bool MethodB(List<string> args)
        {
            Console.WriteLine();
            Console.WriteLine(args[2]);
            return true;
        }

    }

    class Program
    {
        static void Main(string[] args)
        {
            using(var cmd = new CommandHandler())
            {
                var A = new TestClass1(cmd);
                var B = new TestClass2(cmd);

                while (cmd.IsRunning)
                {
                    Thread.Sleep(1000);
                    Console.Write(".");
                }
            }

            Console.WriteLine();
            Console.WriteLine("Stopped running... Press any key to exit");
            Console.ReadKey();
        }
    }
}
