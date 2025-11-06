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

    private bool _isRecording;
    private AnimationClip _recordingClip;
    private Dictionary<HumanBodyBones, Transform> _boneTransforms = new Dictionary<HumanBodyBones, Transform>();
    private List<AnimationClipFrame> _frames = new List<AnimationClipFrame>();
    private float _startTime;
    private float _lastFrameTime;
    private float _frameInterval;

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
        if (currentTime - _lastFrameTime >= _frameInterval)
        {
          RecordFrame(currentTime);
          _lastFrameTime = currentTime;
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

      if (_frames.Count == 0)
      {
        Debug.LogWarning("HumanoidAnimationClipRecorder: No frames recorded");
        return null;
      }

      var clip = CreateAnimationClip();
      LastRecordedClip = clip;

      if (_saveToAssets)
      {
        SaveClipToAssets(clip);
      }

      if (_exportJson)
      {
        SaveClipToJson(clip);
      }

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
        frame.RootRotation = _animator.transform.localRotation;
      }

      foreach (var kvp in _boneTransforms)
      {
        var bone = kvp.Key;
        var transform = kvp.Value;
        if (transform != null)
        {
          frame.BoneRotations[bone] = transform.localRotation;
        }
      }

      _frames.Add(frame);
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
        var rootPath = GetRootPath();
        if (!string.IsNullOrEmpty(rootPath))
        {
          clip.SetCurve(rootPath, typeof(Transform), "localPosition.x", CreateCurveForVector3X(_frames, f => f.RootPosition));
          clip.SetCurve(rootPath, typeof(Transform), "localPosition.y", CreateCurveForVector3Y(_frames, f => f.RootPosition));
          clip.SetCurve(rootPath, typeof(Transform), "localPosition.z", CreateCurveForVector3Z(_frames, f => f.RootPosition));
          clip.SetCurve(rootPath, typeof(Transform), "localRotation.x", CreateCurveForQuaternionX(_frames, f => f.RootRotation));
          clip.SetCurve(rootPath, typeof(Transform), "localRotation.y", CreateCurveForQuaternionY(_frames, f => f.RootRotation));
          clip.SetCurve(rootPath, typeof(Transform), "localRotation.z", CreateCurveForQuaternionZ(_frames, f => f.RootRotation));
          clip.SetCurve(rootPath, typeof(Transform), "localRotation.w", CreateCurveForQuaternionW(_frames, f => f.RootRotation));
        }
        else
        {
          clip.SetCurve("", typeof(Transform), "localPosition.x", CreateCurveForVector3X(_frames, f => f.RootPosition));
          clip.SetCurve("", typeof(Transform), "localPosition.y", CreateCurveForVector3Y(_frames, f => f.RootPosition));
          clip.SetCurve("", typeof(Transform), "localPosition.z", CreateCurveForVector3Z(_frames, f => f.RootPosition));
          clip.SetCurve("", typeof(Transform), "localRotation.x", CreateCurveForQuaternionX(_frames, f => f.RootRotation));
          clip.SetCurve("", typeof(Transform), "localRotation.y", CreateCurveForQuaternionY(_frames, f => f.RootRotation));
          clip.SetCurve("", typeof(Transform), "localRotation.z", CreateCurveForQuaternionZ(_frames, f => f.RootRotation));
          clip.SetCurve("", typeof(Transform), "localRotation.w", CreateCurveForQuaternionW(_frames, f => f.RootRotation));
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

        var bonePath = GetBonePath(transform);
        if (string.IsNullOrEmpty(bonePath))
        {
          bonePath = transform.name;
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

      return clip;
    }

    private string GetRootPath()
    {
      if (_animator == null)
      {
        return "";
      }

      if (_animator.transform.parent == null)
      {
        return "";
      }

      return _animator.transform.name;
    }

    private string GetBonePath(Transform bone)
    {
      if (bone == null)
      {
        return "";
      }

      if (bone == _animator.transform)
      {
        return GetRootPath();
      }

      var animatorRoot = _animator.transform;
      if (animatorRoot.parent == null)
      {
        var path = "";
        var current = bone;
        while (current != null && current != animatorRoot && current.parent != null)
        {
          if (string.IsNullOrEmpty(path))
          {
            path = current.name;
          }
          else
          {
            path = current.name + "/" + path;
          }
          current = current.parent;
          if (current == animatorRoot)
          {
            break;
          }
        }
        return path;
      }
      else
      {
        var path = "";
        var current = bone;
        while (current != null && current != animatorRoot && current.parent != null)
        {
          if (string.IsNullOrEmpty(path))
          {
            path = current.name;
          }
          else
          {
            path = current.name + "/" + path;
          }
          current = current.parent;
        }

        if (!string.IsNullOrEmpty(path))
        {
          return GetRootPath() + "/" + path;
        }
        return path;
      }
    }

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

        var jsonData = AnimationClipConverter.ConvertToJson(clip, _frames);
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

