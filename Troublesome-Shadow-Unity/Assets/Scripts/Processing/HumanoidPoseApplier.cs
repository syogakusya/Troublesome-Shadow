using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;
using Newtonsoft.Json.Linq;

namespace PoseRuntime
{
    /// <summary>
    /// Retargets MediaPipeベースの骨格データをUnity Humanoidアバターへ適用するシンプルなアダプタ。
    /// AvatarControllerのSampleProcessedイベントを購読し、Animatorに対してボーン回転とルート姿勢を更新します。
    /// </summary>
    public class HumanoidPoseApplier : MonoBehaviour
    {
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("animator")] public Animator _animator;
        [FormerlySerializedAs("updateRoot")] public bool _updateRootTransform = true;
        [FormerlySerializedAs("rootPositionOffset")] public Vector3 _rootPositionOffset = Vector3.zero;
        [FormerlySerializedAs("rootRotationOffset")] public Vector3 _rootRotationOffset = Vector3.zero;
        [FormerlySerializedAs("positionLerp")] public float _positionLerp = 12f;
        [FormerlySerializedAs("rotationLerp")] public float _rotationLerp = 15f;
        [FormerlySerializedAs("useMetadataRootTranslation")] public bool _useMetadataRootTranslation = true;
        [FormerlySerializedAs("metadataRootKey")] public string _metadataRootKey = "root_center_normalized";
        [FormerlySerializedAs("metadataRootScale")] public Vector3 _metadataRootScale = new Vector3(4f, 0f, -4f);
        [FormerlySerializedAs("metadataRootOffset")] public Vector3 _metadataRootOffset = Vector3.zero;
        [FormerlySerializedAs("metadataRootAxisBlend")] public Vector3 _metadataRootAxisBlend = new Vector3(1f, 0f, 1f);
        [FormerlySerializedAs("metadataRootUseDeltaFromStart")] public bool _metadataRootUseDeltaFromStart = true;
        [FormerlySerializedAs("useAdvancedHeadOrientation")] public bool _useAdvancedHeadOrientation = true;
        [FormerlySerializedAs("headForwardJoint")] public string _headForwardJoint = "NOSE";
        [FormerlySerializedAs("headLeftJoint")] public string _headLeftJoint = "LEFT_EAR";
        [FormerlySerializedAs("headRightJoint")] public string _headRightJoint = "RIGHT_EAR";
        [FormerlySerializedAs("headBaseJoint")] public string _headBaseJoint = "NECK";
        [FormerlySerializedAs("headAxisFlip")] public Vector3 _headAxisFlip = Vector3.one;
        [FormerlySerializedAs("useAdvancedTorsoOrientation")] public bool _useAdvancedTorsoOrientation = true;
        [FormerlySerializedAs("torsoAxisFlip")] public Vector3 _torsoAxisFlip = Vector3.one;
        [FormerlySerializedAs("autoPopulate")] public bool _autoPopulate = true;
        [FormerlySerializedAs("debugLogging")] public bool _debugLogging = false;

        [Serializable]
        public class HumanoidBoneMapping
        {
            [FormerlySerializedAs("startJoint")] public string _startJoint = string.Empty;
            [FormerlySerializedAs("endJoint")] public string _endJoint = string.Empty;
            [FormerlySerializedAs("bone")] public HumanBodyBones _bone = HumanBodyBones.LastBone;
            [FormerlySerializedAs("rotationOffset")] public Vector3 _rotationOffset = Vector3.zero;

            [NonSerialized] public Quaternion _restRotation = Quaternion.identity;
            [NonSerialized] public Vector3 _restDirection = Vector3.forward;
            [NonSerialized] public bool _hasRestDirection;
            [NonSerialized] public Vector3 _lastDirection = Vector3.forward;
            [NonSerialized] public bool _hasDirection;
        }

        [FormerlySerializedAs("boneMappings")] public List<HumanoidBoneMapping> _boneMappings = new List<HumanoidBoneMapping>();

