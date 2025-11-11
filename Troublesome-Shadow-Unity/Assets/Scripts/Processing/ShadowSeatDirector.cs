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
    /// - Moves to a distant empty seat when a human sits in the same or adjacent seat.
    /// - Drops to the floor anchor when no empty seat remains.
    /// - Returns to the preferred seat when humans leave and periodically glares at nearby guests.
    /// </summary>
    public class ShadowSeatDirector : MonoBehaviour
    {
        [Header("References")]
        public Transform _shadowRoot;
        public AvatarController _avatarController;
        public Animator _animator;
        public InteractionModeCoordinator _modeCoordinator;

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
        public string _animSurprisedTrigger = "Surprised";
        public string _animGlareTrigger = "Glare";
        public string _animFrustratedTrigger = "Frustrated";
        public string _animSitTrigger = "Sit";
        public string _animSitOnFloorTrigger = "SitOnFloor";
        public string _animStandupStateName = "standup";
        public string _animSitStateName = "Sit";

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
        private bool _onFloor;
        private float _lastGlareTime = -999f;
        private bool _isMoving;
        
        private string _lastActiveSeatId;
        private Dictionary<string, bool> _lastOccupancy = new Dictionary<string, bool>();

        public bool IsMoving => _isMoving;

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
            BuildSeatLookup();
            SnapToDefaultSeat();
            InitializeDebugOccupancy();
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

            if (!HasSeatingChanged(snapshot))
            {
                return;
            }

            UpdateOccupancy(snapshot);
            EvaluateShadowResponse(snapshot);
            
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
            var allHumanOccupied = _seats.Count > 0 && _seats.All(s => s != null && s._isHumanOccupied);

            if (allHumanOccupied)
            {
                StartCoroutine(WaitForFrustratedThenMoveToFloor());
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
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 隣の椅子検出: 現在={_currentSeat._id} (index={_currentSeat._index}), 人の座席={humanSeat._id} (index={humanSeat._index}), 距離={Mathf.Abs(_currentSeat._index - humanSeat._index)}");
                }
                HandleAdjacentOccupancy(humanSeat);
                return;
            }

            if (_currentSeat != null && humanSeat != _currentSeat && !_onFloor)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 別の席に人が座りました: 現在の座席={_currentSeat._id} (index={_currentSeat._index}), 人の座席={humanSeat._id} (index={humanSeat._index})");
                }
                HandleOtherSeatOccupancy(humanSeat);
                return;
            }

            if (!_onFloor && humanSeat != null)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] その他の場合: Glareアニメーションのみ再生します。");
                }
                StartCoroutine(WaitForGlareOnly());
            }
        }

        private void HandleSeatCollision(ShadowSeat humanSeat)
        {
            if (_debugLogSeating)
            {
                Debug.Log($"[ShadowSeatDirector] 同じ席に人が座りました: 座席={humanSeat._id} (index={humanSeat._index})");
            }

            var allHumanOccupied = _seats.Count > 0 && _seats.All(s => s != null && s._isHumanOccupied);
            
            if (allHumanOccupied)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 全席埋まっています。床に座り込みます。");
                }
                StartCoroutine(WaitForSurprisedThenStandupThenMove(null));
                return;
            }

            var target = FindBestSeat(reference: humanSeat, requireGap: false, allowCurrent: false);
            if (target != null)
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動先座席を選択: {target._id} (index={target._index})");
                }
                StartCoroutine(WaitForSurprisedThenStandupThenMove(target));
            }
            else
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 移動できる座席が見つかりませんでした。床に座り込みます。");
                }
                StartCoroutine(WaitForSurprisedThenStandupThenMove(null));
            }
        }

        private void HandleAdjacentOccupancy(ShadowSeat humanSeat)
        {
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
                StartCoroutine(WaitForGlareThenStandupThenMove(target));
            }
            else
            {
                if (_debugLogSeating)
                {
                    Debug.Log($"[ShadowSeatDirector] 1つ開けた座席が見つかりませんでした。移動せず、Glareのみ再生します。");
                }
                StartCoroutine(WaitForGlareOnly());
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
            if (!IsAvatarMode())
            {
            TriggerAnimator(_animGlareTrigger);
            }
            RotateTowardsSeat(seat);
        }

        private void RotateTowardsSeat(ShadowSeat seat)
        {
            if (seat == null)
            {
                return;
            }

            var root = ShadowRoot;
            var rotation = seat.ResolveRotation(root, _flipRotation);
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

            if (!IsAvatarMode())
            {
            TriggerAnimator(trigger);
            }
            var targetPosition = seat.AnchorPosition + Vector3.up * seat._heightOffset;
            var targetRotation = seat.ResolveRotation(ShadowRoot, _flipRotation);
            
            if (_animator != null && !IsAvatarMode() && IsInState("Idle") && _currentSeat != null)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステート（座っている状態）から移動開始。standupアニメーション完了を待機します。");
                }
                StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, seat));
            }
            else
            {
                StartCoroutine(MoveToSeatWithCompletion(seat, targetPosition, targetRotation, _movementDuration));
            }
        }

        private void MoveShadowToFloor()
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
            
            if (_animator != null && !IsAvatarMode() && IsInState("Idle") && _currentSeat != null)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステート（座っている状態）から床へ移動開始。standupアニメーション完了を待機します。");
                }
                StartCoroutine(WaitForStandupAnimationThenMove(_floorAnchor.position, targetRotation, _movementDuration, null));
            }
            else
            {
            BeginMovement(_floorAnchor.position, targetRotation, _movementDuration);
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
            root.position = seat.AnchorPosition + Vector3.up * seat._heightOffset;
            root.rotation = seat.ResolveRotation(root, _flipRotation);
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
            }

            var startPosition = root.position;
            var horizontalTarget = new Vector3(targetPosition.x, root.position.y, targetPosition.z);

            while (Vector3.Distance(root.position, horizontalTarget) > _stoppingDistance)
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

                    var moveDistance = _walkSpeed * Time.deltaTime;
                    if (moveDistance > distance)
                    {
                        moveDistance = distance;
                    }

                    root.position += direction * moveDistance;
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
                else if (!string.IsNullOrEmpty(_animSitTrigger))
                {
                    TriggerAnimator(_animSitTrigger);
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
            }

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
                else if (!string.IsNullOrEmpty(_animSitTrigger))
                {
                    TriggerAnimator(_animSitTrigger);
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

        private bool IsInState(string stateName)
        {
            if (_animator == null || string.IsNullOrEmpty(stateName))
            {
                return false;
            }

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.IsName(stateName);
        }

        private IEnumerator WaitForStandupAnimationThenMove(Vector3 targetPosition, Quaternion targetRotation, float duration, ShadowSeat targetSeat = null)
        {
            if (_animator == null || string.IsNullOrEmpty(_animStandupStateName))
            {
                if (targetSeat != null)
                {
                    StartCoroutine(MoveToSeatWithCompletion(targetSeat, targetPosition, targetRotation, duration));
                }
                else
                {
                    BeginMovement(targetPosition, targetRotation, duration);
                }
                yield break;
            }

            var maxWaitTime = 5.0f;
            var elapsedTime = 0f;
            var standupStarted = false;

            while (elapsedTime < maxWaitTime)
            {
                var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                
                if (!standupStarted && stateInfo.IsName(_animStandupStateName))
                {
                    standupStarted = true;
                    if (_debugLogAnimations)
                    {
                        Debug.Log($"[ShadowSeatDirector] standupアニメーション開始を検出");
                    }
                }

                if (standupStarted && stateInfo.IsName(_animStandupStateName))
                {
                    if (stateInfo.normalizedTime >= 0.99f)
                    {
                        if (_debugLogAnimations)
                        {
                            Debug.Log($"[ShadowSeatDirector] standupアニメーション完了。移動を開始します。");
                        }
                        break;
                    }
                }
                else if (standupStarted && !stateInfo.IsName(_animStandupStateName))
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

            if (targetSeat != null)
            {
                StartCoroutine(MoveToSeatWithCompletion(targetSeat, targetPosition, targetRotation, duration));
            }
            else
            {
                BeginMovement(targetPosition, targetRotation, duration);
            }
        }

        private IEnumerator MoveToSeatWithCompletion(ShadowSeat seat, Vector3 targetPosition, Quaternion targetRotation, float duration)
        {
            BeginMovement(targetPosition, targetRotation, duration);

            while (_isMoving)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            if (seat != null)
            {
                var root = ShadowRoot;
                var correctedPosition = seat.AnchorPosition + Vector3.up * seat._heightOffset;
                var correctedRotation = seat.ResolveRotation(root, _flipRotation);

                root.position = correctedPosition;
                root.rotation = correctedRotation;

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
            }
        }

        private void HandleOtherSeatOccupancy(ShadowSeat humanSeat)
        {
            if (_debugLogSeating)
            {
                Debug.Log($"[ShadowSeatDirector] 別の席に人が座りました。移動せず、Glareのみ再生します。");
            }
            StartCoroutine(WaitForGlareOnly());
        }

        private IEnumerator WaitForGlareThenStandupThenMove(ShadowSeat targetSeat)
        {
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
                Debug.Log("[ShadowSeatDirector] Glareアニメーションを開始");
            }
            TriggerAnimator(_animGlareTrigger);

            yield return StartCoroutine(WaitForAnimationState("Glare", 5.0f));

            yield return StartCoroutine(WaitForIdleState(0.1f));

            if (_animator != null && !IsAvatarMode() && IsInState("Idle") && _currentSeat != null)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステートに戻りました。standupアニメーションを開始します。");
                }
                yield return StartCoroutine(WaitForStandupAnimationThenMoveInternal(targetSeat));
            }
            else
            {
                yield return StartCoroutine(WaitForStandupAnimationThenMoveInternal(targetSeat));
            }
        }

        private IEnumerator WaitForGlareOnly()
        {
            if (_animator == null || IsAvatarMode())
            {
                yield break;
            }

            if (_debugLogAnimations)
            {
                Debug.Log("[ShadowSeatDirector] Glareアニメーションのみ再生（移動なし）");
            }
            TriggerAnimator(_animGlareTrigger);

            yield return StartCoroutine(WaitForAnimationState("Glare", 5.0f));
        }

        private IEnumerator WaitForSurprisedThenStandupThenMove(ShadowSeat targetSeat)
        {
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
        }

        private IEnumerator WaitForStandupAnimationThenMoveInternal(ShadowSeat targetSeat)
        {
            if (targetSeat != null)
            {
                var targetPosition = targetSeat.AnchorPosition + Vector3.up * targetSeat._heightOffset;
                var targetRotation = targetSeat.ResolveRotation(ShadowRoot, _flipRotation);
                yield return StartCoroutine(WaitForStandupAnimationThenMove(targetPosition, targetRotation, _movementDuration, targetSeat));
            }
            else
            {
                var targetRotation = ResolveFloorRotation();
                yield return StartCoroutine(WaitForStandupAnimationThenMove(_floorAnchor.position, targetRotation, _movementDuration, null));
            }
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

        private IEnumerator WaitForFrustratedThenMoveToFloor()
        {
            if (_animator == null || IsAvatarMode())
            {
                MoveShadowToFloor();
                yield break;
            }

            if (_debugLogAnimations)
            {
                Debug.Log("[ShadowSeatDirector] Frustratedアニメーションを開始");
            }
            TriggerAnimator(_animFrustratedTrigger);

            yield return StartCoroutine(WaitForAnimationState("Frustrated", 5.0f));

            if (_animator != null && !IsAvatarMode() && IsInState("Idle") && _currentSeat != null)
            {
                if (_debugLogAnimations)
                {
                    Debug.Log($"[ShadowSeatDirector] Idleステート（座っている状態）から床へ移動開始。standupアニメーション完了を待機します。");
                }
                var targetRotation = ResolveFloorRotation();
                yield return StartCoroutine(WaitForStandupAnimationThenMove(_floorAnchor.position, targetRotation, _movementDuration, null));
            }
            else
            {
                MoveShadowToFloor();
            }
        }

        private IEnumerator WaitForIdleState(float maxWaitTime)
        {
            if (_animator == null)
            {
                yield break;
            }

            var elapsedTime = 0f;
            while (elapsedTime < maxWaitTime)
            {
                if (IsInState("Idle"))
                {
                    if (_debugLogAnimations)
                    {
                        Debug.Log("[ShadowSeatDirector] Idleステートに遷移しました");
                    }
                    yield break;
                }
                elapsedTime += Time.deltaTime;
                yield return null;
            }
        }
    }
}

