using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CA_DataUploaderLib.IOconf
{
    public class IOconfCodeRepo : IOconfRow
    {
        public const string ConfigName = "CodeRepo";
        public const string HiddenURL = "******";
        public const string RepoUrlJsonFile = "CodeRepoURLs.json";
        private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

        public static IOconfCodeRepo Default => new($"{ConfigName}; default; {HiddenURL}", 0, "https://caplugins.blob.core.windows.net/default/" );
        private IOconfCodeRepo(string row, int lineNum, string url) : base(row, lineNum, ConfigName)
        {
            URL = url;
        }

        public IOconfCodeRepo(string row, int lineNum) : base(row, lineNum, ConfigName)
        {
            Format = $"{ConfigName}; Name; URL";
            var list = ToList();
            URL = list.Count >= 3 ? list[2] : throw new FormatException($"Missing URL in {ConfigName}-line in IO.conf: {row}{Environment.NewLine}{Format}");

            if (URL != HiddenURL)
                throw new FormatException($"Raw URL in {ConfigName}-line should not happen: {row}{Environment.NewLine}{Format}");
        }

        public string URL { get; private set; }
        
        public override void ValidateDependencies(IIOconf ioconf)
        {
            var repoURLs = ioconf.GetCodeRepoURLs();
            if (!repoURLs.TryGetValue(Name, out var actualUrl))
                throw new FormatException($"URL for {ConfigName} '{Name}' not found!");
            URL = actualUrl;
            base.ValidateDependencies(ioconf);
        }

        /// <summary>
        /// Extracts URLs from CodeRepo lines in the input configuration string
        /// and returns the cleaned input string together with a dictionary of the extracted URLs.
        /// </summary>
        public static (string cleanedInput, Dictionary<string, string> extractedURLs) ExtractAndHideURLs(string input, Dictionary<string, string> existingURLs)
        {
            var lines = input.Split([Environment.NewLine], StringSplitOptions.None);
            Dictionary<string, string> repoURLs = new(existingURLs);
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Trim().StartsWith(ConfigName, StringComparison.Ordinal))
                    continue;
                
                var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 2)
                    throw new FormatException($"Missing URL in {ConfigName}-line : {line}");
                if (parts[2] == HiddenURL)
                    continue; // already hidden
                    
                var repoName = parts[1].Trim();
                var url = parts[2].Trim();
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    throw new FormatException($"Invalid URL format in {ConfigName}-line: {line}");
                repoURLs[repoName] = url;
                lines[i] = line.Replace(url, HiddenURL);
            }

            return (string.Join(Environment.NewLine, lines), repoURLs);
        }

        public static Dictionary<string, string> ReadURLsFromFile()
        {
            return System.IO.File.Exists(RepoUrlJsonFile)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(RepoUrlJsonFile)) ?? []
                : [];
        }

        /// <summary>
        /// Add to or update the URLs in the JSON file with the provided dictionary of extracted URLs.
        /// </summary>
        /// <param name="extractedURLs"></param>
        public static void WriteURLsToFile(Dictionary<string, string> extractedURLs)
        {
            var repoURLs = ReadURLsFromFile();

            foreach (var repoUrl in extractedURLs)
                repoURLs[repoUrl.Key] = repoUrl.Value;

            var jsonOut = JsonSerializer.Serialize(repoURLs, jsonSerializerOptions);
            System.IO.File.WriteAllText(RepoUrlJsonFile, jsonOut);
        }
    }
}
