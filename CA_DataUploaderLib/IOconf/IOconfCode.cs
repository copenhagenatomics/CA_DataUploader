using System;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfCode : IOconfRow
    {
        private static byte _nodeInstances;

        public IOconfCode(string row, int lineNum) : base(row, lineNum, "Code")
        {
            Format = "Code;Name;Version;[InstanceName]";
            var list = ToList();
            if (list.Count < 3) throw new FormatException($"Missing version in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            if (!Version.TryParse(list[2], out var v)) throw new FormatException($"Invalid version format in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            Version = v;
            Index = _nodeInstances++;
            ClassName = Name;
            if (list.Count > 3 && !string.IsNullOrWhiteSpace(list[3]))
                Name = list[3];
        }

        public string ClassName { get; }
        public int Index { get; }
        public Version Version { get; }

        /// <summary>resets the node instance count used to determine the node index, can be used for testing purposes</summary>
        public static void ResetNodeIndexCount() => _nodeInstances = 0;
    }
}
