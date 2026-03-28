using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

namespace RAWSimO.GymServer
{
    internal class TcpGymServer : IDisposable
    {
        private readonly GymSimulationHost _host;
        private readonly int _port;
        private TcpListener? _listener;
        private bool _running = false;

        public TcpGymServer(GymSimulationHost host, int port = 7654)
        {
            _host = host;
            _port = port;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _running = true;
            Console.WriteLine($"GymServer listening on port {_port}");

            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { break; }

                Console.WriteLine("Client connected.");
                var thread = new Thread(() => HandleClient(client));
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            try
            {
                while (true)
                {
                    var (json, _) = GymProtocol.ReadMessage(stream);
                    var doc = JsonDocument.Parse(json);
                    string cmd = doc.RootElement.GetProperty("cmd").GetString()!;

                    if (cmd == "reset")
                    {
                        int seed = doc.RootElement.TryGetProperty("seed", out var s) ? s.GetInt32() : 0;
                        var obs = _host.Reset(seed);

                        int nBots = _host.BotCount;
                        var header = new { n_bots = nBots, image_size = new[] { _host.CamConfig.ImageWidth, _host.CamConfig.ImageHeight, 3 }, n_actions = 5 };

                        // Concatenate all images
                        byte[] allImages = ConcatImages(obs, nBots);
                        GymProtocol.WriteMessage(stream, header, allImages);
                    }
                    else if (cmd == "step")
                    {
                        var actionsNode = doc.RootElement.GetProperty("actions");
                        var actions = new Dictionary<string, int[]>();
                        foreach (var prop in actionsNode.EnumerateObject())
                        {
                            var arr = prop.Value;
                            var vals = new int[arr.GetArrayLength()];
                            int i = 0;
                            foreach (var v in arr.EnumerateArray())
                                vals[i++] = v.GetInt32();
                            actions[prop.Name] = vals;
                        }

                        var result = _host.Step(actions);

                        // Build response header
                        var rewards = new Dictionary<string, double>();
                        var dones = new Dictionary<string, bool>();
                        var truncated = new Dictionary<string, bool>();
                        var infos = new Dictionary<string, object>();

                        foreach (var (botId, reward) in result.Rewards)
                        {
                            rewards[botId] = reward;
                            dones[botId] = result.SimulationDone;
                            truncated[botId] = result.Truncated;
                            infos[botId] = result.Infos[botId];
                        }

                        var header = new { rewards, dones, truncated, infos };
                        byte[] allImages = ConcatImages(result.Observations, result.Observations.Count);
                        GymProtocol.WriteMessage(stream, header, allImages);
                    }
                    else if (cmd == "close")
                    {
                        Console.WriteLine("Close command received. Shutting down.");
                        _running = false;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        private byte[] ConcatImages(Dictionary<string, byte[]> obs, int nBots)
        {
            int imgSize = _host.CamConfig.ImageWidth * _host.CamConfig.ImageHeight * 3;
            byte[] result = new byte[nBots * imgSize];
            for (int b = 0; b < nBots; b++)
            {
                string key = $"bot_{b}";
                if (obs.TryGetValue(key, out var img))
                    Array.Copy(img, 0, result, b * imgSize, Math.Min(img.Length, imgSize));
            }
            return result;
        }

        public void Dispose()
        {
            _running = false;
            _listener?.Stop();
            _host.Dispose();
        }
    }
}
