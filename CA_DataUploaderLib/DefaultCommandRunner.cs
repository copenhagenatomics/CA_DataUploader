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
        private readonly Dictionary<string, List<Func<List<string>, bool>>> _commands = new();
        public Action AddCommand(string name, Func<List<string>, bool> func)
        {
            name = name.ToLower();
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
            var cmd = cmdString.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

            if (!cmd.Any())
                return false;

            string commandName = cmd[0].ToLower();
            if (!_commands.TryGetValue(commandName, out var commandFunctions))
            {
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - unknown command");
                return false;
            }

            var res = RunCommandFunctions(cmdString, cmd, commandFunctions);
            if (commandName == "help")
                CALog.LogInfoAndConsoleLn(LogID.A, "-------------------------------------");  // end help menu divider
            else if (!res)
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - bad command");
            else
                CALog.LogInfoAndConsoleLn(LogID.A, $"Command: {cmdString} - command accepted");

            return res;
        }

        /// <returns><c>true</c> if at least one function accepted the command, otherwise <c>false</c></returns>
        /// <remarks>If a command returns false or throws an ArgumentException we still run the other commands.</remarks>
        private static bool RunCommandFunctions(string cmdString, List<string> cmd, List<Func<List<string>, bool>> commandFunctions)
        {
            bool accepted = false;
            for (int i = 0; i < commandFunctions.Count; i++)
            {
                try
                {
                    accepted |= commandFunctions[i](cmd);
                }
                catch (ArgumentException ex)
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Command: {cmdString} - invalid arguments", ex);
                }
            }

            return accepted;
        }
    }
}
