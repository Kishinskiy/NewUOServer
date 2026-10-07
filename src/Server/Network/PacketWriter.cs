using System;
using System.IO;
using System.Text;

namespace Server.Network
{
    public class PacketWriter : IDisposable
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _writer;

        public PacketWriter() => _writer = new BinaryWriter(_stream);

        public void WriteByte(byte value) => _writer.Write(value);
        
        public void WriteUInt16(ushort value)
        {
            _writer.Write((byte)(value >> 8));
            _writer.Write((byte)value);
        }

        public void WriteUInt32(uint value)
        {
            _writer.Write((byte)(value >> 24));
            _writer.Write((byte)(value >> 16));
            _writer.Write((byte)(value >> 8));
            _writer.Write((byte)value);
        }

        public void WriteStringFixed(string value, int fixedLength)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            int lengthToSend = Math.Min(bytes.Length, fixedLength);
            _writer.Write(bytes, 0, lengthToSend);
            for (int i = lengthToSend; i < fixedLength; i++) _writer.Write((byte)0);
        }

        public void SeekAndWriteLength()
        {
            ushort length = (ushort)_stream.Length;
            _stream.Position = 1;
            WriteUInt16(length);
        }

        public byte[] ToArray() => _stream.ToArray();
        public void Dispose() { _writer.Dispose(); _stream.Dispose(); }
    }
}
