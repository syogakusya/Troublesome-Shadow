#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AnimationClipRecording.Editor
{
    [CustomEditor(typeof(HumanoidAnimationClipRecorder))]
    public class HumanoidAnimationClipRecorderEditor : UnityEditor.Editor
    {
        private HumanoidAnimationClipRecorder _recorder;

        private void OnEnable()
        {
            _recorder = (HumanoidAnimationClipRecorder)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Animation Clip Recorder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_animator"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordingName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_frameRate"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recording Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_useHumanoidMuscleCurves"), new GUIContent("Use Humanoid Muscle Curves"));

            if (_recorder._useHumanoidMuscleCurves)
            {
                EditorGUILayout.HelpBox("Records HumanPose (root translation/rotation + all Humanoid muscle values). Transform-level options are ignored in this mode.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordRootTransform"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordEntireHierarchy"), new GUIContent("Record Entire Hierarchy"));
                if (!_recorder._recordEntireHierarchy)
                {
                    EditorGUILayout.HelpBox("When disabled, only the selected Humanoid bones will be recorded.", MessageType.Info);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_selectedBones"), true);
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordLocalPositions"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordLocalRotations"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_recordLocalScale"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Export Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_saveToAssets"));

            if (_recorder._saveToAssets)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_assetsPath"));
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_exportJson"));

            if (_recorder._exportJson)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_jsonOutputPath"));
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            EditorGUILayout.BeginHorizontal();

            if (_recorder.IsRecording)
            {
                GUI.color = Color.red;
                if (GUILayout.Button("Stop Recording"))
                {
                    var clip = _recorder.StopRecording();
                    if (clip != null)
                    {
                        EditorUtility.DisplayDialog("Recording Complete", 
                            $"AnimationClip '{clip.name}' has been saved.\n\n" +
                            (_recorder._saveToAssets ? $"Asset: {_recorder._assetsPath}/{clip.name}.anim\n" : "") +
                            (_recorder._exportJson ? $"JSON: {_recorder._jsonOutputPath}/{clip.name}.json" : ""), 
                            "OK");
                    }
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUILayout.Button("Start Recording"))
                {
                    _recorder.StartRecording();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Recording controls are only available in Play Mode.", MessageType.Info);
            }

            if (_recorder.IsRecording)
            {
                EditorGUILayout.HelpBox("Recording in progress...", MessageType.Warning);
            }

            if (_recorder.LastRecordedClip != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last Recorded Clip", EditorStyles.boldLabel);
                EditorGUILayout.ObjectField(_recorder.LastRecordedClip, typeof(AnimationClip), false);
            }
        }
    }

    [CustomEditor(typeof(HumanoidAnimationClipPlayer))]
    public class HumanoidAnimationClipPlayerEditor : UnityEditor.Editor
    {
        private HumanoidAnimationClipPlayer _player;

        private void OnEnable()
        {
            _player = (HumanoidAnimationClipPlayer)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Humanoid Animation Clip Player", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_animator"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_animationClip"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_loop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_speed"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Playback Controls", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying || _player._animationClip == null);
            EditorGUILayout.BeginHorizontal();

            if (_player.IsPlaying)
            {
                if (GUILayout.Button("Stop"))
                {
                    _player.Stop();
                }
                if (GUILayout.Button("Pause"))
                {
                    _player.Pause();
                }
            }
            else
            {
                if (GUILayout.Button("Play"))
                {
                    _player.Play(_player._animationClip);
                }
                if (_player.IsPlaying && GUILayout.Button("Resume"))
                {
                    _player.Resume();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (_player._animationClip != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
                
                var normalizedTime = Application.isPlaying ? _player.NormalizedTime : _player._normalizedTime;
                var newNormalizedTime = EditorGUILayout.Slider("Normalized Time", normalizedTime, 0f, 1f);
                
                if (Application.isPlaying && Mathf.Abs(newNormalizedTime - normalizedTime) > 0.001f)
                {
                    _player.SetNormalizedTime(newNormalizedTime);
                }
                else if (!Application.isPlaying)
                {
                    _player._normalizedTime = newNormalizedTime;
                }

                if (_player._animationClip != null)
                {
                    var currentTime = normalizedTime * _player._animationClip.length;
                    var totalTime = _player._animationClip.length;
                    EditorGUILayout.LabelField($"Time: {currentTime:F2} / {totalTime:F2} seconds");
                }
            }

            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Playback controls are only available in Play Mode.", MessageType.Info);
            }

            if (_player.IsPlaying && Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Playing...", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Load from JSON", EditorStyles.boldLabel);
            
            var jsonPath = EditorGUILayout.TextField("JSON Path", "");
            if (GUILayout.Button("Load and Play"))
            {
                if (!string.IsNullOrEmpty(jsonPath))
                {
                    _player.PlayFromJson(jsonPath);
                }
            }
        }
    }
}
#endif
