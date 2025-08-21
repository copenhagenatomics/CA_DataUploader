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
        //deltas are sensitivity in V/C based on standard values for thermocouple types, such like ones mentioned at https://www.analog.com/media/en/technical-documentation/data-sheets/max31855.pdf
        //these calibration values need to have 10 decimals because otherwise LoopControl detects a difference vs. what boards return
        public static IOconfTemp NewTypeK(string row, int lineNum) => new(row, lineNum, TypeKName, "0.0000412760", "0.0000407300");
        public static IOconfTemp NewTypeJ(string row, int lineNum) => new(row, lineNum, TypeJName, "0.0000579530", "0.0000521360");
        //CAL portNumber,delta,coldJunctionDelta portNumber,delta,...
        static readonly string DefaultCalibrations = $"CAL {string.Join(" ", Enumerable.Range(1, 10).Select(i => $"{i},0.0000412760,0.0000407300"))}";
        
        private readonly string _delta, _coldJunctionDelta;

        private IOconfTemp(string row, int lineNum, string type, string delta, string coldJunctionDelta) : 
            base(row, lineNum, type, false, new BoardSettings() { 
                Calibration = DefaultCalibrations, 
                SkipCalibrationWhenHeaderIsMissing = true //this is so that we don't try calibration temp hubs that don't support calibration + we log a clearer message about the skipped calibration
            })
        {
            Format = $"{type};Name;BoxName;[port number];[skip/all]";
            _delta = delta;
            _coldJunctionDelta = coldJunctionDelta;

            var list = ToList();
            AllJunction = false;
            if (Skip)
                return;
            else if (list[3].Equals("all", StringComparison.InvariantCultureIgnoreCase))
            {
                AllJunction = true;   // all => special command to show all junction temperatures including the first as average (used for calibration)
                PortNumber = 1;
            }
            else if (!HasPort)
                throw new FormatException($"{type}: wrong port number: {row}");
            else if (PortNumber < 1 || PortNumber > 34)
                throw new FormatException($"{type}: invalid port number: {row}");
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            base.ValidateDependencies(ioconf);

            if (PortNumber < 11) //only ports 1 to 10 are for thermocouples
                UpdatePortCalibration(Map.BoardSettings, _delta, _coldJunctionDelta, PortNumber);
        }

        private static void UpdatePortCalibration(BoardSettings settings, string delta, string coldJunctionDelta, int portNumber)
        { //see DefaultCalibrations for the format, spaces separate each port configuration section (first one is just "CAL ")
            var currentPortsCal = (settings.Calibration ?? throw new InvalidOperationException("Unexpected null calibration")).Split(" ");
            currentPortsCal[portNumber] = $"{portNumber},{delta},{coldJunctionDelta}";
            settings.Calibration = string.Join(' ', currentPortsCal);
        }
    }
}
