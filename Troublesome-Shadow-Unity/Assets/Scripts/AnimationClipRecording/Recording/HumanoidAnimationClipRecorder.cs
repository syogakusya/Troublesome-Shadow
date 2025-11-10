using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AnimationClipRecording
{
  [DefaultExecutionOrder(1100)]
  public class HumanoidAnimationClipRecorder : MonoBehaviour
  {
    [FormerlySerializedAs("animator")] public Animator _animator;
    [FormerlySerializedAs("recordingName")] public string _recordingName = "humanoid_motion";
    [FormerlySerializedAs("frameRate")] public int _frameRate = 60;
    [FormerlySerializedAs("recordRootTransform")] public bool _recordRootTransform = true;
    [FormerlySerializedAs("recordAllHumanBones")] public bool _recordEntireHierarchy = true;
    [FormerlySerializedAs("useHumanoidMuscles")] public bool _useHumanoidMuscleCurves = false;
    [FormerlySerializedAs("selectedBones")] public List<HumanBodyBones> _selectedBones = new List<HumanBodyBones>();
    public bool _recordLocalPositions = true;
    public bool _recordLocalRotations = true;
    public bool _recordLocalScale = false;
    [FormerlySerializedAs("saveToAssets")] public bool _saveToAssets = true;
    [FormerlySerializedAs("assetsPath")] public string _assetsPath = "Assets/Recordings/AnimationClips";
    [FormerlySerializedAs("exportJson")] public bool _exportJson = true;
    [FormerlySerializedAs("jsonOutputPath")] public string _jsonOutputPath = "Recordings/Json";
    [FormerlySerializedAs("verifyRecording")] public bool _verifyRecording = true;
    [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;

    private bool _isRecording;
    private readonly List<RecordedTransform> _recordedTransforms = new List<RecordedTransform>();
    private readonly Dictionary<string, int> _pathToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    private List<AnimationClipFrame> _frames = new List<AnimationClipFrame>();
    private readonly List<HumanoidMuscleFrame> _humanoidFrames = new List<HumanoidMuscleFrame>();
    private float _startTime;
    private float _lastFrameTime;
    private float _frameInterval;
    private readonly Queue<float> _pendingFrameTimes = new Queue<float>();
    private HumanPoseHandler _humanPoseHandler;
    private HumanPose _currentHumanPose = new HumanPose();
    private static int _muscleCount;
    private static string[] _muscleNames = Array.Empty<string>();
    private static bool _muscleMetadataInitialized;

    public bool IsRecording => _isRecording;
    public AnimationClip LastRecordedClip { get; private set; }

    private class RecordedTransform
    {
      public Transform Transform;
      public string Path;
      public HumanBodyBones? HumanoidBone;
    }

    private void Awake()
    {
      EnsureHumanoidTraits();
      if (_animator == null)
      {
        _animator = GetComponentInChildren<Animator>();
      }

      if (_animator != null)
      {
        if (_animator.avatar == null || !_animator.avatar.isHuman)
        {
          Debug.LogWarning("HumanoidAnimationClipRecorder: Animator with Humanoid Avatar is recommended for consistent bone mapping");
        }

        InitializeRecordedTransforms();
      }
      else
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Animator component is required");
      }

      _frameInterval = 1f / _frameRate;

      if (_currentHumanPose.muscles == null || _currentHumanPose.muscles.Length != _muscleCount)
      {
        _currentHumanPose.muscles = new float[_muscleCount];
      }
    }

    private static void EnsureHumanoidTraits()
    {
      if (_muscleMetadataInitialized)
      {
        return;
      }

      _muscleCount = HumanTrait.MuscleCount;
      _muscleNames = HumanTrait.MuscleName ?? Array.Empty<string>();
      _muscleMetadataInitialized = true;
    }

    private bool EnsureHumanPoseHandler()
    {
      if (_humanPoseHandler != null)
      {
        return true;
      }

      if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
      {
        Debug.LogError("HumanoidAnimationClipRecorder: Humanoid Animator with a valid Avatar is required to record muscle curves");
        return false;
      }

      _humanPoseHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
      return true;
    }

    private void DisposeHumanPoseHandler()
    {
      if (_humanPoseHandler != null)
      {
        _humanPoseHandler.Dispose();
        _humanPoseHandler = null;
      }
    }

    private void InitializeRecordedTransforms()
    {
      _recordedTransforms.Clear();
      _pathToIndex.Clear();

      if (_animator == null)
      {
        return;
      }

      var root = _animator.transform;
      if (root == null)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Animator has no transform root");
        return;
      }

      var humanoidLookup = BuildHumanoidBoneLookup();

      if (_recordEntireHierarchy)
      {
        AddTransformRecursive(root, root, humanoidLookup);
      }
      else
      {
        var bonesToRecord = _selectedBones != null && _selectedBones.Count > 0 ? _selectedBones : GetAllHumanBones();
        foreach (var bone in bonesToRecord)
        {
          if (bone == HumanBodyBones.LastBone)
          {
            continue;
          }

          var transform = _animator.GetBoneTransform(bone);
          if (transform == null)
          {
            continue;
          }

          var path = GetBonePath(transform);
          if (path == null)
          {
            continue;
          }

          AddRecordedTransform(transform, path, bone);
        }

        if (_recordRootTransform)
        {
          AddRecordedTransform(root, string.Empty, null);
        }
      }

      if (_recordedTransforms.Count == 0)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: No transforms available for recording");
      }
    }

    private void AddTransformRecursive(Transform current, Transform root, Dictionary<Transform, HumanBodyBones> humanoidLookup)
    {
      if (current == null)
      {
        return;
      }

      var isRoot = current == root;
      if (!isRoot || _recordRootTransform)
      {
        var path = GetBonePath(current);
        if (path != null)
        {
          var bone = humanoidLookup != null && humanoidLookup.TryGetValue(current, out var mappedBone) ? mappedBone : (HumanBodyBones?)null;
          AddRecordedTransform(current, path, bone);
        }
      }

      foreach (Transform child in current)
      {
        AddTransformRecursive(child, root, humanoidLookup);
      }
    }

    private void AddRecordedTransform(Transform transform, string path, HumanBodyBones? bone)
    {
      if (transform == null || path == null)
      {
        return;
      }

      if (_pathToIndex.ContainsKey(path))
      {
        return;
      }

      var descriptor = new RecordedTransform
      {
        Transform = transform,
        Path = path,
        HumanoidBone = bone
      };

      _recordedTransforms.Add(descriptor);
      _pathToIndex[path] = _recordedTransforms.Count - 1;
    }

    private Dictionary<Transform, HumanBodyBones> BuildHumanoidBoneLookup()
    {
      var map = new Dictionary<Transform, HumanBodyBones>();

      if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
      {
        return map;
      }

      for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
      {
        var bone = (HumanBodyBones)i;
        var transform = _animator.GetBoneTransform(bone);
        if (transform != null && !map.ContainsKey(transform))
        {
          map[transform] = bone;
        }
      }

      return map;
    }

    private List<HumanBodyBones> GetAllHumanBones()
    {
      var bones = new List<HumanBodyBones>();
      for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
      {
        bones.Add((HumanBodyBones)i);
      }
      return bones;
    }

    private void Update()
    {
      if (_isRecording && _animator != null)
      {
        var currentTime = Time.time - _startTime;
        while (currentTime - _lastFrameTime >= _frameInterval)
        {
          _lastFrameTime += _frameInterval;
          _pendingFrameTimes.Enqueue(_lastFrameTime);
        }
      }
    }

    public void StartRecording()
    {
      EnsureHumanoidTraits();
      if (_isRecording)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Already recording");
        return;
      }

      if (_animator == null)
      {
        Debug.LogError("HumanoidAnimationClipRecorder: Valid Animator is required");
        return;
      }

      if (_useHumanoidMuscleCurves)
      {
        if (!EnsureHumanPoseHandler())
        {
          return;
        }

        _humanoidFrames.Clear();
        _frames.Clear();
      }
      else
      {
        if (!_recordLocalPositions && !_recordLocalRotations && !_recordLocalScale)
        {
          Debug.LogError("HumanoidAnimationClipRecorder: At least one transform property (position/rotation/scale) must be recorded");
          return;
        }

        InitializeRecordedTransforms();
        if (_recordedTransforms.Count == 0)
        {
          Debug.LogError("HumanoidAnimationClipRecorder: No transforms to record");
          return;
        }

        _frames.Clear();
      }
      _isRecording = true;
      _startTime = Time.time;
      _lastFrameTime = 0f;
      _pendingFrameTimes.Clear();
      _pendingFrameTimes.Enqueue(0f);

      if (_debugLogging)
      {
        if (_useHumanoidMuscleCurves)
        {
          Debug.Log($"HumanoidAnimationClipRecorder: Initialized humanoid-muscle recording with {_muscleCount} muscles captured.");
        }
        else
        {
          Debug.Log($"HumanoidAnimationClipRecorder: Initialized recording with {_recordedTransforms.Count} transforms captured.");
        }
      }

      var targetDescription = _useHumanoidMuscleCurves
        ? "humanoid muscle curves"
        : $"{_recordedTransforms.Count} transforms";

      Debug.Log($"HumanoidAnimationClipRecorder: Started recording {targetDescription} at {_frameRate} fps");
    }

    public AnimationClip StopRecording()
    {
      if (!_isRecording)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Not recording");
        return null;
      }

      _isRecording = false;
      FlushPendingFrames();

      var recordedFrameCount = _useHumanoidMuscleCurves ? _humanoidFrames.Count : _frames.Count;
      if (recordedFrameCount == 0)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: No frames recorded");
        return null;
      }

      var clip = CreateAnimationClip();
      LastRecordedClip = clip;

      if (_debugLogging)
      {
        LogRecordingSummary(clip);
      }

      if (_saveToAssets)
      {
        SaveClipToAssets(clip);
      }

      if (_exportJson)
      {
        SaveClipToJson(clip);
      }

