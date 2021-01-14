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
            try
            {
                Console.WriteLine("Waiting for the next multiple of 10 value for IterationSensor");
                var val = await WhenSensorValue("IterationSensor", v => v % 10 == 0, Seconds(12));
                Console.WriteLine($"IterationSensor = {val}. Waiting for value to increase by 3");
                var vector = await When(e => e["IterationSensor"].Value >= val + 3, Seconds(5)); // alt way of waiting / can target multiple sensor values too
                Console.WriteLine($"IterationSensor = {vector["IterationSensor"]}. Waiting 4 seconds");
                await Task.Delay(Seconds(4));
                Console.WriteLine($"Running the help command");
                ExecuteCommand("help");
                Console.WriteLine("Finished long action");
            }
            catch (TaskCanceledException)
            {
                CALog.LogErrorAndConsoleLn(LogID.A, "longaction aborted: timed out waiting for a sensor value");
            }
            catch (Exception ex)
            {
                CALog.LogException(LogID.A, ex);
            }
        }
    }
}
