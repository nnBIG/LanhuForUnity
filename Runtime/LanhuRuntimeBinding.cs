using UnityEngine;

namespace LanhuRuntimeSync
{
    [DisallowMultipleComponent]
    public sealed class LanhuRuntimeBinding : MonoBehaviour
    {
        public const string CoverNodeId = "__lanhu_cover__";

        [Header("Lanhu Node")]
        [SerializeField] private string mProjectId;
        [SerializeField] private string mImageId;
        [SerializeField] private string mNodeId;
        [SerializeField] private string mNodeType;
        [SerializeField] private string mSourcePath;

        [Header("Imported Resource")]
        [SerializeField] private string mLastImageUrl;
        [SerializeField] private string mLastAssetPath;

        [Header("Sync Fields")]
        [SerializeField] private bool mSyncTransform = true;
        [SerializeField] private bool mSyncVisibility = true;
        [SerializeField] private bool mSyncText = true;
        [SerializeField] private bool mSyncImage = true;
        [SerializeField] private bool mSyncStyle = true;

        public string ProjectId => mProjectId;
        public string ImageId => mImageId;
        public string NodeId => mNodeId;
        public string NodeType => mNodeType;
        public string SourcePath => mSourcePath;
        public string LastImageUrl => mLastImageUrl;
        public string LastAssetPath => mLastAssetPath;
        public bool SyncTransform => mSyncTransform;
        public bool SyncVisibility => mSyncVisibility;
        public bool SyncText => mSyncText;
        public bool SyncImage => mSyncImage;
        public bool SyncStyle => mSyncStyle;
        public bool IsCover => mNodeId == CoverNodeId;
        public bool HasSource => !string.IsNullOrWhiteSpace(mNodeId);

        public void SetSource(string projectId, string imageId, string nodeId, string nodeType, string sourcePath)
        {
            mProjectId = projectId ?? string.Empty;
            mImageId = imageId ?? string.Empty;
            mNodeId = nodeId ?? string.Empty;
            mNodeType = nodeType ?? string.Empty;
            mSourcePath = sourcePath ?? string.Empty;
        }

        public void SetImageSource(string imageUrl, string assetPath)
        {
            mLastImageUrl = imageUrl ?? string.Empty;
            mLastAssetPath = assetPath ?? string.Empty;
        }
    }
}
