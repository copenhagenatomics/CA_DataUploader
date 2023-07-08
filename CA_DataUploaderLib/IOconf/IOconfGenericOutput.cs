using CA_DataUploaderLib.Extensions;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfGenericOutput : IOconfOutput
    {
        private static readonly Regex CommandRegex = new(@"(\$\w*)$");
        public IOconfGenericOutput(string row, int lineNum) : base(row, lineNum, "GenericSensor", false, BoardSettings.Default) 
        {
            Format = "GenericOutput;OutputName;BoxName;DefaultValue;command with $field";
            var values = ToList();
            if (values.Count < 5)
                throw new FormatException($"Bad format in line {Row}. Expected format: {Format}");
            if (!values[3].TryToDouble(out var defaultValue))
                throw new FormatException($"Failed to parse default value {Row}. Expected format: {Format}");
            DefaultValue = defaultValue;

            CommandTemplate = CommandRegex.Match(values[4]);
            if (!CommandTemplate.Success || CommandTemplate.Groups.Count != 1)
                throw new FormatException($"Failed to find command $field in {Row}. Expected format: {Format}");
        }
        public double DefaultValue { get; }
        public string TargetField => CommandTemplate.Groups[0].Value.TrimStart('$');
        public Match CommandTemplate { get; }
        public string GetCommand(double value) => CommandTemplate.Result(value.ToString(CultureInfo.InvariantCulture));
    }
}