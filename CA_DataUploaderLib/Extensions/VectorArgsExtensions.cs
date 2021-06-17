using System;
using System.Collections.Generic;
using System.Linq;
using CA.LoopControlPluginBase;

namespace CA_DataUploaderLib.Extensions
{
    public static class VectorArgsExtensions
    {
        public static IEnumerable<SensorSample> WithVectorTime(this IEnumerable<SensorSample> samples, DateTime vectorTime) => 
            samples.Append(new SensorSample("vectortime", vectorTime.ToVectorDouble()));
        public static DateTime GetVectorTime(this NewVectorReceivedArgs args) => args["vectortime"].ToVectorDate();
        // repetition below removes ms rounding differences later (documented behavior of FromOADate).
        // unrelated: the only reason we use FromODate and ToODate is because its an out of the box conversion between DateTime and double.
        public static double ToVectorDouble(this DateTime date) => DateTime.FromOADate(date.ToOADate()).ToOADate(); 
        public static DateTime ToVectorDate(this double date) => DateTime.FromOADate(date); 
    }
}