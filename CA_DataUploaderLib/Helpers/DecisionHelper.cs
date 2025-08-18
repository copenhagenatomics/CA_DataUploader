using System.Text.RegularExpressions;

namespace CA_DataUploaderLib.Helpers
{
    public partial class DecisionHelper
    {
        /// <summary>
        /// Converts an event name to a user-friendly format by replacing underscores with spaces and normalizing
        /// whitespace.
        /// </summary>
        /// <param name="e">The event name to be converted.</param>
        /// <returns>A user-friendly string representation of the event name with underscores replaced by spaces and multiple
        /// whitespace characters reduced to a single space.</returns>
        public static string ToUserEvent(string e) =>
            MergeWhitespace().Replace(e.Replace('_', ' ').Trim(), " ");
 
        [GeneratedRegex(@"\s+")] // Merge multiple whitespace characters
        private static partial Regex MergeWhitespace();
    }
}
