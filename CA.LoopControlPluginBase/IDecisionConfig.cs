using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CA.LoopControlPluginBase
{
    public interface IDecisionConfig 
    {
        string Decision { get; }
        IEnumerable<string> Fields { get; }
        /// <returns><c>false</c> if the field is not configured and its default value should be used</returns>
        bool TryGet(string fieldName, [NotNullWhen(true)] out string? val);

        /// <returns><c>false</c> if the field is not configured and its default value should be used</returns>
        /// <exception cref="FormatException">if the field in the configuration failed to be parsed</exception>
        public bool TryGetDouble(string fieldName, out double val)
        {
            val = 0;
            if (!TryGet(fieldName, out var stringVal))
                return false;

            if (!double.TryParse(stringVal, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                throw new FormatException($"Failed to parse double field {fieldName} value {stringVal} for {Decision}");

            return true;
        }

        /// <returns><c>false</c> if the field is not configured and its default value should be used</returns>
        /// <exception cref="FormatException">if the field in the configuration failed to be parsed</exception>
        public bool TryGetInt(string fieldName, out int val)
        {
            val = 0;
            if (!TryGet(fieldName, out var stringVal))
                return false;

            if (!int.TryParse(stringVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                throw new FormatException($"Failed to parse int field {fieldName} value {stringVal} for {Decision}");

            return true;
        }

        /// <summary>
        /// Ensures only the allowed plugin fields are in the configuration. Note mispellings in the plugin name are not detected here, but the host should report them as unknown fields anyway.
        /// </summary>
        /// <exception cref="NotSupportedException">invalid fields were detected</exception>
        public void ValidateConfiguredFields(IEnumerable<string> allowedFields)
        {
            var allowed = new HashSet<string>(allowedFields);
            List<string>? unexpectedFields = null;

            foreach (var field in Fields)
                if (!allowed.Contains(field))
                    (unexpectedFields ??= new()).Add(field);

            if (unexpectedFields != null)
                throw new NotSupportedException($"Detected config fields not supported for {Decision}: {string.Join(", ", unexpectedFields)}. Allowed fields: {string.Join(", ", allowedFields)}");
        }
    }
}
