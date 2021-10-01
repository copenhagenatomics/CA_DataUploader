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
        public void ToByteArrayReturnsExpectedBytes()
        {
            const string Data = "my message";
            DateTime timespan = new DateTime(2012, 1, 1, 1, 1, 1, 1, DateTimeKind.Utc);
            var args = new EventFiredArgs(Data, 2, timespan);
            var bytes = args.ToByteArray();
            CollectionAssert.AreEqual(new byte[] { 0, 2 }, bytes.Take(2).ToList());
            CollectionAssert.AreEqual(BitConverter.GetBytes(timespan.Ticks), bytes.Skip(2).Take(sizeof(long)).ToList());
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Data), bytes.Skip(2).Skip(sizeof(long)).ToList());
        }
    }
}
