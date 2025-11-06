using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
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
    [FormerlySerializedAs("recordAllHumanBones")] public bool _recordAllHumanBones = true;
    [FormerlySerializedAs("selectedBones")] public List<HumanBodyBones> _selectedBones = new List<HumanBodyBones>();
    [FormerlySerializedAs("saveToAssets")] public bool _saveToAssets = true;
    [FormerlySerializedAs("assetsPath")] public string _assetsPath = "Assets/Recordings/AnimationClips";
    [FormerlySerializedAs("exportJson")] public bool _exportJson = true;
    [FormerlySerializedAs("jsonOutputPath")] public string _jsonOutputPath = "Recordings/Json";
    [FormerlySerializedAs("verifyRecording")] public bool _verifyRecording = true;
    [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;

    private bool _isRecording;
    private readonly Dictionary<HumanBodyBones, Transform> _boneTransforms = new Dictionary<HumanBodyBones, Transform>();
    private readonly Dictionary<HumanBodyBones, string> _bonePaths = new Dictionary<HumanBodyBones, string>();
    private List<AnimationClipFrame> _frames = new List<AnimationClipFrame>();
    private float _startTime;
    private float _lastFrameTime;
    private float _frameInterval;
    private readonly Queue<float> _pendingFrameTimes = new Queue<float>();

    public bool IsRecording => _isRecording;
    public AnimationClip LastRecordedClip { get; private set; }

    private void Awake()
    {
      if (_animator == null)
      {
        _animator = GetComponentInChildren<Animator>();
      }

      if (_animator != null && _animator.avatar != null && _animator.avatar.isHuman)
      {
        InitializeBoneTransforms();
      }
      else
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Animator with Humanoid Avatar is required");
      }

      _frameInterval = 1f / _frameRate;
    }

    private void InitializeBoneTransforms()
    {
      _boneTransforms.Clear();
      _bonePaths.Clear();
      var bonesToRecord = _recordAllHumanBones ? GetAllHumanBones() : _selectedBones;

      foreach (var bone in bonesToRecord)
      {
        if (bone == HumanBodyBones.LastBone)
        {
          continue;
        }

        var transform = _animator.GetBoneTransform(bone);
        if (transform != null)
        {
          _boneTransforms[bone] = transform;
          var path = GetBonePath(transform);
          if (path != null)
          {
            _bonePaths[bone] = path;
          }
          else
          {
            _bonePaths.Remove(bone);
          }
        }
        else
        {
          _bonePaths.Remove(bone);
        }
      }
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
      if (_isRecording)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: Already recording");
        return;
      }

      if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
      {
        Debug.LogError("HumanoidAnimationClipRecorder: Valid Humanoid Animator is required");
        return;
      }

      InitializeBoneTransforms();
      if (_boneTransforms.Count == 0)
      {
        Debug.LogError("HumanoidAnimationClipRecorder: No bones to record");
        return;
      }

      _frames.Clear();
      _isRecording = true;
      _startTime = Time.time;
      _lastFrameTime = 0f;
      _pendingFrameTimes.Clear();
      _pendingFrameTimes.Enqueue(0f);

      if (_debugLogging)
      {
        Debug.Log($"HumanoidAnimationClipRecorder: Initialized recording with {_boneTransforms.Count} bone transforms captured.");
      }

      Debug.Log($"HumanoidAnimationClipRecorder: Started recording {_boneTransforms.Count} bones at {_frameRate} fps");
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

      if (_frames.Count == 0)
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

      Debug.Log($"HumanoidAnimationClipRecorder: Stopped recording. Created clip with {_frames.Count} frames");
      return clip;
    }

    private void RecordFrame(float time)
    {
      var frame = new AnimationClipFrame
      {
        Time = time
      };

      if (_recordRootTransform)
      {
        frame.RootPosition = _animator.transform.localPosition;
        frame.RootRotation = NormalizeQuaternion(_animator.transform.localRotation);
      }

      foreach (var kvp in _boneTransforms)
      {
        var bone = kvp.Key;
        var transform = kvp.Value;
        if (transform != null)
        {
          frame.BoneRotations[bone] = NormalizeQuaternion(transform.localRotation);
        }
      }

      _frames.Add(frame);
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
      Debug.Log($"HumanoidAnimationClipRecorder: Recorded {_frames.Count} frames. Clip length {clip.length:F3}s, frameRate {clip.frameRate}.");

      if (_frames.Count > 0)
      {
        var first = _frames[0];
        Debug.Log($"HumanoidAnimationClipRecorder: First frame bone count {first.BoneRotations.Count}, root rotation {first.RootRotation.eulerAngles}.");

        var missingBones = new List<string>();
        foreach (var kvp in _boneTransforms)
        {
          if (!first.BoneRotations.ContainsKey(kvp.Key))
          {
            var path = _bonePaths.ContainsKey(kvp.Key) ? _bonePaths[kvp.Key] : GetBonePath(kvp.Value);
            missingBones.Add($"{kvp.Key} ({path})");
          }
        }

        if (missingBones.Count > 0)
        {
          Debug.LogWarning($"HumanoidAnimationClipRecorder: First frame is missing rotation data for {missingBones.Count} bones -> {string.Join(", ", missingBones)}");
        }
      }

      var curveBindings = AnimationUtility.GetCurveBindings(clip);
      var rotationCurveCount = 0;
      foreach (var binding in curveBindings)
      {
        if (binding.propertyName.StartsWith("localRotation", StringComparison.Ordinal))
        {
          rotationCurveCount++;
        }
      }
      Debug.Log($"HumanoidAnimationClipRecorder: Animation clip contains {rotationCurveCount} rotation curve bindings.");
#endif
    }

    private AnimationClip CreateAnimationClip()
    {
      var clip = new AnimationClip
      {
        name = _recordingName,
        frameRate = _frameRate
      };

      clip.legacy = false;

      if (_recordRootTransform)
      {
        const string rootPath = "";
        clip.SetCurve(rootPath, typeof(Transform), "localPosition.x", CreateCurveForVector3X(_frames, f => f.RootPosition));
        clip.SetCurve(rootPath, typeof(Transform), "localPosition.y", CreateCurveForVector3Y(_frames, f => f.RootPosition));
        clip.SetCurve(rootPath, typeof(Transform), "localPosition.z", CreateCurveForVector3Z(_frames, f => f.RootPosition));
        clip.SetCurve(rootPath, typeof(Transform), "localRotation.x", CreateCurveForQuaternionX(_frames, f => f.RootRotation));
        clip.SetCurve(rootPath, typeof(Transform), "localRotation.y", CreateCurveForQuaternionY(_frames, f => f.RootRotation));
        clip.SetCurve(rootPath, typeof(Transform), "localRotation.z", CreateCurveForQuaternionZ(_frames, f => f.RootRotation));
        clip.SetCurve(rootPath, typeof(Transform), "localRotation.w", CreateCurveForQuaternionW(_frames, f => f.RootRotation));
      }

      foreach (var kvp in _boneTransforms)
      {
        var bone = kvp.Key;
        var transform = kvp.Value;
        if (transform == null)
        {
          continue;
        }

        var bonePath = _bonePaths.ContainsKey(bone) ? _bonePaths[bone] : GetBonePath(transform);
        if (string.IsNullOrEmpty(bonePath))
        {
          continue;
        }

        bool hasBoneData = false;
        foreach (var frame in _frames)
        {
          if (frame.BoneRotations.ContainsKey(bone))
          {
            hasBoneData = true;
            break;
          }
        }

        if (!hasBoneData)
        {
          continue;
        }

        clip.SetCurve(bonePath, typeof(Transform), "localRotation.x", CreateCurveForQuaternionX(_frames, f => f.BoneRotations.ContainsKey(bone) ? f.BoneRotations[bone] : Quaternion.identity));
        clip.SetCurve(bonePath, typeof(Transform), "localRotation.y", CreateCurveForQuaternionY(_frames, f => f.BoneRotations.ContainsKey(bone) ? f.BoneRotations[bone] : Quaternion.identity));
        clip.SetCurve(bonePath, typeof(Transform), "localRotation.z", CreateCurveForQuaternionZ(_frames, f => f.BoneRotations.ContainsKey(bone) ? f.BoneRotations[bone] : Quaternion.identity));
        clip.SetCurve(bonePath, typeof(Transform), "localRotation.w", CreateCurveForQuaternionW(_frames, f => f.BoneRotations.ContainsKey(bone) ? f.BoneRotations[bone] : Quaternion.identity));
      }

#if UNITY_EDITOR
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
#endif

      clip.EnsureQuaternionContinuity();

      return clip;
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

      var rootTransform = _animator.transform;
      var originalRootPosition = rootTransform.localPosition;
      var originalRootRotation = rootTransform.localRotation;
      var originalBoneRotations = new Dictionary<HumanBodyBones, Quaternion>();

      foreach (var kvp in _boneTransforms)
      {
        if (kvp.Value != null)
        {
          originalBoneRotations[kvp.Key] = kvp.Value.localRotation;
        }
      }

      var rotationBindings = new HashSet<string>(StringComparer.Ordinal);
#if UNITY_EDITOR
      var curves = AnimationUtility.GetAllCurves(clip, true);
      foreach (var curve in curves)
      {
        if (curve.type == typeof(Transform) &&
            curve.propertyName.StartsWith("localRotation", StringComparison.Ordinal))
        {
          rotationBindings.Add(curve.path);
        }
      }
#endif

      var missingRotations = new List<string>();
      foreach (var kvp in _boneTransforms)
      {
        var path = GetBonePath(kvp.Value);
        if (string.IsNullOrEmpty(path))
        {
          continue;
        }

        if (!rotationBindings.Contains(path))
        {
          missingRotations.Add(path);
        }
      }

      if (missingRotations.Count > 0)
      {
        Debug.LogWarning($"HumanoidAnimationClipRecorder: The following bones have no rotation curves recorded -> {string.Join(", ", missingRotations)}");
        if (_debugLogging)
        {
          Debug.Log($"HumanoidAnimationClipRecorder: Rotation curve paths available ({rotationBindings.Count}) -> {string.Join(", ", rotationBindings)}");
        }
      }

      if (_frames.Count == 0)
      {
        return;
      }

      var firstFrame = _frames[0];
      AnimationMode.StartAnimationMode();
      try
      {
        AnimationMode.SampleAnimationClip(_animator.gameObject, clip, firstFrame.Time);

        if (_recordRootTransform)
        {
          var sampledPos = rootTransform.localPosition;
          var sampledRot = rootTransform.localRotation;
          var rootPosDiff = Vector3.Distance(sampledPos, firstFrame.RootPosition);
          var rootRotDiff = Quaternion.Angle(sampledRot, firstFrame.RootRotation);
          if (rootPosDiff > 0.005f || rootRotDiff > 1f)
          {
            Debug.LogWarning($"HumanoidAnimationClipRecorder: Root transform mismatch on frame 0 (pos diff {rootPosDiff:F4}, rot diff {rootRotDiff:F2}°)");
          }
        }

        foreach (var kvp in _boneTransforms)
        {
          var bone = kvp.Key;
          var transform = kvp.Value;
          if (transform == null)
          {
            continue;
          }

          if (!firstFrame.BoneRotations.TryGetValue(bone, out var recordedRot))
          {
            continue;
          }

          var sampledRot = transform.localRotation;
          var angleDiff = Quaternion.Angle(sampledRot, recordedRot);
          if (angleDiff > 1f)
          {
            Debug.LogWarning($"HumanoidAnimationClipRecorder: Bone {bone} rotation mismatch on frame 0 ({angleDiff:F2}°)");

            if (_debugLogging)
            {
              Debug.Log($"HumanoidAnimationClipRecorder:   Recorded rot {recordedRot.eulerAngles}, Sampled rot {sampledRot.eulerAngles}");
            }
          }
        }
      }
      finally
      {
        rootTransform.localPosition = originalRootPosition;
        rootTransform.localRotation = originalRootRotation;
        foreach (var kvp in _boneTransforms)
        {
          if (kvp.Value != null && originalBoneRotations.TryGetValue(kvp.Key, out var rot))
          {
            kvp.Value.localRotation = rot;
          }
        }

        AnimationMode.StopAnimationMode();
      }
    }
