using System;
using System.Globalization;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSamplingRates : IOconfState
    {
        public IOconfSamplingRates(string row, int lineNum) : base(row, lineNum, TypeName)
        {
            Format = $"{TypeName};MainLoop;VectorUploads";

            var list = ToList();
            if (list[0] != TypeName || list.Count != 3) throw new Exception($"{nameof(IOconfSamplingRates)}: wrong format: " + row);
            
            try
            {
                MainLoopDelay = (int)(1000 / double.Parse(list[1], NumberStyles.Float, CultureInfo.InvariantCulture));
                var vectorUploadFreq = double.Parse(list[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                if (vectorUploadFreq > 2)
                    throw new Exception($"{nameof(IOconfSamplingRates)}: too high upload frequency. Must be maximum 2 Hz: " + row);
                VectorUploadDelay = (int)(1000 / vectorUploadFreq);
            }
            catch(Exception ex)
            {
                throw new Exception($"{nameof(IOconfSamplingRates)}: wrong format - expression: " + row, ex);
            }
        }

        public const string TypeName = "SamplingRates";

        public readonly int MainLoopDelay;
        public readonly int VectorUploadDelay;

        public override string UniqueKey() => Type;
        protected override void ValidateName(string name) { } // no validation
    }
}
