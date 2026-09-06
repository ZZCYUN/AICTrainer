using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AICShared;

namespace AICTrainer.Services
{
    public class TrainerClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

        private TcpClient? _tcpClient;
        private StreamWriter? _writer;
        private readonly object _writeLock = new object();
        private CancellationTokenSource? _listenCts;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        public event Action<GameStateDto>? OnStateReceived;
        public event Action? OnConnected;
        public event Action? OnDisconnected;

        public async Task<bool> ConnectAsync(int timeoutMs = 2000)
        {
            InternalDisconnect(false);

            try
            {
                var client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, PipeConstants.IpcPort);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
                {
                    try { client.Close(); } catch { }
                    InternalDisconnect(false);
                    return false;
                }
                await connectTask;

                _tcpClient = client;
                var stream = _tcpClient.GetStream();
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                OnConnected?.Invoke();

                _listenCts = new CancellationTokenSource();
                _ = Task.Run(() => ListenLoop(stream, _listenCts.Token));

                return true;
            }
            catch
            {
                InternalDisconnect(false);
                return false;
            }
        }

        public void Disconnect()
        {
            InternalDisconnect(true);
        }

        private void InternalDisconnect(bool fireEvent)
        {
            _listenCts?.Cancel();
            _listenCts = null;

            bool wasConnected = false;
            lock (_writeLock)
            {
                wasConnected = _tcpClient != null;
                try { _writer?.Dispose(); } catch { }
                _writer = null;
                try { _tcpClient?.Close(); } catch { }
                _tcpClient = null;
            }

            if (fireEvent && wasConnected)
            {
                OnDisconnected?.Invoke();
            }
        }

        public void SendConfig(ModConfigDto config)
        {
            if (!IsConnected || _writer == null) return;

            try
            {
                string cfgJson = JsonSerializer.Serialize(config, JsonOptions);
                var msg = new IpcMessage
                {
                    Type = "ApplyConfig",
                    JsonData = cfgJson
                };
                string rawJson = JsonSerializer.Serialize(msg, JsonOptions);

                lock (_writeLock)
                {
                    _writer.WriteLine(rawJson);
                    _writer.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TrainerClient] SendConfig failed: " + ex.Message);
            }
        }

        public void SendAction(string actionName, int intParam = 0, float floatParam = 0f)
        {
            if (!IsConnected || _writer == null) return;

            try
            {
                var act = new ActionMessage
                {
                    ActionName = actionName,
                    IntParam = intParam,
                    FloatParam = floatParam
                };
                var msg = new IpcMessage
                {
                    Type = "Action",
                    JsonData = JsonSerializer.Serialize(act, JsonOptions)
                };
                string rawJson = JsonSerializer.Serialize(msg, JsonOptions);

                lock (_writeLock)
                {
                    _writer.WriteLine(rawJson);
                    _writer.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[TrainerClient] SendAction failed: " + ex.Message);
            }
        }

        private void ListenLoop(NetworkStream stream, CancellationToken token)
        {
            try
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
                {
                    while (!token.IsCancellationRequested && _tcpClient != null && _tcpClient.Connected)
                    {
                        string? line = reader.ReadLine();
                        if (line == null) break;

                        try
                        {
                            var msg = JsonSerializer.Deserialize<IpcMessage>(line, JsonOptions);
                            if (msg != null && msg.Type == "StateSync")
                            {
                                var state = JsonSerializer.Deserialize<GameStateDto>(msg.JsonData, JsonOptions);
                                if (state != null)
                                {
                                    OnStateReceived?.Invoke(state);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            finally
            {
                Disconnect();
            }
        }
    }
}
