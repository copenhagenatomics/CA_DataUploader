using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CA_DataUploaderLib
{
    public class EventFiredArgs : EventArgs
    {
        public EventFiredArgs(string data, EventType eventType, DateTime timespan) : this(data, (byte) eventType, timespan) { }
        public EventFiredArgs(string data, byte eventType, DateTime timespan)
        {
            Data = data;
            EventType = eventType;
            TimeSpan = timespan;
        }

        public string Data { get; }
        public byte EventType { get; }
        public DateTime TimeSpan { get; }

        public byte[] ToByteArray()
        {
            var timeTicks = TimeSpan.Ticks;
            var encoding = Encoding.UTF8;
            var dataBytesCount = encoding.GetByteCount(Data);
            var bytes = new byte[1 + 1 + sizeof(long) + dataBytesCount]; //version + event type + time ticks + data bytes
            bytes[0] = 0;
            bytes[1] = EventType;
            MemoryMarshal.Write(bytes.AsSpan()[2..], in timeTicks);
            var startOfDataIndex = 1 + 1 + sizeof(long);
            if (dataBytesCount != encoding.GetBytes(Data, 0, Data.Length, bytes, startOfDataIndex))
                throw new InvalidOperationException($"unexpected error getting utf8 bytes for event: {Data}");
            return bytes;
        }
    }
}
