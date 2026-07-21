using UnityEditor;
using UnityEngine;

namespace LanhuRuntimeSync.EditorTools
{
    [CustomEditor(typeof(LanhuRuntimeBinding))]
    public sealed class LanhuRuntimeBindingEditor : UnityEditor.Editor
    {
        private static bool sShowAdvanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var binding = (LanhuRuntimeBinding)target;

            EditorGUILayout.LabelField("Lanhu Node", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Node Id", binding.NodeId);
                EditorGUILayout.TextField("Type", binding.NodeType);
                EditorGUILayout.TextField("Path", binding.SourcePath);
            }

            sShowAdvanced = EditorGUILayout.Foldout(sShowAdvanced, "Sync Fields", true);
            if (sShowAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mSyncTransform"), new GUIContent("Transform"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mSyncVisibility"), new GUIContent("Visibility"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mSyncText"), new GUIContent("Text"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mSyncImage"), new GUIContent("Image"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("mSyncStyle"), new GUIContent("Style"));
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
