﻿using CA_DataUploaderLib;
using System;
using System.Collections.Generic;

namespace CommandHandlerTesterLib
{
    public class TestClass1 : LoopControlExtension
    {
        public TestClass1(CommandHandler cmd) : base(cmd)
        {
            cmd.AddCommand("metA", MethodA);
            cmd.AddCommand("help", HelpMenu);
        }

        protected override void OnNewVectorReceived(object sender, NewVectorReceivedArgs e) 
            => Console.WriteLine($"Iteration: { e["IterationSensor"].Value }");

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
