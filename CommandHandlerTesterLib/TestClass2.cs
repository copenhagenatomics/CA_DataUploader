using CA_DataUploaderLib;
using System;
using System.Collections.Generic;

namespace CommandHandlerTesterLib
{
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

}
