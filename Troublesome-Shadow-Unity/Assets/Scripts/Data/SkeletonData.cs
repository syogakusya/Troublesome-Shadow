using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    [Serializable]
    public class JointSample
    {
        [FormerlySerializedAs("name")] public string _name = string.Empty;
        [FormerlySerializedAs("position")] public Vector3 _position = Vector3.zero;
        [FormerlySerializedAs("rotation")] public Quaternion _rotation = Quaternion.identity;
        [FormerlySerializedAs("confidence")] public float _confidence = 1.0f;

        public JointSample Clone()
        {
            return new JointSample
            {
                _name = _name,
                _position = _position,
                _rotation = _rotation,
                _confidence = _confidence
            };
        }
    }

    [Serializable]
    public class SkeletonSample
    {
        [FormerlySerializedAs("timestamp")] public long _timestamp;
        [FormerlySerializedAs("joints")] public List<JointSample> _joints = new List<JointSample>();
        public Dictionary<string, object> Meta = new Dictionary<string, object>();

        public bool TryGetJoint(string jointName, out JointSample joint)
        {
            joint = _joints.Find(j => string.Equals(j._name, jointName, StringComparison.OrdinalIgnoreCase));
            return joint != null;
        }

        public SkeletonSample Clone()
        {
            var clone = new SkeletonSample
            {
                _timestamp = _timestamp,
                Meta = new Dictionary<string, object>(Meta)
            };
            foreach (var joint in _joints)
            {
                clone._joints.Add(joint.Clone());
            }

            return clone;
        }
    }
}