#if UNITY_EDITOR
      if (_verifyRecording)
      {
        VerifyRecording(clip);
      }
#endif

      Debug.Log($"HumanoidAnimationClipRecorder: Stopped recording. Created clip with {recordedFrameCount} frames");
      return clip;
    }

    private void RecordFrame(float time)
    {
      if (_useHumanoidMuscleCurves)
      {
        RecordHumanoidFrame(time);
        return;
      }

      var transformCount = _recordedTransforms.Count;
      var frame = new AnimationClipFrame(transformCount)
      {
        Time = time
      };

      for (int i = 0; i < transformCount; i++)
      {
        var descriptor = _recordedTransforms[i];
        if (descriptor.Transform == null)
        {
          continue;
        }

        var sample = new TransformSample();

        if (_recordLocalPositions)
        {
          sample.LocalPosition = descriptor.Transform.localPosition;
          sample.HasPosition = true;
        }

        if (_recordLocalRotations)
        {
          sample.LocalRotation = NormalizeQuaternion(descriptor.Transform.localRotation);
          sample.HasRotation = true;
        }

        if (_recordLocalScale)
        {
          sample.LocalScale = descriptor.Transform.localScale;
          sample.HasScale = true;
        }

        frame.TransformSamples[i] = sample;
      }

      _frames.Add(frame);
    }

    private void RecordHumanoidFrame(float time)
    {
      if (_humanPoseHandler == null)
      {
        return;
      }

      _humanPoseHandler.GetHumanPose(ref _currentHumanPose);

      var frame = new HumanoidMuscleFrame(_muscleCount)
      {
        Time = time,
        BodyPosition = _currentHumanPose.bodyPosition,
        BodyRotation = NormalizeQuaternion(_currentHumanPose.bodyRotation)
      };

      Array.Copy(_currentHumanPose.muscles, frame.Muscles, _muscleCount);
      _humanoidFrames.Add(frame);
    }

    private void LateUpdate()
    {
      if (!_isRecording || _animator == null)
      {
        return;
      }

      while (_pendingFrameTimes.Count > 0)
      {
        var frameTime = _pendingFrameTimes.Dequeue();
        RecordFrame(frameTime);
      }
    }

    private void FlushPendingFrames()
    {
      while (_pendingFrameTimes.Count > 0)
      {
        var frameTime = _pendingFrameTimes.Dequeue();
        RecordFrame(frameTime);
      }
    }

    private static Quaternion NormalizeQuaternion(Quaternion value)
    {
      if (value == Quaternion.identity)
      {
        return value;
      }

      var magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
      if (magnitude < Mathf.Epsilon)
      {
        return Quaternion.identity;
      }

      var inverse = 1f / magnitude;
      return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
    }

    private void LogRecordingSummary(AnimationClip clip)
    {
#if UNITY_EDITOR
      if (_useHumanoidMuscleCurves)
      {
        Debug.Log($"HumanoidAnimationClipRecorder: Recorded {_humanoidFrames.Count} humanoid frames. Clip length {clip.length:F3}s @ {clip.frameRate} fps capturing {_muscleCount} muscles.");
        return;
      }

      Debug.Log($"HumanoidAnimationClipRecorder: Recorded {_frames.Count} frames across {_recordedTransforms.Count} transforms (positions: {_recordLocalPositions}, rotations: {_recordLocalRotations}, scale: {_recordLocalScale}). Clip length {clip.length:F3}s @ {clip.frameRate} fps.");

      if (_frames.Count > 0)
      {
        var first = _frames[0];
        var missingTransforms = new List<string>();
        for (int i = 0; i < _recordedTransforms.Count; i++)
        {
          if (first.TransformSamples == null || first.TransformSamples.Length <= i)
          {
            missingTransforms.Add(FormatTransformLabel(_recordedTransforms[i]));
          }
        }

        if (missingTransforms.Count > 0)
        {
          Debug.LogWarning($"HumanoidAnimationClipRecorder: First frame is missing data for {missingTransforms.Count} transforms -> {string.Join(", ", missingTransforms)}");
        }
      }

      var curveBindings = AnimationUtility.GetCurveBindings(clip);
      var positionCurveCount = 0;
      var rotationCurveCount = 0;
      var scaleCurveCount = 0;
      foreach (var binding in curveBindings)
      {
        if (binding.propertyName.StartsWith("localPosition", StringComparison.Ordinal))
        {
          positionCurveCount++;
        }
        else if (binding.propertyName.StartsWith("localRotation", StringComparison.Ordinal))
        {
          rotationCurveCount++;
        }
        else if (binding.propertyName.StartsWith("localScale", StringComparison.Ordinal))
        {
          scaleCurveCount++;
        }
      }

      Debug.Log($"HumanoidAnimationClipRecorder: Animation clip curves -> position:{positionCurveCount}, rotation:{rotationCurveCount}, scale:{scaleCurveCount}.");
#endif
    }

    private AnimationClip CreateAnimationClip()
    {
      return _useHumanoidMuscleCurves ? CreateHumanoidAnimationClip() : CreateTransformAnimationClip();
    }

    private AnimationClip CreateTransformAnimationClip()
    {
      var clip = new AnimationClip
      {
        name = _recordingName,
        frameRate = _frameRate
      };

      clip.legacy = false;

      var transformCount = _recordedTransforms.Count;
      for (int i = 0; i < transformCount; i++)
      {
        var descriptor = _recordedTransforms[i];
        if (descriptor == null)
        {
          continue;
        }

        var path = descriptor.Path ?? string.Empty;
        if (!_recordRootTransform && string.IsNullOrEmpty(path))
        {
          continue;
        }

        if (_recordLocalPositions && HasAnySamples(i, sample => sample.HasPosition))
        {
          clip.SetCurve(path, typeof(Transform), "localPosition.x", CreateCurve(i, sample => sample.LocalPosition.x));
          clip.SetCurve(path, typeof(Transform), "localPosition.y", CreateCurve(i, sample => sample.LocalPosition.y));
          clip.SetCurve(path, typeof(Transform), "localPosition.z", CreateCurve(i, sample => sample.LocalPosition.z));
        }

        if (_recordLocalRotations && HasAnySamples(i, sample => sample.HasRotation))
        {
          clip.SetCurve(path, typeof(Transform), "localRotation.x", CreateCurve(i, sample => sample.LocalRotation.x));
          clip.SetCurve(path, typeof(Transform), "localRotation.y", CreateCurve(i, sample => sample.LocalRotation.y));
          clip.SetCurve(path, typeof(Transform), "localRotation.z", CreateCurve(i, sample => sample.LocalRotation.z));
          clip.SetCurve(path, typeof(Transform), "localRotation.w", CreateCurve(i, sample => sample.LocalRotation.w));
        }

        if (_recordLocalScale && HasAnySamples(i, sample => sample.HasScale))
        {
          clip.SetCurve(path, typeof(Transform), "localScale.x", CreateCurve(i, sample => sample.LocalScale.x));
          clip.SetCurve(path, typeof(Transform), "localScale.y", CreateCurve(i, sample => sample.LocalScale.y));
          clip.SetCurve(path, typeof(Transform), "localScale.z", CreateCurve(i, sample => sample.LocalScale.z));
        }
      }

#if UNITY_EDITOR
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
#endif

      if (_recordLocalRotations)
      {
        clip.EnsureQuaternionContinuity();
      }

      return clip;
    }

    private AnimationClip CreateHumanoidAnimationClip()
    {
      var clip = new AnimationClip
      {
        name = _recordingName,
        frameRate = _frameRate
      };

      clip.legacy = false;

      if (_humanoidFrames.Count == 0)
      {
        return clip;
      }

      // Root translation curves
      clip.SetCurve(string.Empty, typeof(Animator), "RootT.x", CreateHumanoidCurve(frame => frame.BodyPosition.x));
      clip.SetCurve(string.Empty, typeof(Animator), "RootT.y", CreateHumanoidCurve(frame => frame.BodyPosition.y));
      clip.SetCurve(string.Empty, typeof(Animator), "RootT.z", CreateHumanoidCurve(frame => frame.BodyPosition.z));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.x", CreateHumanoidCurve(frame => frame.BodyRotation.x));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.y", CreateHumanoidCurve(frame => frame.BodyRotation.y));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.z", CreateHumanoidCurve(frame => frame.BodyRotation.z));
      clip.SetCurve(string.Empty, typeof(Animator), "RootQ.w", CreateHumanoidCurve(frame => frame.BodyRotation.w));

      // Muscle curves
      for (int i = 0; i < _muscleCount; i++)
      {
        var curve = new AnimationCurve();
        foreach (var frame in _humanoidFrames)
        {
          var value = frame.Muscles != null && frame.Muscles.Length > i ? frame.Muscles[i] : 0f;
          curve.AddKey(frame.Time, value);
        }

        clip.SetCurve(string.Empty, typeof(Animator), _muscleNames[i], curve);
      }

