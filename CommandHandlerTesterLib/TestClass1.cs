using CA_DataUploaderLib;
using System;
using System.Collections.Generic;

namespace CommandHandlerTesterLib
{
    public class TestClass1 : LoopControlPlugin
    {
        public TestClass1(CommandHandler cmd) : base(cmd)
        {
            AddCommand("metA", MethodA);
            AddCommand("help", HelpMenu);
        }

        protected override void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) 
            => Console.Write(e["IterationSensor"].Value % 2 == 0 ? "." : string.Empty);

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
}
