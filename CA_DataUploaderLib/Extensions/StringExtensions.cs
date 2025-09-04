using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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
        public static bool IsNullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
        public static string StringBefore(this string s, string match) => TryGetIndex(s, match, out var pos) ? s[..pos] : string.Empty;
        public static string StringAfter(this string s, string match) => TryGetIndex(s, match, out var pos) ? s[(pos + match.Length)..] : string.Empty;
        public static string StringBetween(this string s, string match1, string match2)
        {
            string tmp = StringAfter(s, match1);
            return TryGetIndex(tmp, match2, out var pos) ? tmp[..pos] : tmp;
        }

        public static int ToInt(this string s) => int.Parse(s);
        public static double ToDouble(this string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        public static bool TryToDouble(this string s, out double val) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        public static bool TryToDouble(this ReadOnlySpan<char> s, out double val) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
        public static List<string> SplitNewLine(this string s, StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries) => [.. s.Split([Environment.NewLine], options)];
        private static bool TryGetIndex(string s, string match, out int pos) => (pos = s.IndexOf(match)) >= 0;

        /// <summary>
        /// Escapes fixed set of hidden characters in a string to be able to see them when printing to log/console.
        /// </summary>
        public static string ToLiteral(this string s)
        {
            return new StringBuilder(s)
                .Replace("\0", @"\0") // Null character
                .Replace("\a", @"\a") // Alert
                .Replace("\b", @"\b") // Backspace
                .Replace("\f", @"\f") // Form feed
                .Replace("\n", @"\n") // New line
                .Replace("\r", @"\r") // Carriage return
                .Replace("\t", @"\t") // Horizontal tab
                .Replace("\v", @"\v") // Vertical tab
                .ToString();
        }
    }
}
