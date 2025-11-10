using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    public enum InteractionMode
    {
        ShadowInstallation,
        HumanoidAvatar,
    }

    public enum InteractionModeSource
    {
        Manual,
        Metadata,
    }

    /// <summary>
    /// Centralises switching between the "shadow installation" behaviour set and the humanoid avatar retargeting pipeline.
    /// When running in metadata mode the component inspects incoming skeleton samples for a mode token and toggles
    /// dependent behaviours (HumanoidPoseApplier, ShadowSeatDirector, recording utilities) automatically.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class InteractionModeCoordinator : MonoBehaviour
    {
        [Header("References")]
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("shadowDirector")] public ShadowSeatDirector _shadowDirector;
        [FormerlySerializedAs("poseApplier")] public HumanoidPoseApplier _poseApplier;
        [FormerlySerializedAs("clipRecorder")] public AnimationClipRecording.HumanoidAnimationClipRecorder _clipRecorder;
        [FormerlySerializedAs("clipPlayer")] public AnimationClipRecording.HumanoidAnimationClipPlayer _clipPlayer;

        [Header("Mode Switching")]
        [FormerlySerializedAs("modeSource")] public InteractionModeSource _modeSource = InteractionModeSource.Metadata;
        [FormerlySerializedAs("manualMode")] public InteractionMode _manualMode = InteractionMode.ShadowInstallation;
        [FormerlySerializedAs("metadataKey")] public string _metadataKey = "mode";
        [FormerlySerializedAs("shadowTokens")] public List<string> _shadowTokens = new List<string> { "shadow", "shadow_installation", "installation" };
        [FormerlySerializedAs("avatarTokens")] public List<string> _avatarTokens = new List<string> { "avatar", "humanoid", "recording", "live" };
        [FormerlySerializedAs("logTransitions")] public bool _logTransitions = true;

        [Header("Optional Controlled Components")]
        [FormerlySerializedAs("shadowOnlyComponents")] public List<MonoBehaviour> _shadowOnlyComponents = new List<MonoBehaviour>();
        [FormerlySerializedAs("avatarOnlyComponents")] public List<MonoBehaviour> _avatarOnlyComponents = new List<MonoBehaviour>();

        private InteractionMode? _activeMode;
        private bool _subscribed;

        public InteractionMode? CurrentMode => _activeMode;

        private void Reset()
        {
            CacheReferences();
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            Subscribe();
            ApplyInitialMode();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                CacheReferences();
            }

            EnsureTokenLists();
        }

        private void Subscribe()
        {
            if (_controller != null && !_subscribed)
            {
                _controller.SampleProcessed += OnSampleProcessed;
                _subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (_controller != null && _subscribed)
            {
                _controller.SampleProcessed -= OnSampleProcessed;
                _subscribed = false;
            }
        }

        private void CacheReferences()
        {
            if (_controller == null)
            {
                _controller = GetComponent<AvatarController>();
            }

            if (_shadowDirector == null)
            {
                _shadowDirector = GetComponent<ShadowSeatDirector>();
            }

            if (_poseApplier == null)
            {
                _poseApplier = GetComponent<HumanoidPoseApplier>();
            }

            if (_clipRecorder == null)
            {
                _clipRecorder = GetComponent<AnimationClipRecording.HumanoidAnimationClipRecorder>();
            }

            if (_clipPlayer == null)
            {
                _clipPlayer = GetComponent<AnimationClipRecording.HumanoidAnimationClipPlayer>();
            }
        }

        private void EnsureTokenLists()
        {
            if (_shadowTokens == null)
            {
                _shadowTokens = new List<string>();
            }

            if (_avatarTokens == null)
            {
                _avatarTokens = new List<string>();
            }
        }

        private void ApplyInitialMode()
        {
            var initialMode = _manualMode;
            ApplyMode(initialMode, true);
        }

        private void OnSampleProcessed(SkeletonSample sample)
        {
            if (_modeSource != InteractionModeSource.Metadata)
            {
                return;
            }

            if (sample?.Meta == null)
            {
                return;
            }

            if (!sample.Meta.TryGetValue(_metadataKey, out var rawMode) || rawMode == null)
            {
                return;
            }

            if (TryResolveMode(rawMode, out var resolvedMode))
            {
                ApplyMode(resolvedMode);
            }
        }

        public void SetManualMode(InteractionMode mode)
        {
            _manualMode = mode;
            if (_modeSource == InteractionModeSource.Manual)
            {
                ApplyMode(mode);
            }
        }

        public void ApplyMode(InteractionMode mode, bool force = false)
        {
            if (!force && _activeMode.HasValue && _activeMode.Value == mode)
            {
                return;
            }

            _activeMode = mode;

            var enableShadow = mode == InteractionMode.ShadowInstallation;
            var enableAvatar = mode == InteractionMode.HumanoidAvatar;

            if (_logTransitions)
            {
                Debug.Log($"InteractionModeCoordinator switched to {mode}");
            }

            if (_shadowDirector != null)
            {
                _shadowDirector.enabled = enableShadow;
            }

            if (_poseApplier != null)
            {
                _poseApplier.enabled = enableAvatar;
            }

            if (_clipRecorder != null)
            {
                _clipRecorder.enabled = enableAvatar;
            }

            if (_clipPlayer != null)
            {
                _clipPlayer.enabled = enableShadow;
            }

            ToggleComponentCollection(_shadowOnlyComponents, enableShadow);
            ToggleComponentCollection(_avatarOnlyComponents, enableAvatar);
        }

        private void ToggleComponentCollection(List<MonoBehaviour> components, bool enabled)
        {
            if (components == null)
            {
                return;
            }

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                component.enabled = enabled;
            }
        }

        private bool TryResolveMode(object raw, out InteractionMode mode)
        {
            mode = _manualMode;

            if (raw is InteractionMode typedMode)
            {
                mode = typedMode;
                return true;
            }

            if (raw == null)
            {
                return false;
            }

            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim().ToLowerInvariant();

            if (MatchesAny(text, _shadowTokens) || text.Contains("shadow"))
            {
                mode = InteractionMode.ShadowInstallation;
                return true;
            }

            if (MatchesAny(text, _avatarTokens) || text.Contains("avatar") || text.Contains("humanoid"))
            {
                mode = InteractionMode.HumanoidAvatar;
                return true;
            }

            return false;
        }

        private static bool MatchesAny(string value, List<string> tokens)
        {
            if (tokens == null)
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (string.Equals(value, token.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
