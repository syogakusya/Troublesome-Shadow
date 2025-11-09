using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PoseRuntime
{
    [Serializable]
    public class ShadowSeat
    {
        public string _id = "seat-1";
        public Transform _anchor;
        public Transform _lookTarget;
        public float _heightOffset = 0f;

        [NonSerialized] public bool _isHumanOccupied;
        [NonSerialized] public bool _isShadowOccupied;
        [NonSerialized] public int _index;

        public Vector3 AnchorPosition => _anchor != null ? _anchor.position : Vector3.zero;

        public Quaternion ResolveRotation(Transform fallback)
        {
            if (_lookTarget != null)
            {
                var direction = _lookTarget.position - AnchorPosition;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.y = 0f;
                    return Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            if (_anchor != null)
            {
                var forward = _anchor.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                {
                    return Quaternion.LookRotation(forward.normalized, Vector3.up);
                }
            }

            return fallback != null ? fallback.rotation : Quaternion.identity;
        }
    }

    /// <summary>
    /// Controls the virtual shadow's seat selection and reaction logic.
    /// </summary>
    public class ShadowSeatDirector : MonoBehaviour
    {
        [Header("References")]
        public Transform _shadowRoot;
        public AvatarController _avatarController;
        public Animator _animator;

        [Header("Seating")]
        public List<ShadowSeat> _seats = new List<ShadowSeat>();
        public string _defaultSeatId;
        public Transform _floorAnchor;
        public Transform _floorLookTarget;

        [Header("Timing")]
        public float _movementDuration = 0.75f;
        public float _lookDuration = 0.35f;
        public float _glareCooldown = 2.0f;
        public float _glareConfidenceThreshold = 0.2f;

        [Header("Animator Parameters")]
        public string _animSeatIndexParam = "SeatIndex";
        public string _animOnFloorParam = "OnFloor";
        public string _animScootTrigger = "Scoot";
        public string _animSurprisedTrigger = "Surprised";
        public string _animGlareTrigger = "Glare";
        public string _animFrustratedTrigger = "Frustrated";
        public string _animSitTrigger = "Sit";

        private readonly Dictionary<string, ShadowSeat> _seatLookup = new Dictionary<string, ShadowSeat>(StringComparer.OrdinalIgnoreCase);
        private ShadowSeat _currentSeat;
        private ShadowSeat _defaultSeat;
        private Coroutine _moveRoutine;
        private bool _onFloor;
        private float _lastGlareTime = -999f;

        private Transform ShadowRoot => _shadowRoot != null ? _shadowRoot : transform;

        private void Awake()
        {
            BuildSeatLookup();
            SnapToDefaultSeat();
        }

        private void OnEnable()
        {
            if (_avatarController != null)
            {
                _avatarController.SampleProcessed += OnSampleProcessed;
            }
        }

        private void OnDisable()
        {
            if (_avatarController != null)
            {
                _avatarController.SampleProcessed -= OnSampleProcessed;
            }
        }

        private void BuildSeatLookup()
        {
            _seatLookup.Clear();
            for (var index = 0; index < _seats.Count; index++)
            {
                var seat = _seats[index];
                if (seat == null || string.IsNullOrEmpty(seat._id))
                {
                    continue;
                }

                seat._index = index;
                _seatLookup[seat._id] = seat;
            }

            if (!string.IsNullOrEmpty(_defaultSeatId) && _seatLookup.TryGetValue(_defaultSeatId, out var seatRef))
            {
                _defaultSeat = seatRef;
            }
            else
            {
                _defaultSeat = _seats.FirstOrDefault(s => s != null);
            }
        }

        private void SnapToDefaultSeat()
        {
            if (_defaultSeat != null)
            {
                MoveShadowInstant(_defaultSeat);
            }
        }

        private void OnSampleProcessed(SkeletonSample sample)
        {
            if (!SeatingMetadataUtility.TryGetSnapshot(sample, out var snapshot))
            {
                return;
            }

            UpdateOccupancy(snapshot);
            EvaluateShadowResponse(snapshot);
        }

        private void UpdateOccupancy(SeatingSnapshot snapshot)
        {
            foreach (var seat in _seats)
            {
                if (seat == null)
                {
                    continue;
                }

                seat._isHumanOccupied = snapshot.TryGetOccupancy(seat._id, out var occupied) && occupied;
            }
        }

        private void EvaluateShadowResponse(SeatingSnapshot snapshot)
        {
            var humanSeat = !string.IsNullOrEmpty(snapshot.ActiveSeatId) ? GetSeat(snapshot.ActiveSeatId) : null;
            var allHumanOccupied = _seats.Count > 0 && _seats.All(s => s != null && s._isHumanOccupied);

            if (allHumanOccupied)
            {
                MoveShadowToFloor(true);
                return;
            }

            if (humanSeat == null)
            {
                if (_onFloor)
                {
                    TryReturnToSeat();
                    return;
                }

                if (_currentSeat == null && _defaultSeat != null)
                {
                    MoveShadowToSeat(_defaultSeat, _animSitTrigger, true);
                }

                return;
            }

            if (_currentSeat != null && humanSeat == _currentSeat)
            {
                HandleSeatCollision(humanSeat);
                return;
            }

            if (_currentSeat != null && AreNeighbours(_currentSeat, humanSeat))
            {
                HandleAdjacentOccupancy(humanSeat);
                return;
            }

            if (!_onFloor)
            {
                MaybeGlareAt(humanSeat, snapshot.Confidence);
            }
        }

        private void HandleSeatCollision(ShadowSeat humanSeat)
        {
            TriggerAnimator(_animSurprisedTrigger);
            var target = FindBestSeat(reference: humanSeat, requireGap: false, allowCurrent: false);
            if (target != null)
            {
                MoveShadowToSeat(target, _animSurprisedTrigger, true);
            }
            else
            {
                MoveShadowToFloor(false);
            }
        }

        private void HandleAdjacentOccupancy(ShadowSeat humanSeat)
        {
            TriggerAnimator(_animScootTrigger);
            var target = FindBestSeat(reference: humanSeat, requireGap: true, allowCurrent: false);
            if (target != null)
            {
                MoveShadowToSeat(target, _animScootTrigger, true);
            }
            else
            {
                MoveShadowToFloor(false);
            }
        }

        private void MaybeGlareAt(ShadowSeat seat, float confidence)
        {
            if (confidence < _glareConfidenceThreshold)
            {
                return;
            }

            if (Time.time - _lastGlareTime < _glareCooldown)
            {
                return;
            }

            _lastGlareTime = Time.time;
            TriggerAnimator(_animGlareTrigger);
            RotateTowardsSeat(seat);
        }

        private void RotateTowardsSeat(ShadowSeat seat)
        {
            if (seat == null)
            {
                return;
            }

            var root = ShadowRoot;
            var rotation = seat.ResolveRotation(root);
            BeginMovement(root.position, rotation, _lookDuration);
        }

        private void TryReturnToSeat()
        {
            var preferred = _defaultSeat != null && !_defaultSeat._isHumanOccupied
                ? _defaultSeat
                : FindBestSeat(reference: null, requireGap: false, allowCurrent: false);

            if (preferred != null)
            {
                MoveShadowToSeat(preferred, _animSitTrigger, true);
            }
        }

        private void MoveShadowToSeat(ShadowSeat seat, string trigger, bool force)
        {
            if (seat == null)
            {
                MoveShadowToFloor(false);
                return;
            }

            if (!force && !_onFloor && seat == _currentSeat)
            {
                return;
            }

            if (seat._isHumanOccupied)
            {
                return;
            }

            _onFloor = false;
            if (_animator != null && !string.IsNullOrEmpty(_animOnFloorParam))
            {
                _animator.SetBool(_animOnFloorParam, false);
            }

            foreach (var s in _seats)
            {
                if (s == null)
                {
                    continue;
                }

                s._isShadowOccupied = s == seat;
            }

            _currentSeat = seat;
            if (_animator != null && !string.IsNullOrEmpty(_animSeatIndexParam))
            {
                _animator.SetInteger(_animSeatIndexParam, seat._index);
            }

            TriggerAnimator(trigger);
            var targetPosition = seat.AnchorPosition + Vector3.up * seat._heightOffset;
            var targetRotation = seat.ResolveRotation(ShadowRoot);
            BeginMovement(targetPosition, targetRotation, _movementDuration);
        }

        private void MoveShadowToFloor(bool frustrated)
        {
            if (_floorAnchor == null)
            {
                return;
            }

            foreach (var seat in _seats)
            {
                if (seat != null)
                {
                    seat._isShadowOccupied = false;
                }
            }

            _currentSeat = null;
            _onFloor = true;

            if (_animator != null)
            {
                if (!string.IsNullOrEmpty(_animOnFloorParam))
                {
                    _animator.SetBool(_animOnFloorParam, true);
                }

                if (frustrated)
                {
                    TriggerAnimator(_animFrustratedTrigger);
                }
            }

            var targetRotation = ResolveFloorRotation();
            BeginMovement(_floorAnchor.position, targetRotation, _movementDuration);
        }

        private Quaternion ResolveFloorRotation()
        {
            if (_floorLookTarget != null)
            {
                var direction = _floorLookTarget.position - _floorAnchor.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    return Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }

            return _floorAnchor.rotation;
        }

        private void MoveShadowInstant(ShadowSeat seat)
        {
            if (seat == null)
            {
                return;
            }

            var root = ShadowRoot;
            root.position = seat.AnchorPosition + Vector3.up * seat._heightOffset;
            root.rotation = seat.ResolveRotation(root);
            _currentSeat = seat;
            _onFloor = false;
            seat._isShadowOccupied = true;
            if (_animator != null && !string.IsNullOrEmpty(_animSeatIndexParam))
            {
                _animator.SetInteger(_animSeatIndexParam, seat._index);
            }
        }

        private void BeginMovement(Vector3 position, Quaternion rotation, float duration)
        {
            var root = ShadowRoot;
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
            }

            if (duration <= Mathf.Epsilon)
            {
                root.position = position;
                root.rotation = rotation;
                _moveRoutine = null;
                return;
            }

            _moveRoutine = StartCoroutine(MoveRoutine(root, position, rotation, duration));
        }

        private IEnumerator MoveRoutine(Transform root, Vector3 position, Quaternion rotation, float duration)
        {
            var startPos = root.position;
            var startRot = root.rotation;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                t = Mathf.SmoothStep(0f, 1f, t);
                root.position = Vector3.Lerp(startPos, position, t);
                root.rotation = Quaternion.Slerp(startRot, rotation, t);
                yield return null;
            }

            root.position = position;
            root.rotation = rotation;
            _moveRoutine = null;
        }

        private ShadowSeat FindBestSeat(ShadowSeat reference, bool requireGap, bool allowCurrent)
        {
            IEnumerable<ShadowSeat> candidates = _seats.Where(s => s != null && !s._isHumanOccupied);
            if (!allowCurrent && !_onFloor && _currentSeat != null)
            {
                candidates = candidates.Where(s => s != _currentSeat);
            }

            if (requireGap && reference != null)
            {
                candidates = candidates.Where(s => Mathf.Abs(s._index - reference._index) > 1);
            }

            if (reference != null)
            {
                candidates = candidates
                    .OrderByDescending(s => Mathf.Abs(s._index - reference._index))
                    .ThenBy(s => s._index);
            }
            else if (_defaultSeat != null)
            {
                candidates = candidates
                    .OrderByDescending(s => Mathf.Abs(s._index - _defaultSeat._index))
                    .ThenBy(s => s._index);
            }

            return candidates.FirstOrDefault();
        }

        private bool AreNeighbours(ShadowSeat a, ShadowSeat b)
        {
            return Mathf.Abs(a._index - b._index) == 1;
        }

        private ShadowSeat GetSeat(string seatId)
        {
            if (string.IsNullOrEmpty(seatId))
            {
                return null;
            }

            _seatLookup.TryGetValue(seatId, out var seat);
            return seat;
        }

        private void TriggerAnimator(string trigger)
        {
            if (_animator == null || string.IsNullOrEmpty(trigger))
            {
                return;
            }

            _animator.ResetTrigger(trigger);
            _animator.SetTrigger(trigger);
        }
    }
}
