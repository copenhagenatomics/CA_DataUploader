using System;
using System.Globalization;
using static System.FormattableString;

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
            if (list[0] != "Alert" || list.Count < 4) throw new Exception("IOconfAlert: wrong format: " + row);
            Name = list[1];
            Sensor = list[2];
            string comparisson = list[3].ToLower();
            bool hasValidValue = list.Count > 4 && double.TryParse(list[4], NumberStyles.Any & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out Value);
            if (!hasValidValue && comparisson != "int" && comparisson != "nan")
                throw new Exception("IOconfAlert: wrong format: " + row);

            MessageTemplate = Invariant($" {Name} ({Sensor}) {list[3]} {Value} (");
            if (comparisson == "int")
            {
                MessageTemplate = $" {Name} ({Sensor}) is an integer (";
            }

            if (comparisson == "nan")
            {
                MessageTemplate = $" {Name} ({Sensor}) is not a number (";
            }

            Message = MessageTemplate;

            switch (comparisson) 
            {
                case "=": type = AlertCompare.EqualTo; break;
                case "!=": type = AlertCompare.NotEqualTo; break;
                case ">": type = AlertCompare.BiggerThan; break;
                case "<": type = AlertCompare.SmallerThan; break;
                case ">=": type = AlertCompare.BiggerOrEqualTo; break;
                case "<=": type = AlertCompare.SmallerOrEqualTo; break;
                case "nan": type = AlertCompare.NaN; break;
                case "int": type = AlertCompare.IsInteger; break;
                default:  throw new Exception("IOconfAlert: wrong format: " + row);
            }

        }


        public string Name { get; set; }
        public string Sensor { get; set; }
        public string Message { get; private set; }
        private readonly AlertCompare type;
        private readonly double Value;
        private double LastValue;
        private readonly string MessageTemplate;
        private bool _isFirstCheck = true;


        public bool CheckValue(double newValue)
        {
            Message = MessageTemplate + newValue.ToString(CultureInfo.InvariantCulture) + ")";
            bool newMatches = RawCheckValue(newValue);
            bool lastMatches = !_isFirstCheck && RawCheckValue(LastValue); // there is no lastValue to check on the first call
            _isFirstCheck = false;
            LastValue = newValue;
            return newMatches && !lastMatches;
        }

        private bool RawCheckValue(double val)
        {
            switch (type)
            {
                case AlertCompare.EqualTo: return val == Value;
                case AlertCompare.NotEqualTo: return val != Value;
                case AlertCompare.BiggerThan: return val > Value;
                case AlertCompare.SmallerThan: return val < Value;
                case AlertCompare.BiggerOrEqualTo: return val >= Value;
                case AlertCompare.SmallerOrEqualTo: return val <= Value;
                case AlertCompare.NaN: return Double.IsNaN(val);
                case AlertCompare.IsInteger: return Math.Abs(val % 1) <= (Double.Epsilon * 100);
                default: throw new Exception("IOconfAlert: this should never happen");
            }
        }

    }
}
