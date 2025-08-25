#nullable enable
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
        public IOconfAlert(string row, int lineNum, EventType eventType = EventType.Alert) : base(row, lineNum, "Alert")
        {
            Format = "Alert;Name;SensorName comparison value;[rateMinutes];[command]";
            EventType = eventType;
            var list = ToList();
            if (list[0] != "Alert" || list.Count < 3) throw new FormatException("IOconfAlert: wrong format: " + row);
   
            (Sensor, Value, MessageTemplate, type) = ParseExpression(
                Name, list[2], $"IOconfAlert: wrong format: {row}. Format: {Format}.");
            Message = MessageTemplate;
            if (list.Count <= 3)
                (RateLimitMinutes, Command) = (DefaultRateLimitMinutes, default);
            else if (int.TryParse(list[3], out var rateLimitMins))
                (RateLimitMinutes, Command) = (rateLimitMins, list.Count > 4 ? list[4] : default);
            else
                (RateLimitMinutes, Command) = (DefaultRateLimitMinutes, list[3]);
        }

        public string Sensor { get; }
        public string Message { get; private set; }
        public string? Command { get; }
        private readonly AlertCompare type;
        private readonly double Value;
        private double LastValue;
        private readonly string MessageTemplate;
        private bool _isFirstCheck = true;
        //expression captures groups: 1-sensor, 2-comparison, 3-value
        //sample expression: SomeValue < 202
        private static readonly Regex comparisonRegex = new(@"^\s*([\w%]+)\s*(=|!=|>|<|>=|<=)\s*([-]?\d+(?:\.\d+)?)\s*$");
        public int RateLimitMinutes { get; }
        public EventType EventType { get; }

        private const int DefaultRateLimitMinutes = 30; // by default fire the same alert max once every 30 mins.
        private DateTime LastTriggered;

        public bool CheckValue(double newValue, DateTime vectorTime)
        {
            //don't alert for invalid values.
            //Sensor readers are responsible for reporting fully lost sensors, while actuating logic is responsible for only doing targeted reactions.
            //Limitation: emergency shutdown alerts ignore board disconnects or lost sensors (10k+ thermocouple errors),
            //            for the earlier separate alerts can be set on the disconnects, for the later use alert + Math like this (filter length controls how long before the alert sees the 10k and triggers): Math;MyFilterDisconnected;MyFilter >= 10000
            //            Also note that some redundant alerts can be added so that if one sensor fails the problem can still be detected by other sensors/boards.
            if (newValue >= 10000) return false;
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
            _ => throw new InvalidOperationException("IOconfAlert: this should never happen"),
        };

        private static (string sensor, double value, string messageTemplate, AlertCompare type) ParseExpression(string name, string expression, string formatErrorMessage)
        {
            var match = comparisonRegex.Match(expression);
            if (!match.Success)
                throw new FormatException(formatErrorMessage + " Supported comparisons: =, !=, >, <, >=, <=");
            return ParseExpression(name, expression, match);
        }

        public void ResetState()
        {
            LastValue = default;
            LastTriggered = default;
            _isFirstCheck = true;
        }

        private static (string sensor, double value, string messageTemplate, AlertCompare type) ParseExpression(string name, string expression, Match match)
        {
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
                _ => throw new FormatException("Alert expression - wrong format: " + expression),
            };

            return (sensor, value, messageTemplate, type);
        }
    }
}
