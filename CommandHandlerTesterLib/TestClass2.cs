using CA_DataUploaderLib;
using System;
using System.Collections.Generic;

namespace CommandHandlerTesterLib
{
    public class TestClass2 : LoopControlExtension
    {
        public TestClass2(CommandHandler cmd) : base(cmd)
        {
            AddCommand("metB", MethodB);
            AddCommand("help", HelpMenu);
        }

        private bool MethodB(List<string> args)
        {
            Console.WriteLine();
            Console.WriteLine(args[2]);
            return true;
        }

        private bool HelpMenu(List<string> args)
        {
            CALog.LogInfoAndConsoleLn(LogID.A, "metB [arg1] [arg2]               - will print the second value after metB");
            return true;
        }
    }
}
