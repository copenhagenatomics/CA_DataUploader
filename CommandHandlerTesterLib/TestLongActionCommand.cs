﻿using CA.LoopControlPluginBase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommandHandlerTesterLib
{
    public class TestLongActionCommand : LoopControlCommand
    {
        public override string Name => "LongAction";
        public override string Description => "runs a bit longer action";
        protected async override Task Command(List<string> args)
        {
            Console.WriteLine("Waiting for the next multiple of 10 value for IterationSensor");
            var val = await WhenSensorValue("IterationSensor", v => v % 10 == 0, Seconds(12));
            Console.WriteLine($"IterationSensor = {val}. Waiting for value to increase by 3");
            var vector = await When(e => e["IterationSensor"] >= val + 3, Seconds(5)); // alt way of waiting / can target multiple sensor values too
            Console.WriteLine($"IterationSensor = {vector["IterationSensor"]}. Waiting 4 seconds");
            await Task.Delay(Seconds(4));
            Console.WriteLine($"Running the help command");
            ExecuteCommand("help");
            Console.WriteLine("Finished long action");
        }
    }
}
