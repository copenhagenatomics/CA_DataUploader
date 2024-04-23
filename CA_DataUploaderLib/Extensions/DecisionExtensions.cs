using CA.LoopControlPluginBase;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace CA_DataUploaderLib.Extensions
{
    public static class DecisionExtensions
    {
        public static IEnumerable<(string command, Func<List<string>, bool> commandValidationFunction)> GetValidationCommands(this LoopControlDecision decision)
        {
            foreach (var e in decision.HandledEvents)
            {
                //This just avoids the commands being reported as rejected for now, but the way to go about in the long run is to add detection of the executed commands by looking at the vectors.
                //Note that even then, the decisions are not reporting which commands they actually handled or ignore, specially as they are receiving all commands and then handle what applies to the decision.
                var expectedArgs = e.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var firstWordInEvent = expectedArgs[0];
                yield return (firstWordInEvent, args =>
                {
                    if (args.Count != expectedArgs.Length && args.Count != expectedArgs.Length + 1)
                        return false;

                    for (int i = 1; i < expectedArgs.Length; i++)
                    {
                        if (!args[i].Equals(expectedArgs[i], StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    if (args.Count == expectedArgs.Length)
                        return true;

                    var target = args[expectedArgs.Length];
                    return target.TryToDouble(out _) ||
                        target.StartsWith("0x") && uint.TryParse(target[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ||
                        target.StartsWith("#") && uint.TryParse(target[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
                }
                );
            }
        }
    }
}
