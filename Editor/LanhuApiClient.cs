using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace LanhuRuntimeSync.EditorTools
{
    internal sealed class LanhuAccessException : InvalidOperationException
    {
        public LanhuAccessException(string message) : base(message)
        {
        }
    }

    internal static class LanhuSessionStore
    {
        private const string CookiePrefsKey = "LanhuRuntimeSync.Cookie";

        private static readonly Regex CurlCookieHeaderRegex = new Regex(
            @"(?:^|\s)(?:-H|--header)(?:\s+|=)\s*\$?(?:'cookie:\s*(?<single>[^']*)'|\""cookie:\s*(?<double>[^\""\r\n]*)\""|cookie:\s*(?<bare>[^\s\\]+))",
            RegexOptions.IgnoreCase);

        private static readonly Regex CurlCookieOptionRegex = new Regex(
            @"(?:^|\s)(?:-b|--cookie)(?:\s+|=)\s*\$?(?:'(?<single>[^']*)'|\""(?<double>[^\""\r\n]*)\""|(?<bare>[^\s\\]+))",
            RegexOptions.IgnoreCase);

        public static string LoadCookie()
        {
            var environmentCookie = Environment.GetEnvironmentVariable("LANHU_COOKIE");
            return !string.IsNullOrWhiteSpace(environmentCookie)
                ? environmentCookie.Trim()
                : EditorPrefs.GetString(CookiePrefsKey, string.Empty);
        }

        public static void SaveCookie(string cookie)
        {
            var normalized = Normalize(cookie);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                EditorPrefs.DeleteKey(CookiePrefsKey);
                return;
            }

            EditorPrefs.SetString(CookiePrefsKey, normalized);
        }

        public static void ClearCookie()
        {
            EditorPrefs.DeleteKey(CookiePrefsKey);
        }

        public static string Normalize(string cookie)
        {
            var raw = cookie ?? string.Empty;
            var curlHeader = CurlCookieHeaderRegex.Match(raw);
            if (curlHeader.Success)
            {
                raw = ReadCapturedValue(curlHeader);
            }
            else
            {
                var curlCookie = CurlCookieOptionRegex.Match(raw);
                if (curlCookie.Success)
                {
                    raw = ReadCapturedValue(curlCookie);
                }
                else
                {
                    var copiedHeader = Regex.Match(raw, @"(?:^|\r?\n)\s*cookie:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
                    if (copiedHeader.Success)
                    {
                        raw = copiedHeader.Groups["value"].Value;
                    }
                    else if (LooksLikeCurl(raw))
                    {
                        return string.Empty;
                    }
                }
            }

            var normalized = raw.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
            const string headerPrefix = "Cookie:";
            if (normalized.StartsWith(headerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(headerPrefix.Length).Trim();
            }

            return normalized;
        }

        public static bool TryNormalize(string input, out string normalized, out string error)
        {
            normalized = Normalize(input);
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = LooksLikeCurl(input)
                    ? "未在这段 cURL 中找到 Cookie。请复制成功的蓝湖 API 请求，并确认 cURL 中包含 -b、--cookie 或 Cookie 请求头。"
                    : "Cookie 内容为空。";
                return false;
            }

            if (normalized.IndexOf('=') <= 0)
            {
                error = "Cookie 格式无法识别。请粘贴完整 Cookie 请求头，或成功蓝湖 API 请求的 Copy as cURL 内容。";
                return false;
            }

            return true;
        }

        private static string ReadCapturedValue(Match match)
        {
            foreach (var groupName in new[] { "single", "double", "bare" })
            {
                var group = match.Groups[groupName];
                if (group.Success)
                {
                    return group.Value;
                }
            }

            return string.Empty;
        }

        private static bool LooksLikeCurl(string value)
        {
            return Regex.IsMatch(value ?? string.Empty, @"^\s*curl(?:\.exe)?(?:\s|$)", RegexOptions.IgnoreCase);
        }
    }

    internal sealed class LanhuApiClient
    {
        private const string BaseUrl = "https://lanhuapp.com";
        private readonly string mCookie;

        public LanhuApiClient(string cookie)
        {
            mCookie = LanhuSessionStore.Normalize(cookie);
        }

        public async Task<IReadOnlyList<LanhuDesignInfo>> LoadDesignsAsync(LanhuSourceReference source)
        {
            var url = $"{BaseUrl}/api/project/images" +
                      $"?project_id={Escape(source.ProjectId)}" +
                      $"&team_id={Escape(source.TeamId)}" +
                      "&dds_status=1&position=1&show_cb_src=1&comment=1";
            var json = ParseApiResponse(await GetStringAsync(url, true), "project images");
            var images = json["data"]?["images"] as JArray;
            if (images == null)
            {
                throw new InvalidOperationException("Lanhu did not return a design list. Check the project URL and account access.");
            }

            return images.OfType<JObject>()
                .Select(LanhuDesignInfo.Parse)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToArray();
        }

        public async Task<LanhuDocument> LoadDocumentAsync(LanhuSourceReference source, LanhuDesignInfo design)
        {
            if (design == null || string.IsNullOrWhiteSpace(design.Id))
            {
                throw new ArgumentException("Select a Lanhu design before importing.", nameof(design));
            }

            var url = $"{BaseUrl}/api/project/image" +
                      "?dds_status=1" +
                      $"&image_id={Escape(design.Id)}" +
                      $"&team_id={Escape(source.TeamId)}" +
                      $"&project_id={Escape(source.ProjectId)}";
            var detailJson = ParseApiResponse(await GetStringAsync(url, true), "design detail");
            var version = (detailJson["result"]?["versions"] as JArray)?.OfType<JObject>().FirstOrDefault();
            var jsonUrl = LanhuDesignInfo.ReadString(version?["json_url"]);

            JObject layerJson = null;
            if (!string.IsNullOrWhiteSpace(jsonUrl))
            {
                var layerText = await GetStringAsync(jsonUrl, false);
                layerJson = JObject.Parse(layerText);
            }

            return LanhuDocument.Parse(source, design, detailJson, layerJson);
        }

        public Task<byte[]> DownloadAssetAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Asset URL is empty.", nameof(url));
            }

            return GetBytesAsync(url);
        }

        private async Task<string> GetStringAsync(string url, bool authenticated)
        {
            using (var client = CreateClient(authenticated))
            using (var response = await client.GetAsync(url))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (authenticated && IsAccessDenied(response.StatusCode, body))
                    {
                        var status = $"HTTP {(int)response.StatusCode}";
                        var message = string.IsNullOrWhiteSpace(mCookie)
                            ? $"这是私有蓝湖项目（{status}），浏览器登录状态不会自动共享给 Unity。请从已登录蓝湖的成功 API 请求中复制 Cookie 或 Copy as cURL，填入 Login Cookie 后点击 Save Local。"
                            : $"蓝湖拒绝了当前登录信息（{status}）。Cookie 可能已过期，复制内容可能不完整，或者当前蓝湖账号没有这个项目的访问权限。";
                        throw new LanhuAccessException(message);
                    }

                    throw new HttpRequestException($"Lanhu request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {Shorten(body)}");
                }

                if (body.TrimStart().StartsWith("<", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Lanhu returned an HTML login page. Refresh the local session cookie and retry.");
                }

                return body;
            }
        }

        private async Task<byte[]> GetBytesAsync(string url)
        {
            using (var client = CreateClient(false))
            using (var response = await client.GetAsync(url))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Lanhu asset download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
                }

                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        private HttpClient CreateClient(bool authenticated)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 LanhuRuntimeSync/1.0 UnityEditor");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://lanhuapp.com/web/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("request-from", "web");
            client.DefaultRequestHeaders.TryAddWithoutValidation("real-path", "/item/project/detailDetach");
            if (authenticated && !string.IsNullOrWhiteSpace(mCookie))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", mCookie);
            }

            return client;
        }

        private static JObject ParseApiResponse(string body, string operation)
        {
            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Lanhu {operation} response is not valid JSON. {exception.Message}");
            }

            var code = LanhuDesignInfo.ReadString(json["code"]);
            if (!string.IsNullOrWhiteSpace(code) && code != "0" && code != "00000" && code != "200")
            {
                var message = LanhuDesignInfo.ReadString(json["msg"] ?? json["message"]);
                throw new InvalidOperationException($"Lanhu {operation} failed. Code={code}, Message={message}");
            }

            return json;
        }

        private static bool IsAccessDenied(HttpStatusCode statusCode, string body)
        {
            var code = (int)statusCode;
            return code == 401 ||
                   code == 403 ||
                   code == 418 ||
                   (body ?? string.Empty).IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string Shorten(string value)
        {
            var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= 240 ? normalized : normalized.Substring(0, 240) + "...";
        }
    }
}
