using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace PoseRuntime
{
    public class DiagnosticsPanel : MonoBehaviour
    {
        public PoseReceiver receiver;
        public Text statusText;
        public float updateInterval = 0.5f;

        private float _timer;
        private long _previousFrameCount;
        private float _lastFrameArrival;
        private readonly StringBuilder _builder = new StringBuilder();

        private void OnEnable()
        {
            if (receiver != null)
            {
                receiver.Connected += OnConnected;
                receiver.Disconnected += OnDisconnected;
                _previousFrameCount = receiver.TotalFramesReceived;
            }

            _lastFrameArrival = Time.time;
        }

        private void OnDisable()
        {
            if (receiver != null)
            {
                receiver.Connected -= OnConnected;
                receiver.Disconnected -= OnDisconnected;
            }
        }

        private void Update()
        {
            if (receiver == null)
            {
                return;
            }

            _timer += Time.deltaTime;

            if (_timer >= updateInterval)
            {
                var totalFrames = receiver.TotalFramesReceived;
                var delta = totalFrames - _previousFrameCount;
                if (delta > 0)
                {
                    _lastFrameArrival = Time.time;
                }

                var fps = delta / Mathf.Max(_timer, 0.001f);
                RefreshPanel(fps, totalFrames, receiver.PendingSamples);
                _previousFrameCount = totalFrames;
                _timer = 0f;
            }
        }

        private void RefreshPanel(float fps, long totalFrames, int pending)
        {
            if (statusText == null)
            {
                return;
            }

            _builder.Length = 0;
            _builder.AppendLine($"Transport: {receiver?.transportType}");
            _builder.AppendLine($"Endpoint: {receiver?.host}:{receiver?.port}");
            _builder.AppendLine($"Frames: {fps:F1} fps (total {totalFrames})");
            _builder.AppendLine($"Queue: {pending}");
            _builder.AppendLine($"Last frame: {(Time.time - _lastFrameArrival):F2}s ago");
            statusText.text = _builder.ToString();
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
