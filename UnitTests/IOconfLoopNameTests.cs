using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfLoopNameTests
    {
        [DataRow("0_cannot_start_with_number")]
        [DataRow("æøå")]
        [DataRow("hat^")]
        [DataRow("pipe|")]
        [DataRow("back_\\slash")]
        [DataRow("forward_/slash")]
        [DataRow("ampersand&")]
        [DataRow("question?")]
        [DataRow("colon:")]
        [DataRow("exclamation!")]
        [DataRow("half½")]
        [DataRow("paragraph§")]
        [DataRow("turtle¤")]
        [DataRow("hash#tag")]
        [DataRow("percent_%rel")]
        [DataRow("angle<bracket>")]
        [DataRow("curly{bracket}")]
        [DataRow("square[bracket]")]
        [DataRow("name with space")]
        [DataRow("name_with(parenthesis)")]
        [DataRow("name~with~tilde")]
        [DataRow("name*with*star")]
        [DataRow("name,with,comma")]
        [DataRow("name.with.dot")]
        [DataRow("name=with=equals")]
        [DataRow("name+with+plus")]
        [TestMethod]
        public void InvalidName(string name) 
        {
            var ex = Assert.Throws<FormatException>(() => new IOconfLoopName($"LoopName; {name}; Normal; https://stagingtsserver.copenhagenatomics.come", 0));
            Assert.StartsWith($"Invalid loop name: {name}", ex.Message, ex.Message);
        }

        [DataRow("name_with_number_42")]
        [DataRow("UPPERCASE")]
        [DataRow("lowercase")]
        [DataRow("-name-starting-with-dash")]
        [DataRow("name-with-dash")]
        [DataRow("_name_starting_with_underscore")]
        [DataRow("name_with_underscore")]
        [TestMethod]
        public void ValidName(string name)
        {
            _ = new IOconfLoopName($"LoopName; {name}; Normal; https://stagingtsserver.copenhagenatomics.come", 0);
        }

        [DataRow("https://stagingtsserver.copenhagenatomics.com")]
        [DataRow("http://localhost")]
        [DataRow("http://localhost:8080")]
        [DataRow("http://127.0.0.1:8080")]
        [DataRow("http://[::1]:8080")]
        [DataRow("https://example.com:8443/api/")]
        [TestMethod]
        public void ValidServer(string server)
        {
            var loopName = new IOconfLoopName($"LoopName;TestLoop;Normal;{server}", 0);

            Assert.AreEqual(server, loopName.Server);
        }

        [DataRow("not-a-url")]
        [DataRow("example.com")]
        [DataRow("/relative/path")]
        [DataRow("ftp://example.com")]
        [DataRow("file:///server")]
        [DataRow("mailto:user@example.com")]
        [DataRow("https://")]
        [DataRow("https://exa mple.com")]
        [DataRow("https://example.com:invalid")]
        [DataRow("https://example.com:65536")]
        [DataRow("https://example.com/path with spaces")]
        [DataRow(@"https://example.com\path")]
        [TestMethod]
        public void InvalidServer(string server)
        {
            var ex = Assert.Throws<FormatException>(
                () => new IOconfLoopName($"LoopName;TestLoop;Normal;{server}", 0));

            Assert.StartsWith("Invalid server URL:", ex.Message, ex.Message);
        }

        [DataRow("LoopName;TestLoop;Normal")]
        [DataRow("LoopName;TestLoop;Normal;")]
        [DataRow("LoopName;TestLoop;Normal;//this is a comment")]
        [TestMethod]
        public void OmittedServerUsesDefault(string row)
        {
            var loopName = new IOconfLoopName(row, 0);

            Assert.AreEqual("https://stagingtsserver.copenhagenatomics.com", loopName.Server);
        }
    }
}
