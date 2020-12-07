using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// Determines whether [is not null or empty] [the specified s].
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns>
        ///   <c>true</c> if [is not null or empty] [the specified s]; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNotNullOrEmpty(this string s)
        {
            return !string.IsNullOrEmpty(s);
        }

        /// <summary>
        /// Determines whether the specified s is empty.
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns>
        ///   <c>true</c> if the specified s is empty; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsEmpty(this string s)
        {
            return (s.Length == 0);
        }

        /// <summary>
        /// Determines whether the specified s is null.
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns>
        ///   <c>true</c> if the specified s is null; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNull(this string s)
        {
            return (s == null);
        }

        /// <summary>
        /// Determines whether [is not null] [the specified s].
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns>
        ///   <c>true</c> if [is not null] [the specified s]; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNotNull(this string s)
        {
            return (s != null);
        }

        /// <summary>
        /// Ases the null if empty.
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns></returns>
        public static string AsNullIfEmpty(this string s)
        {
            if (String.IsNullOrEmpty(s))
            {
                return null;
            }

            return s;
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

        public static string StringDelete(this string s, string beginAt, string endAfter)
        {
            string tmp = String.Empty;
            int pos = s.IndexOf(beginAt);
            if (pos != -1)
                tmp = s.Substring(0, pos);
            return tmp + StringAfter(s, endAfter);

        }

        public static string ReplaceBetween(this string s, string match1, string match2, string newStr)
        {
            return StringBefore(s, match1) + match1 + newStr + match2 + StringAfter(s, match2);
        }

        public static int ToInt(this string s)
        {
            return int.Parse(s);
        }

        public static double ToDouble(this string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        public static bool TryToDouble(this string s, out double val) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);

        public static DateTime ToDateTime(this string s)
        {
            return DateTime.Parse(s, CultureInfo.InvariantCulture);
        }

        public static IEnumerable<int> ToIntList(this string s, char splitAt)
        {
            List<int> list = new List<int>();
            if (s.IsNullOrEmpty())
                return list;
            foreach (string i in s.Split(splitAt))
                list.Add(int.Parse(i));
            return list;
        }

        public static Dictionary<string, string> ToDictionary(this string s)
        {
            return s.Split('&').Select(x => x.Split('=')).ToDictionary(y => y[0], y => y[1]);
        }

        public static string RemoveHTML(this string s)
        {
            string cleanBodyText = Regex.Replace(s, @"<[^>]*>", String.Empty);
            return cleanBodyText.Replace("&nbsp;", " ");
        }

        public static string RemoveNewLine(this string s)
        {
            return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        }
    }
}
