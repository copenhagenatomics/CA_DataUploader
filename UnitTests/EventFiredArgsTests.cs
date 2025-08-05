using CA_DataUploaderLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Text;

namespace UnitTests
{
    [TestClass]
    public class EventFiredArgsTests
    {
        [TestMethod]
        public void ToByteArray_WithUser_ReturnsExpectedBytes()
        {
            // Arrange
            const string Data = "data";
            var dataByteCount = Encoding.UTF8.GetByteCount(Data);
            DateTime timespan = new DateTime(2012, 1, 1, 1, 1, 1, 1, DateTimeKind.Utc);
            const string User = "TestUser";
            var userByteCount = (ushort) Encoding.UTF8.GetByteCount(User);
            var args = new EventFiredArgs(Data, 2, timespan, user: User);
            
            // Act
            var bytes = args.ToByteArray();

            // Assert
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, bytes.Take(2).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(timespan.Ticks), bytes.Skip(2).Take(sizeof(long)).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(dataByteCount), bytes.Skip(2 + sizeof(long)).Take(sizeof(uint)).ToList());
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Data), bytes.Skip(2 + sizeof(long) + sizeof(uint)).Take(dataByteCount).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(userByteCount), bytes.Skip(2 + sizeof(long) + sizeof(uint) + dataByteCount).Take(sizeof(ushort)).ToList());
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(User), bytes.Skip(2 + sizeof(long) + sizeof(uint) + dataByteCount + sizeof(ushort)).Take(userByteCount).ToList());
        }

        [TestMethod]
        public void ToByteArray_WithoutUser_ReturnsExpectedBytes()
        {
            // Arrange
            const string Data = "my message";
            var dataByteCount = Encoding.UTF8.GetByteCount(Data);
            DateTime timespan = new DateTime(2012, 1, 1, 1, 1, 1, 1, DateTimeKind.Utc);
            var args = new EventFiredArgs(Data, 2, timespan);

            // Act
            var bytes = args.ToByteArray();

            // Assert
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, bytes.Take(2).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(timespan.Ticks), bytes.Skip(2).Take(sizeof(long)).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(dataByteCount), bytes.Skip(2 + sizeof(long)).Take(sizeof(uint)).ToList());
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Data), bytes.Skip(2 + sizeof(long) + sizeof(uint)).Take(dataByteCount).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes((ushort)0), bytes.Skip(2 + sizeof(long) + sizeof(uint) + dataByteCount).Take(sizeof(ushort)).ToList());
        }
    }
}
