using CA_DataUploaderLib.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfGenericOutput : IOconfOutput
    {
        private static readonly Regex CommandRegex = new(@"\$(?:{(\w*)}|(\w*))");
        
        public IOconfGenericOutput(string row, int lineNum) : base(row, lineNum, "GenericOutput", false, BoardSettings.Default) 
        {
            Format = "GenericOutput;OutputName;BoxName;DefaultValue;command with $field;[RepeatMilliseconds]";
            var values = ToList();
            if (values.Count < 5)
                throw new FormatException($"Bad format in line {Row}. Expected format: {Format}");
            if (!values[3].TryToDouble(out var defaultValue))
                throw new FormatException($"Failed to parse default value {Row}. Expected format: {Format}");
            DefaultValue = defaultValue;

            CommandTemplate = values[4];
            (TargetFields, TargetFieldsWithPrefix) = ParseTargetFields(CommandTemplate);

            RepeatMilliseconds = values.Count < 6 
                ? 1000 
                : int.TryParse(values[5], out var repeatMs) ? repeatMs : throw new FormatException($"Failed to parse repeat milliseconds {Row}. Expected format: {Format}");
        }

        public double DefaultValue { get; }
        public List<string> TargetFields { get; }
        private List<string> TargetFieldsWithPrefix { get; }
        public string CommandTemplate { get; }
        public int RepeatMilliseconds { get; }

        /// <summary>
        /// Generates the command by replacing all placeholders with their corresponding values.
        /// </summary>
        /// <param name="values">A list of values - the order corresponding to the order of TargetFields/TargetFieldsWithPrefix.</param>
        /// <returns>The generated command string.</returns>
        public string GetCommand(List<double> values)
        {
            if (TargetFieldsWithPrefix.Count != values.Count)
                throw new ArgumentException($"Mismatch between number of target fields and values. Expected {TargetFieldsWithPrefix.Count}, got {values.Count}.");

            var command = CommandTemplate;
            var index = 0;
            foreach (var value in values)
                command = command.Replace(TargetFieldsWithPrefix[index++], value.ToString(CultureInfo.InvariantCulture));
            return command;
        }

        /// <summary>
        /// Parses the command template to extract all placeholders for target fields.
        /// </summary>
        /// <param name="template">The command template string.</param>
        /// <returns>Two lists of the target fields: without and with prefix</returns>
        private (List<string>, List<string> withPrefix) ParseTargetFields(string template)
        {
            var matches = CommandRegex.Matches(template);
            var targetFields = new List<string>();
            var targetFieldsWithPrefix = new List<string>();
            foreach (var match in matches.Cast<Match>())
            {
                targetFieldsWithPrefix.Add(match.Groups[0].Value);
                if (match.Groups[1].Success)
                    targetFields.Add(match.Groups[1].Value); //with curly braces
                else if (match.Groups[2].Success)
                    targetFields.Add(match.Groups[2].Value); //without
                else
                    throw new FormatException($"Failed to find command $field in {Row}. Expected format: {Format}");
            }
            if (targetFields.Count == 0)
                throw new FormatException($"Failed to find command $field in {Row}. Expected format: {Format}");
            return (targetFields, targetFieldsWithPrefix);
        }
    }
}