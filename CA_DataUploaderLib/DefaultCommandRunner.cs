using System;
using System.Collections.Generic;
using System.Linq;

namespace CA_DataUploaderLib
{
    /// <remarks>
    /// runs the registered commands directly on the calling thread (the input handling thread),
    /// showing accept/reject messages on the screen and A.log.
    /// </remarks>
    public class DefaultCommandRunner : ICommandRunner
    {
        private readonly Dictionary<string, List<Func<List<string>, bool>>> _commands = new(StringComparer.OrdinalIgnoreCase);
        public Action AddCommand(string name, Func<List<string>, bool> func)
        {
            if (_commands.ContainsKey(name))
                _commands[name].Add(func);
            else
                _commands.Add(name, new List<Func<List<string>, bool>> { func });

            return () =>
            {
                _commands[name].Remove(func);
                if (_commands[name].Count == 0)
                    _commands.Remove(name);
            };
        }

        public bool Run(string cmdString, bool isUserCommand)
        {
            var cmd = cmdString.Trim().Split(' ').Select(x => x.Trim()).ToList();

            if (!cmd.Any())
                return false;

            string commandName = cmd[0];
            if (!_commands.TryGetValue(commandName, out var commandFunctions))
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - unknown command");
                return false;
            }

            var res = RunCommandFunctions(cmdString, cmd, commandFunctions);
            if (commandName.Equals("help", StringComparison.OrdinalIgnoreCase))
                CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");  // end help menu divider
            else if (res == false)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command");
            else if (res == true)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - command accepted");
            else
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command / accepted by some subsystems");

            return res ?? true; //note we also return true when res is null, which means some executions succeeded and one failed
        }

        /// <returns><c>true</c> if all functions executed the command, <c>false</c> if all functions rejected the command, <c>null</c> for a mix of executed/rejected</returns>
        /// <remarks>
        /// If a command is rejected, we do not run the rest of the commands. This runs on the order the commands were registered.
        /// </remarks>
        private static bool? RunCommandFunctions(string cmdString, List<string> cmd, List<Func<List<string>, bool>> commandFunctions)
        {
            for (int i = 0; i < commandFunctions.Count; i++)
            {
                try
                {
                    if (!commandFunctions[i](cmd))
                        return i == 0 ? false : null;
                }
                catch (ArgumentException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Command: {cmdString} - invalid arguments", ex);
                    return i == 0 ? false : null;
                }
            }

            return true;
        }
    }
}