#if UNITY_EDITOR
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
#endif

      clip.EnsureQuaternionContinuity();
      return clip;
    }

    private AnimationCurve CreateHumanoidCurve(Func<HumanoidMuscleFrame, float> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in _humanoidFrames)
      {
        curve.AddKey(frame.Time, getter(frame));
      }

      return curve;
    }

    private bool HasAnySamples(int transformIndex, Func<TransformSample, bool> predicate)
    {
      foreach (var frame in _frames)
      {
        if (frame.TransformSamples == null || frame.TransformSamples.Length <= transformIndex)
        {
          continue;
        }

        if (predicate(frame.TransformSamples[transformIndex]))
        {
          return true;
        }
      }

      return false;
    }

    private AnimationCurve CreateCurve(int transformIndex, Func<TransformSample, float> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in _frames)
      {
        if (frame.TransformSamples == null || frame.TransformSamples.Length <= transformIndex)
        {
          continue;
        }

        curve.AddKey(frame.Time, getter(frame.TransformSamples[transformIndex]));
      }

      return curve;
    }

    private List<RecordedTransformInfo> BuildRecordedTransformInfo()
    {
      var list = new List<RecordedTransformInfo>(_recordedTransforms.Count);
      foreach (var descriptor in _recordedTransforms)
      {
        if (descriptor == null)
        {
          continue;
        }

        list.Add(new RecordedTransformInfo
        {
          Path = descriptor.Path ?? string.Empty,
          HumanoidBone = descriptor.HumanoidBone?.ToString()
        });
      }

      return list;
    }

    private string FormatTransformLabel(RecordedTransform descriptor)
    {
      if (descriptor == null)
      {
        return "<missing>";
      }

      var path = string.IsNullOrEmpty(descriptor.Path) ? "(root)" : descriptor.Path;
      if (descriptor.HumanoidBone.HasValue)
      {
        return $"{descriptor.HumanoidBone.Value} [{path}]";
      }

      return path;
    }

    private string GetBonePath(Transform bone)
    {
      if (bone == null || _animator == null)
      {
        return null;
      }

      var root = _animator.transform;
      if (bone == root)
      {
        return "";
      }

      var segments = new System.Collections.Generic.List<string>();
      var current = bone;
      while (current != null && current != root)
      {
        segments.Insert(0, current.name);
        current = current.parent;
      }

      if (current != root)
      {
        return null;
      }

      return string.Join("/", segments);
    }

