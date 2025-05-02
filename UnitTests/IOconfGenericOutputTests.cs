using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib.IOconf;
using System.Collections.Generic;

namespace UnitTests
{
    [TestClass]
    public class IOconfGenericOutputTests
    {

        [TestMethod]
        public void TargetFieldsTest()
        {
            // Arrange + act
            var sut = new IOconfGenericOutput("GenericOutput; genericTest; box; 0; ${field_1} p1 $field_2 field_2 $field_1 ${field_3}; -1", 0);

            // Assert
            CollectionAssert.AreEqual(new List<string> { "field_1", "field_2", "field_1", "field_3" }, sut.TargetFields);
        }

        [TestMethod]
        public void GetCommandTest()
        {
            // Arrange
            var sut = new IOconfGenericOutput("GenericOutput; genericTest; box; 0; ${field_1} p1 $field_2 field_2 $field_1 1; -1", 0);
            
            // Act
            var command = sut.GetCommand([2.1, 3.2, 2.1]);
            
            // Assert
            Assert.AreEqual("2.1 p1 3.2 field_2 2.1 1", command);
        }
    }
}
