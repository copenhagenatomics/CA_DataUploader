using System;
using System.Globalization;
using System.Linq;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfCurrent : IOconfInput
    {
        private readonly double _currentTransformerRatio;
        public const string TypeName = "Current";

        static readonly string DefaultCalibration = $"CAL {string.Join(" ", Enumerable.Range(1, 3).Select(i => $"{i},60.000000,0"))}";

        public IOconfCurrent(string row, int lineNum) :
            base(row, lineNum, TypeName, false, new BoardSettings()
            {
                SkipCalibrationWhenHeaderIsMissing = true //Don't try to calibrate boards that don't support calibration
            })
        {
            Format = $"{TypeName};Name;BoxName;PortNumber;LoadSideRating;[MeterSideRating]";

            var list = ToList();
            if (Skip)
                return;
            if (!HasPort || PortNumber < 1 || PortNumber > 3)
                throw new FormatException($"{TypeName}: invalid port number (allowed: [1-3]): {row}");
            if (list.Count < 5)
                throw new FormatException($"{nameof(IOconfCurrent)}: wrong format: {row}. Expected format: {Format}");

            if (!double.TryParse(list[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double loadSideRating) || double.IsNegative(loadSideRating))
                throw new FormatException($"Unsupported load side rating at line '{Row}'. Only positive numbers are allowed. Expected format: {Format}.");

            var meterSideRating = 5.0; // A default meter side rating of 5.0A which can optionally be changed
            if (list.Count >= 6 && (!double.TryParse(list[5], NumberStyles.Float, CultureInfo.InvariantCulture, out meterSideRating) || meterSideRating <= 0.0 || meterSideRating > 5.0))
                throw new FormatException($"Unsupported meter side rating at line '{Row}'. Only numbers between 0 and 5 are allowed. Expected format: {Format}.");

            _currentTransformerRatio = loadSideRating / meterSideRating;
        }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            base.ValidateDependencies(ioconf);
            
            Map.OnBoardDetected += OnBoardDetected;

            // We need to read the current calibration from the board to know if it is supported
            void OnBoardDetected(object? sender, EventArgs e)
            {
                var board = Map.Board ?? throw new InvalidOperationException($"Unexpected Map.OnBoardDetected with a null board for {Map.Name}");
                var calibrationFromBoard = board.Calibration;
                var supportsCalibration = calibrationFromBoard is not null && calibrationFromBoard.StartsWith("CAL");
                if (supportsCalibration)
                {
                    var nfi = new NumberFormatInfo() { NumberDecimalDigits = 6 };
                    UpdatePortCalibration(Map.BoardSettings, _currentTransformerRatio.ToString("F", nfi), PortNumber);
                }
                else
                {
                    CALog.LogErrorAndConsoleLn(LogID.A, $"Error: old current board {Map.Name} cannot use new format for sensor {Name}. Disconnecting board. Received header:{Environment.NewLine}{Map.Board.HeaderLines}");
                    Map.ForceDisconnectBoard().Wait();
                }
            }
        }

        private static void UpdatePortCalibration(BoardSettings settings, string scalar, int portNumber)
        { //see DefaultCalibration for the format, spaces separate each port configuration section (first one is just "CAL ")
            var currentPortsCal = (settings.Calibration ?? DefaultCalibration).Split(" ");
            currentPortsCal[portNumber] = $"{portNumber},{scalar},0";
            settings.Calibration = string.Join(' ', currentPortsCal);
        }
    }

    public class IOconfCurrentFault : IOconfInput
    {
        public const string TypeName = "CurrentFault";

        public IOconfCurrentFault(string row, int lineNum) :
            base(row, lineNum, TypeName, false, BoardSettings.Default)
        {
            Format = $"{TypeName};Name;BoxName";
            PortNumber = 4;
        }
    }
}
