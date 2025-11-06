using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    [CreateAssetMenu(menuName = "PoseRuntime/Skeleton Normalizer", fileName = "SkeletonNormalizer")]
    public class SkeletonNormalizer : ScriptableObject
    {
        [System.Serializable]
        public class AxisRemap
        {
            [FormerlySerializedAs("jointName")] public string _jointName = string.Empty;
            [FormerlySerializedAs("positionScale")] public Vector3 _positionScale = Vector3.one;
            [FormerlySerializedAs("positionOffset")] public Vector3 _positionOffset = Vector3.zero;
            [FormerlySerializedAs("rotationOffset")] public Vector3 _rotationOffset = Vector3.zero;
        }

        [FormerlySerializedAs("globalScale")] public Vector3 _globalScale = Vector3.one;
        [FormerlySerializedAs("globalOffset")] public Vector3 _globalOffset = Vector3.zero;
        [FormerlySerializedAs("invertZAxis")] public bool _invertZAxis = true;
        [FormerlySerializedAs("perJointOverrides")] public List<AxisRemap> _perJointOverrides = new List<AxisRemap>();

        private readonly Dictionary<string, AxisRemap> _overrideLookup = new Dictionary<string, AxisRemap>();

        private void OnEnable()
        {
            BuildLookup();
        }

        public void BuildLookup()
        {
            _overrideLookup.Clear();
            foreach (var remap in _perJointOverrides)
            {
                if (!string.IsNullOrEmpty(remap._jointName))
                {
                    _overrideLookup[remap._jointName.ToLowerInvariant()] = remap;
                }
            }
        }

        public void Normalize(SkeletonSample sample)
        {
            if (sample == null)
            {
                return;
            }

            foreach (var joint in sample._joints)
            {
                var normalized = ApplyGlobal(joint._position);
                if (_overrideLookup.TryGetValue(joint._name.ToLowerInvariant(), out var remap))
                {
                    normalized = Vector3.Scale(normalized, remap._positionScale) + remap._positionOffset;
                    joint._rotation *= Quaternion.Euler(remap._rotationOffset);
                }

                joint._position = normalized;
            }
        }

        private Vector3 ApplyGlobal(Vector3 position)
        {
            var scaled = Vector3.Scale(position, _globalScale) + _globalOffset;
            if (_invertZAxis)
            {
                scaled.z *= -1f;
            }

            return scaled;
        }
    }
}
