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

        public Quaternion ResolveRotation(Transform fallback, bool flipRotation = false)
        {
            Quaternion rotation;

            if (_lookTarget != null)
            {
                var direction = _lookTarget.position - AnchorPosition;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.y = 0f;
                    rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
                else
                {
                    rotation = fallback != null ? fallback.rotation : Quaternion.identity;
                }
            }
            else if (_anchor != null)
            {
                var forward = _anchor.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                }
                else
                {
                    rotation = fallback != null ? fallback.rotation : Quaternion.identity;
                }
            }
            else
            {
                rotation = fallback != null ? fallback.rotation : Quaternion.identity;
            }

            if (flipRotation)
            {
                rotation = rotation * Quaternion.Euler(0, 180, 0);
            }

            return rotation;
        }
    }

    /// <summary>
    /// Controls the virtual shadow's seat selection and reaction logic.
    /// - Starts off-screen until explicitly triggered.
    /// - Reacts to adjacent/same-seat occupancy and sustained touch.
    /// - Uses Animator IK to keep the head looking at the guest when needed.
    /// </summary>
    public class ShadowSeatDirector : MonoBehaviour
    {
        [Header("References")]
        public Transform _shadowRoot;
        public AvatarController _avatarController;
        public Animator _animator;
        public InteractionModeCoordinator _modeCoordinator;
        public ShadowLookAtIK _lookAtIK;
        public ShadowTouchResponder _touchResponder;

        [Header("Startup")]
        public bool _startInactive = true;
        public bool _autoStart = false;
        public Transform _entryAnchor;

        [Header("Seating")]
        public List<ShadowSeat> _seats = new List<ShadowSeat>();
        public string _defaultSeatId;
        public Transform _floorAnchor;
        public Transform _floorLookTarget;
        public float _globalHeightOffset = 0f;
        public float _seatSitOffset = 0f;
        public float _seatPostSitOffset = 0f;

        [Header("Timing")]
        public float _movementDuration = 0.75f;
        public float _lookDuration = 0.35f;
        public float _postMoveLookDuration = 1.5f;
        public int _sameSeatCollisionThreshold = 3;

        [Header("Walking Animation")]
        public bool _useWalkingAnimation = true;
        public float _walkSpeed = 2.0f;
        public float _walkRotationSpeed = 5.0f;
        public float _stoppingDistance = 0.5f;
        public float _minWalkDistance = 1.0f;
        public bool _flipRotation = false;

        [Header("Animator Parameters")]
        public string _animSeatIndexParam = "SeatIndex";
        public string _animOnFloorParam = "OnFloor";
        public string _animWalkSpeedParam = "WalkSpeed";
        public string _animWalkStateName = "Walk";
        public string _animSurprisedTrigger = "Surprised";
        public string _animFrustratedTrigger = "Frustrated";
        public string _animScareTrigger = "Scare";
        public string _animSitTrigger = "Sit";
        public string _animSitOnFloorTrigger = "SitOnFloor";
        public string _animStandupTrigger = "Standup";
        public string _animStandupStateName = "standup";
        public string _animSitStateName = "Sit";

        [Header("Animator Tweaks")]
        public bool _disableRootMotion = true;
        public bool _disableStabilizeFeet = true;
        public bool _forceWalkStateOnMove = true;

        [Header("Debug")]
        public bool _debugLogSeating = false;
        public bool _debugLogAnimations = true;
        public float _debugLogInterval = 1.0f;
        private float _lastDebugLogTime = 0f;

        [Header("Debug Seating Control")]
        public bool _enableDebugSeating = false;
        public KeyCode _debugToggleKey = KeyCode.F1;
        private Dictionary<string, bool> _debugOccupancy = new Dictionary<string, bool>();

        private readonly Dictionary<string, ShadowSeat> _seatLookup = new Dictionary<string, ShadowSeat>(StringComparer.OrdinalIgnoreCase);
        private ShadowSeat _currentSeat;
        private ShadowSeat _defaultSeat;
        private Coroutine _moveRoutine;
        private Coroutine _reactionRoutine;
        private bool _onFloor;
        private bool _isMoving;
        private bool _isActive;
        private int _sameSeatCollisionCount;

        private Transform _activeLookTarget;
        private float _lookUntilTime = -1f;
        private bool _keepLooking;

        private string _lastActiveSeatId;
        private Dictionary<string, bool> _lastOccupancy = new Dictionary<string, bool>();

        public bool IsMoving => _isMoving;
        public bool IsActive => _isActive;
        public bool CanReceiveTouch => _isActive && IsSeatedIdle();

        private Transform ShadowRoot => _shadowRoot != null ? _shadowRoot : transform;

        private bool IsAvatarMode()
        {
            return _modeCoordinator != null &&
                   _modeCoordinator.CurrentMode.HasValue &&
                   _modeCoordinator.CurrentMode.Value == InteractionMode.HumanoidAvatar;
        }

        private void Awake()
        {
            if (_modeCoordinator == null)
            {
                _modeCoordinator = GetComponent<InteractionModeCoordinator>();
            }
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
            if (_lookAtIK == null)
            {
                _lookAtIK = GetComponent<ShadowLookAtIK>();
            }
            if (_touchResponder == null)
            {
                _touchResponder = GetComponent<ShadowTouchResponder>();
            }
            BuildSeatLookup();
            InitializeDebugOccupancy();
            ApplyStartupState();
            ApplyAnimatorTweaks();
        }

        private void InitializeDebugOccupancy()
        {
            _debugOccupancy.Clear();
            foreach (var seat in _seats)
            {
                if (seat != null && !string.IsNullOrEmpty(seat._id))
                {
                    _debugOccupancy[seat._id] = false;
                }
            }
        }

        private void OnEnable()
        {
            if (_avatarController != null)
            {
                _avatarController.SampleProcessed += OnSampleProcessed;
            }

            ApplyAnimatorTweaks();
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

        private void ApplyStartupState()
        {
            if (_autoStart)
            {
                SnapToEntryAnchor();
                _isActive = false;
                StartShadow();
                return;
            }

            if (_startInactive)
            {
                SnapToEntryAnchor();
                _isActive = false;
                return;
            }

            SnapToDefaultSeat();
            _isActive = true;
        }

        private void SnapToEntryAnchor()
        {
            var root = ShadowRoot;
            if (root != null && _entryAnchor != null)
            {
                root.position = _entryAnchor.position;
                root.rotation = _entryAnchor.rotation;
            }

            _currentSeat = null;
            _onFloor = false;
            foreach (var seat in _seats)
            {
                if (seat != null)
                {
                    seat._isShadowOccupied = false;
                }
            }
        }

        private ShadowSeat PickStartSeat()
        {
            if (_defaultSeat != null && !_defaultSeat._isHumanOccupied)
            {
                return _defaultSeat;
            }

            return FindBestSeat(reference: null, requireGap: false, allowCurrent: false);
        }

        public void StartShadow()
        {
            if (_isActive)
            {
                return;
            }

            _isActive = true;
            ClearLookTarget();
            ResetSameSeatCounter();

            if (_entryAnchor != null)
            {
                var root = ShadowRoot;
                root.position = _entryAnchor.position;
                root.rotation = _entryAnchor.rotation;
            }

            var startSeat = PickStartSeat();
            if (startSeat != null)
            {
                MoveShadowToSeat(startSeat, _animSitTrigger, true);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(_debugToggleKey))
            {
                _enableDebugSeating = !_enableDebugSeating;
                Debug.Log($"[ShadowSeatDirector] デバッグ座席モード: {(_enableDebugSeating ? "ON" : "OFF")}");
                if (_enableDebugSeating)
                {
                    Debug.Log("[ShadowSeatDirector] 操作方法:");
                    Debug.Log("  数字キー 1-9: 対応する座席を占有/解放");
                    Debug.Log("  数字キー 0: 全座席を解放");
                }
            }

            if (_enableDebugSeating)
            {
                HandleDebugInput();
            }

            UpdateLookTarget();
        }

        private void LateUpdate()
        {
            if (!_isActive)
            {
                return;
            }

            if (_isMoving || _onFloor || _currentSeat == null)
            {
                return;
            }

            var root = ShadowRoot;
            if (root == null)
            {
                return;
            }

            var shouldSnap = true;
            if (_animator != null && !IsAvatarMode())
            {
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                if (_animator.IsInTransition(0))
                {
                    return;
                }
                var isSit = !string.IsNullOrEmpty(_animSitStateName) && stateInfo.IsName(_animSitStateName);
                var isStandup = !string.IsNullOrEmpty(_animStandupStateName) && stateInfo.IsName(_animStandupStateName);
                var isIdle = stateInfo.IsName("Idle");

                // Sit/Standup 再生中はスナップしない
                if (isSit || isStandup)
                {
                    return;
                }

                shouldSnap = isIdle;
            }

            if (!shouldSnap)
            {
                return;
            }

            var seat = _currentSeat;
            var correctedPosition = GetSeatPosition(seat, _seatPostSitOffset);
            var correctedRotation = seat.ResolveRotation(root, _flipRotation);
            root.position = correctedPosition;
            root.rotation = correctedRotation;
        }

        private void HandleDebugInput()
        {
            for (int i = 0; i < 10; i++)
            {
                var keyCode = KeyCode.Alpha0 + i;
                if (Input.GetKeyDown(keyCode))
                {
                    if (i == 0)
                    {
                        ClearAllDebugOccupancy();
                    }
                    else
                    {
                        ToggleSeatOccupancy(i - 1);
                    }
                }
            }
        }

        private void ToggleSeatOccupancy(int seatIndex)
        {
            if (seatIndex < 0 || seatIndex >= _seats.Count)
            {
                Debug.LogWarning($"[ShadowSeatDirector] 無効な座席インデックス: {seatIndex} (座席数: {_seats.Count})");
                return;
            }

            var seat = _seats[seatIndex];
            if (seat == null || string.IsNullOrEmpty(seat._id))
            {
                Debug.LogWarning($"[ShadowSeatDirector] 座席インデックス {seatIndex} は null または ID が空です");
                return;
            }

            var currentState = _debugOccupancy.ContainsKey(seat._id) && _debugOccupancy[seat._id];
            _debugOccupancy[seat._id] = !currentState;
            seat._isHumanOccupied = !currentState;

            Debug.Log($"[ShadowSeatDirector] 座席 {seat._id} (インデックス {seatIndex}) を {(!currentState ? "占有" : "解放")} しました");

            var snapshot = CreateDebugSnapshot();
            EvaluateShadowResponse(snapshot);
        }

        private void ClearAllDebugOccupancy()
        {
            foreach (var seat in _seats)
            {
                if (seat != null && !string.IsNullOrEmpty(seat._id))
                {
                    _debugOccupancy[seat._id] = false;
                    seat._isHumanOccupied = false;
                }
            }
            Debug.Log("[ShadowSeatDirector] 全座席を解放しました");

            var snapshot = CreateDebugSnapshot();
            EvaluateShadowResponse(snapshot);
        }

        private SeatingSnapshot CreateDebugSnapshot()
        {
            string activeSeatId = null;
            foreach (var kvp in _debugOccupancy)
            {
                if (kvp.Value)
                {
                    activeSeatId = kvp.Key;
                    break;
                }
            }

            var occupancy = new Dictionary<string, bool>(_debugOccupancy);
            var order = _seats.Where(s => s != null && !string.IsNullOrEmpty(s._id)).Select(s => s._id).ToList();
            return new SeatingSnapshot(activeSeatId, string.IsNullOrEmpty(activeSeatId) ? 0f : 1f, occupancy, order);
        }

        private void OnSampleProcessed(SkeletonSample sample)
        {
            if (_enableDebugSeating)
            {
                return;
            }

            if (!SeatingMetadataUtility.TryGetSnapshot(sample, out var snapshot))
            {
                if (_debugLogSeating && Time.time - _lastDebugLogTime >= _debugLogInterval)
                {
                    Debug.LogWarning("ShadowSeatDirector: 座席情報が取得できませんでした");
                    _lastDebugLogTime = Time.time;
                }
                return;
            }

            if (_debugLogSeating && Time.time - _lastDebugLogTime >= _debugLogInterval)
            {
                var occupancyInfo = new System.Text.StringBuilder();
                occupancyInfo.AppendLine($"=== 座席情報 (時刻: {Time.time:F2}) ===");
                occupancyInfo.AppendLine($"アクティブ座席: {snapshot.ActiveSeatId ?? "(なし)"}");
                occupancyInfo.AppendLine($"信頼度: {snapshot.Confidence:F2}");
                occupancyInfo.AppendLine("座席一覧:");
                foreach (var seatId in snapshot.SeatOrder)
                {
                    if (snapshot.TryGetOccupancy(seatId, out var occupied))
                    {
                        var seat = GetSeat(seatId);
                        var shadowOccupied = seat != null && seat._isShadowOccupied;
                        occupancyInfo.AppendLine($"  [{seatId}] 人間: {(occupied ? "○" : "×")}, シャドウ: {(shadowOccupied ? "○" : "×")}");
                    }
                }
                Debug.Log(occupancyInfo.ToString());
                _lastDebugLogTime = Time.time;
            }

            var changed = HasSeatingChanged(snapshot);
            UpdateOccupancy(snapshot);
            if (changed && _isActive)
            {
                EvaluateShadowResponse(snapshot);
            }

            _lastActiveSeatId = snapshot.ActiveSeatId;
            _lastOccupancy.Clear();
            foreach (var seatId in snapshot.SeatOrder)
            {
                if (snapshot.TryGetOccupancy(seatId, out var occupied))
                {
                    _lastOccupancy[seatId] = occupied;
                }
            }
        }

        private bool HasSeatingChanged(SeatingSnapshot snapshot)
        {
            var currentActiveSeatId = snapshot.ActiveSeatId ?? string.Empty;
            var lastActiveSeatId = _lastActiveSeatId ?? string.Empty;

            if (currentActiveSeatId != lastActiveSeatId)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] アクティブ座席が変化: {lastActiveSeatId} -> {currentActiveSeatId}");
                }
                return true;
            }

            foreach (var seatId in snapshot.SeatOrder)
            {
                if (snapshot.TryGetOccupancy(seatId, out var occupied))
                {
                    if (!_lastOccupancy.TryGetValue(seatId, out var lastOccupied) || lastOccupied != occupied)
                    {
                        if (_debugLogSeating)
                        {
                            Debug.Log($"[ShadowSeatDirector] 座席 {seatId} の占有状態が変化: {(_lastOccupancy.TryGetValue(seatId, out var last) ? last.ToString() : "(なし)")} -> {occupied}");
                        }
                        return true;
                    }
                }
            }

            foreach (var kvp in _lastOccupancy)
            {
                if (!snapshot.TryGetOccupancy(kvp.Key, out var currentOccupied) || currentOccupied != kvp.Value)
                {
                    if (_debugLogSeating)
                    {
                        Debug.Log($"[ShadowSeatDirector] 座席 {kvp.Key} の占有状態が変化: {kvp.Value} -> {(snapshot.TryGetOccupancy(kvp.Key, out var current) ? current.ToString() : "(なし)")}");
                    }
                    return true;
                }
            }

            return false;
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
            if (_reactionRoutine != null || _isMoving || _onFloor || _currentSeat == null)
            {
                return;
            }

            if (humanSeat == null)
            {
                ResetSameSeatCounter();
                ClearLookTarget();
                return;
            }

            if (humanSeat == _currentSeat)
            {
                HandleSeatCollision(humanSeat, false);
                return;
            }

            ResetSameSeatCounter();

            if (AreNeighbours(_currentSeat, humanSeat))
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 隣の椅子検出: 現在={_currentSeat._id} (index={_currentSeat._index}), 人の座席={humanSeat._id} (index={humanSeat._index}), 距離={Mathf.Abs(_currentSeat._index - humanSeat._index)}");
                }
                HandleAdjacentOccupancy(humanSeat);
                return;
            }

            ClearLookTarget();
        }

        private void HandleSeatCollision(ShadowSeat humanSeat, bool triggeredByTouch)
        {
            if (_isMoving || _onFloor)
            {
                return;
            }

            if (_debugLogSeating)
            {
                Debug.Log($"[ShadowSeatDirector] 同じ席に人が座りました: 座席={humanSeat._id} (index={humanSeat._index})");
            }

            if (triggeredByTouch)
            {
                ResetSameSeatCounter();
            }
            else
            {
                _sameSeatCollisionCount++;
            }

            if (!triggeredByTouch && _sameSeatCollisionCount >= _sameSeatCollisionThreshold)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 同席が連続 {_sameSeatCollisionThreshold} 回発生。呆れて床に座り込みます。");
                }
                StartReaction(WaitForAnnoyedThenMoveToFloor());
                return;
            }

            var referenceSeat = humanSeat ?? _currentSeat;
            var target = FindBestSeat(reference: referenceSeat, requireGap: false, allowCurrent: false);
            if (target != null)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動先座席を選択: {target._id} (index={target._index})");
                }
                StartReaction(WaitForSurprisedThenStandupThenMove(target, humanSeat, triggeredByTouch, triggeredByTouch));
            }
            else
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動できる座席が見つかりませんでした。床に座り込みます。");
                }
                StartReaction(WaitForSurprisedThenStandupThenMove(null, humanSeat, triggeredByTouch, triggeredByTouch));
            }
        }

        private void HandleAdjacentOccupancy(ShadowSeat humanSeat)
        {
            if (_isMoving || _onFloor)
            {
                return;
            }

            ResetSameSeatCounter();

            if (_debugLogSeating)
            {
                Debug.Log($"[ShadowSeatDirector] 隣の椅子に人が座りました: 現在の座席={_currentSeat?._id} (index={_currentSeat?._index}), 人の座席={humanSeat._id} (index={humanSeat._index})");
            }

            var target = FindBestSeat(reference: humanSeat, requireGap: true, allowCurrent: false);
            if (target != null)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 1つ開けた座席を選択: {target._id} (index={target._index}, 人の座席からの距離={Mathf.Abs(target._index - humanSeat._index)})");
                }
                StartReaction(WaitForLookThenStandupThenMove(target, humanSeat));
            }
            else
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 1つ開けた座席が見つかりませんでした。移動せず、見続けます。");
                }
                SetLookTarget(humanSeat, 0f, true);
            }
        }

        private void StartReaction(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            if (_reactionRoutine != null)
            {
                StopCoroutine(_reactionRoutine);
            }

            ClearLookTarget();
            _reactionRoutine = StartCoroutine(ReactionWrapper(routine));
        }

        private IEnumerator ReactionWrapper(IEnumerator routine)
        {
            yield return StartCoroutine(routine);
            _reactionRoutine = null;
        }

        private void ResetSameSeatCounter()
        {
            _sameSeatCollisionCount = 0;
        }

        private bool IsSeatedIdle()
        {
            if (_currentSeat == null || _onFloor || _isMoving)
            {
                return false;
            }

            if (_animator == null || IsAvatarMode())
            {
                return true;
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            if (!string.IsNullOrEmpty(_animSitStateName) && stateInfo.IsName(_animSitStateName))
            {
                return true;
            }

            return stateInfo.IsName("Idle");
        }

        public void NotifySustainedTouch()
        {
            if (!CanReceiveTouch)
            {
                return;
            }

            ResetSameSeatCounter();

            var humanSeat = GetSeat(_lastActiveSeatId) ?? _currentSeat;
            var referenceSeat = _currentSeat ?? humanSeat;
            var target = FindBestSeat(reference: referenceSeat, requireGap: false, allowCurrent: false);
            StartReaction(WaitForSurprisedThenStandupThenMove(target, humanSeat, true, true));
        }

        private Transform ResolveLookTarget(ShadowSeat seat)
        {
            if (seat == null)
            {
                return null;
            }

            if (seat._lookTarget != null)
            {
                return seat._lookTarget;
            }

            return seat._anchor != null ? seat._anchor : null;
        }

        private void SetLookTarget(ShadowSeat seat, float duration, bool persist)
        {
            var target = ResolveLookTarget(seat);
            if (target == null)
            {
                ClearLookTarget();
                return;
            }

            _activeLookTarget = target;
            _keepLooking = persist;

            if (persist)
            {
                _lookUntilTime = -1f;
            }
            else
            {
                _lookUntilTime = Time.time + Mathf.Max(0f, duration);
            }
        }

        private void ClearLookTarget()
        {
            _activeLookTarget = null;
            _keepLooking = false;
            _lookUntilTime = -1f;
            _lookAtIK?.ClearTarget();
        }

        private void UpdateLookTarget()
        {
            if (_lookAtIK == null)
            {
                return;
            }

            if (!_isActive)
            {
                _lookAtIK.ClearTarget();
                return;
            }

            if (_activeLookTarget == null)
            {
                _lookAtIK.ClearTarget();
                return;
            }

            if (!_keepLooking && _lookUntilTime > 0f && Time.time > _lookUntilTime)
            {
                ClearLookTarget();
                return;
            }

            _lookAtIK.SetTarget(_activeLookTarget.position);
        }

        private void ApplyAnimatorTweaks()
        {
            if (_animator == null || IsAvatarMode())
            {
                return;
            }

            if (_disableRootMotion)
            {
                _animator.applyRootMotion = false;
            }

            if (_disableStabilizeFeet)
            {
                _animator.stabilizeFeet = false;
            }
        }

        private Vector3 GetSeatPosition(ShadowSeat seat, float offset)
        {
            if (seat == null)
            {
                return Vector3.zero;
            }

            var basePosition = seat.AnchorPosition + Vector3.up * (seat._heightOffset + _globalHeightOffset);
            if (Mathf.Abs(offset) <= Mathf.Epsilon)
            {
                return basePosition;
            }

            var rotation = seat.ResolveRotation(ShadowRoot, _flipRotation);
            var forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                forward.Normalize();
            }

            return basePosition - forward * offset;
        }
        private void MoveShadowToSeat(ShadowSeat seat, string trigger, bool force)
        {
            var arrivalTrigger = string.IsNullOrEmpty(trigger) ? _animSitTrigger : trigger;

            if (seat == null)
            {
                MoveShadowToFloor();
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
            if (_animator != null && !string.IsNullOrEmpty(_animOnFloorParam) && !IsAvatarMode())
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animOnFloorParam} = false");
                }
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

            if (_debugLogSeating)
            {
                Debug.Log($"[ShadowSeatDirector] 座席への移動開始: {seat._id} (index={seat._index}), 目標位置 = {seat.AnchorPosition}");
            }

            var targetPosition = GetSeatPosition(seat, _seatSitOffset);
            var targetRotation = seat.ResolveRotation(ShadowRoot, _flipRotation);
            var shouldStandup = !_onFloor && _currentSeat != null && _animator != null && !IsAvatarMode();

            if (_debugLogAnimations)
            {
                Debug.Log($"[ShadowSeatDirector] 座席移動開始: seat={seat._id}, idx={seat._index}, arrivalTrigger={(string.IsNullOrEmpty(arrivalTrigger) ? "(empty)" : arrivalTrigger)}, standup={shouldStandup}");
            }

            if (shouldStandup)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステート（座っている状態）から移動開始。standupアニメーション完了を待機します。");
                }
                StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, seat, arrivalTrigger));
            }
            else
            {
                StartCoroutine(MoveToSeatWithCompletion(seat, targetPosition, targetRotation, _movementDuration, arrivalTrigger));
            }
        }

        private void MoveShadowToFloor()
        {
            if (_floorAnchor == null)
            {
                return;
            }

            ClearLookTarget();

            var shouldStandup = !_onFloor && _currentSeat != null && _animator != null && !IsAvatarMode();

            foreach (var seat in _seats)
            {
                if (seat != null)
                {
                    seat._isShadowOccupied = false;
                }
            }

            _currentSeat = null;
            _onFloor = true;

            if (_animator != null && !IsAvatarMode())
            {
                if (!string.IsNullOrEmpty(_animOnFloorParam))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animOnFloorParam} = true");
                    }
                    _animator.SetBool(_animOnFloorParam, true);
                }

            }

            var targetRotation = ResolveFloorRotation();
            var targetPosition = _floorAnchor.position + Vector3.up * _globalHeightOffset;

            if (shouldStandup)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステート（座っている状態）から床へ移動開始。standupアニメーション完了を待機します。");
                }
                StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, null));
            }
            else
            {
                BeginMovement(targetPosition, targetRotation, _movementDuration);
            }
        }

        private Quaternion ResolveFloorRotation()
        {
            Quaternion rotation;

            if (_floorLookTarget != null)
            {
                var direction = _floorLookTarget.position - _floorAnchor.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
                else
                {
                    rotation = _floorAnchor != null ? _floorAnchor.rotation : Quaternion.identity;
                }
            }
            else
            {
                rotation = _floorAnchor != null ? _floorAnchor.rotation : Quaternion.identity;
            }

            if (_flipRotation)
            {
                rotation = rotation * Quaternion.Euler(0, 180, 0);
            }

            return rotation;
        }

        private void MoveShadowInstant(ShadowSeat seat)
        {
            if (seat == null)
            {
                return;
            }

            var root = ShadowRoot;
            var rotation = seat.ResolveRotation(root, _flipRotation);
            var pivotPosition = GetSeatPosition(seat, _seatPostSitOffset);
            root.rotation = rotation;
            root.position = pivotPosition;
            _currentSeat = seat;
            _onFloor = false;
            seat._isShadowOccupied = true;
            if (_animator != null && !string.IsNullOrEmpty(_animSeatIndexParam) && !IsAvatarMode())
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animSeatIndexParam} = {seat._index} (座席: {seat._id}) [即座移動]");
                }
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
                _isMoving = false;
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動完了 (即座移動): 位置 = {position}");
                }
                return;
            }

            _isMoving = true;
            var distance = Vector3.Distance(root.position, position);
            if (_useWalkingAnimation && _animator != null && !IsAvatarMode())
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動開始 (歩行アニメーション): 距離 = {distance:F2}m, 目標位置 = {position}");
                }
                _moveRoutine = StartCoroutine(WalkToTargetRoutine(root, position, rotation));
            }
            else
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動開始 (Lerp移動): 距離 = {distance:F2}m, 時間 = {duration:F2}秒, 目標位置 = {position}");
                }
                _moveRoutine = StartCoroutine(MoveRoutine(root, position, rotation, duration));
            }
        }

        private IEnumerator WalkToTargetRoutine(Transform root, Vector3 targetPosition, Quaternion targetRotation)
        {
            if (_animator != null && !string.IsNullOrEmpty(_animWalkSpeedParam) && !IsAvatarMode())
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animWalkSpeedParam} = {_walkSpeed} (歩行開始)");
                }
                _animator.SetFloat(_animWalkSpeedParam, _walkSpeed);
                if (_forceWalkStateOnMove && !string.IsNullOrEmpty(_animWalkStateName))
                {
                    _animator.CrossFadeInFixedTime(_animWalkStateName, 0.08f, 0);
                }
            }

            var startPosition = root.position;
            var horizontalTarget = new Vector3(targetPosition.x, targetPosition.y, targetPosition.z);
            var lastPosition = root.position;
            var speed = Mathf.Max(0.001f, _walkSpeed);
            var maxWalkTime = Mathf.Max(1f, Vector3.Distance(startPosition, horizontalTarget) / speed + 2f);
            var elapsed = 0f;
            var lastPlanarDistance = Vector2.Distance(new Vector2(root.position.x, root.position.z), new Vector2(horizontalTarget.x, horizontalTarget.z));
            var stagnationFrames = 0;

            while (Vector3.Distance(new Vector3(root.position.x, horizontalTarget.y, root.position.z),
                       new Vector3(horizontalTarget.x, horizontalTarget.y, horizontalTarget.z)) > _stoppingDistance)
            {
                var direction = (horizontalTarget - root.position);
                direction.y = 0f;
                var distance = direction.magnitude;

                if (distance > 0.001f)
                {
                    direction.Normalize();
                    if (_flipRotation)
                    {
                        direction = -direction;
                    }
                    var targetLookRotation = Quaternion.LookRotation(direction);
                    root.rotation = Quaternion.Slerp(root.rotation, targetLookRotation, Time.deltaTime * _walkRotationSpeed);

                    var moveDistance = speed * Time.deltaTime;
                    if (moveDistance > distance)
                    {
                        moveDistance = distance;
                    }

                    var nextPos = root.position + direction * moveDistance;
                    nextPos.y = Mathf.MoveTowards(root.position.y, targetPosition.y, speed * Time.deltaTime);
                    root.position = nextPos;
                }
                else if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] WalkToTargetRoutine: distance too small ({distance:F4}), breaking.");
                    break;
                }

                if (_animator != null && !string.IsNullOrEmpty(_animWalkSpeedParam) && !IsAvatarMode())
                {
                    var frameSpeed = Vector3.Distance(root.position, lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
                    _animator.SetFloat(_animWalkSpeedParam, frameSpeed);
                }

                lastPosition = root.position;
                elapsed += Time.deltaTime;

                var planarDistance = Vector2.Distance(new Vector2(root.position.x, root.position.z), new Vector2(horizontalTarget.x, horizontalTarget.z));
                if (planarDistance > lastPlanarDistance - 0.001f)
                {
                    stagnationFrames++;
                }
                else
                {
                    stagnationFrames = 0;
                }
                lastPlanarDistance = planarDistance;

                if (elapsed > maxWalkTime)
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] WalkToTargetRoutine: timeout reached (elapsed={elapsed:F2}s, max={maxWalkTime:F2}s, remainingDist={Vector3.Distance(root.position, horizontalTarget):F3}). Forcing arrive.");
                    }
                    break;
                }
                if (stagnationFrames > 30)
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] WalkToTargetRoutine: planar stagnation detected, breaking (planarDist={planarDistance:F3}).");
                    }
                    break;
                }

                yield return null;
            }

            root.position = horizontalTarget;
            if (_flipRotation)
            {
                targetRotation = targetRotation * Quaternion.Euler(0, 180, 0);
            }
            root.rotation = targetRotation;

            if (_debugLogAnimations)
            {
                Debug.Log($"[ShadowSeatDirector] 移動完了 (歩行アニメーション): 到達位置 = {horizontalTarget}");
            }

            if (_animator != null && !IsAvatarMode())
            {
                if (!string.IsNullOrEmpty(_animWalkSpeedParam))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animWalkSpeedParam} = 0 (歩行停止)");
                    }
                    _animator.SetFloat(_animWalkSpeedParam, 0f);
                }
                if (_onFloor && !string.IsNullOrEmpty(_animSitOnFloorTrigger))
                {
                    TriggerAnimator(_animSitOnFloorTrigger);
                }
            }

            _moveRoutine = null;
            _isMoving = false;
        }

        private IEnumerator MoveRoutine(Transform root, Vector3 position, Quaternion rotation, float duration)
        {
            if (_animator != null && !string.IsNullOrEmpty(_animWalkSpeedParam) && !IsAvatarMode())
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animWalkSpeedParam} = {_walkSpeed} (歩行開始)");
                }
                _animator.SetFloat(_animWalkSpeedParam, _walkSpeed);
                if (_forceWalkStateOnMove && !string.IsNullOrEmpty(_animWalkStateName))
                {
                    _animator.CrossFadeInFixedTime(_animWalkStateName, 0.08f, 0);
                }
            }

            var startPos = root.position;
            var startRot = root.rotation;
            var elapsed = 0f;
            var lastPos = startPos;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                t = Mathf.SmoothStep(0f, 1f, t);
                root.position = Vector3.Lerp(startPos, position, t);
                root.rotation = Quaternion.Slerp(startRot, rotation, t);

                if (_animator != null && !string.IsNullOrEmpty(_animWalkSpeedParam) && !IsAvatarMode())
                {
                    var frameSpeed = Vector3.Distance(root.position, lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
                    _animator.SetFloat(_animWalkSpeedParam, frameSpeed);
                }
                lastPos = root.position;
                yield return null;
            }

            root.position = position;
            root.rotation = rotation;

            if (_debugLogAnimations)
            {
                Debug.Log($"[ShadowSeatDirector] 移動完了 (Lerp移動): 到達位置 = {position}");
            }

            if (_animator != null && !IsAvatarMode())
            {
                if (!string.IsNullOrEmpty(_animWalkSpeedParam))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animWalkSpeedParam} = 0 (歩行停止)");
                    }
                    _animator.SetFloat(_animWalkSpeedParam, 0f);
                }
                if (_onFloor && !string.IsNullOrEmpty(_animSitOnFloorTrigger))
                {
                    TriggerAnimator(_animSitOnFloorTrigger);
                }
            }

            _moveRoutine = null;
            _isMoving = false;
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
                var beforeCount = candidates.Count();
                candidates = candidates.Where(s => Mathf.Abs(s._index - reference._index) > 1);
                if (_debugLogSeating)
                {
                    var afterCount = candidates.Count();
                    Debug.Log($"[ShadowSeatDirector] FindBestSeat: requireGap=true, 参照座席={reference._id} (index={reference._index}), 候補数={beforeCount}→{afterCount}");
                    foreach (var candidate in candidates)
                    {
                        var distance = Mathf.Abs(candidate._index - reference._index);
                        Debug.Log($"[ShadowSeatDirector]   候補: {candidate._id} (index={candidate._index}, 距離={distance})");
                    }
                }
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

            if (_debugLogAnimations)
            {
                Debug.Log($"[ShadowSeatDirector] アニメーショントリガー発火: {trigger}");
            }

            _animator.ResetTrigger(trigger);
            _animator.SetTrigger(trigger);
        }

        private void TriggerStandupAnimation()
        {
            if (_animator == null || IsAvatarMode())
            {
                return;
            }

            if (string.IsNullOrEmpty(_animStandupTrigger))
            {
                return;
            }

            if (IsInStandupState())
            {
                return;
            }

            TriggerAnimator(_animStandupTrigger);
        }

        private bool IsInStandupState()
        {
            if (_animator == null)
            {
                return false;
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

            if (string.IsNullOrEmpty(_animStandupStateName))
            {
                return false;
            }

            var hash = Animator.StringToHash(_animStandupStateName);
            return stateInfo.shortNameHash == hash || stateInfo.fullPathHash == hash || stateInfo.IsName(_animStandupStateName);
        }

        private bool IsInState(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName))
            {
                return false;
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName(stateName);
        }

        private IEnumerator WaitForStandupAnimationThenMove(Vector3 targetPosition, Quaternion targetRotation, float duration, ShadowSeat targetSeat = null, string arrivalTrigger = null)
        {
            var canPlayStandup = _animator != null && !IsAvatarMode() && !string.IsNullOrEmpty(_animStandupStateName) && !string.IsNullOrEmpty(_animStandupTrigger);

            if (!canPlayStandup)
            {
                if (targetSeat != null)
                {
                    StartCoroutine(MoveToSeatWithCompletion(targetSeat, targetPosition, targetRotation, duration, arrivalTrigger));
                }
                else
                {
                    BeginMovement(targetPosition, targetRotation, duration);
                }
                yield break;
            }

            TriggerStandupAnimation();
            yield return null;

            var maxWaitTime = 5.0f;
            var elapsedTime = 0f;
            var standupStarted = false;

            while (elapsedTime < maxWaitTime)
            {
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

                var isStandup = IsInStandupState();

                if (!standupStarted && isStandup)
                {
                    standupStarted = true;
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] standupアニメーション開始を検出");
                    }
                }

                if (standupStarted && isStandup)
                {
                    if (!stateInfo.loop && stateInfo.normalizedTime >= 0.99f)
                    {
                        if (_debugLogAnimations)
                        {
                            Debug.Log($"[ShadowSeatDirector] standupアニメーション完了。移動を開始します。");
                        }
                        break;
                    }
                }
                else if (standupStarted && !isStandup)
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] standupアニメーションから遷移しました。移動を開始します。");
                    }
                    break;
                }

                elapsedTime += Time.deltaTime;
                yield return null;
            }

            if (elapsedTime >= maxWaitTime)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] standupアニメーション待機タイムアウト。移動を開始します。");
                }
            }

            if (_animator != null && !string.IsNullOrEmpty(_animStandupTrigger))
            {
                _animator.ResetTrigger(_animStandupTrigger);
            }

            if (targetSeat != null)
            {
                StartCoroutine(MoveToSeatWithCompletion(targetSeat, targetPosition, targetRotation, duration, arrivalTrigger));
            }
            else
            {
                BeginMovement(targetPosition, targetRotation, duration);
            }
        }

        private IEnumerator MoveToSeatWithCompletion(ShadowSeat seat, Vector3 targetPosition, Quaternion targetRotation, float duration, string arrivalTrigger = null)
        {
            if (_debugLogAnimations)
            {
                Debug.Log($"[ShadowSeatDirector] MoveToSeatWithCompletion start: seat={seat?._id ?? "null"}, trigger={(string.IsNullOrEmpty(arrivalTrigger) ? "(empty)" : arrivalTrigger)}");
            }

            BeginMovement(targetPosition, targetRotation, duration);

            while (_isMoving)
            {
                yield return null;
            }

            if (_debugLogAnimations)
            {
                Debug.Log("[ShadowSeatDirector] MoveToSeatWithCompletion: movement finished, finalizing position/trigger");
            }

            yield return null;
            yield return null;

            if (seat != null)
            {
                var root = ShadowRoot;
                var correctedPosition = GetSeatPosition(seat, _seatSitOffset);
                var correctedRotation = seat.ResolveRotation(root, _flipRotation);

                root.rotation = correctedRotation;
                root.position = correctedPosition;

                _currentSeat = seat;
                if (_animator != null && !string.IsNullOrEmpty(_animSeatIndexParam) && !IsAvatarMode())
                {
                    if (_debugLogSeating)
                    {
                        Debug.Log($"[ShadowSeatDirector] 座席への移動完了: {seat._id} (index={seat._index}), 最終位置 = {root.position}");
                    }
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] アニメーションパラメータ変更: {_animSeatIndexParam} = {seat._index} (座席: {seat._id})");
                    }
                    _animator.SetInteger(_animSeatIndexParam, seat._index);
                }
                var triggerToFire = string.IsNullOrEmpty(arrivalTrigger) ? _animSitTrigger : arrivalTrigger;
                if (_animator != null && !IsAvatarMode() && !string.IsNullOrEmpty(triggerToFire))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] 座席到着: トリガー発火 {triggerToFire} (seat={seat._id}, index={seat._index})");
                    }
                    TriggerAnimator(triggerToFire);
                    if (!string.IsNullOrEmpty(_animSitStateName))
                    {
                        yield return StartCoroutine(EnsureStateOrCrossFade(_animSitStateName, 0.5f, 0.08f));
                    }
                }
                else if (_debugLogAnimations)
                {
                    var reason = IsAvatarMode()
                        ? "AvatarModeでスキップ"
                        : (_animator == null ? "Animator未設定" : "arrivalTriggerが空");
                    Debug.Log($"[ShadowSeatDirector] 座席到着: トリガー未発火 ({reason}, seat={seat._id}, index={seat._index})");
                }
            }
        }

        private IEnumerator EnsureStateOrCrossFade(string stateName, float timeoutSeconds, float crossFadeSeconds)
        {
            if (_animator == null || IsAvatarMode() || string.IsNullOrEmpty(stateName))
            {
                yield break;
            }

            yield return null;
            yield return null;

            var elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName(stateName))
                {
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            _animator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, crossFadeSeconds), 0);
        }

        private IEnumerator WaitForLookThenStandupThenMove(ShadowSeat targetSeat, ShadowSeat humanSeat)
        {
            if (humanSeat != null)
            {
                SetLookTarget(humanSeat, _lookDuration, false);
                if (_lookDuration > 0f)
                {
                    yield return new WaitForSeconds(_lookDuration);
                }
            }

            yield return StartCoroutine(WaitForStandupAnimationThenMoveInternal(targetSeat));
            yield return StartCoroutine(WaitForMovementCompletion());

            ClearLookTarget();
        }

        private IEnumerator WaitForSurprisedThenStandupThenMove(ShadowSeat targetSeat, ShadowSeat humanSeat, bool lookFromStart, bool checkTouchAfterMove)
        {
            if (lookFromStart && humanSeat != null)
            {
                SetLookTarget(humanSeat, 0f, true);
            }

            if (_animator == null || IsAvatarMode())
            {
                if (targetSeat != null)
                {
                    MoveShadowToSeat(targetSeat, _animSitTrigger, true);
                }
                else
                {
                    MoveShadowToFloor();
                }
                yield break;
            }

            if (_debugLogAnimations)
            {
                Debug.Log("[ShadowSeatDirector] Surprisedアニメーションを開始");
            }
            TriggerAnimator(_animSurprisedTrigger);

            yield return StartCoroutine(WaitForAnimationState("Surprised", 5.0f));

            yield return StartCoroutine(WaitForStandupAnimationThenMoveInternal(targetSeat));
            yield return StartCoroutine(WaitForMovementCompletion());

            if (humanSeat != null)
            {
                SetLookTarget(humanSeat, _postMoveLookDuration, false);
            }
            else
            {
                ClearLookTarget();
            }

            if (checkTouchAfterMove && _touchResponder != null && _touchResponder.IsTouching && !IsAvatarMode())
            {
                TriggerAnimator(_animScareTrigger);
            }
        }

        private IEnumerator WaitForStandupAnimationThenMoveInternal(ShadowSeat targetSeat)
        {
            if (targetSeat != null)
            {
                var targetPosition = GetSeatPosition(targetSeat, _seatSitOffset);
                var targetRotation = targetSeat.ResolveRotation(ShadowRoot, _flipRotation);
                yield return StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, targetSeat, _animSitTrigger));
            }
            else
            {
                var targetRotation = ResolveFloorRotation();
                var targetPosition = _floorAnchor.position + Vector3.up * _globalHeightOffset;
                yield return StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, null, null));
            }
        }

        private IEnumerator WaitForMovementCompletion()
        {
            while (_isMoving)
            {
                yield return null;
            }

            yield return null;
            yield return null;
        }

        private IEnumerator WaitForAnimationState(string stateName, float maxWaitTime)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName))
            {
                yield break;
            }

            var elapsedTime = 0f;
            var animationStarted = false;

            while (elapsedTime < maxWaitTime)
            {
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

                if (!animationStarted && stateInfo.IsName(stateName))
                {
                    animationStarted = true;
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] {stateName}アニメーション開始を検出");
                    }
                }

                if (animationStarted && stateInfo.IsName(stateName))
                {
                    if (stateInfo.normalizedTime >= 0.99f)
                    {
                        if (_debugLogAnimations)
                        {
                            Debug.Log($"[ShadowSeatDirector] {stateName}アニメーション完了");
                        }
                        break;
                    }
                }
                else if (animationStarted && !stateInfo.IsName(stateName))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] {stateName}アニメーションから遷移しました");
                    }
                    break;
                }

                elapsedTime += Time.deltaTime;
                yield return null;
            }

            if (elapsedTime >= maxWaitTime)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] {stateName}アニメーション待機タイムアウト");
                }
            }
        }

        private IEnumerator WaitForAnnoyedThenMoveToFloor()
        {
            if (_floorAnchor == null)
            {
                yield break;
            }

            if (_animator == null || IsAvatarMode())
            {
                MoveShadowToFloor();
                yield break;
            }

            ClearLookTarget();

            if (_debugLogAnimations)
            {
                Debug.Log("[ShadowSeatDirector] Frustratedアニメーションを開始");
            }
            TriggerAnimator(_animFrustratedTrigger);

            yield return StartCoroutine(WaitForAnimationState("Frustrated", 5.0f));

            var targetRotation = ResolveFloorRotation();
            yield return StartCoroutine(WaitForStandupAnimationThenMove(_floorAnchor.position, targetRotation, _movementDuration, null));
            yield return StartCoroutine(WaitForMovementCompletion());
        }
    }
}
