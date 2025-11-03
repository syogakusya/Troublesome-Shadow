using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    public class AvatarController : MonoBehaviour
    {
        public PoseReceiver receiver;
        public SkeletonNormalizer normalizer;
        public CalibrationManager calibration;
        public BoneMapper boneMapper;
        public MotionSmoother smoother;
        public float interpolationSpeed = 10f;
        public bool applyPosition = true;

        private SkeletonSample _currentSample;
        private readonly Dictionary<string, Quaternion> _targetRotations = new Dictionary<string, Quaternion>();
        private readonly Dictionary<string, Vector3> _targetPositions = new Dictionary<string, Vector3>();

        private void Awake()
        {
            if (boneMapper != null)
            {
                boneMapper.BuildLookup();
            }
        }

        private void Update()
        {
            if (receiver != null && receiver.TryDequeue(out var sample))
            {
                ProcessSample(sample);
            }
        }

        private void LateUpdate()
        {
            if (_currentSample == null || boneMapper == null)
            {
                return;
            }

            foreach (var mapping in boneMapper.EnumerateMappings())
            {
                if (mapping.targetTransform == null)
                {
                    continue;
                }

                if (!_targetRotations.TryGetValue(mapping.jointName, out var targetRot))
                {
                    continue;
                }

                mapping.targetTransform.rotation = Quaternion.Slerp(
                    mapping.targetTransform.rotation,
                    targetRot * Quaternion.Euler(mapping.rotationOffset),
                    Time.deltaTime * interpolationSpeed);

                if (applyPosition && _targetPositions.TryGetValue(mapping.jointName, out var targetPos))
                {
                    mapping.targetTransform.position = Vector3.Lerp(
                        mapping.targetTransform.position,
                        targetPos,
                        Time.deltaTime * interpolationSpeed);
                }
            }
        }

        private void ProcessSample(SkeletonSample sample)
        {
            _currentSample = sample;

            normalizer?.Normalize(sample);

            foreach (var joint in sample.joints)
            {
                if (calibration != null && calibration.IsCalibrated)
                {
                    joint.position -= calibration.GetOffset(joint.name);
                }

                if (smoother != null)
                {
                    smoother.Apply(joint);
                }

                _targetRotations[joint.name] = joint.rotation;
                _targetPositions[joint.name] = joint.position;
            }
        }
    }
}
