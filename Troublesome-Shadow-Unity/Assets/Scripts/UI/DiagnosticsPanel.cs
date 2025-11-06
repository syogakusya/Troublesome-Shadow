using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace PoseRuntime
{
    public class DiagnosticsPanel : MonoBehaviour
    {
        [FormerlySerializedAs("receiver")] public PoseReceiver _receiver;
        [FormerlySerializedAs("statusText")] public Text _statusText;
        [FormerlySerializedAs("updateInterval")] public float _updateInterval = 0.5f;

        private float _timer;
        private long _previousFrameCount;
        private float _lastFrameArrival;
        private readonly StringBuilder _builder = new StringBuilder();

        private void OnEnable()
        {
            if (_receiver != null)
            {
                _receiver.Connected += OnConnected;
                _receiver.Disconnected += OnDisconnected;
                _previousFrameCount = _receiver.TotalFramesReceived;
            }

            _lastFrameArrival = Time.time;
        }

        private void OnDisable()
        {
            if (_receiver != null)
            {
                _receiver.Connected -= OnConnected;
                _receiver.Disconnected -= OnDisconnected;
            }
        }

        private void Update()
        {
            if (_receiver == null)
            {
                return;
            }

            _timer += Time.deltaTime;

            if (_timer >= _updateInterval)
            {
                var totalFrames = _receiver.TotalFramesReceived;
                var delta = totalFrames - _previousFrameCount;
                if (delta > 0)
                {
                    _lastFrameArrival = Time.time;
                }

                var fps = delta / Mathf.Max(_timer, 0.001f);
                RefreshPanel(fps, totalFrames, _receiver.PendingSamples);
                _previousFrameCount = totalFrames;
                _timer = 0f;
            }
        }

        private void RefreshPanel(float fps, long totalFrames, int pending)
        {
            if (_statusText == null)
            {
                return;
            }

            _builder.Length = 0;
            _builder.AppendLine($"Transport: {_receiver?._transportType}");
            _builder.AppendLine($"Endpoint: {_receiver?._host}:{_receiver?._port}");
            _builder.AppendLine($"Frames: {fps:F1} fps (total {totalFrames})");
            _builder.AppendLine($"Queue: {pending}");
            _builder.AppendLine($"Last frame: {(Time.time - _lastFrameArrival):F2}s ago");
            _statusText.text = _builder.ToString();
        }

        private void OnConnected()
        {
            Debug.Log("DiagnosticsPanel detected receiver connected");
        }

        private void OnDisconnected()
        {
            Debug.LogWarning("DiagnosticsPanel detected receiver disconnected");
        }
    }
}
