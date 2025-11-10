using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AnimationClipRecording
{
  public static class AnimationClipConverter
  {
    [Serializable]
    public class AnimationClipJsonData
    {
      public string Name;
      public float Length;
      public float FrameRate;
      public bool LoopTime;
      public List<TransformInfo> Transforms = new List<TransformInfo>();
      public List<FrameData> Frames = new List<FrameData>();
      public List<HumanoidFrameData> HumanoidFrames = new List<HumanoidFrameData>();
      public AnimationClipMetadata Metadata = new AnimationClipMetadata();
    }

    [Serializable]
    public class FrameData
    {
      public float Time;
      public List<SampleData> Samples = new List<SampleData>();
    }

    [Serializable]
    public class SampleData
    {
      public float[] Position;
      public float[] Rotation;
      public float[] Scale;
    }

    [Serializable]
    public class HumanoidFrameData
    {
      public float Time;
      public float[] BodyPosition = new float[3];
      public float[] BodyRotation = new float[4];
      public float[] Muscles;
    }

    [Serializable]
    public class TransformInfo
    {
      public string Path;
      public string HumanoidBone;
    }

    [Serializable]
    public class AnimationClipMetadata
    {
      public string RecordedAt;
      public int TransformCount;
      public int MuscleCount;
      public string DataFormat = "transform";
      public bool RecordRootTransform = true;
      public bool RecordPositions = true;
      public bool RecordRotations = true;
      public bool RecordScale = false;
    }

    public static string ConvertToJson(
      AnimationClip clip,
      IReadOnlyList<RecordedTransformInfo> transforms,
      List<AnimationClipFrame> frames,
      bool includeRootTransform,
      bool includePositions,
      bool includeRotations,
      bool includeScale,
      List<HumanoidMuscleFrame> humanoidFrames)
    {
      var hasHumanoidFrames = humanoidFrames != null && humanoidFrames.Count > 0;
      var hasTransformFrames = frames != null && frames.Count > 0;

      if (clip == null || (!hasHumanoidFrames && !hasTransformFrames))
      {
        return "{}";
      }

      var transformCount = 0;
      if (hasTransformFrames)
      {
        transformCount = transforms != null ? transforms.Count : frames[0].TransformSamples?.Length ?? 0;
        if (transformCount <= 0)
        {
          transformCount = frames[0].TransformSamples?.Length ?? 0;
        }
      }

      var data = new AnimationClipJsonData
      {
        Name = clip.name,
        Length = clip.length,
        FrameRate = clip.frameRate,
        LoopTime = clip.isLooping,
        Metadata = new AnimationClipMetadata
        {
          RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
          RecordRootTransform = includeRootTransform,
          RecordPositions = includePositions,
          RecordRotations = includeRotations,
          RecordScale = includeScale,
          TransformCount = transformCount,
          MuscleCount = hasHumanoidFrames ? HumanTrait.MuscleCount : 0,
          DataFormat = hasHumanoidFrames ? "humanoid" : "transform"
        }
      };

      if (hasTransformFrames)
      {
        if (transforms != null)
        {
          foreach (var transformInfo in transforms)
          {
            data.Transforms.Add(new TransformInfo
            {
              Path = transformInfo?.Path ?? string.Empty,
              HumanoidBone = transformInfo?.HumanoidBone
            });
          }
        }
        else if (transformCount > 0)
        {
          for (int i = 0; i < transformCount; i++)
          {
            data.Transforms.Add(new TransformInfo
            {
              Path = string.Empty,
              HumanoidBone = null
            });
          }
        }
      }

      if (hasTransformFrames)
      {
        foreach (var frame in frames)
        {
          var frameData = new FrameData
          {
            Time = frame.Time
          };

          var samples = frame.TransformSamples;
          var expectedCount = data.Transforms.Count;
          if (expectedCount == 0 && samples != null)
          {
            expectedCount = samples.Length;
          }

          for (int i = 0; i < expectedCount; i++)
          {
            var sampleData = new SampleData();

            if (samples != null && samples.Length > i)
            {
              var sample = samples[i];

              if (includePositions && sample.HasPosition)
              {
                sampleData.Position = new[] { sample.LocalPosition.x, sample.LocalPosition.y, sample.LocalPosition.z };
              }

              if (includeRotations && sample.HasRotation)
              {
                sampleData.Rotation = new[] { sample.LocalRotation.x, sample.LocalRotation.y, sample.LocalRotation.z, sample.LocalRotation.w };
              }

              if (includeScale && sample.HasScale)
              {
                sampleData.Scale = new[] { sample.LocalScale.x, sample.LocalScale.y, sample.LocalScale.z };
              }
            }

            frameData.Samples.Add(sampleData);
          }

          data.Frames.Add(frameData);
        }
      }

      if (hasHumanoidFrames)
      {
        foreach (var frame in humanoidFrames)
        {
          var humanoidFrame = new HumanoidFrameData
          {
            Time = frame.Time,
            BodyPosition = new[] { frame.BodyPosition.x, frame.BodyPosition.y, frame.BodyPosition.z },
            BodyRotation = new[] { frame.BodyRotation.x, frame.BodyRotation.y, frame.BodyRotation.z, frame.BodyRotation.w },
            Muscles = frame.Muscles != null ? (float[])frame.Muscles.Clone() : Array.Empty<float>()
          };

          data.HumanoidFrames.Add(humanoidFrame);
        }
      }

      var json = JsonConvert.SerializeObject(data, Formatting.Indented);
      return json;
    }

    public static AnimationClip LoadFromJson(string jsonPath, Animator animator = null)
    {
      if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
      {
        Debug.LogError($"AnimationClipConverter: File not found: {jsonPath}");
        return null;
      }

      try
      {
        var json = File.ReadAllText(jsonPath);
        var data = JsonConvert.DeserializeObject<AnimationClipJsonData>(json);

        if (data == null ||
            ((data.Frames == null || data.Frames.Count == 0) && (data.HumanoidFrames == null || data.HumanoidFrames.Count == 0)))
        {
          Debug.LogError("AnimationClipConverter: Invalid JSON data");
          return null;
        }

        return CreateAnimationClipFromJson(data, animator);
      }
      catch (Exception ex)
      {
        Debug.LogError($"AnimationClipConverter: Failed to load JSON: {ex.Message}");
        return null;
      }
    }

    private static AnimationClip CreateAnimationClipFromJson(AnimationClipJsonData data, Animator animator = null)
    {
      var clip = new AnimationClip
      {
        name = data.Name,
        frameRate = data.FrameRate
      };

      clip.legacy = false;

      var metadata = data.Metadata ?? new AnimationClipMetadata();

      if (data.HumanoidFrames != null && data.HumanoidFrames.Count > 0)
      {
        ApplyHumanoidFrameData(clip, data.HumanoidFrames);

#if UNITY_EDITOR
            var humanoidSettings = AnimationUtility.GetAnimationClipSettings(clip);
            humanoidSettings.loopTime = data.LoopTime;
            AnimationUtility.SetAnimationClipSettings(clip, humanoidSettings);
#endif

        clip.EnsureQuaternionContinuity();
        return clip;
      }

      var transformInfos = data.Transforms ?? new List<TransformInfo>();
      var frameCount = data.Frames?.Count ?? 0;
      if (frameCount == 0)
      {
        return clip;
      }

      if (transformInfos.Count == 0)
      {
        var firstSamples = data.Frames[0].Samples;
        var inferredCount = firstSamples?.Count ?? 0;
        for (int i = 0; i < inferredCount; i++)
        {
          transformInfos.Add(new TransformInfo());
        }
      }

      var transformCount = transformInfos.Count;
      var pathOverrides = animator != null ? BuildHumanoidBonePathMap(animator) : null;
      var resolvedPaths = new List<string>(transformCount);
      for (int i = 0; i < transformCount; i++)
      {
        resolvedPaths.Add(ResolveTransformPath(transformInfos[i], pathOverrides));
      }

      var recordPositions = metadata.RecordPositions;
      var recordRotations = metadata.RecordRotations;
      var recordScale = metadata.RecordScale;
      var includeRoot = metadata.RecordRootTransform;

      AnimationCurve[] posX = null, posY = null, posZ = null;
      AnimationCurve[] rotX = null, rotY = null, rotZ = null, rotW = null;
      AnimationCurve[] scaleX = null, scaleY = null, scaleZ = null;

      Vector3[] lastPosition = null;
      bool[] hasPosition = null;
      Quaternion[] lastRotation = null;
      bool[] hasRotation = null;
      Vector3[] lastScale = null;
      bool[] hasScale = null;

      if (recordPositions)
      {
        posX = new AnimationCurve[transformCount];
        posY = new AnimationCurve[transformCount];
        posZ = new AnimationCurve[transformCount];
        lastPosition = new Vector3[transformCount];
        hasPosition = new bool[transformCount];
        for (int i = 0; i < transformCount; i++)
        {
          posX[i] = new AnimationCurve();
          posY[i] = new AnimationCurve();
          posZ[i] = new AnimationCurve();
        }
      }

      if (recordRotations)
      {
        rotX = new AnimationCurve[transformCount];
        rotY = new AnimationCurve[transformCount];
        rotZ = new AnimationCurve[transformCount];
        rotW = new AnimationCurve[transformCount];
        lastRotation = new Quaternion[transformCount];
        hasRotation = new bool[transformCount];
        for (int i = 0; i < transformCount; i++)
        {
          rotX[i] = new AnimationCurve();
          rotY[i] = new AnimationCurve();
          rotZ[i] = new AnimationCurve();
          rotW[i] = new AnimationCurve();
        }
      }

      if (recordScale)
      {
        scaleX = new AnimationCurve[transformCount];
        scaleY = new AnimationCurve[transformCount];
        scaleZ = new AnimationCurve[transformCount];
        lastScale = new Vector3[transformCount];
        hasScale = new bool[transformCount];
        for (int i = 0; i < transformCount; i++)
        {
          scaleX[i] = new AnimationCurve();
          scaleY[i] = new AnimationCurve();
          scaleZ[i] = new AnimationCurve();
          lastScale[i] = Vector3.one;
        }
      }

      foreach (var frame in data.Frames)
      {
        var time = frame.Time;
        var samples = frame.Samples ?? new List<SampleData>();

        for (int i = 0; i < transformCount; i++)
        {
          var sample = i < samples.Count ? samples[i] : null;

          if (recordPositions)
          {
            Vector3 value;
            if (sample?.Position != null && sample.Position.Length >= 3)
            {
              value = new Vector3(sample.Position[0], sample.Position[1], sample.Position[2]);
              lastPosition[i] = value;
              hasPosition[i] = true;
            }
            else
            {
              value = hasPosition[i] ? lastPosition[i] : Vector3.zero;
            }

            posX[i].AddKey(time, value.x);
            posY[i].AddKey(time, value.y);
            posZ[i].AddKey(time, value.z);
          }

          if (recordRotations)
          {
            Quaternion value;
            if (sample?.Rotation != null && sample.Rotation.Length >= 4)
            {
              value = NormalizeQuaternion(new Quaternion(sample.Rotation[0], sample.Rotation[1], sample.Rotation[2], sample.Rotation[3]));
              lastRotation[i] = value;
              hasRotation[i] = true;
            }
            else
            {
              value = hasRotation[i] ? lastRotation[i] : Quaternion.identity;
            }

            rotX[i].AddKey(time, value.x);
            rotY[i].AddKey(time, value.y);
            rotZ[i].AddKey(time, value.z);
            rotW[i].AddKey(time, value.w);
          }

          if (recordScale)
          {
            Vector3 value;
            if (sample?.Scale != null && sample.Scale.Length >= 3)
            {
              value = new Vector3(sample.Scale[0], sample.Scale[1], sample.Scale[2]);
              lastScale[i] = value;
              hasScale[i] = true;
            }
            else
            {
              value = hasScale[i] ? lastScale[i] : Vector3.one;
            }

            scaleX[i].AddKey(time, value.x);
            scaleY[i].AddKey(time, value.y);
            scaleZ[i].AddKey(time, value.z);
          }
        }
      }

      for (int i = 0; i < transformCount; i++)
      {
        var path = resolvedPaths[i];
        if (path == null)
        {
          continue;
        }

        if (!includeRoot && string.IsNullOrEmpty(path))
        {
          continue;
        }

        if (recordPositions && posX != null)
        {
          clip.SetCurve(path, typeof(Transform), "localPosition.x", posX[i]);
          clip.SetCurve(path, typeof(Transform), "localPosition.y", posY[i]);
          clip.SetCurve(path, typeof(Transform), "localPosition.z", posZ[i]);
        }

        if (recordRotations && rotX != null)
        {
          clip.SetCurve(path, typeof(Transform), "localRotation.x", rotX[i]);
          clip.SetCurve(path, typeof(Transform), "localRotation.y", rotY[i]);
          clip.SetCurve(path, typeof(Transform), "localRotation.z", rotZ[i]);
          clip.SetCurve(path, typeof(Transform), "localRotation.w", rotW[i]);
        }

        if (recordScale && scaleX != null)
        {
          clip.SetCurve(path, typeof(Transform), "localScale.x", scaleX[i]);
          clip.SetCurve(path, typeof(Transform), "localScale.y", scaleY[i]);
          clip.SetCurve(path, typeof(Transform), "localScale.z", scaleZ[i]);
        }
      }

#if UNITY_EDITOR
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = data.LoopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
#endif

      if (recordRotations)
      {
        clip.EnsureQuaternionContinuity();
      }

      return clip;
    }

    private static Dictionary<string, string> BuildHumanoidBonePathMap(Animator animator)
    {
      var map = new Dictionary<string, string>(StringComparer.Ordinal);
      if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
      {
        return map;
      }

      var root = animator.transform;
      for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
      {
        var bone = (HumanBodyBones)i;
        var transform = animator.GetBoneTransform(bone);
        if (transform == null)
        {
          continue;
        }

        var path = GetRelativePath(transform, root);
        if (path == null)
        {
          continue;
        }

        map[bone.ToString()] = path;
      }

      return map;
    }

    private static string GetRelativePath(Transform target, Transform root)
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

    private static string ResolveTransformPath(TransformInfo info, Dictionary<string, string> overrideMap)
    {
      if (info == null)
      {
        return string.Empty;
      }

      if (!string.IsNullOrEmpty(info.Path))
      {
        return info.Path;
      }

      if (overrideMap != null && !string.IsNullOrEmpty(info.HumanoidBone) && overrideMap.TryGetValue(info.HumanoidBone, out var mappedPath))
      {
        return mappedPath;
      }

      return info.HumanoidBone ?? string.Empty;
    }

    private static void ApplyHumanoidFrameData(AnimationClip clip, List<HumanoidFrameData> frames)
    {
      if (clip == null || frames == null || frames.Count == 0)
      {
        return;
      }

      AnimationCurve CreateCurve(Func<HumanoidFrameData, float> getter)
      {
        var curve = new AnimationCurve();
        foreach (var frame in frames)
        {
          curve.AddKey(frame.Time, getter(frame));
        }
        return curve;
      }

      float GetValue(float[] values, int index, float fallback)
      {
        return values != null && values.Length > index ? values[index] : fallback;
      }

      clip.SetCurve(string.Empty, typeof(Animator), "RootT.x", CreateCurve(frame => GetValue(frame.BodyPosition, 0, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootT.y", CreateCurve(frame => GetValue(frame.BodyPosition, 1, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootT.z", CreateCurve(frame => GetValue(frame.BodyPosition, 2, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.x", CreateCurve(frame => GetValue(frame.BodyRotation, 0, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.y", CreateCurve(frame => GetValue(frame.BodyRotation, 1, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.z", CreateCurve(frame => GetValue(frame.BodyRotation, 2, 0f)));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.w", CreateCurve(frame => GetValue(frame.BodyRotation, 3, 1f)));

      var muscleNames = HumanTrait.MuscleName;
      for (int i = 0; i < muscleNames.Length; i++)
      {
        var muscleIndex = i;
        clip.SetCurve(string.Empty, typeof(Animator), muscleNames[i], CreateCurve(frame => GetValue(frame.Muscles, muscleIndex, 0f)));
      }
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
      var magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
      if (magnitude < Mathf.Epsilon)
      {
        return Quaternion.identity;
      }

      var inverse = 1f / magnitude;
      value.x *= inverse;
      value.y *= inverse;
      value.z *= inverse;
      value.w *= inverse;
      return value;
    }

    public static AnimationClip LoadFromJsonData(string json, Animator animator = null)
    {
      try
      {
        var data = JsonConvert.DeserializeObject<AnimationClipJsonData>(json);
        if (data == null)
        {
          return null;
        }
        return CreateAnimationClipFromJson(data, animator);
      }
      catch (Exception ex)
      {
        Debug.LogError($"AnimationClipConverter: Failed to parse JSON: {ex.Message}");
        return null;
      }
    }
  }
}
