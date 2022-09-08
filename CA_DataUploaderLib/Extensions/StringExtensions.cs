using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CA_DataUploaderLib.Extensions
{
    /// <summary>
    /// String extensions
    /// </summary>
    public static class StringExtensions
    {

        /// <summary>
        /// Determines whether the string [is null or empty] 
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns>
        ///   <c>true</c> if [is null or empty] [the specified s]; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNullOrEmpty(this string s)
        {
            return string.IsNullOrEmpty(s);
        }

        public static string StringBefore(this string s, string match)
        {
            int pos = s.IndexOf(match);
            if (pos == -1)
                return String.Empty;
            else
                return s.Substring(0, pos);
        }

        public static string StringAfter(this string s, string match)
        {
            int pos = s.IndexOf(match);
            if (pos == -1)
                return String.Empty;
            else
                return s.Substring(pos + match.Length);
        }

        public static string StringBetween(this string s, string match1, string match2)
        {
            string tmp = StringAfter(s, match1);
            int pos = tmp.IndexOf(match2);
            if (pos == -1)
                return tmp;
            else
                return tmp.Substring(0, pos);
        }

        public static int ToInt(this string s)
        {
            return int.Parse(s);
        }

        public static double ToDouble(this string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        public static bool TryToDouble(this string s, out double val) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        public static bool TryToDouble(this ReadOnlySpan<char> s, out double val) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        public static List<string> SplitNewLine(this string s) => s.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
