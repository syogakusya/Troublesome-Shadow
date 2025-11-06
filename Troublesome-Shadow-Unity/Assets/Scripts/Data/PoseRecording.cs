using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    [Serializable]
    public class PoseRecording
    {
        [FormerlySerializedAs("name")] public string _name = "recording";
        [FormerlySerializedAs("durationMs")] public long _durationMs;
        [FormerlySerializedAs("frames")] public List<SkeletonSample> _frames = new List<SkeletonSample>();
        public Dictionary<string, object> Meta = new Dictionary<string, object>();
    }
}
