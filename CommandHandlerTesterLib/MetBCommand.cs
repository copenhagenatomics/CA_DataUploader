using CA.LoopControlPluginBase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommandHandlerTesterLib
{
    public class MetBCommand : LoopControlCommand
    {
        public override string Name => "metB";
        public override string Description => "will print the second value after metB";
        public override string ArgsHelp => "[arg1] [arg2]";

        protected override Task Command(List<string> args)
        {
            Console.WriteLine();
            Console.WriteLine(args[2]);
            return Task.CompletedTask;
        }
    }
}
