using System;

namespace CA.LoopControlPluginBase
{
    public interface ILoopControlPlugin
    {
        string[] Model { get; }
        void MakeDecision(VectorArgs args);
    }
    public abstract class LoopControlPlugin : ILoopControlPlugin
    {
        public string Name { get; }
        public abstract string[] Model { get; }
        public string StateWaitName { get; }

        public LoopControlPlugin(string name)
        {
            Name = name;
            StateWaitName = name + "statestarted";
        }

        public abstract void MakeDecision(VectorArgs args);
        public bool After(VectorArgs args, double timeoutMs) => args.After(StateWaitName, timeoutMs);
        public bool After(VectorArgs args, string timeoutNameInVector) => After(args, args[timeoutNameInVector]);
    }

    public class ArgonCycleExample : ILoopControlPlugin
    {
        public const string LuminoxP = nameof(LuminoxP);
        public const string conf_highpressure = nameof(conf_highpressure);
        public const string conf_highpressuremilliseconds = nameof(conf_highpressuremilliseconds);
        public const string conf_lowpressure = nameof(conf_lowpressure);
        public const string conf_reachpressuremilliseconds = nameof(conf_reachpressuremilliseconds);
        public const string out_argonout_on = nameof(out_argonout_on);
        public const string out_argonin_on = nameof(out_argonin_on);
        public const string state_argoncycle = nameof(state_argoncycle);
        public const string state_argoncycle_waittime = nameof(state_argoncycle_waittime);
        public const string vectortime = nameof(vectortime);

        public string[] Model => new []{
            LuminoxP, conf_highpressure, conf_highpressuremilliseconds, conf_lowpressure, conf_reachpressuremilliseconds, out_argonout_on, out_argonin_on, state_argoncycle, state_argoncycle_waittime, vectortime
        };

