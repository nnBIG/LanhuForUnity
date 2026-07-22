using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LanhuRuntimeSync.EditorTools
{
    internal sealed class LanhuImportOptions
    {
        public string OutputFolder = "Assets/Resources/Prefabs/LanhuRuntime";
        public bool SkipHiddenNodes = true;
        public bool DeleteMissingNodes = true;
        public bool UseCoverFallback = true;
        public bool RedownloadSprites;
        public bool InstantiateInScene = true;
        public bool PromptOnMissingLayerImages;
    }

    internal sealed class LanhuImportReport
    {
        public string PrefabPath;
        public int CreatedNodes;
        public int UpdatedNodes;
        public int DeletedNodes;
        public int HiddenNodesSkipped;
        public int DownloadedSprites;
        public int ReusedSprites;
        public int TextNodes;
        public int SourceNodes;
        public int SourceExportedImages;
        public bool UsedCoverFallback;
        public bool UpdatedExistingPrefab;
        public readonly List<string> Warnings = new List<string>();

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(UpdatedExistingPrefab ? "Updated" : "Created");
            builder.Append($" Lanhu prefab: {PrefabPath}\n");
            builder.Append($"Source nodes={SourceNodes}, exported layer images={SourceExportedImages}.\n");
            builder.Append($"Nodes created={CreatedNodes}, updated={UpdatedNodes}, deleted={DeletedNodes}, hidden skipped={HiddenNodesSkipped}.\n");
            builder.Append($"Texts={TextNodes}, sprites downloaded={DownloadedSprites}, reused={ReusedSprites}, cover fallback={UsedCoverFallback}.");
            foreach (var warning in Warnings.Distinct())
            {
                builder.Append($"\nWarning: {warning}");
            }

            return builder.ToString();
        }
    }

    internal static class LanhuPrefabSynchronizer
    {
        private const string SpritesRootFolder = "Assets/Sprites";
        private const string LegacyGeneratedRootFolder = "Assets/LanhuRuntimeSync/Generated";
        private static readonly Dictionary<string, TMP_FontAsset> FontCache = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        public static async Task<LanhuImportReport> ImportOrUpdateAsync(
            LanhuSourceReference source,
            LanhuDesignInfo design,
            string cookie,
            LanhuImportOptions options)
        {
            var client = new LanhuApiClient(cookie);
            var document = await client.LoadDocumentAsync(source, design);
            ResolveMissingLayerImageMode(document, options);
            var outputFolder = NormalizeAssetFolder(options.OutputFolder, "Assets/Resources/Prefabs/LanhuRuntime");
            EnsureAssetFolder(outputFolder);

            var existingPrefabPath = FindExistingPrefabPath(outputFolder, source.ProjectId, design.Id);
            var report = new LanhuImportReport
            {
                UpdatedExistingPrefab = !string.IsNullOrWhiteSpace(existingPrefabPath),
                SourceNodes = document.Root == null ? 0 : Math.Max(0, document.Root.DescendantsAndSelf().Count() - 1),
                SourceExportedImages = document.ExportedImageCount
            };
            report.Warnings.AddRange(document.Warnings);

            GameObject prefabContents = null;
            var isLoadedPrefabContents = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(existingPrefabPath))
                {
                    prefabContents = PrefabUtility.LoadPrefabContents(existingPrefabPath);
                    isLoadedPrefabContents = true;
                }
                else
                {
                    prefabContents = CreateRootObject(document.Design?.Name);
                    existingPrefabPath = BuildNewPrefabPath(outputFolder, document.Design?.Name, document.Design?.Id);
                }

                var pageRoot = EnsurePageRoot(prefabContents, document.Design?.Name);
                await ApplyDocumentAsync(pageRoot, document, client, options, report);
                var savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabContents, existingPrefabPath);
                if (!savedPrefab)
                {
                    throw new InvalidOperationException($"Unity failed to save the Lanhu prefab at '{existingPrefabPath}'.");
                }

                report.PrefabPath = existingPrefabPath;
            }
            finally
            {
                if (prefabContents)
                {
                    if (isLoadedPrefabContents)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabContents);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(prefabContents);
                    }
                }

                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(existingPrefabPath);
            if (options.InstantiateInScene && prefabAsset)
            {
                var sceneRoot = FindSceneRoot(source.ProjectId, design.Id);
                if (!sceneRoot)
                {
                    var instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                    if (instance)
                    {
                        instance.name = prefabAsset.name;
                        Undo.RegisterCreatedObjectUndo(instance, "Instantiate Lanhu UI Prefab");
                        Selection.activeGameObject = instance;
                        EditorSceneManager.MarkSceneDirty(instance.scene);
                    }
                }
                else
                {
                    Selection.activeGameObject = sceneRoot.gameObject;
                }
            }
            else if (prefabAsset)
            {
                Selection.activeObject = prefabAsset;
            }

            return report;
        }

        public static async Task<LanhuImportReport> UpdateRootAsync(LanhuRuntimeSyncRoot targetRoot, string cookie)
        {
            if (!targetRoot)
            {
                throw new ArgumentNullException(nameof(targetRoot));
            }

            if (!LanhuSourceReference.TryParse(targetRoot.SourceUrl, out var source, out var sourceError))
            {
                throw new InvalidOperationException(sourceError);
            }

            var design = new LanhuDesignInfo
            {
                Id = targetRoot.ImageId,
                Name = targetRoot.DesignName,
                LatestVersionId = targetRoot.VersionId,
                SketchId = "bound"
            };
            var client = new LanhuApiClient(cookie);
            var document = await client.LoadDocumentAsync(source, design);
            var options = new LanhuImportOptions
            {
                SkipHiddenNodes = targetRoot.SkipHiddenNodes,
                DeleteMissingNodes = targetRoot.DeleteMissingNodes,
                UseCoverFallback = targetRoot.UseCoverFallback,
                RedownloadSprites = targetRoot.RedownloadSprites,
                InstantiateInScene = false
            };
            var report = new LanhuImportReport
            {
                UpdatedExistingPrefab = true,
                SourceNodes = document.Root == null ? 0 : Math.Max(0, document.Root.DescendantsAndSelf().Count() - 1),
                SourceExportedImages = document.ExportedImageCount
            };
            report.Warnings.AddRange(document.Warnings);

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(targetRoot.gameObject);
            if (!string.IsNullOrWhiteSpace(prefabPath))
            {
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var contentsRoot = EnsurePageRoot(contents, document.Design?.Name);
                    await ApplyDocumentAsync(contentsRoot, document, client, options, report);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                    EditorUtility.ClearProgressBar();
                }

                report.PrefabPath = prefabPath;
            }
            else
            {
                await ApplyDocumentAsync(targetRoot.gameObject, document, client, options, report);
                EditorUtility.SetDirty(targetRoot.gameObject);
                if (targetRoot.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(targetRoot.gameObject.scene);
                }

                report.PrefabPath = "Scene object";
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        private static void ResolveMissingLayerImageMode(LanhuDocument document, LanhuImportOptions options)
        {
            if (!options.PromptOnMissingLayerImages || document.Root == null || document.ExportedImageCount > 0)
            {
                return;
            }

            var nodeCount = Math.Max(0, document.Root.DescendantsAndSelf().Count() - 1);
            var choice = EditorUtility.DisplayDialogComplex(
                "蓝湖没有逐层图片",
                $"页面“{document.Design.Name}”包含 {nodeCount} 个节点和 {document.TextCount} 个文本节点，但蓝湖返回的逐层图片数量为 0。\n\n" +
                "“导入可编辑层”会生成 TMP 文本和可识别的纯色形状；没有切片的按钮、图标和复杂效果无法还原。\n\n" +
                "“使用整页预览”可以保持画面完整，但其他节点只作为更新绑定和位置元数据存在。",
                "导入可编辑层",
                "取消",
                "使用整页预览");
            if (choice == 1)
            {
                throw new OperationCanceledException("Lanhu import was cancelled.");
            }

            options.UseCoverFallback = choice == 2;
        }

        private static async Task ApplyDocumentAsync(
            GameObject rootObject,
            LanhuDocument document,
            LanhuApiClient client,
            LanhuImportOptions options,
            LanhuImportReport report)
        {
            if (!rootObject)
            {
                throw new ArgumentNullException(nameof(rootObject));
            }

            var syncRoot = GetOrAdd<LanhuRuntimeSyncRoot>(rootObject);
            syncRoot.SetSource(
                document.Source.OriginalUrl,
                document.Source.TeamId,
                document.Source.ProjectId,
                document.Design.Id,
                document.VersionId,
                document.Design.Name,
                document.JsonUrl);
            syncRoot.SetImportOptions(options.SkipHiddenNodes, options.DeleteMissingNodes, options.UseCoverFallback, options.RedownloadSprites);
            rootObject.name = SafeObjectName(document.Design.Name, "Lanhu UI");

            var rootRect = GetOrAddRectTransform(rootObject);
            var artboardFrame = document.Root?.Frame ?? new LanhuFrame
            {
                Width = Mathf.Max(1f, document.Design.Width),
                Height = Mathf.Max(1f, document.Design.Height)
            };
            ConfigureRootCanvas(rootObject, rootRect, artboardFrame);

            var existingBindings = syncRoot.GetBindings()
                .Where(binding => binding && binding.HasSource)
                .GroupBy(binding => binding.NodeId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            var coverFallback = options.UseCoverFallback && (document.Root == null || document.ExportedImageCount == 0) && !string.IsNullOrWhiteSpace(document.CoverUrl);
            report.UsedCoverFallback = coverFallback;

            var spriteRequests = new List<SpriteRequest>();
            if (coverFallback)
            {
                if (!existingBindings.TryGetValue(LanhuRuntimeBinding.CoverNodeId, out var coverBinding) ||
                    !coverBinding ||
                    coverBinding.SyncImage)
                {
                    spriteRequests.Add(new SpriteRequest(LanhuRuntimeBinding.CoverNodeId, "Cover", document.CoverUrl));
                }
            }
            else if (document.Root != null)
            {
                spriteRequests.AddRange(document.Root.DescendantsAndSelf()
                    .Where(node => node != document.Root && node.HasImage)
                    .Where(node => !options.SkipHiddenNodes || node.Visible)
                    .Where(node => !existingBindings.TryGetValue(node.Id, out var binding) || !binding || binding.SyncImage)
                    .Select(node => new SpriteRequest(node.Id, node.Name, node.ImageUrl)));
            }

            var sprites = await DownloadSpritesAsync(
                document.Design.Name,
                spriteRequests,
                existingBindings,
                client,
                options.RedownloadSprites,
                report);

            ConfigureCover(rootRect, document, coverFallback, sprites, existingBindings, usedNodeIds, report);
            if (document.Root != null)
            {
                var rootChildren = document.Root.Children.AsEnumerable().Reverse().ToArray();
                var siblingIndex = coverFallback ? 1 : 0;
                foreach (var child in rootChildren)
                {
                    ApplyNodeRecursive(
                        child,
                        rootRect,
                        document.Root.Frame,
                        siblingIndex++,
                        document,
                        options,
                        coverFallback,
                        sprites,
                        existingBindings,
                        usedNodeIds,
                        report);
                }
            }

            if (options.DeleteMissingNodes)
            {
                foreach (var binding in syncRoot.GetBindings().Where(binding => binding && binding.HasSource).ToArray())
                {
                    if (binding.ProjectId != document.Source.ProjectId || binding.ImageId != document.Design.Id || usedNodeIds.Contains(binding.NodeId))
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(binding.gameObject);
                    report.DeletedNodes++;
                }
            }

            EditorUtility.SetDirty(rootObject);
            EditorUtility.SetDirty(syncRoot);
        }

        private static void ApplyNodeRecursive(
            LanhuNode node,
            RectTransform parent,
            LanhuFrame parentFrame,
            int siblingIndex,
            LanhuDocument document,
            LanhuImportOptions options,
            bool suppressGraphics,
            IReadOnlyDictionary<string, Sprite> sprites,
            IDictionary<string, LanhuRuntimeBinding> existingBindings,
            ISet<string> usedNodeIds,
            LanhuImportReport report)
        {
            if (node == null)
            {
                return;
            }

            if (options.SkipHiddenNodes && !node.Visible)
            {
                report.HiddenNodesSkipped += node.DescendantsAndSelf().Count();
                return;
            }

            var binding = existingBindings.TryGetValue(node.Id, out var existing) && existing
                ? existing
                : null;
            GameObject nodeObject;
            var isNew = !binding;
            if (!isNew)
            {
                nodeObject = binding.gameObject;
                report.UpdatedNodes++;
            }
            else
            {
                nodeObject = new GameObject(SafeObjectName(node.Name, "Lanhu Node"), typeof(RectTransform), typeof(LanhuRuntimeBinding));
                binding = nodeObject.GetComponent<LanhuRuntimeBinding>();
                report.CreatedNodes++;
            }

            var rect = GetOrAddRectTransform(nodeObject);
            if (isNew || binding.SyncTransform)
            {
                if (rect.parent != parent)
                {
                    rect.SetParent(parent, false);
                }

                rect.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, parent.childCount - 1)));
                ApplyRect(rect, node.Frame, parentFrame);
            }

            nodeObject.name = SafeObjectName(node.Name, "Lanhu Node");
            if (isNew || binding.SyncVisibility)
            {
                nodeObject.SetActive(node.Visible);
            }

            binding.SetSource(document.Source.ProjectId, document.Design.Id, node.Id, node.Type, node.Path);
            usedNodeIds.Add(node.Id);

            if (node.IsText)
            {
                ApplyText(nodeObject, node, suppressGraphics, report, isNew || binding.SyncText, isNew || binding.SyncStyle);
                report.TextNodes++;
            }
            else if (node.HasImage)
            {
                sprites.TryGetValue(node.Id, out var sprite);
                var syncImage = isNew || binding.SyncImage;
                ApplyImage(
                    nodeObject,
                    sprite,
                    ColorWithOpacity(Color.white, node.Opacity),
                    suppressGraphics,
                    syncImage,
                    isNew || binding.SyncStyle);
                if (syncImage)
                {
                    binding.SetImageSource(node.ImageUrl, sprite ? AssetDatabase.GetAssetPath(sprite) : string.Empty);
                }
            }
            else if (node.HasSolidFill)
            {
                ApplyImage(
                    nodeObject,
                    null,
                    ColorWithOpacity(node.Style.FillColor.Value.ToUnityColor(), node.Opacity),
                    suppressGraphics,
                    isNew || binding.SyncImage,
                    isNew || binding.SyncStyle);
            }
            else if (isNew || (binding.SyncText && binding.SyncImage && binding.SyncStyle))
            {
                DisableManagedGraphics(nodeObject);
            }

            EditorUtility.SetDirty(binding);
            EditorUtility.SetDirty(nodeObject);

            if (node.HasImage)
            {
                return;
            }

            var children = node.Children.AsEnumerable().Reverse().ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                ApplyNodeRecursive(
                    children[index],
                    rect,
                    node.Frame,
                    index,
                    document,
                    options,
                    suppressGraphics,
                    sprites,
                    existingBindings,
                    usedNodeIds,
                    report);
            }
        }

        private static void ConfigureCover(
            RectTransform rootRect,
            LanhuDocument document,
            bool coverFallback,
            IReadOnlyDictionary<string, Sprite> sprites,
            IDictionary<string, LanhuRuntimeBinding> existingBindings,
            ISet<string> usedNodeIds,
            LanhuImportReport report)
        {
            if (!coverFallback)
            {
                if (existingBindings.TryGetValue(LanhuRuntimeBinding.CoverNodeId, out var staleCover) &&
                    staleCover &&
                    staleCover.SyncVisibility)
                {
                    staleCover.gameObject.SetActive(false);
                    EditorUtility.SetDirty(staleCover.gameObject);
                }

                return;
            }

            LanhuRuntimeBinding binding;
            GameObject coverObject;
            if (existingBindings.TryGetValue(LanhuRuntimeBinding.CoverNodeId, out binding) && binding)
            {
                coverObject = binding.gameObject;
                report.UpdatedNodes++;
            }
            else
            {
                coverObject = new GameObject("__LanhuCover", typeof(RectTransform), typeof(Image), typeof(LanhuRuntimeBinding));
                binding = coverObject.GetComponent<LanhuRuntimeBinding>();
                report.CreatedNodes++;
            }

            var isNew = !existingBindings.ContainsKey(LanhuRuntimeBinding.CoverNodeId) ||
                        !existingBindings[LanhuRuntimeBinding.CoverNodeId];
            var rect = GetOrAddRectTransform(coverObject);
            if (isNew || binding.SyncTransform)
            {
                rect.SetParent(rootRect, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.SetSiblingIndex(0);
            }

            if (isNew || binding.SyncVisibility)
            {
                coverObject.SetActive(true);
            }

            sprites.TryGetValue(LanhuRuntimeBinding.CoverNodeId, out var sprite);
            ApplyImage(coverObject, sprite, Color.white, false, isNew || binding.SyncImage, isNew || binding.SyncStyle);
            binding.SetSource(document.Source.ProjectId, document.Design.Id, LanhuRuntimeBinding.CoverNodeId, "cover", "__LanhuCover");
            if (isNew || binding.SyncImage)
            {
                binding.SetImageSource(document.CoverUrl, sprite ? AssetDatabase.GetAssetPath(sprite) : string.Empty);
            }
            usedNodeIds.Add(LanhuRuntimeBinding.CoverNodeId);
            EditorUtility.SetDirty(binding);
        }

        private static async Task<Dictionary<string, Sprite>> DownloadSpritesAsync(
            string designName,
            IEnumerable<SpriteRequest> requests,
            IReadOnlyDictionary<string, LanhuRuntimeBinding> existingBindings,
            LanhuApiClient client,
            bool forceDownload,
            LanhuImportReport report)
        {
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var uniqueRequests = requests
                .Where(request => !string.IsNullOrWhiteSpace(request.NodeId) && !string.IsNullOrWhiteSpace(request.Url))
                .GroupBy(request => request.NodeId)
                .Select(group => group.First())
                .ToArray();
            var spriteFolder = $"{SpritesRootFolder}/{SafePathPart(designName, "Lanhu UI")}";
            EnsureAssetFolder(spriteFolder);

            for (var index = 0; index < uniqueRequests.Length; index++)
            {
                var request = uniqueRequests[index];
                EditorUtility.DisplayProgressBar(
                    "Lanhu Runtime Sync",
                    $"Downloading sprite {index + 1}/{uniqueRequests.Length}: {request.Name}",
                    uniqueRequests.Length == 0 ? 1f : (float)index / uniqueRequests.Length);
                var assetPath = $"{spriteFolder}/{SafePathPart(request.NodeId, "Node")}_{SafePathPart(request.Name, "Sprite")}.png";
                var existingMatches = existingBindings.TryGetValue(request.NodeId, out var binding) &&
                                      binding &&
                                      binding.LastImageUrl == request.Url &&
                                      !string.IsNullOrWhiteSpace(binding.LastAssetPath);
                var existingAssetPath = existingMatches
                    ? MoveManagedSpriteAsset(binding.LastAssetPath, assetPath, report)
                    : assetPath;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(existingAssetPath);
                if (!forceDownload && existingMatches && sprite)
                {
                    result[request.NodeId] = sprite;
                    report.ReusedSprites++;
                    continue;
                }

                var bytes = await client.DownloadAssetAsync(request.Url);
                if (binding &&
                    !string.IsNullOrWhiteSpace(binding.LastAssetPath) &&
                    !string.Equals(binding.LastAssetPath, assetPath, StringComparison.Ordinal) &&
                    IsManagedSpritePath(binding.LastAssetPath))
                {
                    AssetDatabase.DeleteAsset(binding.LastAssetPath);
                }

                var projectFolder = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
                var absolutePath = Path.GetFullPath(Path.Combine(projectFolder, assetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                File.WriteAllBytes(absolutePath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ConfigureTextureImporter(assetPath);
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (!sprite)
                {
                    report.Warnings.Add($"Unity could not import sprite '{request.Name}' from {request.Url}.");
                    continue;
                }

                result[request.NodeId] = sprite;
                report.DownloadedSprites++;
            }

            EditorUtility.ClearProgressBar();
            return result;
        }

        private static string MoveManagedSpriteAsset(string currentPath, string targetPath, LanhuImportReport report)
        {
            if (string.IsNullOrWhiteSpace(currentPath) || string.Equals(currentPath, targetPath, StringComparison.Ordinal))
            {
                return targetPath;
            }

            var currentAsset = AssetDatabase.LoadAssetAtPath<Sprite>(currentPath);
            if (!currentAsset || !IsManagedSpritePath(currentPath))
            {
                return currentAsset ? currentPath : targetPath;
            }

            if (AssetDatabase.LoadAssetAtPath<Sprite>(targetPath))
            {
                return targetPath;
            }

            EnsureAssetFolder(Path.GetDirectoryName(targetPath)?.Replace('\\', '/') ?? SpritesRootFolder);
            var error = AssetDatabase.MoveAsset(currentPath, targetPath);
            if (string.IsNullOrWhiteSpace(error))
            {
                return targetPath;
            }

            report.Warnings.Add($"Could not move sprite '{currentPath}' to the Figma-style folder '{targetPath}': {error}");
            return currentPath;
        }

        private static bool IsManagedSpritePath(string assetPath)
        {
            return assetPath.StartsWith(LegacyGeneratedRootFolder + "/", StringComparison.Ordinal) ||
                   assetPath.StartsWith(SpritesRootFolder + "/", StringComparison.Ordinal);
        }

        private static void ConfigureTextureImporter(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        private static void ConfigureRootCanvas(GameObject pageObject, RectTransform pageRect, LanhuFrame frame)
        {
            var canvasObject = EnsureCanvasParent(pageObject);
            var referenceResolution = new Vector2(Mathf.Max(1f, frame.Width), Mathf.Max(1f, frame.Height));

            pageRect.anchorMin = pageRect.anchorMax = new Vector2(0.5f, 0.5f);
            pageRect.pivot = new Vector2(0.5f, 0.5f);
            pageRect.anchoredPosition = Vector2.zero;
            pageRect.sizeDelta = referenceResolution;
            pageRect.localScale = Vector3.one;
            pageRect.localRotation = Quaternion.identity;

            var canvas = GetOrAdd<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetOrAdd<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            GetOrAdd<GraphicRaycaster>(canvasObject);

            var canvasRect = GetOrAddRectTransform(canvasObject);
            canvasRect.localScale = Vector3.one;
            canvasRect.localRotation = Quaternion.identity;

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                SetLayerRecursively(canvasObject, uiLayer);
            }
        }

        private static GameObject EnsureCanvasParent(GameObject pageObject)
        {
            var parentCanvas = pageObject.transform.parent
                ? pageObject.transform.parent.GetComponentInParent<Canvas>()
                : null;
            if (parentCanvas)
            {
                RemoveCanvasComponents(pageObject);
                return parentCanvas.gameObject;
            }

            var oldParent = pageObject.transform.parent;
            var oldSiblingIndex = pageObject.transform.GetSiblingIndex();
            var canvasObject = new GameObject(
                $"{SafeObjectName(pageObject.name, "Lanhu UI")} Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            if (oldParent)
            {
                canvasObject.transform.SetParent(oldParent, false);
                canvasObject.transform.SetSiblingIndex(oldSiblingIndex);
            }

            pageObject.transform.SetParent(canvasObject.transform, false);
            RemoveCanvasComponents(pageObject);
            return canvasObject;
        }

        private static void RemoveCanvasComponents(GameObject pageObject)
        {
            var raycaster = pageObject.GetComponent<GraphicRaycaster>();
            if (raycaster)
            {
                UnityEngine.Object.DestroyImmediate(raycaster);
            }

            var scaler = pageObject.GetComponent<CanvasScaler>();
            if (scaler)
            {
                UnityEngine.Object.DestroyImmediate(scaler);
            }

            var canvas = pageObject.GetComponent<Canvas>();
            if (canvas)
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void ApplyRect(RectTransform rect, LanhuFrame frame, LanhuFrame parentFrame)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(frame.Left - parentFrame.Left, -(frame.Top - parentFrame.Top));
            rect.sizeDelta = new Vector2(Mathf.Max(0.01f, frame.Width), Mathf.Max(0.01f, frame.Height));
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private static void ApplyImage(
            GameObject gameObject,
            Sprite sprite,
            Color color,
            bool suppressed,
            bool applySprite = true,
            bool applyStyle = true)
        {
            var image = GetOrAdd<Image>(gameObject);
            if (applySprite)
            {
                image.sprite = sprite;
            }

            if (applyStyle)
            {
                image.color = color;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.raycastTarget = false;
            }

            image.enabled = !suppressed;

            var text = gameObject.GetComponent<TextMeshProUGUI>();
            if (text)
            {
                text.enabled = false;
            }
        }

        private static void ApplyText(
            GameObject gameObject,
            LanhuNode node,
            bool suppressed,
            LanhuImportReport report,
            bool applyContent = true,
            bool applyStyle = true)
        {
            var text = GetOrAdd<TextMeshProUGUI>(gameObject);
            var baseStyle = node.Text?.BaseStyle ?? node.Text?.Styles.FirstOrDefault();
            var font = baseStyle?.Font;
            if (applyContent)
            {
                text.text = BuildRichText(node.Text);
                text.richText = true;
            }

            if (applyStyle)
            {
                text.fontSize = Mathf.Max(1f, font?.Size ?? 14f);
                text.color = ColorWithOpacity(baseStyle?.Color?.ToUnityColor() ?? Color.white, node.Opacity);
                text.fontStyle = ResolveFontStyle(font);
                text.fontWeight = ResolveFontWeight(font);
                text.alignment = ResolveAlignment(font);
                text.enableAutoSizing = false;
                text.characterSpacing = 0f;
                text.lineSpacing = font != null && font.LineHeight > 0f
                    ? font.LineHeight - text.fontSize
                    : 0f;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.margin = Vector4.zero;
                text.raycastTarget = false;

                var fontAsset = FindFontAsset(font);
                if (fontAsset)
                {
                    text.font = fontAsset;
                }
                else
                {
                    var requestedFont = DescribeFont(font);
                    if (!string.IsNullOrWhiteSpace(requestedFont))
                    {
                        report.Warnings.Add($"Missing TMP Font Asset for '{requestedFont}'. Using the TMP default font. Add the matching font under Assets and create a TMP Font Asset, then pull this page again.");
                    }

                    if (TMP_Settings.defaultFontAsset)
                    {
                        text.font = TMP_Settings.defaultFontAsset;
                    }
                }

                ReportUnsupportedSegmentFonts(node.Text, font, report);

                LanhuTextMaterialUtility.Apply(text, node.Style);
            }

            text.enabled = !suppressed;

            var image = gameObject.GetComponent<Image>();
            if (image)
            {
                image.enabled = false;
            }

        }

        private static void DisableManagedGraphics(GameObject gameObject)
        {
            var image = gameObject.GetComponent<Image>();
            if (image)
            {
                image.enabled = false;
            }

            var text = gameObject.GetComponent<TextMeshProUGUI>();
            if (text)
            {
                text.enabled = false;
            }
        }

        private static string BuildRichText(LanhuTextData data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            if (data.Styles.Count <= 1)
            {
                return data.Value ?? string.Empty;
            }

            var concatenated = string.Concat(data.Styles.Select(style => style.Content));
            if (!string.Equals(concatenated, data.Value, StringComparison.Ordinal))
            {
                return data.Value ?? concatenated;
            }

            var baseStyle = data.BaseStyle ?? data.Styles[0];
            var baseColor = baseStyle.Color?.ToHex();
            var baseSize = baseStyle.Font?.Size ?? 0f;
            var builder = new StringBuilder();
            foreach (var style in data.Styles)
            {
                var segment = style.Content ?? string.Empty;
                var color = style.Color?.ToHex();
                var size = style.Font?.Size ?? 0f;
                var wrapColor = !string.IsNullOrWhiteSpace(color) && !string.Equals(color, baseColor, StringComparison.OrdinalIgnoreCase);
                var wrapSize = size > 0f && baseSize > 0f && !Mathf.Approximately(size, baseSize);
                var weight = style.Font?.EffectiveWeight ?? 400;
                var baseWeight = baseStyle.Font?.EffectiveWeight ?? 400;
                var wrapWeight = weight != baseWeight;
                var wrapItalic = style.Font?.Italic == true && baseStyle.Font?.Italic != true;
                var wrapUnderline = style.Font?.Underline == true && baseStyle.Font?.Underline != true;
                var wrapStrikethrough = style.Font?.Strikethrough == true && baseStyle.Font?.Strikethrough != true;

                if (wrapColor)
                {
                    segment = $"<color=#{color}>{segment}</color>";
                }

                if (wrapSize)
                {
                    segment = $"<size={Mathf.RoundToInt(size)}>{segment}</size>";
                }

                if (wrapWeight)
                {
                    segment = $"<font-weight={weight}>{segment}</font-weight>";
                }

                if (wrapItalic)
                {
                    segment = $"<i>{segment}</i>";
                }

                if (wrapUnderline)
                {
                    segment = $"<u>{segment}</u>";
                }

                if (wrapStrikethrough)
                {
                    segment = $"<s>{segment}</s>";
                }

                builder.Append(segment);
            }

            return builder.ToString();
        }

        private static FontStyles ResolveFontStyle(LanhuFontData font)
        {
            var style = FontStyles.Normal;
            if (font?.Bold == true)
            {
                style |= FontStyles.Bold;
            }

            if (font?.Italic == true)
            {
                style |= FontStyles.Italic;
            }

            if (font?.Underline == true)
            {
                style |= FontStyles.Underline;
            }

            if (font?.Strikethrough == true)
            {
                style |= FontStyles.Strikethrough;
            }

            return style;
        }

        private static FontWeight ResolveFontWeight(LanhuFontData font)
        {
            return font?.Bold == true ? FontWeight.Bold : FontWeight.Regular;
        }

        private static TextAlignmentOptions ResolveAlignment(LanhuFontData font)
        {
            var horizontal = (font?.Align ?? string.Empty).ToLowerInvariant();
            var vertical = (font?.VerticalAlignment ?? string.Empty).ToLowerInvariant();
            if (vertical.Contains("bottom"))
            {
                return horizontal.Contains("right") ? TextAlignmentOptions.BottomRight : horizontal.Contains("center") ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft;
            }

            if (vertical.Contains("top"))
            {
                return horizontal.Contains("right") ? TextAlignmentOptions.TopRight : horizontal.Contains("center") ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            }

            return horizontal.Contains("right") ? TextAlignmentOptions.Right : horizontal.Contains("center") ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        }

        private static TMP_FontAsset FindFontAsset(LanhuFontData font)
        {
            if (font == null)
            {
                return null;
            }

            var key = string.Join("|", font.PostScriptName, font.FamilyName, font.Type, font.EffectiveWeight, font.Italic);
            if (string.IsNullOrWhiteSpace(font.PostScriptName) && string.IsNullOrWhiteSpace(font.FamilyName))
            {
                return null;
            }

            if (FontCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            TMP_FontAsset bestAsset = null;
            var bestScore = 0;
            var bestPath = string.Empty;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (!asset)
                {
                    continue;
                }

                var score = ScoreFontAsset(font, asset);
                if (score > bestScore || score == bestScore && score > 0 && string.CompareOrdinal(path, bestPath) < 0)
                {
                    bestAsset = asset;
                    bestScore = score;
                    bestPath = path;
                }
            }

            if (bestScore < 60)
            {
                FontCache.Remove(key);
                return null;
            }

            FontCache[key] = bestAsset;
            return bestAsset;
        }

        private static int ScoreFontAsset(LanhuFontData source, TMP_FontAsset asset)
        {
            var sourcePostScript = NormalizeFontName(source.PostScriptName);
            var sourceFamily = NormalizeFontName(source.FamilyName);
            var sourceType = NormalizeFontName(source.Type);
            var sourceCombined = sourceFamily + sourceType;
            var assetName = NormalizeFontName(asset.name);
            var assetFamily = NormalizeFontName(asset.faceInfo.familyName);
            var assetStyle = NormalizeFontName(asset.faceInfo.styleName);
            var assetCombined = assetFamily + assetStyle;
            var score = 0;

            if (!string.IsNullOrEmpty(sourcePostScript) && (sourcePostScript == assetName || sourcePostScript == assetCombined)) score = Math.Max(score, 130);
            if (!string.IsNullOrEmpty(sourceCombined) && (sourceCombined == assetName || sourceCombined == assetCombined)) score = Math.Max(score, 120);
            if (!string.IsNullOrEmpty(sourceFamily) && sourceFamily == assetFamily) score = Math.Max(score, 90);
            if (NamesContainEachOther(sourcePostScript, assetName) || NamesContainEachOther(sourceCombined, assetName)) score = Math.Max(score, 75);
            if (NamesContainEachOther(sourceFamily, assetFamily)) score = Math.Max(score, 65);
            if (score == 0)
            {
                return 0;
            }

            var assetDescriptor = string.Join(" ", asset.name, asset.faceInfo.familyName, asset.faceInfo.styleName);
            var assetWeight = LanhuFontData.InferWeight(assetDescriptor, 400);
            var weightDifference = Mathf.Abs(source.EffectiveWeight - assetWeight);
            score += weightDifference == 0 ? 30 : weightDifference <= 100 ? 18 : weightDifference <= 200 ? 6 : -10;

            var assetItalic = assetDescriptor.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assetDescriptor.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) >= 0;
            score += source.Italic == assetItalic ? 10 : -10;
            return score;
        }

        private static bool NamesContainEachOther(string left, string right)
        {
            return left.Length >= 3 && right.Length >= 3 &&
                (left.IndexOf(right, StringComparison.Ordinal) >= 0 || right.IndexOf(left, StringComparison.Ordinal) >= 0);
        }

        private static string NormalizeFontName(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string DescribeFont(LanhuFontData font)
        {
            if (font == null)
            {
                return string.Empty;
            }

            var name = !string.IsNullOrWhiteSpace(font.PostScriptName) ? font.PostScriptName : font.FamilyName;
            return string.IsNullOrWhiteSpace(name) ? string.Empty : $"{name} ({font.EffectiveWeight}{(font.Italic ? " Italic" : string.Empty)})";
        }

        private static void ReportUnsupportedSegmentFonts(LanhuTextData textData, LanhuFontData baseFont, LanhuImportReport report)
        {
            if (textData == null || baseFont == null)
            {
                return;
            }

            var baseFamily = NormalizeFontName(baseFont.FamilyName);
            foreach (var style in textData.Styles)
            {
                var segmentFont = style?.Font;
                var segmentFamily = NormalizeFontName(segmentFont?.FamilyName);
                if (string.IsNullOrEmpty(segmentFamily) || segmentFamily == baseFamily)
                {
                    continue;
                }

                report.Warnings.Add($"Text node '{textData.Value}' mixes font families. TMP rich text keeps '{DescribeFont(baseFont)}' as the node font; split the source into separate text layers for exact per-family rendering.");
                return;
            }
        }

        private static string FindExistingPrefabPath(string folder, string projectId, string imageId)
        {
            var matches = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root = prefab ? prefab.GetComponentInChildren<LanhuRuntimeSyncRoot>(true) : null;
                if (root && root.ProjectId == projectId && root.ImageId == imageId)
                {
                    matches.Add(path);
                }
            }

            return matches
                .OrderByDescending(path => path.StartsWith(folder + "/", StringComparison.Ordinal))
                .ThenBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault() ?? string.Empty;
        }

        private static LanhuRuntimeSyncRoot FindSceneRoot(string projectId, string imageId)
        {
            return UnityEngine.Object.FindObjectsOfType<LanhuRuntimeSyncRoot>(true)
                .FirstOrDefault(root => root && root.gameObject.scene.IsValid() && root.ProjectId == projectId && root.ImageId == imageId);
        }

        private static GameObject EnsurePageRoot(GameObject prefabRoot, string designName)
        {
            var syncRoot = prefabRoot.GetComponentInChildren<LanhuRuntimeSyncRoot>(true);
            if (syncRoot && syncRoot.gameObject != prefabRoot)
            {
                return syncRoot.gameObject;
            }

            var pageObject = new GameObject(
                SafeObjectName(designName, "Lanhu UI"),
                typeof(RectTransform),
                typeof(LanhuRuntimeSyncRoot));
            pageObject.transform.SetParent(prefabRoot.transform, false);

            if (syncRoot)
            {
                var existingChildren = prefabRoot.transform.Cast<Transform>()
                    .Where(child => child.gameObject != pageObject)
                    .ToArray();
                foreach (var child in existingChildren)
                {
                    child.SetParent(pageObject.transform, false);
                }

                UnityEngine.Object.DestroyImmediate(syncRoot);
            }

            return pageObject;
        }

        private static GameObject CreateRootObject(string designName)
        {
            return new GameObject(
                $"{SafeObjectName(designName, "Lanhu UI")} Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
        }

        private static string BuildNewPrefabPath(string folder, string designName, string imageId)
        {
            var fileName = $"{SafePathPart(designName, "LanhuUI")}_{ShortId(imageId)}.prefab";
            return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");
        }

        private static void EnsureAssetFolder(string folder)
        {
            var normalized = NormalizeAssetFolder(folder, "Assets");
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            var parts = normalized.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private static string NormalizeAssetFolder(string value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Replace('\\', '/').Trim().Trim('/');
            if (normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                normalized = $"Assets/{normalized}";
            }

            return normalized;
        }

        private static string SafeObjectName(string value, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return normalized.Replace('/', '_').Replace('\\', '_');
        }

        private static string SafePathPart(string value, string fallback)
        {
            var name = SafeObjectName(value, fallback);
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(character => invalid.Contains(character) || character == ':' ? '_' : character).ToArray();
            var result = new string(chars).Trim(' ', '.', '_');
            if (result.Length > 64)
            {
                result = result.Substring(0, 64).TrimEnd(' ', '.', '_');
            }

            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        private static string ShortId(string value)
        {
            var compact = (value ?? string.Empty).Replace("-", string.Empty);
            return compact.Length <= 8 ? compact : compact.Substring(0, 8);
        }

        private static Color ColorWithOpacity(Color color, float opacity)
        {
            color.a *= Mathf.Clamp01(opacity);
            return color;
        }

        private static RectTransform GetOrAddRectTransform(GameObject gameObject)
        {
            var rect = gameObject.GetComponent<RectTransform>();
            return rect ? rect : gameObject.AddComponent<RectTransform>();
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component
        {
            var component = gameObject.GetComponent<T>();
            return component ? component : gameObject.AddComponent<T>();
        }

        private readonly struct SpriteRequest
        {
            public readonly string NodeId;
            public readonly string Name;
            public readonly string Url;

            public SpriteRequest(string nodeId, string name, string url)
            {
                NodeId = nodeId;
                Name = name;
                Url = url;
            }
        }
    }
}
