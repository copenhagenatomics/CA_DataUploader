using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using CA_DataUploaderLib.IOconf;

namespace UnitTests
{
    [TestClass]
    public class BoardSettingsTests
    {
        [TestMethod]
        public void DefaultSettingsParsesExpectedValuesFormat()
        {
            var parser = BoardSettings.Default.Parser;
            CollectionAssert.AreEqual(new[] {0.01,0.05,0.06}, parser.TryParseAsDoubleList("0.01,0.05,0.06"));
        }
    }
}
