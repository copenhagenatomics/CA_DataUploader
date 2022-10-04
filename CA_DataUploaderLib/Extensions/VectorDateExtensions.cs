using System;

namespace CA_DataUploaderLib.Extensions
{
    public static class VectordDateExtensions
    {
        // repetition below removes ms rounding differences later (documented behavior of FromOADate).
        // unrelated: the only reason we use FromODate and ToODate is because its an out of the box conversion between DateTime and double.
        public static double ToVectorDouble(this DateTime date) => DateTime.FromOADate(date.ToOADate()).ToOADate(); 
        public static DateTime ToVectorDate(this double date) => DateTime.FromOADate(date); 
    }
}