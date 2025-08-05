using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CA_DataUploaderLib
{
    public class EventFiredArgs : EventArgs
    {
        public EventFiredArgs(string data, EventType eventType, DateTime timespan, string? user = null) : this(data, (byte) eventType, timespan, user: user) { }
        public EventFiredArgs(string data, byte eventType, DateTime timespan, byte nodeId = byte.MaxValue, string? user = null)
        {
            Data = data;
            EventType = eventType;
            TimeSpan = timespan;
            NodeId = nodeId;
            User = user;
        }

        public string Data { get; }
        public byte EventType { get; }
        public DateTime TimeSpan { get; }
        public byte NodeId { get; }
        public string? User { get; }

        public byte[] ToByteArray()
        {
            var timeTicks = TimeSpan.Ticks;
            var encoding = Encoding.UTF8;
            var dataBytesCount = encoding.GetByteCount(Data);
            var userBytesCount = User is not null ? (ushort)encoding.GetByteCount(User) : (ushort)0;
            var bytes = new byte[1 + 1 + sizeof(long) + sizeof(uint) + dataBytesCount + sizeof(ushort) + userBytesCount]; //version + event type + time ticks + data count + data bytes + user count + user bytes
            bytes[0] = 1; //Version
            bytes[1] = EventType;
            MemoryMarshal.Write(bytes.AsSpan()[2..], in timeTicks);

            var span = bytes.AsSpan()[(2 + sizeof(long))..];
            MemoryMarshal.Write(span, in dataBytesCount);
            if (dataBytesCount != encoding.GetBytes(Data, span[sizeof(uint)..]))
                throw new InvalidOperationException($"Unexpected error getting utf8 bytes for event: {Data}");
            
            span = span[(sizeof(uint) + dataBytesCount)..];
            MemoryMarshal.Write(span, in userBytesCount);
            if (User is not null && userBytesCount != encoding.GetBytes(User, span[sizeof(ushort)..]))
                throw new InvalidOperationException($"Unexpected error getting utf8 bytes for user: {User}");

            return bytes;
        }
    }
}
