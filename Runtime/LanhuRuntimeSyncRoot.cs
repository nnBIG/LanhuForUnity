using System.Collections.Generic;
using UnityEngine;

namespace LanhuRuntimeSync
{
    [DisallowMultipleComponent]
    public sealed class LanhuRuntimeSyncRoot : MonoBehaviour
    {
        [Header("Lanhu Source")]
        [SerializeField] private string mSourceUrl;
        [SerializeField] private string mTeamId;
        [SerializeField] private string mProjectId;
        [SerializeField] private string mImageId;
        [SerializeField] private string mVersionId;
        [SerializeField] private string mDesignName;
        [SerializeField, HideInInspector] private string mJsonUrl;

        [Header("Import Options")]
        [SerializeField] private bool mSkipHiddenNodes = true;
        [SerializeField] private bool mDeleteMissingNodes = true;
        [SerializeField] private bool mUseCoverFallback = true;
        [SerializeField] private bool mRedownloadSprites;

        public string SourceUrl => mSourceUrl;
        public string TeamId => mTeamId;
        public string ProjectId => mProjectId;
        public string ImageId => mImageId;
        public string VersionId => mVersionId;
        public string DesignName => mDesignName;
        public string JsonUrl => mJsonUrl;
        public bool SkipHiddenNodes => mSkipHiddenNodes;
        public bool DeleteMissingNodes => mDeleteMissingNodes;
        public bool UseCoverFallback => mUseCoverFallback;
        public bool RedownloadSprites => mRedownloadSprites;

        public void SetSource(
            string sourceUrl,
            string teamId,
            string projectId,
            string imageId,
            string versionId,
            string designName,
            string jsonUrl)
        {
            mSourceUrl = sourceUrl ?? string.Empty;
            mTeamId = teamId ?? string.Empty;
            mProjectId = projectId ?? string.Empty;
            mImageId = imageId ?? string.Empty;
            mVersionId = versionId ?? string.Empty;
            mDesignName = designName ?? string.Empty;
            mJsonUrl = jsonUrl ?? string.Empty;
        }

        public void SetImportOptions(bool skipHiddenNodes, bool deleteMissingNodes, bool useCoverFallback, bool redownloadSprites)
        {
            mSkipHiddenNodes = skipHiddenNodes;
            mDeleteMissingNodes = deleteMissingNodes;
            mUseCoverFallback = useCoverFallback;
            mRedownloadSprites = redownloadSprites;
        }

        public IReadOnlyList<LanhuRuntimeBinding> GetBindings()
        {
            return GetComponentsInChildren<LanhuRuntimeBinding>(true);
        }
    }
}
