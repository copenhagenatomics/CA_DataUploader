using CA_DataUploaderLib;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommandHandlerTesterLib
{
    public class MetACommand : LoopControlCommand
    {
        public override string Name => "metA";
        public override string Description => "will print the first value after metA, try lower case too";
        public override string ArgsHelp => "[arg1]";

        public override void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) 
            => Console.Write(e["IterationSensor"].Value % 2 == 0 ? "." : string.Empty);

        protected override Task Command(List<string> args)
        {
            Console.WriteLine();
            Console.WriteLine(args[1]);
            return Task.CompletedTask;
        }
    }
}
