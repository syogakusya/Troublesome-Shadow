using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    public class PoseRecorder : MonoBehaviour
    {
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("recordingName")] public string _recordingName = "session";
        [FormerlySerializedAs("outputDirectory")] public string _outputDirectory = "Recordings";
        [FormerlySerializedAs("appendTimestamp")] public bool _appendTimestamp = true;

        private bool _isRecording;
        private PoseRecording _recording;
        private List<SkeletonSample> _frames = new List<SkeletonSample>();

        public bool IsRecording => _isRecording;
        public PoseRecording LastRecording { get; private set; }

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponent<AvatarController>();
            }
        }

        private void OnDisable()
        {
            if (_isRecording)
            {
                StopRecording();
            }
        }

        public void StartRecording()
        {
            if (_isRecording)
            {
                return;
            }

            _frames = new List<SkeletonSample>();
            _recording = new PoseRecording
            {
                _name = _recordingName
            };
            LastRecording = null;

            if (_controller != null)
            {
                _controller.SampleProcessed += OnSampleProcessed;
            }

            _isRecording = true;
            Debug.Log("PoseRecorder started recording");
        }

        public string StopRecording()
        {
            if (!_isRecording)
            {
                return null;
            }

            _isRecording = false;
            if (_controller != null)
            {
                _controller.SampleProcessed -= OnSampleProcessed;
            }

            if (_frames.Count == 0)
            {
                Debug.LogWarning("PoseRecorder stopped but no frames were captured");
                return null;
            }

            _recording._frames = new List<SkeletonSample>(_frames);
            _recording._durationMs = _frames[_frames.Count - 1]._timestamp - _frames[0]._timestamp;

            if (_recording.Meta.Count == 0 && _frames[0].Meta != null)
            {
                foreach (var kvp in _frames[0].Meta)
                {
                    _recording.Meta[kvp.Key] = kvp.Value;
                }
            }

            var path = BuildFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(_recording, Formatting.Indented);
            File.WriteAllText(path, json);
            Debug.Log($"PoseRecorder saved { _frames.Count } frames to { path }");

            LastRecording = _recording;

            return path;
        }

        private void OnSampleProcessed(SkeletonSample sample)
        {
            if (!_isRecording || sample == null)
            {
                return;
            }

            _frames.Add(sample.Clone());
        }

        private string BuildFilePath()
        {
            var root = Path.Combine(Application.persistentDataPath, _outputDirectory);
            var suffix = _appendTimestamp ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : string.Empty;
            var fileName = string.IsNullOrEmpty(suffix)
                ? $"{_recordingName}.json"
                : $"{_recordingName}_{suffix}.json";
            return Path.Combine(root, fileName);
        }
    }
}