#endif

    private AnimationCurve CreateCurveForVector3X(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Vector3> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).x);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForVector3Y(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Vector3> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).y);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForVector3Z(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Vector3> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).z);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForQuaternionX(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Quaternion> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).x);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForQuaternionY(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Quaternion> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).y);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForQuaternionZ(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Quaternion> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).z);
      }
      return curve;
    }

    private AnimationCurve CreateCurveForQuaternionW(List<AnimationClipFrame> frames, Func<AnimationClipFrame, Quaternion> getter)
    {
      var curve = new AnimationCurve();
      foreach (var frame in frames)
      {
        curve.AddKey(frame.Time, getter(frame).w);
      }
      return curve;
    }

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
        if (clip == null || _frames == null || _frames.Count == 0)
        {
          Debug.LogWarning("HumanoidAnimationClipRecorder: Cannot save JSON - clip or frames are null/empty");
          return;
        }

        var jsonData = AnimationClipConverter.ConvertToJson(clip, _frames, _bonePaths, _recordRootTransform);
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

        Debug.Log($"HumanoidAnimationClipRecorder: Saved JSON to {filePath} ({_frames.Count} frames)");
      }
      catch (Exception ex)
      {
        Debug.LogError($"HumanoidAnimationClipRecorder: Failed to save JSON: {ex.Message}\n{ex.StackTrace}");
      }
    }
  }

  [Serializable]
  public class AnimationClipFrame
  {
    public float Time;
    public Vector3 RootPosition;
    public Quaternion RootRotation;
    public Dictionary<HumanBodyBones, Quaternion> BoneRotations = new Dictionary<HumanBodyBones, Quaternion>();
  }
}
