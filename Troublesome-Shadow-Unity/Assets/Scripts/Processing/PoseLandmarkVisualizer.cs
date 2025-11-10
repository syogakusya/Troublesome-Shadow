using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PoseRuntime
{
    /// <summary>
    /// Renders MediaPipe landmark positions inside the Unity scene view to aid calibration and debugging.
    /// </summary>
    [DefaultExecutionOrder(450)]
    public class PoseLandmarkVisualizer : MonoBehaviour
    {
        [FormerlySerializedAs("controller")] public AvatarController _controller;
        [FormerlySerializedAs("poseSpaceOrigin")] public Transform _poseSpaceOrigin;
        [FormerlySerializedAs("drawInPlayMode")] public bool _drawInPlayMode = true;
        [FormerlySerializedAs("drawWhenSelectedOnly")] public bool _drawWhenSelectedOnly = false;
        [FormerlySerializedAs("jointColor")] public Color _jointColor = new Color(0.2f, 0.9f, 0.6f, 0.85f);
        [FormerlySerializedAs("connectionColor")] public Color _connectionColor = new Color(0.2f, 0.6f, 1f, 0.5f);
        [FormerlySerializedAs("jointSize")] public float _jointSize = 0.025f;
        [FormerlySerializedAs("drawConnections")] public bool _drawConnections = true;
        [FormerlySerializedAs("showLabels")] public bool _showLabels = false;
        [FormerlySerializedAs("showSeatingInfo")] public bool _showSeatingInfo = true;
#if UNITY_EDITOR
        [FormerlySerializedAs("labelStyle")] public GUIStyle _labelStyle;
        [FormerlySerializedAs("seatingInfoStyle")] public GUIStyle _seatingInfoStyle;
#endif

        private SkeletonSample _latestSample;
        private bool _subscribed;
        private readonly Dictionary<string, Vector3> _positionCache = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private static readonly (string, string)[] DefaultConnections =
        {
            ("LEFT_SHOULDER", "RIGHT_SHOULDER"),
            ("LEFT_SHOULDER", "LEFT_ELBOW"),
            ("LEFT_ELBOW", "LEFT_WRIST"),
            ("LEFT_WRIST", "LEFT_INDEX"),
            ("LEFT_WRIST", "LEFT_PINKY"),
            ("LEFT_WRIST", "LEFT_THUMB"),
            ("RIGHT_SHOULDER", "RIGHT_ELBOW"),
            ("RIGHT_ELBOW", "RIGHT_WRIST"),
            ("RIGHT_WRIST", "RIGHT_INDEX"),
            ("RIGHT_WRIST", "RIGHT_PINKY"),
            ("RIGHT_WRIST", "RIGHT_THUMB"),
            ("LEFT_SHOULDER", "LEFT_HIP"),
            ("RIGHT_SHOULDER", "RIGHT_HIP"),
            ("LEFT_HIP", "RIGHT_HIP"),
            ("LEFT_HIP", "LEFT_KNEE"),
            ("LEFT_KNEE", "LEFT_ANKLE"),
            ("LEFT_ANKLE", "LEFT_FOOT_INDEX"),
            ("RIGHT_HIP", "RIGHT_KNEE"),
            ("RIGHT_KNEE", "RIGHT_ANKLE"),
            ("RIGHT_ANKLE", "RIGHT_FOOT_INDEX"),
            ("NOSE", "LEFT_EYE"),
            ("NOSE", "RIGHT_EYE"),
            ("LEFT_EYE", "LEFT_EAR"),
            ("RIGHT_EYE", "RIGHT_EAR"),
        };

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
            _latestSample = sample?.Clone();
        }

        private void OnDrawGizmos()
        {
            if (!ShouldDrawInCurrentContext(isSelected: false))
            {
                return;
            }

            DrawVisualization();
        }

        private void OnDrawGizmosSelected()
        {
            if (!ShouldDrawInCurrentContext(isSelected: true))
            {
                return;
            }

            DrawVisualization();
        }

        private bool ShouldDrawInCurrentContext(bool isSelected)
        {
            if (_latestSample == null)
            {
                return false;
            }

            if (!Application.isPlaying && !_drawWhenSelectedOnly)
            {
                // Always draw in edit mode unless explicitly opted out.
                return true;
            }

            if (Application.isPlaying)
            {
                if (!_drawInPlayMode)
                {
                    return false;
                }

                if (_drawWhenSelectedOnly && !isSelected)
                {
                    return false;
                }
            }
            else if (_drawWhenSelectedOnly && !isSelected)
            {
                return false;
            }

            return true;
        }

        private void DrawVisualization()
        {
            var sample = _latestSample;
            if (sample == null)
            {
                return;
            }

            _positionCache.Clear();

            Gizmos.color = _jointColor;
            foreach (var joint in sample._joints)
            {
                if (joint == null)
                {
                    continue;
                }

                var worldPos = ConvertToWorld(joint._position);
                _positionCache[joint._name] = worldPos;
                Gizmos.DrawSphere(worldPos, _jointSize);
            }

            if (_drawConnections)
            {
                Gizmos.color = _connectionColor;
                foreach (var connection in DefaultConnections)
                {
                    if (!_positionCache.TryGetValue(connection.Item1, out var from))
                    {
                        continue;
                    }

                    if (!_positionCache.TryGetValue(connection.Item2, out var to))
                    {
                        continue;
                    }

                    Gizmos.DrawLine(from, to);
                }
            }
        }

#if UNITY_EDITOR
            if (_showLabels)
            {
                Handles.color = _jointColor;
                var style = _labelStyle ?? new GUIStyle("label") { normal = { textColor = _jointColor } };
                foreach (var joint in sample._joints)
                {
                    if (joint == null)
                    {
                        continue;
                    }

                    if (!_positionCache.TryGetValue(joint._name, out var pos))
                    {
                        continue;
                    }

                    Handles.Label(pos, joint._name, style);
                }
            }

            if (_showSeatingInfo)
            {
                DrawSeatingInfo(sample);
            }
#endif
        }

        private Vector3 ConvertToWorld(Vector3 position)
        {
            if (_poseSpaceOrigin != null)
            {
                return _poseSpaceOrigin.TransformPoint(position);
            }

            return position;
        }

