using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LanhuRuntimeSync.EditorTools
{
    public sealed class LanhuRuntimeSyncWindow : EditorWindow
    {
        private const string LastUrlPrefsKey = "LanhuRuntimeSync.LastUrl";
        private const string OutputFolderPrefsKey = "LanhuRuntimeSync.OutputFolder";

        private string mSourceUrl;
        private string mCookie;
        private string mOutputFolder;
        private string mSearch;
        private string mReport;
        private bool mBusy;
        private bool mSkipHiddenNodes = true;
        private bool mDeleteMissingNodes = true;
        private bool mUseCoverFallback = true;
        private bool mRedownloadSprites;
        private bool mInstantiateInScene = true;
        private Vector2 mDesignScroll;
        private IReadOnlyList<LanhuDesignInfo> mDesigns = Array.Empty<LanhuDesignInfo>();
        private string mSelectedDesignId;

        [MenuItem("Tools/Lanhu Runtime Sync")]
        private static void OpenWindow()
        {
            var window = GetWindow<LanhuRuntimeSyncWindow>();
            window.titleContent = new GUIContent("Lanhu Sync");
            window.minSize = new Vector2(520f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            mSourceUrl = EditorPrefs.GetString(LastUrlPrefsKey, string.Empty);
            mOutputFolder = EditorPrefs.GetString(OutputFolderPrefsKey, "Assets/Resources/Prefabs/LanhuRuntime");
            mCookie = LanhuSessionStore.LoadCookie();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Lanhu Source", EditorStyles.boldLabel);
            mSourceUrl = EditorGUILayout.TextField("Project URL", mSourceUrl);
            using (new EditorGUILayout.HorizontalScope())
            {
                mCookie = EditorGUILayout.PasswordField("Login Cookie / cURL", mCookie);
                if (GUILayout.Button("Save Local", GUILayout.Width(82f)))
                {
                    LanhuSessionStore.SaveCookie(mCookie);
                    mReport = "Lanhu login cookie saved to local EditorPrefs only.";
                }

                if (GUILayout.Button("Forget", GUILayout.Width(62f)))
                {
                    LanhuSessionStore.ClearCookie();
                    mCookie = string.Empty;
                    mReport = "Local Lanhu login cookie removed.";
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Lanhu"))
                {
                    Application.OpenURL(string.IsNullOrWhiteSpace(mSourceUrl) ? "https://lanhuapp.com/web/" : mSourceUrl);
                }

                using (new EditorGUI.DisabledScope(mBusy || LanhuRuntimeSyncService.IsBusy))
                {
                    if (GUILayout.Button("Load Pages"))
                    {
                        LoadPages();
                    }
                }
            }

            EditorGUILayout.Space(8f);
            DrawDesignList();
            EditorGUILayout.Space(8f);
            DrawImportOptions();

            using (new EditorGUI.DisabledScope(mBusy || LanhuRuntimeSyncService.IsBusy || SelectedDesign == null))
            {
                if (GUILayout.Button("Import Selected Page", GUILayout.Height(32f)))
                {
                    ImportSelected();
                }
            }

            if (mBusy || LanhuRuntimeSyncService.IsBusy)
            {
                EditorGUILayout.HelpBox("Lanhu Runtime Sync is working. Progress is shown in the Unity progress bar.", MessageType.Info);
            }

            if (!string.IsNullOrWhiteSpace(mReport))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(mReport, MessageType.None);
            }
        }

        private void DrawDesignList()
        {
            EditorGUILayout.LabelField($"Pages ({mDesigns.Count})", EditorStyles.boldLabel);
            mSearch = EditorGUILayout.TextField("Search", mSearch);
            var filtered = mDesigns
                .Where(design => string.IsNullOrWhiteSpace(mSearch) || design.Name.IndexOf(mSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            using (var scroll = new EditorGUILayout.ScrollViewScope(mDesignScroll, GUILayout.MinHeight(180f), GUILayout.MaxHeight(300f)))
            {
                mDesignScroll = scroll.scrollPosition;
                if (filtered.Length == 0)
                {
                    EditorGUILayout.HelpBox(mDesigns.Count == 0 ? "Load the project page list first." : "No pages match the search.", MessageType.None);
                    return;
                }

                foreach (var design in filtered)
                {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        var selected = mSelectedDesignId == design.Id;
                        if (GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(18f)) != selected)
                        {
                            mSelectedDesignId = design.Id;
                        }

                        using (new EditorGUILayout.VerticalScope())
                        {
                            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(design.Name) ? design.Id : design.Name, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField(
                                $"{design.Width:0.##} x {design.Height:0.##}   {(design.HasLayerData ? "Layer metadata" : "Cover only")}   {design.UpdateTime}",
                                EditorStyles.miniLabel);
                        }
                    }
                }
            }
        }

        private void DrawImportOptions()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            mOutputFolder = EditorGUILayout.TextField("Prefab Folder", mOutputFolder);
            mSkipHiddenNodes = EditorGUILayout.Toggle("Skip Hidden Nodes", mSkipHiddenNodes);
            mUseCoverFallback = EditorGUILayout.Toggle("Whole-page Preview Fallback", mUseCoverFallback);
            mInstantiateInScene = EditorGUILayout.Toggle("Add To Current Scene", mInstantiateInScene);

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Advanced", EditorStyles.miniBoldLabel);
            mDeleteMissingNodes = EditorGUILayout.Toggle("Delete Missing Nodes", mDeleteMissingNodes);
            mRedownloadSprites = EditorGUILayout.Toggle("Redownload Sprites", mRedownloadSprites);
        }

        private async void LoadPages()
        {
            if (!LanhuSourceReference.TryParse(mSourceUrl, out var source, out var error))
            {
                mReport = error;
                return;
            }

            mBusy = true;
            mReport = "Loading Lanhu page list...";
            EditorPrefs.SetString(LastUrlPrefsKey, mSourceUrl);
            Repaint();
            try
            {
                var client = new LanhuApiClient(mCookie);
                mDesigns = await client.LoadDesignsAsync(source);
                if (SelectedDesign == null)
                {
                    mSelectedDesignId = mDesigns.FirstOrDefault(design => design.HasLayerData)?.Id ?? mDesigns.FirstOrDefault()?.Id;
                }

                var layered = mDesigns.Count(design => design.HasLayerData);
                mReport = $"Loaded {mDesigns.Count} pages. {layered} include layer data; {mDesigns.Count - layered} are cover-only.";
            }
            catch (LanhuAccessException exception)
            {
                mReport = $"蓝湖访问被拒绝：{exception.Message}";
                Debug.LogWarning($"[LanhuRuntimeSync] {mReport}");
            }
            catch (Exception exception)
            {
                mReport = $"Failed to load Lanhu pages: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                mBusy = false;
                Repaint();
            }
        }

        private async void ImportSelected()
        {
            var design = SelectedDesign;
            if (design == null)
            {
                mReport = "Select a Lanhu page first.";
                return;
            }

            if (!LanhuSourceReference.TryParse(mSourceUrl, out var source, out var error))
            {
                mReport = error;
                return;
            }

            mBusy = true;
            mReport = $"Importing '{design.Name}'...";
            EditorPrefs.SetString(LastUrlPrefsKey, mSourceUrl);
            EditorPrefs.SetString(OutputFolderPrefsKey, mOutputFolder);
            Repaint();
            try
            {
                var options = new LanhuImportOptions
                {
                    OutputFolder = mOutputFolder,
                    SkipHiddenNodes = mSkipHiddenNodes,
                    DeleteMissingNodes = mDeleteMissingNodes,
                    UseCoverFallback = mUseCoverFallback,
                    RedownloadSprites = mRedownloadSprites,
                    InstantiateInScene = mInstantiateInScene,
                    PromptOnMissingLayerImages = true
                };
                var report = await LanhuRuntimeSyncService.ImportOrUpdateAsync(source, design, mCookie, options);
                mReport = report.ToString();
                Debug.Log($"[LanhuRuntimeSync] {mReport}");
            }
            catch (OperationCanceledException)
            {
                mReport = "已取消蓝湖页面导入。";
            }
            catch (LanhuAccessException exception)
            {
                mReport = $"蓝湖访问被拒绝：{exception.Message}";
                Debug.LogWarning($"[LanhuRuntimeSync] {mReport}");
            }
            catch (Exception exception)
            {
                mReport = $"Lanhu import failed: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                mBusy = false;
                Repaint();
            }
        }

        private LanhuDesignInfo SelectedDesign => mDesigns.FirstOrDefault(design => design.Id == mSelectedDesignId);
    }
}
