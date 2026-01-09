using System.Collections.Generic;
using UnityEngine;

namespace AnimationClipRecording
{
  public static class AnimationClipRecordingUtility
  {
    public static Quaternion NormalizeQuaternion(Quaternion value)
    {
      var magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
      if (magnitude < Mathf.Epsilon)
      {
        return Quaternion.identity;
      }

      var inverse = 1f / magnitude;
      return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
    }

    public static string GetRelativePath(Transform target, Transform root)
    {
      if (target == null || root == null)
      {
        return null;
      }

      if (target == root)
      {
        return string.Empty;
      }

      var segments = new Stack<string>();
      var current = target;
      while (current != null && current != root)
      {
        segments.Push(current.name);
        current = current.parent;
      }

      if (current != root)
      {
        return null;
      }

      return string.Join("/", segments.ToArray());
    }
  }
}
