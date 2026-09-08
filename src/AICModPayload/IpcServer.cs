using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AICShared;
using UnityEngine;

namespace AICMod
{
    public class IpcServer
    {
        private static IpcServer? _instance;
        public static IpcServer Instance => _instance ??= new IpcServer();

        private Thread? _serverThread;
        private volatile bool _isRunning;
        private TcpListener? _listener;
        private TcpClient? _activeClient;
        private StreamWriter? _writer;
        private readonly object _writeLock = new object();

        public event Action<ModConfigDto>? OnConfigReceived;
        public event Action<ActionMessage>? OnActionReceived;

        public bool IsClientConnected => _activeClient != null && _activeClient.Connected;

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _serverThread = new Thread(ServerWorker)
            {
                IsBackground = true,
                Name = "AICMod_IpcServer"
            };
            _serverThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                lock (_writeLock)
                {
                    _writer?.Dispose();
                    _writer = null;
                }
                _activeClient?.Close();
                _listener?.Stop();
            }
            catch { }
        }

        public void SendState(GameStateDto state)
        {
            if (!IsClientConnected || _writer == null) return;

            try
            {
                string stateJson = JsonUtility.ToJson(state);
                var msg = new IpcMessage
                {
                    Type = "StateSync",
                    JsonData = stateJson
                };
                string msgJson = JsonUtility.ToJson(msg);

                lock (_writeLock)
                {
                    if (_writer != null && _activeClient != null && _activeClient.Connected)
                    {
                        _writer.WriteLine(msgJson);
                        _writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] Failed to send state over IPC: " + ex.Message);
            }
        }

        public void SendMapList(MapListDto mapList)
        {
            if (!IsClientConnected || _writer == null) return;

            try
            {
                // 注意：Unity JsonUtility 无法序列化 List<自定义类> 字段（返回 {}），列表统一手工构造 JSON
                string listJson = AicJson.MapList(mapList.Maps);
                var msg = new IpcMessage
                {
                    Type = "MapList",
                    JsonData = listJson
                };
                string msgJson = JsonUtility.ToJson(msg);

                lock (_writeLock)
                {
                    if (_writer != null && _activeClient != null && _activeClient.Connected)
                    {
                        _writer.WriteLine(msgJson);
                        _writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] Failed to send map list over IPC: " + ex.Message);
            }
        }

        public void SendItemList(ItemListDto itemList)
        {
            if (!IsClientConnected || _writer == null) return;

            try
            {
                // 注意：Unity JsonUtility 无法序列化 List<自定义类> 字段（返回 {}），列表统一手工构造 JSON
                string listJson = AicJson.ItemList(itemList.Items);
                var msg = new IpcMessage
                {
                    Type = "ItemList",
                    JsonData = listJson
                };
                string msgJson = JsonUtility.ToJson(msg);

                lock (_writeLock)
                {
                    if (_writer != null && _activeClient != null && _activeClient.Connected)
                    {
                        _writer.WriteLine(msgJson);
                        _writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] Failed to send item list over IPC: " + ex.Message);
            }
        }

        private void ServerWorker()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, PipeConstants.IpcPort);
                _listener.Start();
                Debug.Log($"[AICMod] IPC TCP Server listening on 127.0.0.1:{PipeConstants.IpcPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AICMod] Failed to start IPC TCP listener: " + ex);
                return;
            }

            while (_isRunning)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    Debug.Log("[AICMod] Trainer connected via TCP IPC.");

                    _activeClient = client;
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        lock (_writeLock)
                        {
                            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                        }

                        // 立即向修改器客户端发送初始就绪状态（无需等待进入关卡）
                        SendState(new GameStateDto
                        {
                            IsGameReady = false,
                            GameVersion = "0.30d",
                            EngineVersion = "2022.3.62f2",
                            ActivePatchCount = ResilientPatcher.ActivePatches,
                            TotalPatchCount = ResilientPatcher.TotalPatches
                        });

                        while (_isRunning && client.Connected)
                        {
                            string? line = reader.ReadLine();
                            if (line == null) break;

                            ProcessIncomingMessage(line);
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Debug.LogWarning("[AICMod] IPC Server client loop: " + ex.Message);
                        Thread.Sleep(300);
                    }
                }
                finally
                {
                    lock (_writeLock)
                    {
                        _writer = null;
                    }
                    try { _activeClient?.Close(); } catch { }
                    _activeClient = null;
                }
            }
        }

        private void ProcessIncomingMessage(string rawJson)
        {
            try
            {
                var msg = JsonUtility.FromJson<IpcMessage>(rawJson);
                if (msg == null) return;

                switch (msg.Type)
                {
                    case "ApplyConfig":
                        var cfg = JsonUtility.FromJson<ModConfigDto>(msg.JsonData);
                        if (cfg != null)
                        {
                            AICModConfig.Update(cfg);
                            OnConfigReceived?.Invoke(cfg);
                        }
                        break;

                    case "Action":
                        var act = JsonUtility.FromJson<ActionMessage>(msg.JsonData);
                        if (act != null)
                        {
                            OnActionReceived?.Invoke(act);
                        }
                        break;

                    case "Ping":
                        lock (_writeLock)
                        {
                            if (_writer != null && _activeClient != null && _activeClient.Connected)
                            {
                                var pong = new IpcMessage { Type = "Pong", JsonData = "" };
                                _writer.WriteLine(JsonUtility.ToJson(pong));
                                _writer.Flush();
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AICMod] Error parsing incoming IPC message: " + ex);
            }
        }
    }
}
