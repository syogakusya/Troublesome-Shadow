using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    public enum PoseTransportType
    {
        WebSocket,
        Udp
    }

    public class PoseReceiver : MonoBehaviour
    {
        [FormerlySerializedAs("transportType")] [Header("Connection")]
        public PoseTransportType _transportType = PoseTransportType.WebSocket;
        [FormerlySerializedAs("host")] public string _host = "127.0.0.1";
        [FormerlySerializedAs("port")] public int _port = 9000;
        [FormerlySerializedAs("webSocketPath")] public string _webSocketPath = "/pose";
        [FormerlySerializedAs("reconnectDelay")] public float _reconnectDelay = 2f;
        [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;

        public event Action Connected;
        public event Action Disconnected;

        private readonly ConcurrentQueue<SkeletonSample> _incoming = new ConcurrentQueue<SkeletonSample>();
        private CancellationTokenSource _cts;
        private Task _worker;
        private ClientWebSocket _webSocket;
        private UdpClient _udpClient;
        private long _framesReceived;
        private long _lastTimestampMs;
        private readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            FloatParseHandling = FloatParseHandling.Double
        };

        public long TotalFramesReceived => Interlocked.Read(ref _framesReceived);
        public long LastTimestampMs => Interlocked.Read(ref _lastTimestampMs);
        public int PendingSamples => _incoming.Count;

        private void OnEnable()
        {
            StartReceiver();
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        public bool TryDequeue(out SkeletonSample sample) => _incoming.TryDequeue(out sample);

        public void StartReceiver()
        {
            if (_worker != null && !_worker.IsCompleted)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            while (_incoming.TryDequeue(out _))
            {
            }
            Interlocked.Exchange(ref _framesReceived, 0);
            if (_debugLogging)
            {
                Debug.Log($"PoseReceiver starting ({_transportType})");
            }
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }

        public void StopReceiver()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // ignored
            }

            _worker = null;
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_transportType == PoseTransportType.WebSocket)
                    {
                        await RunWebSocketAsync(token);
                    }
                    else
                    {
                        await RunUdpAsync(token);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Debug.LogWarning("PoseReceiver connection cancelled unexpectedly");
                    }
                    break;
                }
                catch (WebSocketException ex) when (token.IsCancellationRequested)
                {
                    // 再生停止などでキャンセルされた場合は警告にしない
                    if (_debugLogging)
                    {
                        Debug.Log($"PoseReceiver WebSocket task cancelled: {ex.Message}");
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"PoseReceiver connection error: {ex.Message}");
                    if (_debugLogging)
                    {
                        Debug.LogException(ex);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(_reconnectDelay), token);
                }
            }
        }

        private async Task RunWebSocketAsync(CancellationToken token)
        {
            try
            {
                using (_webSocket = new ClientWebSocket())
                {
                    var uri = new Uri($"ws://{_host}:{_port}{_webSocketPath}");
                    if (_debugLogging)
                    {
                        Debug.Log($"PoseReceiver connecting to {uri}");
                    }
                    await _webSocket.ConnectAsync(uri, token);
                    Debug.Log($"PoseReceiver connected to {uri}");
                    Connected?.Invoke();

                    var buffer = new byte[65536];
                    while (!token.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
                    {
                        var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, token);
                            break;
                        }

                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        EnqueueSample(json);
                    }
                }
            }
            finally
            {
                Disconnected?.Invoke();
                if (_debugLogging)
                {
                    Debug.Log("PoseReceiver disconnected");
                }
            }
        }

        private async Task RunUdpAsync(CancellationToken token)
        {
            try
            {
                using (_udpClient = new UdpClient(_port))
                {
                    _udpClient.Client.ReceiveTimeout = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
                    Connected?.Invoke();

                    while (!token.IsCancellationRequested)
                    {
                        UdpReceiveResult result;
                        try
                        {
                            result = await _udpClient.ReceiveAsync();
                        }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode == SocketError.TimedOut)
                            {
                                continue;
                            }
                            throw;
                        }

                        var json = Encoding.UTF8.GetString(result.Buffer);
                        EnqueueSample(json);
                    }
                }
            }
            finally
            {
                Disconnected?.Invoke();
            }
        }

        private void EnqueueSample(string json)
        {
            try
            {
                var sample = JsonConvert.DeserializeObject<SkeletonSample>(json, _serializerSettings);
                if (sample != null)
                {
                    _incoming.Enqueue(sample);
                    Interlocked.Increment(ref _framesReceived);
                    Interlocked.Exchange(ref _lastTimestampMs, sample._timestamp);
                    if (_debugLogging && (_framesReceived % 30 == 1))
                    {
                        Debug.Log($"PoseReceiver buffered frame {_framesReceived} (timestamp {sample._timestamp})");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse skeleton sample: {ex.Message}\n{json}");
                if (_debugLogging)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
