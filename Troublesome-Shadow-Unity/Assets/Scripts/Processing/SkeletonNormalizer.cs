using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    [CreateAssetMenu(menuName = "PoseRuntime/Skeleton Normalizer", fileName = "SkeletonNormalizer")]
    public class SkeletonNormalizer : ScriptableObject
    {
        [System.Serializable]
        public class AxisRemap
        {
            public string jointName = string.Empty;
            public Vector3 positionScale = Vector3.one;
            public Vector3 positionOffset = Vector3.zero;
            public Vector3 rotationOffset = Vector3.zero;
        }

        public Vector3 globalScale = Vector3.one;
        public Vector3 globalOffset = Vector3.zero;
        public bool invertZAxis = true;
        public List<AxisRemap> perJointOverrides = new List<AxisRemap>();

        private readonly Dictionary<string, AxisRemap> _overrideLookup = new Dictionary<string, AxisRemap>();

        private void OnEnable()
        {
            BuildLookup();
        }

        public void BuildLookup()
        {
            _overrideLookup.Clear();
            foreach (var remap in perJointOverrides)
            {
                if (!string.IsNullOrEmpty(remap.jointName))
                {
                    _overrideLookup[remap.jointName.ToLowerInvariant()] = remap;
                }
            }
        }

        public void Normalize(SkeletonSample sample)
        {
            if (sample == null)
            {
                return;
            }

            foreach (var joint in sample.joints)
            {
                var normalized = ApplyGlobal(joint.position);
                if (_overrideLookup.TryGetValue(joint.name.ToLowerInvariant(), out var remap))
                {
                    normalized = Vector3.Scale(normalized, remap.positionScale) + remap.positionOffset;
                    joint.rotation *= Quaternion.Euler(remap.rotationOffset);
                }

                joint.position = normalized;
            }
        }

        private Vector3 ApplyGlobal(Vector3 position)
        {
            var scaled = Vector3.Scale(position, globalScale) + globalOffset;
            if (invertZAxis)
            {
                scaled.z *= -1f;
            }

            return scaled;
        }
    }
}