        private readonly Dictionary<string, JointSample> _jointLookup = new Dictionary<string, JointSample>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _lastKnownJointPositions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _restJointPositions = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        private bool _hasSample;
        private readonly HashSet<string> _missingJointWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<HumanBodyBones> _missingBoneWarnings = new HashSet<HumanBodyBones>();
        private int _debugFrameCounter;
        private Quaternion _rootRestRotation = Quaternion.identity;
        private Vector3 _rootRestForward = Vector3.forward;
        private Vector3 _rootRestUp = Vector3.up;
        private Vector3 _rootRestRight = Vector3.right;
        private Vector3 _metadataRootPosition = Vector3.zero;
        private Vector3 _metadataRootOrigin = Vector3.zero;
        private bool _metadataRootOriginCaptured;
        private bool _hasMetadataRootPosition;

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponent<AvatarController>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_animator != null)
            {
                _rootRestRotation = _animator.transform.rotation;
                _rootRestForward = _animator.transform.forward;
                _rootRestUp = _animator.transform.up;
                _rootRestRight = _animator.transform.right;
            }

            if (_autoPopulate && _boneMappings.Count == 0)
            {
                PopulateDefaultMappings();
            }

            InitializeAutoRotationOffsets();

            ResetMetadataRoot();
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.SampleProcessed += OnSampleProcessed;
            }
            if (_debugLogging)
            {
                Debug.Log("HumanoidPoseApplier enabled");
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.SampleProcessed -= OnSampleProcessed;
            }
            _hasSample = false;
            ResetMetadataRoot();
            if (_debugLogging)
            {
                Debug.Log("HumanoidPoseApplier disabled");
            }
        }

        private void OnSampleProcessed(SkeletonSample sample)
        {
            _jointLookup.Clear();
            foreach (var joint in sample._joints)
            {
                _jointLookup[joint._name] = joint;
                _lastKnownJointPositions[joint._name] = joint._position;
                _missingJointWarnings.Remove(joint._name);
            }

            ExtractMetadataRoot(sample);

            _hasSample = true;
            _debugFrameCounter++;
            if (_debugLogging && _debugFrameCounter >= 15)
            {
                _debugFrameCounter = 0;
                Debug.Log($"HumanoidPoseApplier received sample with {sample._joints.Count} joints (timestamp {sample._timestamp})");
            }
        }

        private void LateUpdate()
        {
            if (!_hasSample || _animator == null)
            {
                return;
            }

            if (_updateRootTransform)
            {
                UpdateRootTransform();
            }

            foreach (var mapping in _boneMappings)
            {
                if (mapping._bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                if (!TryGetJointPosition(mapping._startJoint, out var start))
                {
                    continue;
                }

                var hasEnd = TryGetJointPosition(mapping._endJoint, out var end);
                var usedRestFallback = false;
                if (!hasEnd)
                {
                    if (mapping._hasRestDirection)
                    {
                        end = start + mapping._restDirection;
                        usedRestFallback = true;
                    }
                    else if (mapping._hasDirection)
                    {
                        end = start + mapping._lastDirection;
                    }
                    else
                    {
                        if (_debugLogging)
                        {
                            Debug.LogWarning($"HumanoidPoseApplier: joint {mapping._endJoint} missing and no rest direction cached");
                        }
                        continue;
                    }
                }

                var boneTransform = _animator.GetBoneTransform(mapping._bone);
                if (boneTransform == null)
                {
                    if (_debugLogging && _missingBoneWarnings.Add(mapping._bone))
                    {
                        Debug.LogWarning($"HumanoidPoseApplier: bone transform missing for {mapping._bone}");
                    }
                    continue;
                }

                if (mapping._bone == HumanBodyBones.Head && TryApplyAdvancedHeadOrientation(mapping, boneTransform))
                {
                    continue;
                }

                if (IsTorsoBone(mapping._bone) && TryApplyAdvancedTorsoOrientation(mapping, boneTransform))
                {
                    continue;
                }

                var direction = end - start;
                Vector3 usableDirection;
                if (direction.sqrMagnitude > 1e-6f)
                {
                    usableDirection = direction.normalized;
                    if (!usedRestFallback)
                    {
                        mapping._lastDirection = usableDirection;
                        mapping._hasDirection = true;
                    }
                }
                else if (mapping._hasRestDirection)
                {
                    usableDirection = mapping._restDirection;
                    usedRestFallback = true;
                }
                else if (mapping._hasDirection)
                {
                    usableDirection = mapping._lastDirection;
                }
                else
                {
                    if (_debugLogging)
                    {
                        Debug.LogWarning($"HumanoidPoseApplier: zero-length direction for {mapping._startJoint}->{mapping._endJoint}");
                    }
                    continue;
                }

                if (usedRestFallback && !string.IsNullOrEmpty(mapping._endJoint) && mapping._hasRestDirection)
                {
                    _lastKnownJointPositions[mapping._endJoint] = start + mapping._restDirection;
                }

                if (usableDirection.sqrMagnitude < 1e-6f || !mapping._hasRestDirection)
                {
                    continue;
                }

                var rotationDelta = Quaternion.FromToRotation(mapping._restDirection, usableDirection.normalized);
                var targetRotation = rotationDelta * mapping._restRotation * Quaternion.Euler(mapping._rotationOffset);

                boneTransform.rotation = Quaternion.Slerp(
                    boneTransform.rotation,
                    targetRotation,
                    Time.deltaTime * _rotationLerp);
                if (_debugLogging)
                {
                    Debug.Log($"HumanoidPoseApplier applied rotation to {mapping._bone}: {targetRotation.eulerAngles}");
                }
            }
        }

        private bool TryGetJointPosition(string jointName, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(jointName))
            {
                return false;
            }

            if (_jointLookup.TryGetValue(jointName, out var joint))
            {
                position = joint._position;
                _lastKnownJointPositions[jointName] = position;
                return true;
            }

            switch (jointName.ToUpperInvariant())
            {
                case "NECK":
                    if (TryAverage(new[] { "LEFT_SHOULDER", "RIGHT_SHOULDER" }, out position))
                    {
                        _lastKnownJointPositions[jointName] = position;
                        return true;
                    }
                    break;
                case "CHEST":
                    if (TryAverage(new[] { "LEFT_SHOULDER", "RIGHT_SHOULDER", "LEFT_HIP", "RIGHT_HIP" }, out position))
                    {
                        _lastKnownJointPositions[jointName] = position;
                        return true;
                    }
                    break;
                case "PELVIS":
                case "HIP_CENTER":
                    if (TryAverage(new[] { "LEFT_HIP", "RIGHT_HIP" }, out position))
                    {
                        _lastKnownJointPositions[jointName] = position;
                        return true;
                    }
                    break;
            }

            if (_restJointPositions.TryGetValue(jointName, out position))
            {
                _lastKnownJointPositions[jointName] = position;
                return true;
            }

            if (_lastKnownJointPositions.TryGetValue(jointName, out position))
            {
                return true;
            }

            if (_debugLogging && _missingJointWarnings.Add(jointName))
            {
                Debug.LogWarning($"HumanoidPoseApplier: joint {jointName} not found in incoming data");
            }

            return false;
        }

        private bool TryAverage(IReadOnlyList<string> joints, out Vector3 position)
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var name in joints)
            {
                if (_jointLookup.TryGetValue(name, out var joint))
                {
                    sum += joint._position;
                    count++;
                }
            }

            if (count > 0)
            {
                position = sum / count;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        private void ExtractMetadataRoot(SkeletonSample sample)
        {
            if (!_updateRootTransform || !_useMetadataRootTranslation)
            {
                return;
            }

            if (sample?.Meta == null)
            {
                return;
            }

            if (!sample.Meta.TryGetValue(_metadataRootKey, out var rawValue))
            {
                return;
            }

            if (!TryParseMetadataVector3(rawValue, out var metadataRoot))
            {
                return;
            }

            if (_metadataRootUseDeltaFromStart)
            {
                if (!_metadataRootOriginCaptured)
                {
                    _metadataRootOrigin = metadataRoot;
                    _metadataRootOriginCaptured = true;
                }

                metadataRoot -= _metadataRootOrigin;
            }

            _metadataRootPosition = Vector3.Scale(metadataRoot, _metadataRootScale) + _metadataRootOffset;
            _hasMetadataRootPosition = true;
        }

        private bool TryParseMetadataVector3(object rawValue, out Vector3 vector)
        {
            switch (rawValue)
            {
                case null:
                    vector = Vector3.zero;
                    return false;
                case JObject jObject:
                    vector = new Vector3(
                        ReadFloat(jObject, "x"),
                        ReadFloat(jObject, "y"),
                        ReadFloat(jObject, "z"));
                    return true;
                case JArray jArray:
                    vector = new Vector3(
                        ReadFloat(jArray, 0),
                        ReadFloat(jArray, 1),
                        ReadFloat(jArray, 2));
                    return true;
                case IDictionary<string, object> dictionary:
                    return TryParseFromDictionary(dictionary, out vector);
                case IList list:
                    vector = new Vector3(
                        ReadFloat(list, 0),
                        ReadFloat(list, 1),
                        ReadFloat(list, 2));
                    return true;
                case Vector3 vector3:
                    vector = vector3;
                    return true;
                case double d:
                    vector = new Vector3((float)d, 0f, 0f);
                    return true;
                case float f:
                    vector = new Vector3(f, 0f, 0f);
                    return true;
                case long l:
                    vector = new Vector3(l, 0f, 0f);
                    return true;
                case int i:
                    vector = new Vector3(i, 0f, 0f);
                    return true;
                case JValue jValue:
                    return TryParseMetadataVector3(jValue.Value, out vector);
            }

            if (float.TryParse(rawValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                vector = new Vector3(parsed, 0f, 0f);
                return true;
            }

            vector = Vector3.zero;
            return false;
        }

        private bool TryParseFromDictionary(IDictionary<string, object> dictionary, out Vector3 vector)
        {
            var hasX = TryGetFloat(dictionary, "x", out var x);
            var hasY = TryGetFloat(dictionary, "y", out var y);
            var hasZ = TryGetFloat(dictionary, "z", out var z);

            if (!(hasX || hasY || hasZ))
            {
                vector = Vector3.zero;
                return false;
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        private static bool TryGetFloat(IDictionary<string, object> dictionary, string key, out float value)
        {
            value = 0f;
            if (dictionary == null)
            {
                return false;
            }

            if (!dictionary.TryGetValue(key, out var rawValue))
            {
                var lower = key.ToLowerInvariant();
                var upper = key.ToUpperInvariant();
                if (!dictionary.TryGetValue(lower, out rawValue) && !dictionary.TryGetValue(upper, out rawValue))
                {
                    return false;
                }
            }

            value = ConvertToSingle(rawValue);
            return true;
        }

        private static float ReadFloat(IList list, int index)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return 0f;
            }

            return ConvertToSingle(list[index]);
        }

        private static float ReadFloat(JArray array, int index)
        {
            if (array == null || index < 0 || index >= array.Count)
            {
                return 0f;
            }

            var token = array[index];
            return token != null ? (float)token.Value<double>() : 0f;
        }

        private static float ReadFloat(JObject obj, string key)
        {
            if (obj == null)
            {
                return 0f;
            }

            var token = obj.GetValue(key, StringComparison.OrdinalIgnoreCase);
            return token != null ? (float)token.Value<double>() : 0f;
        }

        private static float ConvertToSingle(object rawValue)
        {
            switch (rawValue)
            {
                case null:
                    return 0f;
                case float f:
                    return f;
                case double d:
                    return (float)d;
                case int i:
                    return i;
                case long l:
                    return l;
                case JValue jValue:
                    return ConvertToSingle(jValue.Value);
                case string str:
                    if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                    break;
            }

            try
            {
                return Convert.ToSingle(rawValue, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private void ResetMetadataRoot()
        {
            _metadataRootPosition = Vector3.zero;
            _metadataRootOrigin = Vector3.zero;
            _metadataRootOriginCaptured = false;
            _hasMetadataRootPosition = false;
        }

        private bool TryApplyAdvancedHeadOrientation(HumanoidBoneMapping mapping, Transform boneTransform)
        {
            if (!_useAdvancedHeadOrientation)
            {
                return false;
            }

            if (!TryComputeHeadOrientation(out var forward, out var up, out var right))
            {
                return false;
            }

            return TryApplyAdvancedOrientation(mapping, boneTransform, forward, up, right, _headAxisFlip, "head");
        }

        private bool TryComputeHeadOrientation(out Vector3 forward, out Vector3 up, out Vector3 right)
        {
            forward = Vector3.zero;
            up = Vector3.zero;
            right = Vector3.zero;

            if (string.IsNullOrEmpty(_headLeftJoint) || string.IsNullOrEmpty(_headRightJoint))
            {
                return false;
            }

            if (!TryGetJointPosition(_headLeftJoint, out var leftEar) ||
                !TryGetJointPosition(_headRightJoint, out var rightEar))
            {
                return false;
            }

            right = rightEar - leftEar;
            if (right.sqrMagnitude < 1e-6f)
            {
                return false;
            }
            right = right.normalized;

            var earCenter = (leftEar + rightEar) * 0.5f;

            var hasBase = false;
            var baseJoint = Vector3.zero;
            if (!string.IsNullOrEmpty(_headBaseJoint))
            {
                hasBase = TryGetJointPosition(_headBaseJoint, out baseJoint);
            }
            var baseVector = hasBase ? (earCenter - baseJoint) : Vector3.zero;

            Vector3 forwardCandidate = Vector3.zero;
            if (!string.IsNullOrEmpty(_headForwardJoint) &&
                TryGetJointPosition(_headForwardJoint, out var forwardJoint))
            {
                forwardCandidate = forwardJoint - earCenter;
                forwardCandidate = Vector3.ProjectOnPlane(forwardCandidate, right);
            }

            if (forwardCandidate.sqrMagnitude < 1e-6f && hasBase)
            {
                forwardCandidate = Vector3.Cross(right, baseVector);
            }

            if (forwardCandidate.sqrMagnitude < 1e-6f)
            {
                forwardCandidate = Vector3.Cross(right, Vector3.up);
            }

            if (forwardCandidate.sqrMagnitude < 1e-6f)
            {
                forwardCandidate = Vector3.forward;
            }

            forward = forwardCandidate.normalized;

            var upCandidate = Vector3.ProjectOnPlane(baseVector, right);
            if (upCandidate.sqrMagnitude < 1e-6f)
            {
                upCandidate = Vector3.ProjectOnPlane(Vector3.up, right);
            }
            if (upCandidate.sqrMagnitude < 1e-6f)
            {
                upCandidate = Vector3.Cross(right, forward);
            }

            up = upCandidate.sqrMagnitude > 1e-6f ? upCandidate.normalized : Vector3.up;

            right = Vector3.Cross(forward, up);
            if (right.sqrMagnitude < 1e-6f)
            {
                right = Vector3.Cross(forward, Vector3.up);
            }
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;

            up = Vector3.Cross(right, forward);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.up;
            }
            else
            {
                up = up.normalized;
            }

            return forward.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f && right.sqrMagnitude > 1e-6f;
        }

        private bool TryApplyAdvancedTorsoOrientation(HumanoidBoneMapping mapping, Transform boneTransform)
        {
            if (!_useAdvancedTorsoOrientation || !IsTorsoBone(mapping._bone))
            {
                return false;
            }

            if (!TryGetJointPosition("LEFT_HIP", out var leftHip) ||
                !TryGetJointPosition("RIGHT_HIP", out var rightHip) ||
                !TryGetJointPosition("LEFT_SHOULDER", out var leftShoulder) ||
                !TryGetJointPosition("RIGHT_SHOULDER", out var rightShoulder))
            {
                return false;
            }

            var hipCenter = (leftHip + rightHip) * 0.5f;
            var shoulderCenter = (leftShoulder + rightShoulder) * 0.5f;

            Vector3 right;
            Vector3 up;

            if (mapping._bone == HumanBodyBones.Hips)
            {
                right = rightHip - leftHip;
                up = shoulderCenter - hipCenter;
            }
            else
            {
                right = rightShoulder - leftShoulder;
                if (!TryGetJointPosition("NECK", out var neck))
                {
                    neck = shoulderCenter + (shoulderCenter - hipCenter);
                }
                up = neck - shoulderCenter;
                if (up.sqrMagnitude < 1e-6f)
                {
                    up = shoulderCenter - hipCenter;
                }
            }

            if (up.sqrMagnitude < 1e-6f)
            {
                up = _rootRestUp;
            }

            if (right.sqrMagnitude < 1e-6f)
            {
                right = _rootRestRight;
            }

            var forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = _rootRestForward;
            }

            return TryApplyAdvancedOrientation(mapping, boneTransform, forward, up, right, _torsoAxisFlip, "torso");
        }

        private bool TryApplyAdvancedOrientation(
            HumanoidBoneMapping mapping,
            Transform boneTransform,
            Vector3 forward,
            Vector3 up,
            Vector3 right,
            Vector3 axisFlip,
            string label)
        {
            const float epsilon = 1e-6f;

            if (forward.sqrMagnitude < epsilon || up.sqrMagnitude < epsilon || right.sqrMagnitude < epsilon)
            {
                return false;
            }

            forward = Vector3.Scale(forward, axisFlip);
            up = Vector3.Scale(up, axisFlip);
            right = Vector3.Scale(right, axisFlip);

            if (forward.sqrMagnitude < epsilon)
            {
                return false;
            }

            forward = forward.normalized;

            right = Vector3.ProjectOnPlane(right, forward);
            if (right.sqrMagnitude < epsilon)
            {
                right = Vector3.Cross(forward, up);
            }
            if (right.sqrMagnitude < epsilon)
            {
                right = Vector3.Cross(forward, Vector3.up);
            }
            if (right.sqrMagnitude < epsilon)
            {
                return false;
            }
            right = right.normalized;

            up = Vector3.Cross(right, forward);
            if (up.sqrMagnitude < epsilon)
            {
                up = Vector3.ProjectOnPlane(up, forward);
            }
            if (up.sqrMagnitude < epsilon)
            {
                up = Vector3.up;
            }
            up = up.normalized;

            var restRotation = mapping._restRotation;
            var restForward = (restRotation * Vector3.forward).normalized;
            var restUp = (restRotation * Vector3.up).normalized;

            if (restForward.sqrMagnitude < epsilon)
            {
                restForward = Vector3.forward;
            }
            if (restUp.sqrMagnitude < epsilon)
            {
                restUp = Vector3.up;
            }

            if (Vector3.Dot(forward, restForward) < 0f)
            {
                forward = -forward;
                right = -right;
            }

            if (Vector3.Dot(up, restUp) < 0f)
            {
                up = -up;
            }

            var restBasis = Quaternion.LookRotation(restForward, restUp);
            var targetBasis = Quaternion.LookRotation(forward, up);
            var delta = targetBasis * Quaternion.Inverse(restBasis);
            var targetRotation = delta * restRotation * Quaternion.Euler(mapping._rotationOffset);

            mapping._lastDirection = forward;
            mapping._hasDirection = true;

            boneTransform.rotation = Quaternion.Slerp(
                boneTransform.rotation,
                targetRotation,
                Time.deltaTime * _rotationLerp);

            if (_debugLogging)
            {
                Debug.Log($"HumanoidPoseApplier advanced {label} rotation for {mapping._bone}: {targetRotation.eulerAngles}");
            }

            return true;
        }

        private static bool IsTorsoBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.Hips ||
                   bone == HumanBodyBones.Spine ||
                   bone == HumanBodyBones.Chest ||
                   bone == HumanBodyBones.UpperChest;
        }

        private void UpdateRootTransform()
        {
            if (!TryGetJointPosition("LEFT_HIP", out var leftHip) ||
                !TryGetJointPosition("RIGHT_HIP", out var rightHip) ||
                !TryGetJointPosition("LEFT_SHOULDER", out var leftShoulder) ||
                !TryGetJointPosition("RIGHT_SHOULDER", out var rightShoulder))
            {
                if (_debugLogging)
                {
                    Debug.LogWarning("HumanoidPoseApplier: root transform update skipped (missing joints)");
                }
                return;
            }

            var pelvis = (leftHip + rightHip) * 0.5f;
            var shoulders = (leftShoulder + rightShoulder) * 0.5f;

            var up = shoulders - pelvis;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = _rootRestUp;
            }
            else
            {
                up = up.normalized;
            }
            if (Vector3.Dot(up, _rootRestUp) < 0f)
            {
                up = -up;
            }

            var across = rightShoulder - leftShoulder;
            if (across.sqrMagnitude < 1e-6f)
            {
                across = _rootRestRight;
            }
            else
            {
                across = across.normalized;
            }
            if (Vector3.Dot(across, _rootRestRight) < 0f)
            {
                across = -across;
            }

            var forward = Vector3.Cross(across, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = _rootRestForward;
            }
            else
            {
                forward = forward.normalized;
            }
            if (Vector3.Dot(forward, _rootRestForward) < 0f)
            {
                forward = -forward;
            }

            across = Vector3.Cross(up, forward).normalized;

            var basePosition = pelvis;
            Vector3 targetPosition;
            if (_useMetadataRootTranslation && _hasMetadataRootPosition)
            {
                var blend = new Vector3(
                    Mathf.Clamp01(_metadataRootAxisBlend.x),
                    Mathf.Clamp01(_metadataRootAxisBlend.y),
                    Mathf.Clamp01(_metadataRootAxisBlend.z));
                var metadataPosition = _metadataRootPosition;
                targetPosition = new Vector3(
                    metadataPosition.x * blend.x + basePosition.x * (1f - blend.x),
                    metadataPosition.y * blend.y + basePosition.y * (1f - blend.y),
                    metadataPosition.z * blend.z + basePosition.z * (1f - blend.z));
            }
            else
            {
                targetPosition = basePosition;
            }
            targetPosition += _rootPositionOffset;
            var targetRotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(_rootRotationOffset);

            var root = _animator.transform;
            root.position = Vector3.Lerp(root.position, targetPosition, Time.deltaTime * _positionLerp);
            root.rotation = Quaternion.Slerp(root.rotation, targetRotation, Time.deltaTime * _rotationLerp);
            if (_debugLogging)
            {
                Debug.Log($"HumanoidPoseApplier root target -> pos {targetPosition}, rot {targetRotation.eulerAngles}");
            }
        }

        private void PopulateDefaultMappings()
        {
            _boneMappings = new List<HumanoidBoneMapping>
            {
                new HumanoidBoneMapping { _startJoint = "PELVIS", _endJoint = "CHEST", _bone = HumanBodyBones.Hips },
                new HumanoidBoneMapping { _startJoint = "PELVIS", _endJoint = "CHEST", _bone = HumanBodyBones.Spine },
                new HumanoidBoneMapping { _startJoint = "CHEST", _endJoint = "NECK", _bone = HumanBodyBones.Chest },
                new HumanoidBoneMapping { _startJoint = "CHEST", _endJoint = "NECK", _bone = HumanBodyBones.UpperChest },
                new HumanoidBoneMapping { _startJoint = "CHEST", _endJoint = "NECK", _bone = HumanBodyBones.Neck },
                new HumanoidBoneMapping { _startJoint = "NECK", _endJoint = "NOSE", _bone = HumanBodyBones.Head },
                new HumanoidBoneMapping { _startJoint = "CHEST", _endJoint = "LEFT_ELBOW", _bone = HumanBodyBones.LeftShoulder },
                new HumanoidBoneMapping { _startJoint = "LEFT_SHOULDER", _endJoint = "LEFT_ELBOW", _bone = HumanBodyBones.LeftUpperArm },
                new HumanoidBoneMapping { _startJoint = "LEFT_ELBOW", _endJoint = "LEFT_WRIST", _bone = HumanBodyBones.LeftLowerArm },
                new HumanoidBoneMapping { _startJoint = "LEFT_WRIST", _endJoint = "LEFT_INDEX", _bone = HumanBodyBones.LeftHand },
                new HumanoidBoneMapping { _startJoint = "LEFT_WRIST", _endJoint = "LEFT_THUMB", _bone = HumanBodyBones.LeftThumbProximal },
                new HumanoidBoneMapping { _startJoint = "CHEST", _endJoint = "RIGHT_ELBOW", _bone = HumanBodyBones.RightShoulder },
                new HumanoidBoneMapping { _startJoint = "RIGHT_SHOULDER", _endJoint = "RIGHT_ELBOW", _bone = HumanBodyBones.RightUpperArm },
                new HumanoidBoneMapping { _startJoint = "RIGHT_ELBOW", _endJoint = "RIGHT_WRIST", _bone = HumanBodyBones.RightLowerArm },
                new HumanoidBoneMapping { _startJoint = "RIGHT_WRIST", _endJoint = "RIGHT_INDEX", _bone = HumanBodyBones.RightHand },
                new HumanoidBoneMapping { _startJoint = "RIGHT_WRIST", _endJoint = "RIGHT_THUMB", _bone = HumanBodyBones.RightThumbProximal },
                new HumanoidBoneMapping { _startJoint = "LEFT_HIP", _endJoint = "LEFT_KNEE", _bone = HumanBodyBones.LeftUpperLeg },
                new HumanoidBoneMapping { _startJoint = "LEFT_KNEE", _endJoint = "LEFT_ANKLE", _bone = HumanBodyBones.LeftLowerLeg },
                new HumanoidBoneMapping { _startJoint = "LEFT_ANKLE", _endJoint = "LEFT_FOOT_INDEX", _bone = HumanBodyBones.LeftFoot },
                new HumanoidBoneMapping { _startJoint = "RIGHT_HIP", _endJoint = "RIGHT_KNEE", _bone = HumanBodyBones.RightUpperLeg },
                new HumanoidBoneMapping { _startJoint = "RIGHT_KNEE", _endJoint = "RIGHT_ANKLE", _bone = HumanBodyBones.RightLowerLeg },
                new HumanoidBoneMapping { _startJoint = "RIGHT_ANKLE", _endJoint = "RIGHT_FOOT_INDEX", _bone = HumanBodyBones.RightFoot },
            };
        }

        private void InitializeAutoRotationOffsets()
        {
            if (_animator == null)
            {
                return;
            }

            _restJointPositions.Clear();
            foreach (var mapping in _boneMappings)
            {
                if (mapping._bone == HumanBodyBones.LastBone)
                {
                    continue;
                }

                var bone = _animator.GetBoneTransform(mapping._bone);
                if (bone == null)
                {
                    continue;
                }

                var child = GetChildBoneTransform(mapping._bone);
                if (child == bone)
                {
                    child = null;
                }
                Vector3 referenceDirection;
                if (child != null)
                {
                    referenceDirection = child.position - bone.position;
                }
                else
                {
                    referenceDirection = bone.rotation * Vector3.forward;
                }

                if (referenceDirection.sqrMagnitude < 1e-6f)
                {
                    referenceDirection = bone.rotation * Vector3.up;
                }

                mapping._restRotation = bone.rotation;
                mapping._restDirection = referenceDirection.normalized;
                mapping._hasRestDirection = mapping._restDirection.sqrMagnitude > 1e-6f;
                mapping._lastDirection = mapping._restDirection;
                mapping._hasDirection = mapping._hasRestDirection;

                CacheRestJointPosition(mapping._startJoint, bone.position);
                if (child != null)
                {
                    CacheRestJointPosition(mapping._endJoint, child.position);
                }
                else if (!string.IsNullOrEmpty(mapping._endJoint) && mapping._hasRestDirection)
                {
                    CacheRestJointPosition(mapping._endJoint, bone.position + mapping._restDirection);
                }

                if (!string.IsNullOrEmpty(mapping._startJoint) &&
                    !string.IsNullOrEmpty(mapping._endJoint) &&
                    _restJointPositions.TryGetValue(mapping._startJoint, out var restStart) &&
                    _restJointPositions.TryGetValue(mapping._endJoint, out var restEnd))
                {
                    var restVector = restEnd - restStart;
                    if (restVector.sqrMagnitude > 1e-6f)
                    {
                        var normalized = restVector.normalized;
                        mapping._restDirection = normalized;
                        mapping._hasRestDirection = true;
                        mapping._lastDirection = normalized;
                        mapping._hasDirection = true;
                    }
                }
            }
        }

        private void CacheRestJointPosition(string jointName, Vector3 position)
        {
            if (string.IsNullOrEmpty(jointName))
            {
                return;
            }

            if (!_restJointPositions.ContainsKey(jointName))
            {
                if (TryGetBoneTransformForJoint(jointName, out var jointTransform))
                {
                    position = jointTransform.position;
                }
                _restJointPositions[jointName] = position;
            }

            if (!_lastKnownJointPositions.ContainsKey(jointName))
            {
                _lastKnownJointPositions[jointName] = _restJointPositions[jointName];
            }
        }

        private Transform GetChildBoneTransform(HumanBodyBones bone)
        {
            if (_animator == null)
            {
                return null;
            }

            switch (bone)
            {
                case HumanBodyBones.Hips:
                    var spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                    if (spine != null)
                    {
                        return spine;
                    }
                    return _animator.GetBoneTransform(HumanBodyBones.Chest);
                case HumanBodyBones.Chest:
                    var upperChest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
                    if (upperChest != null)
                    {
                        return upperChest;
                    }
                    return _animator.GetBoneTransform(HumanBodyBones.Neck);
                case HumanBodyBones.UpperChest:
                    return _animator.GetBoneTransform(HumanBodyBones.Neck);
                case HumanBodyBones.Neck:
                    return _animator.GetBoneTransform(HumanBodyBones.Head);
                case HumanBodyBones.LeftShoulder:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                case HumanBodyBones.LeftUpperArm:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                case HumanBodyBones.LeftLowerArm:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                case HumanBodyBones.RightShoulder:
                    return _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                case HumanBodyBones.RightUpperArm:
                    return _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                case HumanBodyBones.RightLowerArm:
                    return _animator.GetBoneTransform(HumanBodyBones.RightHand);
                case HumanBodyBones.LeftHand:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                case HumanBodyBones.LeftThumbProximal:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
                case HumanBodyBones.RightHand:
                    return _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                case HumanBodyBones.RightThumbProximal:
                    return _animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
                case HumanBodyBones.LeftUpperLeg:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                case HumanBodyBones.LeftLowerLeg:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                case HumanBodyBones.RightUpperLeg:
                    return _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                case HumanBodyBones.RightLowerLeg:
                    return _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                case HumanBodyBones.LeftFoot:
                    return _animator.GetBoneTransform(HumanBodyBones.LeftToes);
                case HumanBodyBones.RightFoot:
                    return _animator.GetBoneTransform(HumanBodyBones.RightToes);
                case HumanBodyBones.Spine:
                    var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
                    if (chest == null)
                    {
                        chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
                    }
                    return chest;
                case HumanBodyBones.Head:
                    return _animator.GetBoneTransform(HumanBodyBones.Head);
                default:
                    return null;
            }
        }

        private bool TryGetBoneTransformForJoint(string jointName, out Transform jointTransform)
        {
            jointTransform = null;
            if (_animator == null || string.IsNullOrEmpty(jointName))
            {
                return false;
            }

            switch (jointName.ToUpperInvariant())
            {
                case "PELVIS":
                case "HIP_CENTER":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.Hips);
                    break;
                case "CHEST":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.Chest)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.UpperChest)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.Spine);
                    break;
                case "NECK":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.Neck)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.Head);
                    break;
                case "NOSE":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.Head);
                    break;
                case "LEFT_SHOULDER":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftShoulder)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    break;
                case "RIGHT_SHOULDER":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightShoulder)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    break;
                case "LEFT_ELBOW":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    break;
                case "RIGHT_ELBOW":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    break;
                case "LEFT_WRIST":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    break;
                case "RIGHT_WRIST":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                    break;
                case "LEFT_INDEX":
                case "LEFT_PINKY":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    break;
                case "RIGHT_INDEX":
                case "RIGHT_PINKY":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightHand);
                    break;
                case "LEFT_THUMB":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
                    break;
                case "RIGHT_THUMB":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
                    break;
                case "LEFT_HIP":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    break;
                case "RIGHT_HIP":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    break;
                case "LEFT_KNEE":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                    break;
                case "RIGHT_KNEE":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                    break;
                case "LEFT_ANKLE":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    break;
                case "RIGHT_ANKLE":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    break;
                case "LEFT_HEEL":
                case "LEFT_FOOT_INDEX":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.LeftToes)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    break;
                case "RIGHT_HEEL":
                case "RIGHT_FOOT_INDEX":
                    jointTransform = _animator.GetBoneTransform(HumanBodyBones.RightToes)
                                    ?? _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    break;
            }

            return jointTransform != null;
        }
    }
}
