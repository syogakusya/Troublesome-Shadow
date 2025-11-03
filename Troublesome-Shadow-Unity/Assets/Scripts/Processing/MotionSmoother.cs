using System.Collections.Generic;
using UnityEngine;

namespace PoseRuntime
{
    public class MotionSmoother : MonoBehaviour
    {
        [Range(0f, 1f)]
        public float positionSmoothing = 0.5f;
        [Range(0f, 1f)]
        public float rotationSmoothing = 0.5f;

        private readonly Dictionary<string, Vector3> _positionState = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Quaternion> _rotationState = new Dictionary<string, Quaternion>();

        public void ResetState()
        {
            _positionState.Clear();
            _rotationState.Clear();
        }

        public void Apply(JointSample joint)
        {
            if (!_positionState.TryGetValue(joint.name, out var previousPosition))
            {
                previousPosition = joint.position;
            }

            if (!_rotationState.TryGetValue(joint.name, out var previousRotation))
            {
                previousRotation = joint.rotation;
            }

            joint.position = Vector3.Lerp(previousPosition, joint.position, 1f - positionSmoothing);
            joint.rotation = Quaternion.Slerp(previousRotation, joint.rotation, 1f - rotationSmoothing);

            _positionState[joint.name] = joint.position;
            _rotationState[joint.name] = joint.rotation;
        }
    }
}
