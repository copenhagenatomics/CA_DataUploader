using System;
using System.Globalization;
using static System.FormattableString;
using CA_DataUploaderLib.Extensions;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.IOconf
{
    public enum AlertCompare
    {
        EqualTo = 0, 
        NotEqualTo = 1, 
        BiggerThan = 2,
        SmallerThan = 3, 
        BiggerOrEqualTo = 4, 
        SmallerOrEqualTo = 5,
        NaN = 6,
        IsInteger = 7
    }

    public class IOconfAlert : IOconfRow
    {
        public IOconfAlert(string row, int lineNum) : base(row, lineNum, "Alert")
        {
            var list = ToList();
            if (list[0] != "Alert" || list.Count < 3) throw new Exception("IOconfAlert: wrong format: " + row);
            Name = list[1];
            string comparisson;
            (Sensor, comparisson, Value) = ParseAlertExpression(list, row);

            MessageTemplate = Invariant($" {Name} ({Sensor}) {comparisson} {Value} (");
            if (comparisson == "int")
                MessageTemplate = $" {Name} ({Sensor}) is an integer (";
            if (comparisson == "nan")
                MessageTemplate = $" {Name} ({Sensor}) is not a number (";

            Message = MessageTemplate;
            type = comparisson switch
            {
                "=" => AlertCompare.EqualTo,
                "!=" => AlertCompare.NotEqualTo,
                ">" => AlertCompare.BiggerThan,
                "<" => AlertCompare.SmallerThan,
                ">=" => AlertCompare.BiggerOrEqualTo,
                "<=" => AlertCompare.SmallerOrEqualTo,
                "nan" => AlertCompare.NaN,
                "int" => AlertCompare.IsInteger,
                _ => throw new Exception("IOconfAlert: wrong format: " + row),
            };
        }

        private (string Sensor, string Comparisson, double Value) ParseAlertExpression(List<string> list, string row)
        {
            if (list.Count == 4 || list.Count == 5)
                return ParseOldFormat(list, row);
            else if (list.Count != 3)
                throw new Exception("IOconfAlert: wrong format: " + row);

            var match = comparisonRegex.Match(list[2]);
            if (!match.Success)
                throw new Exception("IOconfAlert: wrong format: " + row);

            return (match.Groups[1].Value, match.Groups[2].Value.ToLower(), match.Groups[3].Value.ToDouble());
        }

        private static (string Sensor, string Comparisson, double Value) ParseOldFormat(List<string> list, string row)
        {
            string comparisson = list[3].ToLower();
            if (list.Count > 4 && list[4].TryToDouble(out var value))
                return (list[2], comparisson, value);
            if (comparisson == "int" || comparisson == "nan")
                return (list[2], list[3], 0d);
            throw new Exception("IOconfAlert: wrong format: " + row);
        }

        public string Name { get; set; }
        public string Sensor { get; set; }
        public string Message { get; private set; }
        private readonly AlertCompare type;
        private readonly double Value;
        private double LastValue;
        private readonly string MessageTemplate;
        private bool _isFirstCheck = true;
        //expression captures groups: 1-sensor, 2-comparison, 3-value
        //sample expression: SomeValue < 202
        private readonly Regex comparisonRegex = new Regex(@"(\w+)\s*(=|!=|>|<|>=|<=)\s*([-]?\d+(?:\.\d+)?)");

        public bool CheckValue(double newValue)
        {
            Message = MessageTemplate + newValue.ToString(CultureInfo.InvariantCulture) + ")";
            bool newMatches = RawCheckValue(newValue);
            bool lastMatches = !_isFirstCheck && RawCheckValue(LastValue); // there is no lastValue to check on the first call
            _isFirstCheck = false;
            LastValue = newValue;
            return newMatches && !lastMatches;
        }

        private bool RawCheckValue(double val) => type switch
        {
            AlertCompare.EqualTo => val == Value,
            AlertCompare.NotEqualTo => val != Value,
            AlertCompare.BiggerThan => val > Value,
            AlertCompare.SmallerThan => val < Value,
            AlertCompare.BiggerOrEqualTo => val >= Value,
            AlertCompare.SmallerOrEqualTo => val <= Value,
            AlertCompare.NaN => Double.IsNaN(val),
            AlertCompare.IsInteger => Math.Abs(val % 1) <= (Double.Epsilon * 100),
            _ => throw new Exception("IOconfAlert: this should never happen"),
        };
    }
}