        public void MakeDecision(VectorArgs args)
        {//pseudo code, assumes: vectortime is in milliseconds (and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args[state_argoncycle];
            var (newState, inputOn, outputOn) = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => (States.OpenInput, true, false),
                States.Off => (States.Off, false, false),
                _ when args.UserCommands.Contains("argoncycle off") => (States.Off, false, false),
                _ when args.UserCommands.Contains("argoncycle on") => (States.CloseOutput, false, false),
                States.OpenInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => (States.Off, false, false),
                States.OpenInput when args[LuminoxP] >= args[conf_highpressure] => (States.CloseInput, false, false),
                States.OpenInput => (States.OpenInput, true, false),
                States.CloseInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_highpressuremilliseconds] => (States.OpenOutput, false, true),
                States.CloseInput => (States.CloseInput, false, false),
                States.OpenOutput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => (States.Off, false, false),
                States.OpenOutput when args[LuminoxP] <= args[conf_lowpressure] => (States.CloseOutput, false, false),
                States.OpenOutput => (States.OpenOutput, false, true),
                States.CloseOutput when args[vectortime] >= args[state_argoncycle_waittime] + 200 => (States.OpenInput, false, false),
                States.CloseOutput => (States.CloseOutput, false, false),
                _ => (States.Off, false, false)
            };
            args[out_argonin_on] = inputOn ? 1.0 : 0.0;
            args[out_argonout_on] = outputOn ? 1.0 : 0.0;
            if (newState == currState) return;
            args[state_argoncycle_waittime] = args[vectortime];
        }

        public void MakeDecisionSplitStateAction(VectorArgs args)
        {//pseudo code, assumes: vectortime is in milliseconds (and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args[state_argoncycle];
            var newState = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => States.OpenInput,
                _ when args.UserCommands.Contains("argoncycle off") => States.Off,
                _ when args.UserCommands.Contains("argoncycle on") => States.CloseOutput,
                States.OpenInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => States.Off,
                States.OpenInput when args[LuminoxP] >= args[conf_highpressure] => States.CloseInput,
                States.CloseInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_highpressuremilliseconds] => States.OpenOutput,
                States.OpenOutput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => States.Off,
                States.OpenOutput when args[LuminoxP] <= args[conf_lowpressure] => States.CloseOutput,
                States.CloseOutput when args[vectortime] >= args[state_argoncycle_waittime] + 200 => States.OpenInput,
                var s => s //in all other cases stay in the current state
            };
            if (newState != currState)
                args[state_argoncycle_waittime] = args[vectortime];
            (args[out_argonin_on], args[out_argonout_on]) = newState switch 
            { 
                States.OpenInput => (1.0, 0.0), 
                States.OpenOutput => (0.0, 1.0), 
                _ => (0.0, 0.0) 
            };
        }

        public void MakeDecisionSimplerOutputForThisCase(VectorArgs args)
        {//pseudo code, assumes: vectortime is in milliseconds (and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args[state_argoncycle];
            var newState = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => States.OpenInput,
                _ when args.UserCommands.Contains("argoncycle off") => States.Off,
                _ when args.UserCommands.Contains("argoncycle on") => States.CloseOutput,
                States.OpenInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => States.Off,
                States.OpenInput when args[LuminoxP] >= args[conf_highpressure] => States.CloseInput,
                States.CloseInput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_highpressuremilliseconds] => States.OpenOutput,
                States.OpenOutput when args[vectortime] >= args[state_argoncycle_waittime] + args[conf_reachpressuremilliseconds] => States.Off,
                States.OpenOutput when args[LuminoxP] <= args[conf_lowpressure] => States.CloseOutput,
                States.CloseOutput when args[vectortime] >= args[state_argoncycle_waittime] + 200 => States.OpenInput,
                var s => s
            };
            if (newState != currState)
                args[state_argoncycle_waittime] = args[vectortime];
            args[out_argonin_on] = newState == States.OpenInput ? 1.0 : 0.0;
            args[out_argonin_on] = newState == States.CloseOutput ? 1.0 : 0.0;
        }

        public void MakeDecisionWithWaitExtension(VectorArgs args)
        {//pseudo code, assumes: vectortime is in milliseconds (and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args[state_argoncycle];
            var newState = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => States.OpenInput,
                _ when args.UserCommands.Contains("argoncycle off") => States.Off,
                _ when args.UserCommands.Contains("argoncycle on") => States.CloseOutput,
                States.OpenInput when args.After(state_argoncycle_waittime, conf_reachpressuremilliseconds) => States.Off,
                States.OpenInput when args[LuminoxP] >= args[conf_highpressure] => States.CloseInput,
                States.CloseInput when args.After(state_argoncycle_waittime, conf_highpressuremilliseconds) => States.OpenOutput,
                States.OpenOutput when args.After(state_argoncycle_waittime, conf_reachpressuremilliseconds) => States.Off,
                States.OpenOutput when args[LuminoxP] <= args[conf_lowpressure] => States.CloseOutput,
                States.CloseOutput when args.After(state_argoncycle_waittime, 200) => States.OpenInput,
                var s => s
            };
            if (newState != currState)
                args.ResetWaitTime(state_argoncycle_waittime);
            args[out_argonin_on] = newState == States.OpenInput ? 1.0 : 0.0;
            args[out_argonin_on] = newState == States.CloseOutput ? 1.0 : 0.0;
        }

        public enum States
        { 
            Off = 0,
            OpenInput = 1,
            CloseInput = 2,
            OpenOutput = 3,
            CloseOutput = 4
        }
    }

    public class ArgonCycleExampleWithBaseClassAfter : LoopControlPlugin
    {
        public const string LuminoxP = nameof(LuminoxP);
        public const string conf_highpressure = nameof(conf_highpressure);
        public const string conf_highpressuremilliseconds = nameof(conf_highpressuremilliseconds);
        public const string conf_lowpressure = nameof(conf_lowpressure);
        public const string conf_reachpressuremilliseconds = nameof(conf_reachpressuremilliseconds);
        public const string out_argonout_on = nameof(out_argonout_on);
        public const string out_argonin_on = nameof(out_argonin_on);
        public const string state_argoncycle = nameof(state_argoncycle);
        public const string state_argoncycle_waittime = nameof(state_argoncycle_waittime);
        public const string vectortime = nameof(vectortime);
        public ArgonCycleExampleWithBaseClassAfter() : base("argoncycle") { }
        public override string[] Model => new[] { 
            LuminoxP, conf_highpressure, conf_highpressuremilliseconds, conf_lowpressure, conf_reachpressuremilliseconds, out_argonout_on, out_argonin_on, state_argoncycle, state_argoncycle_waittime, 
            vectortime, StateWaitName
        };
        public override void MakeDecision(VectorArgs args)
        {
            // pseudo code, assumes: vectortime is in milliseconds(and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args[state_argoncycle];
            var newState = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => States.OpenInput,
                _ when args.UserCommands.Contains("argoncycle off") => States.Off,
                _ when args.UserCommands.Contains("argoncycle on") => States.CloseOutput,
                States.OpenInput when After(args, conf_reachpressuremilliseconds) => States.Off,
                States.OpenInput when args[LuminoxP] >= args[conf_highpressure] => States.CloseInput,
                States.CloseInput when After(args, conf_highpressuremilliseconds) => States.OpenOutput,
                States.OpenOutput when After(args, conf_reachpressuremilliseconds) => States.Off,
                States.OpenOutput when args[LuminoxP] <= args[conf_lowpressure] => States.CloseOutput,
                States.CloseOutput when After(args, 200) => States.OpenInput,
                var s => s
            };
            if (newState != currState)
                args.ResetWaitTime(state_argoncycle_waittime);
            args[out_argonin_on] = newState == States.OpenInput ? 1.0 : 0.0;
            args[out_argonin_on] = newState == States.CloseOutput ? 1.0 : 0.0;
        }

        public enum States
        {
            Off = 0,
            OpenInput = 1,
            CloseInput = 2,
            OpenOutput = 3,
            CloseOutput = 4
        }
    }

    public class ArgonCycleExampleWithoutConstants : LoopControlPlugin
    {
        public ArgonCycleExampleWithoutConstants() : base("argoncycle") { }
        public override string[] Model => new[] {
            "LuminoxP", "conf_highpressure", "conf_highpressuremilliseconds", "conf_lowpressure", "conf_reachpressuremilliseconds", "out_argonout_on", "out_argonin_on", "state_argoncycle", "state_argoncycle_waittime",
            "vectortime", StateWaitName
        };
        public override void MakeDecision(VectorArgs args)
        {
            // pseudo code, assumes: vectortime is in milliseconds(and fits the double range), valve subsystem's actuator cycle no longer depends on timeoff (does it automatically underneath + tracks if its not getting fresh commands)
            var currState = (States)args["state_argoncycle"];
            var newState = currState switch
            {
                States.Off when args.UserCommands.Contains("argoncycle on") => States.OpenInput,
                _ when args.UserCommands.Contains("argoncycle off") => States.Off,
                _ when args.UserCommands.Contains("argoncycle on") => States.CloseOutput,
                States.OpenInput when After(args, "conf_reachpressuremilliseconds") => States.Off,
                States.OpenInput when args["LuminoxP"] >= args["conf_highpressure"] => States.CloseInput,
                States.CloseInput when After(args, "conf_highpressuremilliseconds") => States.OpenOutput,
                States.OpenOutput when After(args, "conf_reachpressuremilliseconds") => States.Off,
                States.OpenOutput when args["LuminoxP"] <= args["conf_lowpressure"] => States.CloseOutput,
                States.CloseOutput when After(args, 200) => States.OpenInput,
                var s => s
            };
            if (newState != currState)
                args.ResetWaitTime("state_argoncycle_waittime");
            args["out_argonin_on"] = newState == States.OpenInput ? 1.0 : 0.0;
            args["out_argonin_on"] = newState == States.CloseOutput ? 1.0 : 0.0;
        }

        public enum States
        {
            Off = 0,
            OpenInput = 1,
            CloseInput = 2,
            OpenOutput = 3,
            CloseOutput = 4
        }
    }

}