#if UNITY_EDITOR
    private void VerifyRecording(AnimationClip clip)
    {
      if (clip == null || _animator == null)
      {
        return;
      }

      if (_useHumanoidMuscleCurves)
      {
        VerifyHumanoidRecording(clip);
        return;
      }

      if (_frames.Count == 0)
      {
        return;
      }

      var originalStates = new List<RecordedTransformState>();
      foreach (var descriptor in _recordedTransforms)
      {
        if (descriptor.Transform == null)
        {
          continue;
        }

        originalStates.Add(new RecordedTransformState
        {
          Transform = descriptor.Transform,
          LocalPosition = descriptor.Transform.localPosition,
          LocalRotation = descriptor.Transform.localRotation,
          LocalScale = descriptor.Transform.localScale
        });
      }

      var firstFrame = _frames[0];
      AnimationMode.StartAnimationMode();
      try
      {
        AnimationMode.SampleAnimationClip(_animator.gameObject, clip, firstFrame.Time);

        for (int i = 0; i < _recordedTransforms.Count; i++)
        {
          var descriptor = _recordedTransforms[i];
          var transform = descriptor.Transform;
          if (transform == null)
          {
            continue;
          }

          if (firstFrame.TransformSamples == null || firstFrame.TransformSamples.Length <= i)
          {
            continue;
          }

          var recordedSample = firstFrame.TransformSamples[i];
          var label = FormatTransformLabel(descriptor);

          if (_recordLocalPositions && recordedSample.HasPosition)
          {
            var positionDiff = Vector3.Distance(transform.localPosition, recordedSample.LocalPosition);
            if (positionDiff > 0.005f)
            {
              Debug.LogWarning($"HumanoidAnimationClipRecorder: Position mismatch on frame 0 for {label} (diff {positionDiff:F4})");
            }
          }

          if (_recordLocalRotations && recordedSample.HasRotation)
          {
            var angleDiff = Quaternion.Angle(transform.localRotation, recordedSample.LocalRotation);
            if (angleDiff > 1f)
            {
              Debug.LogWarning($"HumanoidAnimationClipRecorder: Rotation mismatch on frame 0 for {label} ({angleDiff:F2}°)");

              if (_debugLogging)
              {
                Debug.Log($"HumanoidAnimationClipRecorder:   Recorded rot {recordedSample.LocalRotation.eulerAngles}, Sampled rot {transform.localRotation.eulerAngles}");
              }
            }
          }

          if (_recordLocalScale && recordedSample.HasScale)
          {
            var scaleDiff = (transform.localScale - recordedSample.LocalScale).magnitude;
            if (scaleDiff > 0.01f)
            {
              Debug.LogWarning($"HumanoidAnimationClipRecorder: Scale mismatch on frame 0 for {label} (diff {scaleDiff:F4})");
            }
          }
        }
      }
      finally
      {
        foreach (var state in originalStates)
        {
          if (state.Transform == null)
          {
            continue;
          }

          state.Transform.localPosition = state.LocalPosition;
          state.Transform.localRotation = state.LocalRotation;
          state.Transform.localScale = state.LocalScale;
        }

        AnimationMode.StopAnimationMode();
      }
    }

    private struct RecordedTransformState
    {
      public Transform Transform;
      public Vector3 LocalPosition;
      public Quaternion LocalRotation;
      public Vector3 LocalScale;
    }

    private void VerifyHumanoidRecording(AnimationClip clip)
    {
      if (_humanoidFrames.Count == 0 || _animator.avatar == null || !_animator.avatar.isHuman)
      {
        return;
      }

      var firstFrame = _humanoidFrames[0];
      var pose = new HumanPose
      {
        muscles = new float[_muscleCount]
      };

      using (var verificationHandler = new HumanPoseHandler(_animator.avatar, _animator.transform))
      {
        AnimationMode.StartAnimationMode();
        try
        {
          AnimationMode.SampleAnimationClip(_animator.gameObject, clip, firstFrame.Time);
          verificationHandler.GetHumanPose(ref pose);

          var posDiff = Vector3.Distance(pose.bodyPosition, firstFrame.BodyPosition);
          if (posDiff > 0.01f)
          {
            Debug.LogWarning($"HumanoidAnimationClipRecorder: Root bodyPosition mismatch ({posDiff:F4}) on frame 0");
          }

          var rotDiff = Quaternion.Angle(pose.bodyRotation, firstFrame.BodyRotation);
          if (rotDiff > 1f)
          {
            Debug.LogWarning($"HumanoidAnimationClipRecorder: Root bodyRotation mismatch ({rotDiff:F2}°) on frame 0");
          }

          float maxMuscleDiff = 0f;
          int maxMuscleIndex = -1;
          for (int i = 0; i < _muscleCount; i++)
          {
            var diff = Mathf.Abs(pose.muscles[i] - firstFrame.Muscles[i]);
            if (diff > maxMuscleDiff)
            {
              maxMuscleDiff = diff;
              maxMuscleIndex = i;
            }
          }

          if (maxMuscleDiff > 0.01f && maxMuscleIndex >= 0)
          {
            Debug.LogWarning($"HumanoidAnimationClipRecorder: Muscle '{_muscleNames[maxMuscleIndex]}' mismatch (Δ={maxMuscleDiff:F3}) on frame 0");
          }
        }
        finally
        {
          AnimationMode.StopAnimationMode();
        }
      }
    }
