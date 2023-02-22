using System;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfTemp : IOconfInput
    {
        public const string TypeKName = "TypeK";
        public const string TypeJName = "TypeJ";
        public bool AllJunction { get; }

        public override bool IsSpecialDisconnectValue(double value) => value >= 10000;
        //deltas are sensitivity in uV/C
        public static IOconfTemp NewTypeK(string row, int lineNum) => new(row, lineNum, TypeKName, "0.000041276", "0.00004073");
        public static IOconfTemp NewTypeJ(string row, int lineNum) => new(row, lineNum, TypeJName, "0.000057953", "0.000052136");
        //CAL portNumber,delta,coldJunctionDelta portNumber,delta,...
        static readonly string DefaultCalibrations = $"CAL {string.Join(" ", Enumerable.Range(1, 10).Select(i => $"{i},0.000041276,0.00004073"))}";

        private IOconfTemp(string row, int lineNum, string type, string delta, string coldJunctionDelta) : 
            base(row, lineNum, type, false, new BoardSettings() { 
                Calibration = DefaultCalibrations, 
                SkipCalibrationWhenHeaderIsMissing = true //this is so that we don't try calibration temp hubs that don't support calibration + we log a clearer message about the skipped calibration
            })
        {
            Format = $"{type};Name;BoxName;[port number];[skip/all]";

            var list = ToList();
            AllJunction = false;
            if (list[3].ToLower() == "all")
            {
                AllJunction = true;   // all => special command to show all junction temperatures including the first as average (used for calibration)
                PortNumber = 1;
            }
            else if (!Skip && !HasPort)
                throw new Exception($"{type}: wrong port number: {row}");
            else if (!Skip && (PortNumber < 1 || PortNumber > 34)) 
                throw new Exception($"{type}: invalid port number: {row}");

            UpdatePortCalibration(Map.BoardSettings, delta, coldJunctionDelta, PortNumber);
        }

        private static void UpdatePortCalibration(BoardSettings settings, string delta, string coldJunctionDelta, int portNumber)
        { //see DefaultCalibrations for the format, spaces separate each port configuration section (first one is just "CAL ")
            var currentPortsCal = (settings.Calibration ?? throw new InvalidOperationException("unexpected null calibration")).Split(" ");
            currentPortsCal[portNumber] = $"{portNumber},{delta},{coldJunctionDelta}";
            settings.Calibration = string.Join(' ', currentPortsCal);
        }
    }
}
