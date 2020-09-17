﻿using System.Linq;
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
            return theObject.Where(x => x.Map.Board != null);
        }

        public static double TriangleFilter(this IEnumerable<Tuple<double, DateTime>> list, double filterLength)  // filterLength in seconds
        {

            return 0;
        }
    }
}
