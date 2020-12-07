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

        /// <summary>
        /// filter where values weight more the closest they are to the latest. 
        /// </summary>
        internal static double TriangleFilter(this List<List<SensorSample>> list, double filterLength, DateTime latestEntryTime)  // filterLength in seconds
        {
            // find the sum of all timespans.  
            var sum = list.Sum(x => filterLength - latestEntryTime.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds);
            return list.Sum(x => x.Average(y => y.Value) * (filterLength - latestEntryTime.Subtract(x.Select(y => y.TimeStamp.Ticks).AverageTime()).TotalSeconds) / sum);
        }
    }
}
