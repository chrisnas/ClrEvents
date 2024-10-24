using System;
using System.Text;

namespace Shared
{
    public class EventSourcePayload
    {
        private byte[] _payload;
        private int _pos = 0;

        public EventSourcePayload(byte[] payload)
        {
            _payload = payload;
        }

        public string GetString()
        {
            StringBuilder builder = new StringBuilder();
            while (_pos < _payload.Length)
            {
                var characters = UnicodeEncoding.Unicode.GetString(_payload, _pos, 2);
                _pos += 2;

                if (characters == "\0")
                {
                    break;
                }
                builder.Append(characters);
            }

            return builder.ToString();
        }

        public byte GetByte()
        {
            return _payload[_pos++];
        }

        public UInt16 GetUnit16()
        {
            UInt16 value = BitConverter.ToUInt16(_payload, _pos);
            _pos += sizeof(UInt16);
            return value;
        }

        public UInt32 GetUInt32()
        {
            UInt32 value = BitConverter.ToUInt32(_payload, _pos);
            _pos += sizeof(UInt32);
            return value;
        }

        public UInt64 GetUInt64()
        {
            UInt64 value = BitConverter.ToUInt64(_payload, _pos);
            _pos += sizeof(UInt64);
            return value;
        }

        public Int64 GetInt64()
        {
            Int64 value = BitConverter.ToInt64(_payload, _pos);
            _pos += sizeof(UInt64);
            return value;
        }

        public double GetDouble()
        {
            double value = BitConverter.ToDouble(_payload, _pos);
            _pos += sizeof(double);
            return value;
        }
    }
}
