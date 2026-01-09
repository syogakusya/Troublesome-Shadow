using System;
using UnityEngine;

namespace AnimationClipRecording
{
  [Serializable]
  public class AnimationClipFrame
  {
    public float Time;
    public TransformSample[] TransformSamples;

    public AnimationClipFrame(int transformCount)
    {
      TransformSamples = transformCount > 0 ? new TransformSample[transformCount] : Array.Empty<TransformSample>();
    }
  }

  [Serializable]
  public struct TransformSample
  {
    public bool HasPosition;
    public bool HasRotation;
    public bool HasScale;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation;
    public Vector3 LocalScale;
  }

  [Serializable]
  public class RecordedTransformInfo
  {
    public string Path;
    public string HumanoidBone;
  }

  [Serializable]
  public class HumanoidMuscleFrame
  {
    public float Time;
    public Vector3 BodyPosition;
    public Quaternion BodyRotation;
    public float[] Muscles;

    public HumanoidMuscleFrame(int muscleCount)
    {
      Muscles = muscleCount > 0 ? new float[muscleCount] : Array.Empty<float>();
    }
  }
}
