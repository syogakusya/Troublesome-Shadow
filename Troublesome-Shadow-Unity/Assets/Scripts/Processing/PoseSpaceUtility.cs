using UnityEngine;

namespace PoseRuntime
{
    public static class PoseSpaceUtility
    {
        public static Vector3 ToWorld(Transform poseSpaceOrigin, Vector3 posePosition)
        {
            return poseSpaceOrigin != null ? poseSpaceOrigin.TransformPoint(posePosition) : posePosition;
        }
    }
}


