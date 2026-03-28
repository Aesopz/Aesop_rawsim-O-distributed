using System;
using System.IO;
using System.Text.Json;
using RAWSimO.GymServer;
using Xunit;

namespace RAWSimO.GymServer.Tests
{
    public class GymProtocolTests
    {
        [Fact]
        public void WriteRead_HeaderOnly_RoundTrips()
        {
            var ms = new MemoryStream();
            GymProtocol.WriteMessage(ms, new { cmd = "reset", seed = 42 });
            ms.Position = 0;

            var (json, extra) = GymProtocol.ReadMessage(ms);
            var doc = JsonDocument.Parse(json);
            Assert.Equal("reset", doc.RootElement.GetProperty("cmd").GetString());
            Assert.Equal(42, doc.RootElement.GetProperty("seed").GetInt32());
            Assert.Null(extra);
        }

        [Fact]
        public void WriteRead_WithImageData_RoundTrips()
        {
            byte[] fakeImage = new byte[96 * 96 * 3];
            new Random(42).NextBytes(fakeImage);

            var ms = new MemoryStream();
            GymProtocol.WriteMessage(ms, new { n_bots = 1 }, fakeImage);
            ms.Position = 0;

            var (json, extra) = GymProtocol.ReadMessage(ms, fakeImage.Length);
            Assert.Equal(fakeImage.Length, extra!.Length);
            Assert.Equal(fakeImage[0], extra[0]);
            Assert.Equal(fakeImage[^1], extra[^1]);
        }

        [Fact]
        public void WriteRead_MultipleMessages_RoundTrip()
        {
            var ms = new MemoryStream();

            // Write two messages
            GymProtocol.WriteMessage(ms, new { cmd = "reset", seed = 0 });
            GymProtocol.WriteMessage(ms, new { cmd = "step" });
            ms.Position = 0;

            // Read first
            var (json1, _) = GymProtocol.ReadMessage(ms);
            var doc1 = JsonDocument.Parse(json1);
            Assert.Equal("reset", doc1.RootElement.GetProperty("cmd").GetString());

            // Read second
            var (json2, _) = GymProtocol.ReadMessage(ms);
            var doc2 = JsonDocument.Parse(json2);
            Assert.Equal("step", doc2.RootElement.GetProperty("cmd").GetString());
        }

        [Fact]
        public void WriteRead_LargeJson_RoundTrips()
        {
            // Build a message with many bot entries
            var rewards = new System.Collections.Generic.Dictionary<string, double>();
            for (int i = 0; i < 50; i++)
                rewards[$"bot_{i}"] = i * 0.01;

            var ms = new MemoryStream();
            GymProtocol.WriteMessage(ms, new { rewards });
            ms.Position = 0;

            var (json, _discard) = GymProtocol.ReadMessage(ms);
            var doc = JsonDocument.Parse(json);
            int count = 0;
            foreach (var prop in doc.RootElement.GetProperty("rewards").EnumerateObject())
                count++;
            Assert.Equal(50, count);
        }
    }
}
