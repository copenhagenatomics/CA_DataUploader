using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfSamplingRates : IOconfState
    {
        public IOconfSamplingRates(string row, int lineNum) : base(row, lineNum, "SamplingRates")
        {
            Format = "SamplingRates;MainLoop;VectorUploads";

            var list = ToList();
            if (list[0] != "SamplingRates" || list.Count != 3) throw new Exception("IOconfSamplingRates: wrong format: " + row);
            
            try
            {
                MainLoopDelay = (int)(1000 / double.Parse(list[1]));
                var VectorUploadFreq = double.Parse(list[2]);
                if(VectorUploadFreq > 2)
                    throw new Exception("IOconfSamplingRates: too high upload frequency. Must be maximum 2 Hz: " + row);
                VectorUploadDelay = (int)(1000 / VectorUploadFreq);
            }
            catch(Exception ex)
            {
                throw new Exception("IOconfMath: wrong format - expression: " + row, ex);
            }
        }

        public readonly int MainLoopDelay;
        public readonly int VectorUploadDelay;
    }
}
