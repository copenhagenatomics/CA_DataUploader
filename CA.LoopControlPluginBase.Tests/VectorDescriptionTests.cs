namespace CA.LoopControlPluginBase.Tests
{
    [TestClass]
    public sealed class VectorDescriptionTests
    {
        [TestMethod]
        public void CanAccess3DescriptionFields()
        {
            var desc = new VectorDescription(["f1", "f2", "f3"]);
            Assert.AreEqual(3, desc.Count);
            Assert.AreEqual("f1", desc[0]);
            Assert.AreEqual("f2", desc[1]);
            Assert.AreEqual("f3", desc[2]);
        }
    }
}
