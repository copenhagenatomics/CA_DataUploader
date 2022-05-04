using System.Linq;
using System.Collections.Generic;
using System;

namespace CA_DataUploaderLib.Extensions
{
    public static class LinqExtensions
    {
        public static DateTime AverageTime(this IEnumerable<long> input)
        {
            decimal avg = input.Average(l => (decimal)l);
            return new DateTime((long)avg);
        }
    }
}
