using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace LanhuRuntimeSync.EditorTools
{
    [InitializeOnLoad]
    internal static class LanhuPackageUpdater
    {
        private const string PackageName = "io.github.nnbig.lanhu-for-unity";
        private const string DevelopmentVersion = "1.0.0";
        private const string AutoCheckPrefsKey = "LanhuRuntimeSync.Updater.AutoCheck";
        private const string LastCheckPrefsKey = "LanhuRuntimeSync.Updater.LastCheckUtcTicks";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

        private static bool sChecking;
        private static AddRequest sAddRequest;
        private static string sInstallingVersion;

        static LanhuPackageUpdater()
        {
            EditorApplication.delayCall += TryAutomaticCheck;
        }

        internal static bool AutoCheckEnabled
        {
            get => EditorPrefs.GetBool(AutoCheckPrefsKey, true);
            set => EditorPrefs.SetBool(AutoCheckPrefsKey, value);
        }

        internal static string CurrentVersion => LoadPackageContext()?.Version ?? DevelopmentVersion;

        internal static string InstallSource
        {
            get
            {
                var context = LoadPackageContext();
                return context == null ? "Unknown" : context.CanSelfUpdate ? "Git (automatic updates enabled)" : "Assets/development copy";
            }
        }

        [MenuItem("Tools/Lanhu Runtime Sync - Check for Updates")]
        private static void CheckFromMenu()
        {
            CheckForUpdates(true, true);
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Preferences/Lanhu Runtime Sync", SettingsScope.User)
            {
                label = "Lanhu Runtime Sync",
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("Plugin Updates", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Installed Version", CurrentVersion);
                    EditorGUILayout.LabelField("Install Source", InstallSource);
                    AutoCheckEnabled = EditorGUILayout.Toggle("Check Daily", AutoCheckEnabled);
                    EditorGUILayout.Space(5f);
                    if (GUILayout.Button("Check for Updates", GUILayout.Width(160f)))
                    {
                        CheckForUpdates(true, true);
                    }

                    var context = LoadPackageContext();
                    if (context != null && !context.CanSelfUpdate)
                    {
                        EditorGUILayout.HelpBox(
                            "This is an Assets/development copy. Install the Git package in Package Manager before using one-click updates.",
                            MessageType.Info);
                    }
                },
                keywords = new System.Collections.Generic.HashSet<string>(new[] { "Lanhu", "Update", "Package", "Version" })
            };
        }

        private static void TryAutomaticCheck()
        {
            if (Application.isBatchMode || !AutoCheckEnabled || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            var context = LoadPackageContext();
            if (context == null || !context.CanSelfUpdate || !ShouldCheckNow())
            {
                return;
            }

            CheckForUpdates(false, false);
        }

        private static async void CheckForUpdates(bool interactive, bool ignoreInterval)
        {
            if (sChecking)
            {
                if (interactive)
                {
                    EditorUtility.DisplayDialog("Lanhu Runtime Sync", "An update check is already running.", "OK");
                }

                return;
            }

            var context = LoadPackageContext();
            if (context == null || string.IsNullOrWhiteSpace(context.RepositoryUrl))
            {
                ShowError(interactive, "The package repository could not be read from package.json.");
                return;
            }

            if (!ignoreInterval && !ShouldCheckNow())
            {
                return;
            }

            sChecking = true;
            try
            {
                var remote = await LoadRemotePackageAsync(context);
                EditorPrefs.SetString(LastCheckPrefsKey, DateTime.UtcNow.Ticks.ToString());
                if (!SemanticVersion.TryParse(context.Version, out var currentVersion) ||
                    !SemanticVersion.TryParse(remote.Version, out var latestVersion))
                {
                    throw new InvalidOperationException($"Invalid package version. Installed='{context.Version}', remote='{remote.Version}'.");
                }

                if (latestVersion.CompareTo(currentVersion) <= 0)
                {
                    if (interactive)
                    {
                        EditorUtility.DisplayDialog("Lanhu Runtime Sync", $"Version {context.Version} is up to date.", "OK");
                    }

                    return;
                }

                var choice = EditorUtility.DisplayDialogComplex(
                    "Lanhu Runtime Sync Update",
                    $"Version {remote.Version} is available. Installed version: {context.Version}.",
                    context.CanSelfUpdate ? "Update Now" : "Installation Help",
                    "Open Repository",
                    "Later");
                if (choice == 1)
                {
                    Application.OpenURL(remote.RepositoryPage);
                }
                else if (choice == 0)
                {
                    if (context.CanSelfUpdate)
                    {
                        StartPackageUpdate(remote.InstallUrl, remote.Version);
                    }
                    else
                    {
                        ShowAssetsInstallHelp(remote.InstallUrl);
                    }
                }
            }
            catch (Exception exception)
            {
                ShowError(interactive, $"Update check failed: {exception.Message}");
            }
            finally
            {
                sChecking = false;
            }
        }

        private static async Task<RemotePackage> LoadRemotePackageAsync(PackageContext context)
        {
            if (!TryGetGitHubCoordinates(context.RepositoryUrl, out var owner, out var repository))
            {
                throw new InvalidOperationException("Automatic updates currently require a github.com repository URL.");
            }

            var directory = string.IsNullOrWhiteSpace(context.Directory)
                ? string.Empty
                : context.Directory.Trim('/').Replace(" ", "%20") + "/";
            var manifestUrl = $"https://raw.githubusercontent.com/{owner}/{repository}/{context.Branch}/{directory}package.json";
            string json;
            string commitJson;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"LanhuRuntimeSync/{context.Version}");
                json = await client.GetStringAsync(manifestUrl);
                commitJson = await client.GetStringAsync($"https://api.github.com/repos/{owner}/{repository}/commits/{context.Branch}");
            }

            var manifest = JObject.Parse(json);
            var commitSha = JObject.Parse(commitJson).Value<string>("sha");
            var remoteName = manifest.Value<string>("name");
            var remoteVersion = manifest.Value<string>("version");
            if (!string.Equals(remoteName, PackageName, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(remoteVersion) || string.IsNullOrWhiteSpace(commitSha))
            {
                throw new InvalidOperationException("The remote package metadata is missing the expected name, version, or commit SHA.");
            }

            var repositoryUrl = NormalizeRepositoryUrl(context.RepositoryUrl);
            var pathQuery = string.IsNullOrWhiteSpace(context.Directory) ? string.Empty : $"?path=/{context.Directory.Trim('/')}";
            return new RemotePackage
            {
                Version = remoteVersion,
                InstallUrl = $"{repositoryUrl}{pathQuery}#{commitSha}",
                RepositoryPage = repositoryUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? repositoryUrl.Substring(0, repositoryUrl.Length - 4)
                    : repositoryUrl
            };
        }

        private static void StartPackageUpdate(string installUrl, string version)
        {
            if (sAddRequest != null && !sAddRequest.IsCompleted)
            {
                EditorUtility.DisplayDialog("Lanhu Runtime Sync", "A Package Manager update is already running.", "OK");
                return;
            }

            sInstallingVersion = version;
            sAddRequest = Client.Add(installUrl);
            EditorApplication.update += MonitorPackageUpdate;
            Debug.Log($"[LanhuRuntimeSync] Updating package to {version} from {installUrl}.");
        }

        private static void MonitorPackageUpdate()
        {
            if (sAddRequest == null || !sAddRequest.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= MonitorPackageUpdate;
            if (sAddRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[LanhuRuntimeSync] Package update to {sInstallingVersion} completed.");
            }
            else
            {
                var message = sAddRequest.Error?.message ?? "Unknown Package Manager error.";
                Debug.LogError($"[LanhuRuntimeSync] Package update failed: {message}");
                EditorUtility.DisplayDialog("Lanhu Runtime Sync", $"Package update failed:\n{message}", "OK");
            }

            sAddRequest = null;
            sInstallingVersion = string.Empty;
        }

        private static PackageContext LoadPackageContext()
        {
            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LanhuPackageUpdater).Assembly);
                var manifestPath = packageInfo != null
                    ? Path.Combine(packageInfo.resolvedPath, "package.json")
                    : Path.GetFullPath("Assets/LanhuRuntimeSync/package.json");
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var repository = manifest["repository"];
                var repositoryUrl = repository?.Type == JTokenType.String
                    ? repository.ToString()
                    : repository?["url"]?.ToString();
                var directory = repository?.Type == JTokenType.Object ? repository["directory"]?.ToString() : string.Empty;
                return new PackageContext
                {
                    Version = packageInfo?.version ?? manifest.Value<string>("version") ?? DevelopmentVersion,
                    RepositoryUrl = repositoryUrl ?? string.Empty,
                    Directory = directory ?? string.Empty,
                    Branch = manifest.Value<string>("lanhuUpdaterBranch") ?? "main",
                    CanSelfUpdate = packageInfo != null && packageInfo.source == PackageSource.Git
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LanhuRuntimeSync] Could not read package metadata: {exception.Message}");
                return null;
            }
        }

        private static bool ShouldCheckNow()
        {
            if (!long.TryParse(EditorPrefs.GetString(LastCheckPrefsKey, string.Empty), out var ticks))
            {
                return true;
            }

            try
            {
                return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) >= CheckInterval;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

        private static bool TryGetGitHubCoordinates(string repositoryUrl, out string owner, out string repository)
        {
            owner = string.Empty;
            repository = string.Empty;
            var normalized = NormalizeRepositoryUrl(repositoryUrl);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2)
            {
                return false;
            }

            owner = parts[0];
            repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? parts[1].Substring(0, parts[1].Length - 4)
                : parts[1];
            return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repository);
        }

        private static string NormalizeRepositoryUrl(string repositoryUrl)
        {
            var result = (repositoryUrl ?? string.Empty).Trim();
            if (result.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(4);
            }

            var separator = result.IndexOfAny(new[] { '?', '#' });
            if (separator >= 0)
            {
                result = result.Substring(0, separator);
            }

            return result.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? result : result + ".git";
        }

        private static void ShowAssetsInstallHelp(string installUrl)
        {
            EditorGUIUtility.systemCopyBuffer = installUrl;
            EditorUtility.DisplayDialog(
                "Lanhu Runtime Sync",
                "This project uses an Assets/development copy, which cannot safely replace itself. The latest Git install URL was copied to the clipboard. Remove the Assets copy, then use Package Manager > Add package from git URL.",
                "OK");
        }

        private static void ShowError(bool interactive, string message)
        {
            if (interactive)
            {
                EditorUtility.DisplayDialog("Lanhu Runtime Sync", message, "OK");
            }
            else
            {
                Debug.LogWarning($"[LanhuRuntimeSync] {message}");
            }
        }

        private sealed class PackageContext
        {
            public string Version;
            public string RepositoryUrl;
            public string Directory;
            public string Branch;
            public bool CanSelfUpdate;
        }

        private sealed class RemotePackage
        {
            public string Version;
            public string InstallUrl;
            public string RepositoryPage;
        }

        private struct SemanticVersion : IComparable<SemanticVersion>
        {
            private int Major;
            private int Minor;
            private int Patch;
            private bool Prerelease;

            public static bool TryParse(string raw, out SemanticVersion version)
            {
                version = default;
                var value = (raw ?? string.Empty).Trim().TrimStart('v', 'V');
                var metadataIndex = value.IndexOf('+');
                if (metadataIndex >= 0) value = value.Substring(0, metadataIndex);
                var prereleaseIndex = value.IndexOf('-');
                var prerelease = prereleaseIndex >= 0;
                if (prerelease) value = value.Substring(0, prereleaseIndex);
                var parts = value.Split('.');
                if (parts.Length < 3 || !int.TryParse(parts[0], out var major) ||
                    !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch))
                {
                    return false;
                }

                version = new SemanticVersion { Major = major, Minor = minor, Patch = patch, Prerelease = prerelease };
                return true;
            }

            public int CompareTo(SemanticVersion other)
            {
                var result = Major.CompareTo(other.Major);
                if (result != 0) return result;
                result = Minor.CompareTo(other.Minor);
                if (result != 0) return result;
                result = Patch.CompareTo(other.Patch);
                if (result != 0) return result;
                return other.Prerelease.CompareTo(Prerelease);
            }
        }
    }
}
