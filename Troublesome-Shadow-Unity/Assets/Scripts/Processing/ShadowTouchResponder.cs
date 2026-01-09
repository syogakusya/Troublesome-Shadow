using UnityEngine;
using UnityEngine.Serialization;

namespace PoseRuntime
{
    /// <summary>
    /// Detects sustained hand contact near the projected shadow avatar and notifies ShadowSeatDirector.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public class ShadowTouchResponder : MonoBehaviour
    {
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("shadowRoot")] public Transform _shadowRoot;
        [FormerlySerializedAs("poseSpaceOrigin")] public Transform _poseSpaceOrigin;
        public ShadowSeatDirector _seatDirector;
        [FormerlySerializedAs("leftHandJoint")] public string _leftHandJoint = "LEFT_INDEX";
        [FormerlySerializedAs("rightHandJoint")] public string _rightHandJoint = "RIGHT_INDEX";
        [FormerlySerializedAs("touchRadius")] public float _touchRadius = 0.35f;
        public float _touchHoldSeconds = 0.6f;
        [FormerlySerializedAs("cooldownSeconds")] public float _cooldownSeconds = 1.0f;
        public bool _requireSeatedIdle = true;
        [FormerlySerializedAs("minimumConfidence")] public float _minimumConfidence = 0.2f;
        [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;
        [FormerlySerializedAs("drawDebug")] public bool _drawDebug = false;
        [FormerlySerializedAs("debugColor")] public Color _debugColor = Color.cyan;

        private SkeletonSample _latestSample;
        private float _lastTriggerTime = -999f;
        private bool _subscribed;
        private bool _isTouching;
        private float _touchStartTime = -1f;
        private bool _touchTriggered;
        private Vector3 _lastTouchWorldPosition;

        public bool IsTouching => _isTouching;
        public Vector3 LastTouchWorldPosition => _lastTouchWorldPosition;

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

            if (_seatDirector == null)
            {
                _seatDirector = GetComponent<ShadowSeatDirector>();
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
            if (_latestSample == null)
            {
                return;
            }

            var root = _shadowRoot != null ? _shadowRoot : transform;
            var rootPosition = root.position;
            var touching = false;
            var touchPosition = Vector3.zero;

            if (IsJointWithinRadius(_leftHandJoint, rootPosition, out var leftPosition))
            {
                touching = true;
                touchPosition = leftPosition;
            }
            else if (IsJointWithinRadius(_rightHandJoint, rootPosition, out var rightPosition))
            {
                touching = true;
                touchPosition = rightPosition;
            }

            var allowTrigger = !_requireSeatedIdle || _seatDirector == null || _seatDirector.CanReceiveTouch;
            UpdateTouchState(touching, touchPosition, allowTrigger);
        }

        private void UpdateTouchState(bool touching, Vector3 touchPosition, bool allowTrigger)
        {
            _isTouching = touching;

            if (!touching)
            {
                ResetTouchState();
                return;
            }

            _lastTouchWorldPosition = touchPosition;

            if (!allowTrigger)
            {
                if (!_touchTriggered)
                {
                    _touchStartTime = -1f;
                }
                return;
            }

            if (_touchStartTime < 0f)
            {
                _touchStartTime = Time.time;
            }

            if (_touchTriggered)
            {
                return;
            }

            if (Time.time - _touchStartTime < _touchHoldSeconds)
            {
                return;
            }

            if (Time.time - _lastTriggerTime < _cooldownSeconds)
            {
                return;
            }

            _lastTriggerTime = Time.time;
            _touchTriggered = true;

            if (_seatDirector != null)
            {
                _seatDirector.NotifySustainedTouch();
            }

            if (_debugLogging)
            {
                Debug.Log("ShadowTouchResponder: sustained touch detected");
            }
        }

        private void ResetTouchState()
        {
            _touchStartTime = -1f;
            _touchTriggered = false;
            _isTouching = false;
        }

        private bool IsJointWithinRadius(string jointName, Vector3 rootPosition, out Vector3 jointWorld)
        {
            jointWorld = Vector3.zero;
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

            jointWorld = PoseSpaceUtility.ToWorld(_poseSpaceOrigin, joint._position);
            var distance = Vector3.Distance(rootPosition, jointWorld);

            if (_drawDebug)
            {
                Debug.DrawLine(rootPosition, jointWorld, _debugColor, Time.deltaTime);
            }

            return distance <= _touchRadius;
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
