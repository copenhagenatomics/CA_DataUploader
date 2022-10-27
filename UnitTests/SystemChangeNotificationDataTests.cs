using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CA_DataUploaderLib;
using System.Text.Json;
using System.Linq;
using System.Text;

namespace UnitTests
{
    [TestClass]
    public class SystemChangeNotificationDataTests
    {
        [TestMethod]
        public void TestMethod1()
        {
            var data = new SystemChangeNotificationData("node", new() { 
                new ("usb1") 
                {
                    SerialNumber = "2B001E3335510137353430",
                    ProductType = "AC Board",
                    ProductSubType = "0",
                    McuFamily = "STM32F401xB/C Rev 1",
                    SoftwareVersion = "5e28d00",
                    CompileDate = "2022-02-14",
                    GitSha = "5e28d00ea474939642116b8086793900c168c7e4",
                    PcbVersion = "5.8",
                    Calibration = "abc",
                    UpdatedCalibration = "ccc",
                    MappedBoardName = "my board"
                },
                new ("usb2")
                {
                    ProductType = "Temperature Board",
                    CompileDate = "2022-01-15",
                    PcbVersion = "4.1",
                    MappedBoardName = "second board"
                }
            });
            var bytes = data.ToBoardsSerialInfoJsonUtf8Bytes(new DateTime(2022, 1, 1, 1, 1, 1, DateTimeKind.Utc));
            var val = Encoding.UTF8.GetString(bytes);
            //now simulate the receiver deserializing
            var serials = JsonSerializer.Deserialize<McuserialNumber[]>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(serials);
            CollectionAssert.AreEqual(new[] { "2B001E3335510137353430", "usb2" }, serials.Select(b => b.SerialNumber).ToList());
            CollectionAssert.AreEqual(new[] { "AC Board", "Temperature Board" }, serials.Select(b => b.ProductType).ToList());
            CollectionAssert.AreEqual(new[] { "2022-02-14", "2022-01-15" }, serials.Select(b => b.CompileDate).ToList());
        }

        //this is just used to reproduce similar parsing to the receiver of the data
        public class McuserialNumber
        {
            public int? LoopNameId { get; set; }
            public int? PlotNameId { get; set; }
            public string? SerialNumber { get; set; }
            public string? ProductType { get; set; }
            public string? ProductSubType { get; set; }
            public string? McuFamily { get; set; }
            public string? SoftwareVersion { get; set; }
            public string? CompileDate { get; set; }
            public string? GitSha { get; set; }
            public string? PcbVersion { get; set; }
            public string? Calibration { get; set; }
            public DateTime TimeStamp { get; set; }
        }
    }
}
