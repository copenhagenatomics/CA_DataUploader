using System.Linq;
using System.Collections.Generic;
using CA_DataUploaderLib.IOconf;
using System;
using System.Diagnostics;

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
            return theObject.Where(x => x.Map.Board != null);
        }

        public static double TriangleFilter(this IEnumerable<Tuple<double, DateTime>> list, double filterLength)  // filterLength in seconds
        {
            // order with the latest sample first. 
            list = list.OrderByDescending(x => x.Item2).ToList();
            var now = list.First().Item2;

            // skip samples that are outside the filterlength
            list = list.Where(x => x.Item2 > now.AddSeconds(-filterLength)).ToList();

            // find the sum of all timespans.  
            var sum = list.Sum(x => filterLength - now.Subtract(x.Item2).TotalSeconds);
            return list.Sum(x => x.Item1 * (filterLength - now.Subtract(x.Item2).TotalSeconds) / sum);
        }
    }
}
