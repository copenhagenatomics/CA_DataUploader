using CA_DataUploaderLib.Extensions;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfGenericOutput : IOconfOutput
    {
        private static readonly Regex CommandRegex = new(@"\$(?:{(\w*)}|(\w*))");
        private readonly string targetFieldWithPrefix;
        public IOconfGenericOutput(string row, int lineNum) : base(row, lineNum, "GenericOutput", false, BoardSettings.Default) 
        {
            Format = "GenericOutput;OutputName;BoxName;DefaultValue;command with $field";
            var values = ToList();
            if (values.Count < 5)
                throw new FormatException($"Bad format in line {Row}. Expected format: {Format}");
            if (!values[3].TryToDouble(out var defaultValue))
                throw new FormatException($"Failed to parse default value {Row}. Expected format: {Format}");
            DefaultValue = defaultValue;

            CommandTemplate = values[4];
            var match = CommandRegex.Match(CommandTemplate);
            if (!match.Success || match.Groups.Count != 3)
                throw new FormatException($"Failed to find command $field in {Row}. Expected format: {Format}");
            targetFieldWithPrefix = match.Groups[0].Value;
            if (match.Groups[1].Success)
                TargetField = match.Groups[1].Value;
            else if (match.Groups[2].Success)
                TargetField = match.Groups[2].Value;
            else
                throw new FormatException($"Failed to find command $field in {Row}. Expected format: {Format}");
        }
        public double DefaultValue { get; }
        public string TargetField { get; }
        public string CommandTemplate { get; }
        public string GetCommand(double value) => CommandTemplate.Replace(targetFieldWithPrefix, value.ToString(CultureInfo.InvariantCulture));
    }
}