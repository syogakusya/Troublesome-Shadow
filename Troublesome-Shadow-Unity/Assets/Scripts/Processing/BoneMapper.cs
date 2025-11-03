using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    [System.Serializable]
    public class BoneMapping
    {
        public string jointName;
        public Transform targetTransform;
        public Vector3 rotationOffset;
    }

    public class BoneMapper : MonoBehaviour
    {
        public List<BoneMapping> mappings = new List<BoneMapping>();
        private readonly Dictionary<string, BoneMapping> _lookup = new Dictionary<string, BoneMapping>();

        private void Awake()
        {
            BuildLookup();
        }

        public void BuildLookup()
        {
            _lookup.Clear();
            foreach (var mapping in mappings)
            {
                if (mapping.targetTransform != null && !string.IsNullOrEmpty(mapping.jointName))
                {
                    _lookup[mapping.jointName.ToLowerInvariant()] = mapping;
                }
            }
        }

        public IEnumerable<BoneMapping> EnumerateMappings() => _lookup.Values;

        public bool TryGetMapping(string jointName, out BoneMapping mapping)
        {
            return _lookup.TryGetValue(jointName.ToLowerInvariant(), out mapping);
        }
    }
}
