using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CA_DataUploaderLib
{
    public class SystemChangeNotificationData
    {
        public string NodeName { get; init; }
        public List<BoardInfo> Boards { get; init; }
        private static JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        internal string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
        internal static SystemChangeNotificationData ParseJson(string data) => JsonSerializer.Deserialize<SystemChangeNotificationData>(data, SerializerOptions);
        /// <summary>
        /// Gets utf8 bytes of a json representation containing a top level array with only the properties expected by the remote boards serials endpoint
        /// </summary>
        public byte[] ToBoardsSerialInfoJsonUtf8Bytes(DateTime timeSpan)
        {
            using var stream = new MemoryStream(Boards.Count * 400);
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartArray();
                foreach (var board in Boards)
                {
                    if (board.MappedBoardName == null) continue; //only boards that have a corresponding IO.conf entry are shared
                    writer.WriteStartObject();
                    writer.WriteString("SerialNumber", board.SerialNumber ?? board.Port);
                    writer.WriteString("productType", board.ProductType);
                    writer.WriteString("ProductSubType", board.ProductSubType);
                    writer.WriteString("McuFamily", board.McuFamily);
                    writer.WriteString("SoftwareVersion", board.SoftwareVersion);
                    writer.WriteString("CompileDate", board.CompileDate);
                    writer.WriteString("GitSha", board.GitSha);
                    writer.WriteString("PcbVersion", board.PcbVersion);
                    writer.WriteString("Calibration", board.Calibration);
                    writer.WriteString("TimeStamp", timeSpan);
                    writer.WriteString("NodeName", NodeName);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            return stream.ToArray();
        }

        public class BoardInfo
        {
            public string SerialNumber { get; init; }
            public string ProductType { get; init; }
            public string ProductSubType { get; init; }
            public string McuFamily { get; init; }
            public string SoftwareVersion { get; init; }
            public string CompileDate { get; init; }
            public string GitSha { get; init; }
            public string PcbVersion { get; init; }
            public string Calibration { get; init; }
            public string MappedBoardName { get; init; }
            public string Port { get; init; }
            public string UpdatedCalibration { get; init; }
        }
    }
}
