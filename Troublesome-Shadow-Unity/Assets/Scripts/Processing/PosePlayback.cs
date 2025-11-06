using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    public class PosePlayback : MonoBehaviour
    {
        [FormerlySerializedAs("recordingPath")] public string _recordingPath;
        [FormerlySerializedAs("loop")] public bool _loop = false;
        [FormerlySerializedAs("playbackSpeed")] public float _playbackSpeed = 1.0f;

        private PoseRecording _recording;
        private int _currentIndex;
        private float _startTime;
        private long _startTimestampMs;
        private bool _isPlaying;

        public bool IsPlaying => _isPlaying;

        public void LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"PosePlayback could not find recording at {path}");
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var recording = JsonConvert.DeserializeObject<PoseRecording>(json);
                if (recording == null || recording._frames == null || recording._frames.Count == 0)
                {
                    Debug.LogWarning($"PosePlayback recording {path} contains no frames");
                    return;
                }

                _recordingPath = path;
                _recording = recording;
            }
            catch (JsonException ex)
            {
                Debug.LogError($"PosePlayback failed to parse recording {path}: {ex.Message}");
            }
        }

        public void PlayFromFile(string path)
        {
            LoadFromFile(path);
            if (_recording != null)
            {
                Play(_recording);
            }
        }

        public void Play(PoseRecording recording)
        {
            if (recording == null || recording._frames.Count == 0)
            {
                Debug.LogWarning("PosePlayback cannot play an empty recording");
                return;
            }

            _recording = recording;
            _currentIndex = 0;
            _startTime = Time.time;
            _startTimestampMs = recording._frames[0]._timestamp;
            _isPlaying = true;
            Debug.Log($"PosePlayback started with {recording._frames.Count} frames");
        }

        public void Stop()
        {
            _isPlaying = false;
        }

        public bool TryGetNext(out SkeletonSample sample)
        {
            sample = null;

            if (!_isPlaying || _recording == null || _recording._frames.Count == 0)
            {
                return false;
            }

            var frames = _recording._frames;
            var elapsedMs = (long)((Time.time - _startTime) * 1000f * _playbackSpeed);

            SkeletonSample latest = null;
            while (_currentIndex < frames.Count)
            {
                var frame = frames[_currentIndex];
                var relative = frame._timestamp - _startTimestampMs;
                if (relative > elapsedMs)
                {
                    break;
                }

                latest = frame;
                _currentIndex++;
            }

            if (latest != null)
            {
                sample = latest.Clone();
                return true;
            }

            if (_currentIndex >= frames.Count)
            {
                if (_loop)
                {
                    Restart();
                }
                else
                {
                    _isPlaying = false;
                }
            }

            return false;
        }

        private void Restart()
        {
            _currentIndex = 0;
            _startTime = Time.time;
            if (_recording != null && _recording._frames.Count > 0)
            {
                _startTimestampMs = _recording._frames[0]._timestamp;
            }
        }
    }
}
