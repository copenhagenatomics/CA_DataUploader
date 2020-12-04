using System.Linq;
using System.Collections.Generic;
using CA_DataUploaderLib.IOconf;
using System;

namespace CA_DataUploaderLib.Extensions
{
    public static class LinqExtensions
    {
        public static bool In<T>(this T theObject, params T[] collection)
        {
            return collection.Contains(theObject);
        }

        // only return IOconfInput rows where the MCUBoard was initialized. 
        public static IEnumerable<T> IsInitialized<T>(this IEnumerable<T> theObject) where T : IOconfInput
        {
            return theObject.Where(x => x.Skip || x.Map.Board != null);
        }

        public static DateTime AverageTime(this IEnumerable<long> input)
        {
            decimal avg = input.Average(l => (decimal)l);
            return new DateTime((long)avg);
        }

        internal static double TriangleFilter(this List<List<SensorSample>> list, double filterLength)  // filterLength in seconds
        {
            // order with the latest sample first. 
            list = list.OrderByDescending(x => x.Select(y => y.TimeStamp.Ticks).AverageTime()).ToList();
            var now = list.First().Select(y => y.TimeStamp.Ticks).AverageTime();

            // skip samples that are outside the filterlength
            list = list.Where(x => x.Select(y => y.TimeStamp.Ticks).AverageTime() > now.AddSeconds(-filterLength)).ToList();

            // find the sum of all timespans.  
            var sum = list.Sum(x => filterLength - now.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds);
            return list.Sum(x => x.Average(y => y.Value) * (filterLength - now.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds) / sum);
        }
    }
}
