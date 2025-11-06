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
      public List<FrameData> Frames = new List<FrameData>();
      public AnimationClipMetadata Metadata = new AnimationClipMetadata();
    }

    [Serializable]
    public class FrameData
    {
      public float Time;
      public RootTransformData RootTransform;
      public List<BoneRotationEntry> BoneRotations = new List<BoneRotationEntry>();
    }

    [Serializable]
    public class BoneRotationEntry
    {
      public string BoneName;
      public string Path;
      public BoneRotationData Rotation;
    }

    [Serializable]
    public class RootTransformData
    {
      public float[] Position = new float[3];
      public float[] Rotation = new float[4];
    }

    [Serializable]
    public class BoneRotationData
    {
      public float[] Rotation = new float[4];
    }

    [Serializable]
    public class AnimationClipMetadata
    {
      public string RecordedAt;
      public int BoneCount;
      public bool RecordRootTransform;
    }

    public static string ConvertToJson(AnimationClip clip, List<AnimationClipFrame> frames, Dictionary<HumanBodyBones, string> bonePaths, bool includeRootTransform)
    {
      if (clip == null || frames == null || frames.Count == 0)
      {
        return "{}";
      }

      bonePaths ??= new Dictionary<HumanBodyBones, string>();
      var recordedBonePaths = new HashSet<string>(StringComparer.Ordinal);

      var data = new AnimationClipJsonData
      {
        Name = clip.name,
        Length = clip.length,
        FrameRate = clip.frameRate,
        LoopTime = clip.isLooping,
        Metadata = new AnimationClipMetadata
        {
          RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
          RecordRootTransform = includeRootTransform
        }
      };

      foreach (var frame in frames)
      {
        var frameData = new FrameData
        {
          Time = frame.Time
        };

        if (includeRootTransform)
        {
          frameData.RootTransform = new RootTransformData
          {
            Position = new[] { frame.RootPosition.x, frame.RootPosition.y, frame.RootPosition.z },
            Rotation = new[] { frame.RootRotation.x, frame.RootRotation.y, frame.RootRotation.z, frame.RootRotation.w }
          };
        }

        foreach (var kvp in frame.BoneRotations)
        {
          var bone = kvp.Key;
          var boneName = bone.ToString();
          var path = bonePaths != null && bonePaths.TryGetValue(bone, out var storedPath) && storedPath != null
            ? storedPath
            : boneName;
          recordedBonePaths.Add(path);

          frameData.BoneRotations.Add(new BoneRotationEntry
          {
            BoneName = boneName,
            Path = path,
            Rotation = new BoneRotationData
            {
              Rotation = new[] { kvp.Value.x, kvp.Value.y, kvp.Value.z, kvp.Value.w }
            }
          });
        }

        data.Frames.Add(frameData);
      }

      data.Metadata.BoneCount = recordedBonePaths.Count;

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

        if (data == null || data.Frames == null || data.Frames.Count == 0)
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

      var bonePathOverrides = animator != null ? BuildHumanoidBonePathMap(animator) : null;

      if (data.Frames.Count > 0)
      {
        var firstFrame = data.Frames[0];

        if (firstFrame.RootTransform != null)
        {
          var rootPositionX = new AnimationCurve();
          var rootPositionY = new AnimationCurve();
          var rootPositionZ = new AnimationCurve();
          var rootRotationX = new AnimationCurve();
          var rootRotationY = new AnimationCurve();
          var rootRotationZ = new AnimationCurve();
          var rootRotationW = new AnimationCurve();

          foreach (var frame in data.Frames)
          {
            if (frame.RootTransform != null)
            {
              var pos = frame.RootTransform.Position;
              var rot = frame.RootTransform.Rotation;

              rootPositionX.AddKey(frame.Time, pos[0]);
              rootPositionY.AddKey(frame.Time, pos[1]);
              rootPositionZ.AddKey(frame.Time, pos[2]);
              rootRotationX.AddKey(frame.Time, rot[0]);
              rootRotationY.AddKey(frame.Time, rot[1]);
              rootRotationZ.AddKey(frame.Time, rot[2]);
              rootRotationW.AddKey(frame.Time, rot[3]);
            }
          }

          clip.SetCurve("", typeof(Transform), "localPosition.x", rootPositionX);
          clip.SetCurve("", typeof(Transform), "localPosition.y", rootPositionY);
          clip.SetCurve("", typeof(Transform), "localPosition.z", rootPositionZ);
          clip.SetCurve("", typeof(Transform), "localRotation.x", rootRotationX);
          clip.SetCurve("", typeof(Transform), "localRotation.y", rootRotationY);
          clip.SetCurve("", typeof(Transform), "localRotation.z", rootRotationZ);
          clip.SetCurve("", typeof(Transform), "localRotation.w", rootRotationW);
        }

        var boneIdentifiers = new HashSet<string>();
        foreach (var frame in data.Frames)
        {
          foreach (var entry in frame.BoneRotations)
          {
            var identifier = ResolveBonePath(entry, bonePathOverrides);
            boneIdentifiers.Add(identifier);
          }
        }

        foreach (var boneIdentifier in boneIdentifiers)
        {
          var rotX = new AnimationCurve();
          var rotY = new AnimationCurve();
          var rotZ = new AnimationCurve();
          var rotW = new AnimationCurve();

          foreach (var frame in data.Frames)
          {
            var entry = frame.BoneRotations.Find(e =>
            {
              var identifier = ResolveBonePath(e, bonePathOverrides);
              return identifier == boneIdentifier;
            });
            if (entry != null)
            {
              var rot = entry.Rotation.Rotation;
              rotX.AddKey(frame.Time, rot[0]);
              rotY.AddKey(frame.Time, rot[1]);
              rotZ.AddKey(frame.Time, rot[2]);
              rotW.AddKey(frame.Time, rot[3]);
            }
          }

          var bonePath = boneIdentifier;
          clip.SetCurve(bonePath, typeof(Transform), "localRotation.x", rotX);
          clip.SetCurve(bonePath, typeof(Transform), "localRotation.y", rotY);
          clip.SetCurve(bonePath, typeof(Transform), "localRotation.z", rotZ);
          clip.SetCurve(bonePath, typeof(Transform), "localRotation.w", rotW);
        }
      }

#if UNITY_EDITOR
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = data.LoopTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
#endif

      clip.EnsureQuaternionContinuity();

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

    private static string ResolveBonePath(BoneRotationEntry entry, Dictionary<string, string> overrideMap)
    {
      if (!string.IsNullOrEmpty(entry.Path))
      {
        return entry.Path;
      }

      if (overrideMap != null && !string.IsNullOrEmpty(entry.BoneName) && overrideMap.TryGetValue(entry.BoneName, out var mappedPath))
      {
        return mappedPath;
      }

      return entry.BoneName;
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
