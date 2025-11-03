using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    public class CalibrationManager : MonoBehaviour
    {
        public PoseReceiver receiver;
        public SkeletonNormalizer normalizer;
        public Transform referenceRoot;

        private readonly Dictionary<string, Vector3> _offsets = new Dictionary<string, Vector3>();
        private bool _calibrated;

        public bool IsCalibrated => _calibrated;

        public void ResetCalibration()
        {
            _offsets.Clear();
            _calibrated = false;
        }

        public void CaptureCalibrationPose()
        {
            if (receiver == null)
            {
                throw new InvalidOperationException("PoseReceiver is not configured");
            }

            if (!receiver.TryDequeue(out var sample))
            {
                Debug.LogWarning("No skeleton sample available for calibration");
                return;
            }

            ApplyCalibration(sample);
        }

        private void ApplyCalibration(SkeletonSample sample)
        {
            normalizer?.Normalize(sample);

            _offsets.Clear();
            foreach (var joint in sample.joints)
            {
                _offsets[joint.name] = joint.position;
            }

            _calibrated = true;
        }

        public Vector3 GetOffset(string jointName)
        {
            return _offsets.TryGetValue(jointName, out var offset) ? offset : Vector3.zero;
        }
    }
}
