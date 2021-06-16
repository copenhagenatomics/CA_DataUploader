using System;
using System.Globalization;
using static System.FormattableString;
using CA_DataUploaderLib.Extensions;
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
        SmallerOrEqualTo = 5
    }

    public class IOconfAlert : IOconfRow
    {
        public IOconfAlert(string row, int lineNum) : base(row, lineNum, "Alert")
        {
            var list = ToList();
            if (list[0] != "Alert" || list.Count < 3) throw new Exception("IOconfAlert: wrong format: " + row);
            Name = list[1];
            (Sensor, Value, MessageTemplate, type) = ParseExpression(
                Name, list[2], "IOconfAlert: wrong format: " + row + ". Format: Alert;Name;SensorName comparison value;[rateMinutes];[emergencyshutdown].");
            Message = MessageTemplate;
            if (list.Count <= 3)
                (RateLimitMinutes, TriggersEmergencyShutdown) = (DefaultRateLimitMinutes, false);
            else 
            {
                TriggersEmergencyShutdown = list[3] == "emergencyshutdown" || list.Count > 4 && list[4] == "emergencyshutdown";
                RateLimitMinutes = !TriggersEmergencyShutdown || list.Count > 4 ? list[3].ToInt() : DefaultRateLimitMinutes;
            }
        }

        public IOconfAlert(string name, string expression, int? rateLimit = DefaultRateLimitMinutes, bool triggersEmergencyShutdown = true) : base("DynamicAlert", 0, "DynamicAlert")
        {
            Name = name;
            (Sensor, Value, MessageTemplate, type) = ParseExpression(
                name, expression, "alert expression - wrong format: " + expression + ". Expression format: SensorName comparison value.");
            Message = MessageTemplate;
            RateLimitMinutes = rateLimit ?? DefaultRateLimitMinutes;
            TriggersEmergencyShutdown = triggersEmergencyShutdown;
        }

        public string Name { get; set; }
        public string Sensor { get; set; }
        public string Message { get; private set; }
        public bool TriggersEmergencyShutdown { get; private set; }
        private readonly AlertCompare type;
        private readonly double Value;
        private double LastValue;
        private readonly string MessageTemplate;
        private bool _isFirstCheck = true;
        //expression captures groups: 1-sensor, 2-comparison, 3-value
        //sample expression: SomeValue < 202
        private static readonly Regex comparisonRegex = new Regex(@"^\s*([\w%]+)\s*(=|!=|>|<|>=|<=)\s*([-]?\d+(?:\.\d+)?)\s*$");
        private readonly int RateLimitMinutes;
        private const int DefaultRateLimitMinutes = 30; // by default fire the same alert max once every 30 mins.
        private DateTime LastTriggered;

        public bool CheckValue(double newValue, DateTime vectorTime)
        {
            Message = MessageTemplate + newValue.ToString(CultureInfo.InvariantCulture) + ")";
            bool newMatches = RawCheckValue(newValue);
            bool lastMatches = !_isFirstCheck && RawCheckValue(LastValue); // there is no lastValue to check on the first call
            _isFirstCheck = false;
            LastValue = newValue;
            return newMatches && !lastMatches && !RateLimit(vectorTime);
        }

        private bool RateLimit(DateTime vectorTime)
        {
            if (vectorTime.Subtract(LastTriggered).TotalMinutes < RateLimitMinutes)
                return true;
            LastTriggered = vectorTime;
            return false;
        }

        private bool RawCheckValue(double val) => type switch
        {
            AlertCompare.EqualTo => val == Value,
            AlertCompare.NotEqualTo => val != Value,
            AlertCompare.BiggerThan => val > Value,
            AlertCompare.SmallerThan => val < Value,
            AlertCompare.BiggerOrEqualTo => val >= Value,
            AlertCompare.SmallerOrEqualTo => val <= Value,
            _ => throw new Exception("IOconfAlert: this should never happen"),
        };

        private static (string sensor, double value, string messageTemplate, AlertCompare type) ParseExpression(string name, string expression, string formatErrorMessage)
        {
            var match = comparisonRegex.Match(expression);
            if (!match.Success)
                throw new Exception(formatErrorMessage + " Supported comparisons: =,!=, >, <, >=, <=");
            var sensor = match.Groups[1].Value;
            var comparison = match.Groups[2].Value.ToLower();
            var value = match.Groups[3].Value.ToDouble();
            string messageTemplate = Invariant($" {name} ({sensor}) {comparison} {value} (");
            var type = comparison switch
            {
                "=" => AlertCompare.EqualTo,
                "!=" => AlertCompare.NotEqualTo,
                ">" => AlertCompare.BiggerThan,
                "<" => AlertCompare.SmallerThan,
                ">=" => AlertCompare.BiggerOrEqualTo,
                "<=" => AlertCompare.SmallerOrEqualTo,
                _ => throw new Exception("alert expression - wrong format: " + expression),
            };

            return (sensor, value, messageTemplate, type);
        }
    }
}
