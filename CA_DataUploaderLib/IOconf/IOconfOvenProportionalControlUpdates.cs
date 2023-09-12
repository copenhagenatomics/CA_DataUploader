#nullable enable
using System;
using CA_DataUploaderLib.Extensions;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfOvenProportionalControlUpdates : IOconfDriver
    {
        public IOconfOvenProportionalControlUpdates(string row, int lineNum) : base(row, lineNum, TypeName)
        {
            Format = $"{TypeName};[MaxProportionalGain];[MaxControlPeriod];[MaxOutputPercentage]";

            var list = ToList();
            if (!list[1].TryToDouble(out var maxpGain)) 
                throw new FormatException($"failed to parse MaxProportionalGain: {row}. Expected format: {Format}");

            if (!TimeSpan.TryParse(list[2], out var maxPeriod))
                throw new FormatException($"Failed to parse MaxControlPeriod: {row}. Expected format: {Format}");

            if (!int.TryParse(list[3], out var maxOutput))
                throw new FormatException($"Failed to parse MaxOutputPercentage: {row}. Expected format: {Format}");
            if (maxOutput < 0 || maxOutput > 100)
                throw new FormatException($"Max output percentage must be a whole number between 0 and 100: {row}. Expected format: {Format}");

            (MaxProportionalGain, MaxControlPeriod, MaxOutputPercentage) = (maxpGain, maxPeriod, maxOutput / 100d);
        }

        public const string TypeName = "OvenProportionalControlUpdates";

        /// <remarks><see cref="IOconfOven.ProportionalGain"/></remarks>
        public double MaxProportionalGain { get; }
        public TimeSpan MaxControlPeriod { get; }
        public double MaxOutputPercentage { get; }
    }
}
