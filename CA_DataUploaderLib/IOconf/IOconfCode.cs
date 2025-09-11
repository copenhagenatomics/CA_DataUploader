using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfCode : IOconfRow
    {
        private static readonly ConditionalWeakTable<IIOconf, object> _nodeInstances = []; //using object to work around reference only limitations for the ConditionalWeakTable value
        private static readonly Version _pluginBaseVersion = typeof(CA.LoopControlPluginBase.LoopControlDecision).Assembly.GetName().Version ?? throw new InvalidOperationException("Could not determine plugin base version.");

        public IOconfCode(string row, int lineNum) : base(row, lineNum, "Code", requireName: false)
        {
            Format = "Code; [RepoName/]Name; Version; [InstanceName]";
            var list = ToList();
            if (list.Count < 3) throw new FormatException($"Missing version in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            if (!Version.TryParse(list[2], out var v)) throw new FormatException($"Invalid version format in Code line in IO.conf: {row} {Environment.NewLine}{Format}");
            Version = v;
            if (list[1].Contains('/'))
            {
                ClassName = Name = list[1][(list[1].LastIndexOf('/') + 1)..];
                RepoName = list[1][..(list[1].LastIndexOf('/'))];
                ValidateName(RepoName);
            }
            else
                ClassName = Name = list[1];
            if (list.Count > 3 && !string.IsNullOrWhiteSpace(list[3]))
                Name = list[3];
        }

        public string ClassName { get; }
        public string? RepoName { get; }
        public IOconfCodeRepo? CodeRepo { get; private set; }
        public int Index { get; private set; }
        public Version Version { get; }

        public override void ValidateDependencies(IIOconf ioconf)
        {
            if (!string.IsNullOrEmpty(RepoName))
            {
                CodeRepo = ioconf.GetEntries<IOconfCodeRepo>().Where(r => r.Name == RepoName).SingleOrDefault()
                    ?? throw new FormatException($"CodeRepo with name '{RepoName}' not found for Code line: {Row}");
            }
            else
                CodeRepo = ioconf.GetEntries<IOconfCodeRepo>().Where(r => r.Name == "default").SingleOrDefault() ?? IOconfCodeRepo.Default;

            Index = _nodeInstances.TryGetValue(ioconf, out var instances) ? (byte)instances : (byte)0;
            _nodeInstances.AddOrUpdate(ioconf, (byte)(Index + 1));
            base.ValidateDependencies(ioconf);
        }

        /// <summary>
        /// Generate plugin filename as stored locally in the plugins folder (possibly with repo name prepended - except for the default repo).
        /// </summary>
        public string GenerateLocalFilename() => $"{(CodeRepo is not null && CodeRepo.Name != "default" ? CodeRepo.Name + '-' : "")}{GenerateRepoFilename()}";
        
        /// <summary>
        /// Generate plugin filename as stored in the repository.
        /// </summary>
        public string GenerateRepoFilename() => $"{ClassName}-{Version}-base{_pluginBaseVersion}.dll";

    }
}
