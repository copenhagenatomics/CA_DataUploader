using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfFileTests
    {
        [TestMethod]
        public void WhenTwoRowsInDifferentGroupsHaveTheSameName_ThenNoExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeK; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }

        [TestMethod]
        [ExpectedException(typeof(Exception), "An exception should have been thrown but was not.")]
        public void WhenTwoRowsInTheSameGroupHaveTheSameName_ThenAnExceptionIsThrown()
        {
            var _ = new IOconfFile(new(){
                "Map; 1234567890; tm01",
                "TypeJ; sameName; tm01; 1",
                "TypeJ; sameName; tm01; 2" });
        }
    }
}
