using UnityEngine;
using UnityEngine.Serialization;

namespace AnimationClipRecording
{
  public class HumanoidAnimationClipPlayer : MonoBehaviour
  {
    [FormerlySerializedAs("animator")] public Animator _animator;
    [FormerlySerializedAs("animationClip")] public AnimationClip _animationClip;
    [FormerlySerializedAs("playOnStart")] public bool _playOnStart = false;
    [FormerlySerializedAs("loop")] public bool _loop = false;
    [FormerlySerializedAs("speed")] public float _speed = 1.0f;
    [FormerlySerializedAs("normalizedTime")][Range(0f, 1f)] public float _normalizedTime = 0f;

    private bool _isPlaying;
    private float _currentTime;
    private float _clipLength;
    private bool _animatorInitiallyEnabled;
    private Transform[] _cachedTransforms;
    private Vector3[] _cachedPositions;
    private Quaternion[] _cachedRotations;
    private Vector3[] _cachedScales;
    private bool _poseCached;

    public bool IsPlaying => _isPlaying;
    public float CurrentTime => _currentTime;
    public float NormalizedTime => _clipLength > Mathf.Epsilon ? Mathf.Clamp01(_currentTime / _clipLength) : 0f;

    private void Awake()
    {
      if (_animator == null)
      {
        _animator = GetComponentInChildren<Animator>();
      }

      if (_animator != null)
      {
        _animatorInitiallyEnabled = _animator.enabled;
      }
    }

    private void Start()
    {
      if (_playOnStart && _animationClip != null)
      {
        Play(_animationClip);
      }
    }

    private void Update()
    {
      if (!_isPlaying || _animationClip == null)
      {
        return;
      }

      if (_clipLength <= Mathf.Epsilon)
      {
        ApplySample(0f);
        StopPlayback(true, true);
        return;
      }

      _currentTime += Time.deltaTime * _speed;

      if (_loop)
      {
        var wrapped = Mathf.Repeat(_currentTime, _clipLength);
        ApplySample(wrapped);
      }
      else
      {
        if (_currentTime >= _clipLength)
        {
          ApplySample(_clipLength);
          StopPlayback(true, true);
          return;
        }

        if (_currentTime < 0f)
        {
          _currentTime = 0f;
        }

        ApplySample(_currentTime);
      }
    }

    public void Play(AnimationClip clip)
    {
      if (clip == null)
      {
        Debug.LogWarning("HumanoidAnimationClipPlayer: Cannot play null AnimationClip");
        return;
      }

      if (_animator == null)
      {
        Debug.LogError("HumanoidAnimationClipPlayer: Animator is required");
        return;
      }

      if (_isPlaying || _poseCached)
      {
        StopPlayback(false, false);
      }

      _animationClip = clip;
      _clipLength = clip.length;
      if (_clipLength <= Mathf.Epsilon)
      {
        _clipLength = clip.frameRate > Mathf.Epsilon ? 1f / clip.frameRate : 1f / 60f;
      }

      CachePose();

      _animatorInitiallyEnabled = _animator.enabled;
      _animator.enabled = false;

      _isPlaying = true;
      _currentTime = 0f;
      ApplySample(0f);

      Debug.Log($"HumanoidAnimationClipPlayer: Started playing {clip.name}");
    }

    public void PlayFromJson(string jsonPath)
    {
      var clip = AnimationClipConverter.LoadFromJson(jsonPath, _animator);
      if (clip != null)
      {
        Play(clip);
      }
      else
      {
        Debug.LogError($"HumanoidAnimationClipPlayer: Failed to load AnimationClip from {jsonPath}");
      }
    }

    public void Stop()
    {
      StopPlayback(false, false);
    }

    public void Pause()
    {
      if (!_isPlaying)
      {
        return;
      }

      _isPlaying = false;
    }

    public void Resume()
    {
      if (_animationClip == null)
      {
        Debug.LogWarning("HumanoidAnimationClipPlayer: Cannot resume without a valid AnimationClip");
        return;
      }

      if (_animator == null)
      {
        Debug.LogWarning("HumanoidAnimationClipPlayer: Animator is required to resume playback");
        return;
      }

      CachePose();
      _animator.enabled = false;
      _isPlaying = true;
    }

    public void SetTime(float time)
    {
      if (_animationClip == null)
      {
        return;
      }

      CachePose();
      var duration = Mathf.Max(_clipLength, Mathf.Epsilon);
      var clamped = Mathf.Clamp(time, 0f, duration);
      _currentTime = clamped;
      ApplySample(_loop ? Mathf.Repeat(clamped, duration) : clamped);
    }

    public void SetNormalizedTime(float normalizedTime)
    {
      if (_animationClip == null)
      {
        return;
      }

      normalizedTime = Mathf.Clamp01(normalizedTime);
      var targetTime = _clipLength > Mathf.Epsilon ? normalizedTime * _clipLength : 0f;
      SetTime(targetTime);
    }

    private void ApplySample(float time)
    {
      if (_animationClip == null || _animator == null)
      {
        return;
      }

      var sampleTime = _clipLength > Mathf.Epsilon ? Mathf.Clamp(time, 0f, _clipLength) : 0f;
      _animationClip.SampleAnimation(_animator.gameObject, sampleTime);

      _currentTime = sampleTime;
      _normalizedTime = _clipLength > Mathf.Epsilon ? Mathf.Clamp01(sampleTime / _clipLength) : 0f;
    }

    private void CachePose()
    {
      if (_poseCached || _animator == null)
      {
        return;
      }

      _cachedTransforms = _animator.GetComponentsInChildren<Transform>(true);
      var count = _cachedTransforms.Length;

      _cachedPositions = new Vector3[count];
      _cachedRotations = new Quaternion[count];
      _cachedScales = new Vector3[count];

      for (int i = 0; i < count; i++)
      {
        var transform = _cachedTransforms[i];
        _cachedPositions[i] = transform.localPosition;
        _cachedRotations[i] = transform.localRotation;
        _cachedScales[i] = transform.localScale;
      }

      _poseCached = true;
    }

    private void RestorePose()
    {
      if (!_poseCached || _cachedTransforms == null)
      {
        return;
      }

      for (int i = 0; i < _cachedTransforms.Length; i++)
      {
        var transform = _cachedTransforms[i];
        if (transform == null)
        {
          continue;
        }

        transform.localPosition = _cachedPositions[i];
        transform.localRotation = _cachedRotations[i];
        transform.localScale = _cachedScales[i];
      }

      _poseCached = false;
      _cachedTransforms = null;
      _cachedPositions = null;
      _cachedRotations = null;
      _cachedScales = null;
    }

    private void StopPlayback(bool reachedEnd, bool keepPose)
    {
      _isPlaying = false;

      if (_animator != null)
      {
        _animator.enabled = _animatorInitiallyEnabled;
      }

      if (!keepPose)
      {
        RestorePose();
      }

      if (reachedEnd && _clipLength > Mathf.Epsilon)
      {
        _currentTime = _clipLength;
        _normalizedTime = 1f;
      }
      else
      {
        _currentTime = 0f;
        _normalizedTime = 0f;
      }
    }

    private void OnDestroy()
    {
      if (_isPlaying || _poseCached)
      {
        StopPlayback(false, false);
      }
    }
  }
}
