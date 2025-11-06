using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    /// <summary>
    /// Minimum adapter that forwards incoming skeleton samples to listeners (e.g. HumanoidPoseApplier).
    /// </summary>
    public class AvatarController : MonoBehaviour
    {
        [FormerlySerializedAs("receiver")] public PoseReceiver _receiver;
        [FormerlySerializedAs("playback")] public PosePlayback _playback;
        [FormerlySerializedAs("normalizer")] public SkeletonNormalizer _normalizer;
        [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;

        public event Action<SkeletonSample> SampleProcessed;

        private int _debugFrameCounter;

        private void Update()
        {
            if (_playback != null && _playback.TryGetNext(out var playbackSample))
            {
                ProcessSample(playbackSample);
                return;
            }

            if (_receiver != null && _receiver.TryDequeue(out var sample))
            {
                ProcessSample(sample);
            }
        }

        private void ProcessSample(SkeletonSample sample)
        {
            _normalizer?.Normalize(sample);

            if (_debugLogging)
            {
                _debugFrameCounter++;
                if (_debugFrameCounter >= 15)
                {
                    _debugFrameCounter = 0;
                    Debug.Log($"AvatarController processed {sample._joints.Count} joints (timestamp {sample._timestamp})");
                }
            }

            SampleProcessed?.Invoke(sample.Clone());
        }
    }
}
