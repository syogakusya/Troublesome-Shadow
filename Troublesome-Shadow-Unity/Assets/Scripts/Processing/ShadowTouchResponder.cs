using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    /// <summary>
    /// Emits an animator trigger when MediaPipe hand joints approach the projected shadow avatar.
    /// Useful for playing "poked" or "surprised" reactions when visitors wave into the projection.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public class ShadowTouchResponder : MonoBehaviour
    {
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("shadowRoot")] public Transform _shadowRoot;
        [FormerlySerializedAs("poseSpaceOrigin")] public Transform _poseSpaceOrigin;
        [FormerlySerializedAs("animator")] public Animator _animator;
        [FormerlySerializedAs("leftHandJoint")] public string _leftHandJoint = "LEFT_INDEX";
        [FormerlySerializedAs("rightHandJoint")] public string _rightHandJoint = "RIGHT_INDEX";
        [FormerlySerializedAs("touchTrigger")] public string _touchTrigger = "Touched";
        [FormerlySerializedAs("touchRadius")] public float _touchRadius = 0.35f;
        [FormerlySerializedAs("cooldownSeconds")] public float _cooldownSeconds = 1.0f;
        [FormerlySerializedAs("minimumConfidence")] public float _minimumConfidence = 0.2f;
        [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;
        [FormerlySerializedAs("drawDebug")] public bool _drawDebug = false;
        [FormerlySerializedAs("debugColor")] public Color _debugColor = Color.cyan;

        private SkeletonSample _latestSample;
        private float _lastTriggerTime = -999f;
        private bool _subscribed;

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
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void CacheReferences()
        {
            if (_controller == null)
            {
                _controller = GetComponent<AvatarController>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
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

        private void OnSampleProcessed(SkeletonSample sample)
        {
            _latestSample = sample;
            EvaluateTouch();
        }

        private void EvaluateTouch()
        {
            if (_animator == null || string.IsNullOrEmpty(_touchTrigger))
            {
                return;
            }

            if (_latestSample == null)
            {
                return;
            }

            if (Time.time - _lastTriggerTime < _cooldownSeconds)
            {
                return;
            }

            var root = _shadowRoot != null ? _shadowRoot : transform;
            var rootPosition = root.position;

            if (IsJointWithinRadius(_leftHandJoint, rootPosition) || IsJointWithinRadius(_rightHandJoint, rootPosition))
            {
                _lastTriggerTime = Time.time;
                _animator.ResetTrigger(_touchTrigger);
                _animator.SetTrigger(_touchTrigger);
                if (_debugLogging)
                {
                    Debug.Log("ShadowTouchResponder: touch detected");
                }
            }
        }

        private bool IsJointWithinRadius(string jointName, Vector3 rootPosition)
        {
            if (string.IsNullOrEmpty(jointName))
            {
                return false;
            }

            if (_latestSample == null || !_latestSample.TryGetJoint(jointName, out var joint) || joint == null)
            {
                return false;
            }

            if (joint._confidence < _minimumConfidence)
            {
                return false;
            }

            var jointWorld = ConvertToWorld(joint._position);
            var distance = Vector3.Distance(rootPosition, jointWorld);

            if (_drawDebug)
            {
                Debug.DrawLine(rootPosition, jointWorld, _debugColor, Time.deltaTime);
            }

            return distance <= _touchRadius;
        }

        private Vector3 ConvertToWorld(Vector3 posePosition)
        {
            if (_poseSpaceOrigin != null)
            {
                return _poseSpaceOrigin.TransformPoint(posePosition);
            }

            return posePosition;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!_drawDebug)
            {
                return;
            }

            var root = _shadowRoot != null ? _shadowRoot : transform;
            Gizmos.color = _debugColor;
            Gizmos.DrawWireSphere(root.position, _touchRadius);
        }
#endif
    }
}
