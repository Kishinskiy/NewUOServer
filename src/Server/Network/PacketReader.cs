using System;
using System.Text;

namespace Server.Network
{
    public class PacketReader
    {
        private readonly byte[] _buffer;
        private int _index;

        public int Index => _index;
        public int Size => _buffer.Length;

        public PacketReader(byte[] buffer)
        {
            _buffer = buffer;
            _index = 0;
        }

        // Чтение одного байта
        public byte ReadByte()
        {
            if (_index >= _buffer.Length)
                return 0;

            return _buffer[_index++];
        }

        // Чтение двухбайтового целого числа (ushort / Int16) с конвертацией из Big-Endian
        public ushort ReadUInt16()
        {
            if (_index + 2 > _buffer.Length)
                return 0;

            ushort value = (ushort)((_buffer[_index] << 8) | _buffer[_index + 1]);
            _index += 2;
            return value;
        }

        // Чтение четырехбайтового целого числа (uint / Int32) с конвертацией из Big-Endian
        public uint ReadUInt32()
        {
            if (_index + 4 > _buffer.Length)
                return 0;

            uint value = (uint)((_buffer[_index] << 24) | 
                                (_buffer[_index + 1] << 16) | 
                                (_buffer[_index + 2] << 8) | 
                                _buffer[_index + 3]);
            _index += 4;
            return value;
        }

        // Чтение фиксированной ASCII строки (используется для логина и пароля)
        public string ReadString(int length)
        {
            if (_index + length > _buffer.Length)
                return string.Empty;

            // Находим реальный конец строки до нулевых байт (\0)
            int realLength = 0;
            while (realLength < length && _buffer[_index + realLength] != 0)
            {
                realLength++;
            }

            string result = Encoding.ASCII.GetString(_buffer, _index, realLength);
            _index += length; // Сдвигаем указатель на фиксированную длину
            return result;
        }
    }
}
