﻿using System;
using System.Runtime.CompilerServices;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfCode : IOconfRow
    {
        private static readonly ConditionalWeakTable<IIOconf, object> _nodeInstances = []; //using object to work around reference only limitations for the ConditionalWeakTable value

        public IOconfCode(string row, int lineNum) : base(row, lineNum, "Code")
        {
            Format = "Code;Name;Version;[InstanceName]";
            var list = ToList();
            if (list.Count < 3) throw new FormatException($"Missing version in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            if (!Version.TryParse(list[2], out var v)) throw new FormatException($"Invalid version format in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            Version = v;
            ClassName = Name;
            if (list.Count > 3 && !string.IsNullOrWhiteSpace(list[3]))
                Name = list[3];
        }

        public string ClassName { get; }
        public int Index { get; private set; }
        public Version Version { get; }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            Index = _nodeInstances.TryGetValue(ioconf, out var instances) ? (byte)instances : (byte)0;
            _nodeInstances.AddOrUpdate(ioconf, (byte)(Index + 1));
            base.ValidateDependencies(ioconf);
        }
    }
}
