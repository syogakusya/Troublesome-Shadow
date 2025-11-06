using UnityEngine;
using UnityEngine.Playables;
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

    private PlayableGraph _graph;
    private AnimationClipPlayable _clipPlayable;
    private bool _isPlaying;
    private float _currentTime;

    public bool IsPlaying => _isPlaying;
    public float CurrentTime => _currentTime;
    public float NormalizedTime => _animationClip != null ? (_currentTime / _animationClip.length) : 0f;

    private void Awake()
    {
      if (_animator == null)
      {
        _animator = GetComponentInChildren<Animator>();
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
      if (_isPlaying && _animationClip != null && _graph.IsValid())
      {
        _currentTime += Time.deltaTime * _speed;
        var normalizedTime = _currentTime / _animationClip.length;

        if (normalizedTime >= 1f)
        {
          if (_loop)
          {
            _currentTime = 0f;
            normalizedTime = 0f;
          }
          else
          {
            Stop();
            return;
          }
        }

        _normalizedTime = normalizedTime;
        if (_clipPlayable.IsValid())
        {
          _clipPlayable.SetTime(_currentTime);
        }
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

      Stop();

      _animationClip = clip;
      _currentTime = 0f;
      _isPlaying = true;
      _normalizedTime = 0f;

      SetupPlayableGraph();
      Debug.Log($"HumanoidAnimationClipPlayer: Started playing {clip.name}");
    }

    public void PlayFromJson(string jsonPath)
    {
      var clip = AnimationClipConverter.LoadFromJson(jsonPath);
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
      _isPlaying = false;
      if (_graph.IsValid())
      {
        _graph.Stop();
      }
    }

    public void Pause()
    {
      _isPlaying = false;
      if (_graph.IsValid())
      {
        _graph.Stop();
      }
    }

    public void Resume()
    {
      if (_animationClip != null)
      {
        _isPlaying = true;
        if (_graph.IsValid())
        {
          _graph.Play();
        }
      }
    }

    public void SetTime(float time)
    {
      if (_animationClip != null)
      {
        _currentTime = Mathf.Clamp(time, 0f, _animationClip.length);
        _normalizedTime = _currentTime / _animationClip.length;
        if (_clipPlayable.IsValid())
        {
          _clipPlayable.SetTime(_currentTime);
        }
      }
    }

    public void SetNormalizedTime(float normalizedTime)
    {
      normalizedTime = Mathf.Clamp01(normalizedTime);
      if (_animationClip != null)
      {
        _currentTime = normalizedTime * _animationClip.length;
        _normalizedTime = normalizedTime;
        if (_clipPlayable.IsValid())
        {
          _clipPlayable.SetTime(_currentTime);
        }
      }
    }

    private void SetupPlayableGraph()
    {
      if (_animator == null || _animationClip == null)
      {
        return;
      }

      if (_graph.IsValid())
      {
        _graph.Destroy();
      }

      _graph = PlayableGraph.Create();
      _clipPlayable = AnimationClipPlayable.Create(_graph, _animationClip);
      _clipPlayable.SetDuration(_animationClip.length);
      _clipPlayable.SetSpeed(_speed);

      var output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
      output.SetSourcePlayable(_clipPlayable);

      _graph.Play();
    }

    private void OnDestroy()
    {
      if (_graph.IsValid())
      {
        _graph.Destroy();
      }
    }
  }
}

