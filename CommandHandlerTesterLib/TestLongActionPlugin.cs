using CA_DataUploaderLib;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommandHandlerTesterLib
{
    public class TestLongActionPlugin : LoopControlPlugin
    {
        public TestLongActionPlugin(CommandHandler cmd) : base(cmd)
        {
            AddCommand(nameof(LongAction), LongAction);
            AddCommand("help", HelpMenu);
        }

        private bool HelpMenu(List<string> _)
        { 
            CALog.LogInfoAndConsoleLn(LogID.A, "longaction                - runs a bit longer action");
            return true;
        }

        private bool LongAction(List<string> arg)
        {
            Task.Run(DoLongAction);
            return true;
        }

        private async Task DoLongAction()
        {
            Console.WriteLine("Waiting for the next multiple of 10 value for IterationSensor");
            var val = await WhenSensorValue("IterationSensor", v => v % 10 == 0);
            Console.WriteLine($"IterationSensor = {val}. Waiting 4 seconds");
            await Task.Delay(TimeSpan.FromSeconds(4));
            Console.WriteLine($"Running the help command");
            ExecuteCommand("help");
            Console.WriteLine("Finished long action");
        }
    }
}
