using System;
using UnityEditor;
using UnityEngine;

namespace LanhuRuntimeSync.EditorTools
{
    [CustomEditor(typeof(LanhuRuntimeSyncRoot))]
    public sealed class LanhuRuntimeSyncRootEditor : UnityEditor.Editor
    {
        private static bool sShowAdvanced;

        private SerializedProperty mSourceUrl;
        private SerializedProperty mTeamId;
        private SerializedProperty mProjectId;
        private SerializedProperty mImageId;
        private SerializedProperty mVersionId;
        private SerializedProperty mDesignName;
        private SerializedProperty mSkipHiddenNodes;
        private SerializedProperty mDeleteMissingNodes;
        private SerializedProperty mUseCoverFallback;
        private SerializedProperty mRedownloadSprites;
        private string mReport;

        private void OnEnable()
        {
            mSourceUrl = serializedObject.FindProperty("mSourceUrl");
            mTeamId = serializedObject.FindProperty("mTeamId");
            mProjectId = serializedObject.FindProperty("mProjectId");
            mImageId = serializedObject.FindProperty("mImageId");
            mVersionId = serializedObject.FindProperty("mVersionId");
            mDesignName = serializedObject.FindProperty("mDesignName");
            mSkipHiddenNodes = serializedObject.FindProperty("mSkipHiddenNodes");
            mDeleteMissingNodes = serializedObject.FindProperty("mDeleteMissingNodes");
            mUseCoverFallback = serializedObject.FindProperty("mUseCoverFallback");
            mRedownloadSprites = serializedObject.FindProperty("mRedownloadSprites");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var root = (LanhuRuntimeSyncRoot)target;

            EditorGUILayout.LabelField("Lanhu Source", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(mDesignName, new GUIContent("Page"));
                EditorGUILayout.PropertyField(mProjectId, new GUIContent("Project Id"));
                EditorGUILayout.PropertyField(mImageId, new GUIContent("Image Id"));
                EditorGUILayout.PropertyField(mVersionId, new GUIContent("Version Id"));
                EditorGUILayout.PropertyField(mTeamId, new GUIContent("Team Id"));
                EditorGUILayout.PropertyField(mSourceUrl, new GUIContent("Project URL"));
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(LanhuRuntimeSyncService.IsBusy))
            {
                if (GUILayout.Button("Pull Latest And Apply", GUILayout.Height(28f)))
                {
                    serializedObject.ApplyModifiedProperties();
                    PullLatest(root);
                }

                if (root.UseCoverFallback && GUILayout.Button("Rebuild Supported Editable Layers", GUILayout.Height(24f)))
                {
                    mUseCoverFallback.boolValue = false;
                    serializedObject.ApplyModifiedProperties();
                    PullLatest(root);
                }
            }

            if (root.UseCoverFallback)
            {
                EditorGUILayout.HelpBox(
                    "当前使用整页预览图。蓝湖节点仍保留，但其渲染内容被预览图替代。可重建蓝湖实际提供的 TMP 文本和纯色形状。",
                    MessageType.Info);
            }

            if (LanhuRuntimeSyncService.IsBusy)
            {
                EditorGUILayout.HelpBox("Lanhu Runtime Sync is working.", MessageType.Info);
            }

            sShowAdvanced = EditorGUILayout.Foldout(sShowAdvanced, "Advanced", true);
            if (sShowAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(mSkipHiddenNodes, new GUIContent("Skip Hidden Nodes"));
                    EditorGUILayout.PropertyField(mDeleteMissingNodes, new GUIContent("Delete Missing Nodes"));
                    EditorGUILayout.PropertyField(mUseCoverFallback, new GUIContent("Whole-page Preview Fallback"));
                    EditorGUILayout.PropertyField(mRedownloadSprites, new GUIContent("Redownload Sprites"));
                }
            }

            if (!string.IsNullOrWhiteSpace(mReport))
            {
                EditorGUILayout.HelpBox(mReport, MessageType.None);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private async void PullLatest(LanhuRuntimeSyncRoot root)
        {
            try
            {
                var report = await LanhuRuntimeSyncService.UpdateRootAsync(root, LanhuSessionStore.LoadCookie());
                mReport = report.ToString();
                Debug.Log($"[LanhuRuntimeSyncRoot] {mReport}", root);
            }
            catch (LanhuAccessException exception)
            {
                mReport = $"蓝湖访问被拒绝：{exception.Message}";
                Debug.LogWarning($"[LanhuRuntimeSyncRoot] {mReport}", root);
            }
            catch (Exception exception)
            {
                mReport = $"Lanhu update failed: {exception.Message}";
                Debug.LogException(exception, root);
            }
            finally
            {
                Repaint();
            }
        }
    }
}