#endif

    private void SaveClipToAssets(AnimationClip clip)
    {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(_assetsPath))
            {
                _assetsPath = "Assets/Recordings/AnimationClips";
            }

            var directory = _assetsPath;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var fileName = $"{_recordingName}.anim";
            var assetPath = Path.Combine(directory, fileName);
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"HumanoidAnimationClipRecorder: Saved AnimationClip to {assetPath}");
#endif
    }

    private void SaveClipToJson(AnimationClip clip)
    {
      try
      {
        var hasTransformData = !_useHumanoidMuscleCurves && _frames != null && _frames.Count > 0;
        var hasHumanoidData = _useHumanoidMuscleCurves && _humanoidFrames != null && _humanoidFrames.Count > 0;

        if (clip == null || (!hasTransformData && !hasHumanoidData))
        {
          Debug.LogWarning("HumanoidAnimationClipRecorder: Cannot save JSON - no recorded data available");
          return;
        }

        var transformMetadata = _useHumanoidMuscleCurves ? null : BuildRecordedTransformInfo();
        var transformFrames = _useHumanoidMuscleCurves ? null : _frames;
        var humanoidFrames = _useHumanoidMuscleCurves ? _humanoidFrames : null;

        var jsonData = AnimationClipConverter.ConvertToJson(
          clip,
          transformMetadata,
          transformFrames,
          _recordRootTransform,
          _recordLocalPositions,
          _recordLocalRotations,
          _recordLocalScale,
          humanoidFrames);
        if (string.IsNullOrEmpty(jsonData) || jsonData == "{}")
        {
          Debug.LogWarning("HumanoidAnimationClipRecorder: JSON conversion returned empty data");
          return;
        }

        var directory = Path.Combine(Application.persistentDataPath, _jsonOutputPath);
        if (!Directory.Exists(directory))
        {
          Directory.CreateDirectory(directory);
        }

        var fileName = $"{_recordingName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllText(filePath, jsonData);

        var frameCount = hasHumanoidData ? _humanoidFrames.Count : _frames.Count;
        Debug.Log($"HumanoidAnimationClipRecorder: Saved JSON to {filePath} ({frameCount} frames)");
      }
      catch (Exception ex)
      {
        Debug.LogError($"HumanoidAnimationClipRecorder: Failed to save JSON: {ex.Message}\n{ex.StackTrace}");
      }
    }
    private void OnDestroy()
    {
      DisposeHumanPoseHandler();
    }
  }

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
