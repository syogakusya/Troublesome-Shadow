using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    [Serializable]
    public class JointSample
    {
        public string name = string.Empty;
        public Vector3 position = Vector3.zero;
        public Quaternion rotation = Quaternion.identity;
        public float confidence = 1.0f;
    }

    [Serializable]
    public class SkeletonSample
    {
        public long timestamp;
        public List<JointSample> joints = new List<JointSample>();
        public Dictionary<string, object> meta = new Dictionary<string, object>();

        public bool TryGetJoint(string jointName, out JointSample joint)
        {
            joint = joints.Find(j => string.Equals(j.name, jointName, StringComparison.OrdinalIgnoreCase));
            return joint != null;
        }
    }
}
