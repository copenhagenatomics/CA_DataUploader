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
            if (list[0] != "Alert" || list.Count < 3) throw new Exception("IOconfAlert: wrong format: " + row);
            Name = list[1];
            string comparisson = list[2].ToLower();
            bool hasValidValue = list.Count > 3 && double.TryParse(list[3], NumberStyles.Any & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out Value);
            if (!hasValidValue && comparisson != "int" && comparisson != "nan")
                throw new Exception("IOconfAlert: wrong format: " + row);

            MessageTemplate = Invariant($" {Name} {list[2]} {Value} (");
            if (comparisson == "int")
            {
                MessageTemplate = $" {Name} is an integer (";
            }

            if (comparisson == "nan")
            {
                MessageTemplate = $" {Name} is not a number (";
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
        public string Message { get; private set; }
        private AlertCompare type;
        private double Value;
        private double LastValue;
        private string MessageTemplate;


        public bool CheckValue(double newValue)
        {
            Message = MessageTemplate + newValue.ToString(CultureInfo.InvariantCulture) + ")"; 
            double lastValue = LastValue;
            LastValue = newValue;
            switch(type)
            {
                case AlertCompare.EqualTo:  return newValue == Value && lastValue != Value;
                case AlertCompare.NotEqualTo: return newValue != Value && lastValue == Value;
                case AlertCompare.BiggerThan: return newValue > Value && lastValue <= Value;
                case AlertCompare.SmallerThan: return newValue < Value && lastValue >= Value;
                case AlertCompare.BiggerOrEqualTo: return newValue >= Value && lastValue < Value;
                case AlertCompare.SmallerOrEqualTo: return newValue <= Value && lastValue > Value;
                case AlertCompare.NaN: return Double.IsNaN(newValue) && !Double.IsNaN(lastValue);
                case AlertCompare.IsInteger: return Math.Abs(newValue % 1) <= (Double.Epsilon * 100);
                default: throw new Exception("IOconfAlert: this should never happen");
            }
        }

    }
}