#if UNITY_EDITOR
        private void DrawSeatingInfo(SkeletonSample sample)
        {
            if (!SeatingMetadataUtility.TryGetSnapshot(sample, out var snapshot))
            {
                return;
            }

            var root = ResolveRootPosition();
            var labelOffset = Vector3.up * Mathf.Max(0.1f, _jointSize * 4f);
            var text = BuildSeatingLabel(snapshot);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var style = _seatingInfoStyle ?? new GUIStyle("box")
            {
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = _jointColor,
                },
            };

            Handles.color = _jointColor;
            Handles.Label(root + labelOffset, text, style);
        }

        private string BuildSeatingLabel(SeatingSnapshot snapshot)
        {
            if (snapshot.SeatOrder == null || snapshot.SeatOrder.Count == 0)
            {
                return string.IsNullOrEmpty(snapshot.ActiveSeatId)
                    ? string.Empty
                    : $"Seat: {snapshot.ActiveSeatId}";
            }

            var active = string.IsNullOrEmpty(snapshot.ActiveSeatId) ? "(none)" : snapshot.ActiveSeatId;
            var confidence = snapshot.Confidence;
            return $"Seat: {active}\nConfidence: {confidence:0.00}";
        }

        private Vector3 ResolveRootPosition()
        {
            if (_positionCache.TryGetValue("ROOT", out var root))
            {
                return root;
            }

            var hasLeft = _positionCache.TryGetValue("LEFT_HIP", out var leftHip);
            var hasRight = _positionCache.TryGetValue("RIGHT_HIP", out var rightHip);
            if (hasLeft && hasRight)
            {
                return (leftHip + rightHip) * 0.5f;
            }

            if (hasLeft)
            {
                return leftHip;
            }

            if (hasRight)
            {
                return rightHip;
            }

            return transform.position;
        }
#endif
    }
}
