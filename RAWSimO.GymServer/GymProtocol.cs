using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RAWSimO.GymServer
{
    internal static class GymProtocol
    {
        // Message format: [4 bytes uint32 JSON length][JSON bytes][image bytes]
        public static void WriteMessage(Stream stream, object headerObj, byte[]? imageData = null)
        {
            string json = JsonSerializer.Serialize(headerObj);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] lenBytes = BitConverter.GetBytes((uint)jsonBytes.Length);
            if (!BitConverter.IsLittleEndian) Array.Reverse(lenBytes);

            stream.Write(lenBytes, 0, 4);
            stream.Write(jsonBytes, 0, jsonBytes.Length);
            if (imageData != null)
                stream.Write(imageData, 0, imageData.Length);
        }

        public static (string json, byte[]? extraBytes) ReadMessage(Stream stream, int extraByteCount = 0)
        {
            byte[] lenBytes = ReadExact(stream, 4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
            int jsonLen = (int)BitConverter.ToUInt32(lenBytes, 0);
            byte[] jsonBytes = ReadExact(stream, jsonLen);
            string json = Encoding.UTF8.GetString(jsonBytes);
            byte[]? extra = extraByteCount > 0 ? ReadExact(stream, extraByteCount) : null;
            return (json, extra);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buf, read, count - read);
                if (n == 0) throw new EndOfStreamException("Connection closed");
                read += n;
            }
            return buf;
        }
    }
}
