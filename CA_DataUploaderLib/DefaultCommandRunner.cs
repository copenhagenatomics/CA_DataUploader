using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    /// <remarks>
    /// runs the registered commands directly on the calling thread (the input handling thread),
    /// showing accept/reject messages on the screen and A.log.
    /// </remarks>
    public class DefaultCommandRunner(ILog logger) : ICommandRunner
    {
        private readonly Dictionary<string, List<Func<List<string>, bool>>> _commands = new(StringComparer.OrdinalIgnoreCase);
        public Action AddCommand(string name, Func<List<string>, bool> func)
        {
            if (_commands.TryGetValue(name, out var value))
                value.Add(func);
            else
                _commands.Add(name, [func]);

            return () =>
            {
                _commands[name].Remove(func);
                if (_commands[name].Count == 0)
                    _commands.Remove(name);
            };
        }

        public bool Run(string cmdString, bool isUserCommand)
        {
            var cmd = ParseCommand(cmdString);
            if (cmd.Count == 0)
                return false;

            string commandName = cmd[0];
            if (!_commands.TryGetValue(commandName, out var commandFunctions))
            {
                logger.LogInfo(LogID.A, $"Command: {cmdString} - unknown command");
                return false;
            }

            var res = RunCommandFunctions(cmdString, cmd, commandFunctions);
            if (commandName.Equals("help", StringComparison.OrdinalIgnoreCase))
                logger.LogInfo(LogID.A, "-------------------------------------");  // end help menu divider
            else if (!res)
                logger.LogError(LogID.A, $"Command: {cmdString} - bad command");
            else
                logger.LogInfo(LogID.A, $"Command: {cmdString} - command accepted");

            return res;
        }

        public List<string> ParseCommand(string cmdString) =>
            [.. cmdString.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

        /// <returns><c>true</c> if at least one function accepted the command, otherwise <c>false</c></returns>
        /// <remarks>If a command returns false or throws an ArgumentException we still run the other commands.</remarks>
        private bool RunCommandFunctions(string cmdString, List<string> cmd, List<Func<List<string>, bool>> commandFunctions)
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
                    logger.LogError(LogID.A, $"Command: {cmdString} - invalid arguments", ex);
                }
            }

            return accepted;
        }
    }
}
