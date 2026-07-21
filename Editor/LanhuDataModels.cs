using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LanhuRuntimeSync.EditorTools
{
    internal sealed class LanhuSourceReference
    {
        public string OriginalUrl { get; private set; }
        public string TeamId { get; private set; }
        public string ProjectId { get; private set; }

        public static bool TryParse(string rawUrl, out LanhuSourceReference source, out string error)
        {
            source = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            {
                error = "Enter a valid Lanhu project URL.";
                return false;
            }

            if (!uri.Host.EndsWith("lanhuapp.com", StringComparison.OrdinalIgnoreCase))
            {
                error = "The URL must use the lanhuapp.com domain.";
                return false;
            }

            var parameters = ParseParameters(rawUrl);
            var teamId = Find(parameters, "tid", "teamId", "team_id");
            var projectId = Find(parameters, "pid", "projectId", "project_id");
            if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(projectId))
            {
                error = "The Lanhu URL is missing tid or pid.";
                return false;
            }

            source = new LanhuSourceReference
            {
                OriginalUrl = rawUrl.Trim(),
                TeamId = teamId,
                ProjectId = projectId
            };
            return true;
        }

        private static Dictionary<string, string> ParseParameters(string rawUrl)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queryStart = rawUrl.IndexOf('?');
            if (queryStart < 0 || queryStart >= rawUrl.Length - 1)
            {
                return result;
            }

            var query = rawUrl.Substring(queryStart + 1);
            var hash = query.IndexOf('#');
            if (hash >= 0)
            {
                query = query.Substring(0, hash);
            }

            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                var separator = pair.IndexOf('=');
                var key = separator >= 0 ? pair.Substring(0, separator) : pair;
                var value = separator >= 0 ? pair.Substring(separator + 1) : string.Empty;
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }

            return result;
        }

        private static string Find(IReadOnlyDictionary<string, string> parameters, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }

    internal sealed class LanhuDesignInfo
    {
        public string Id;
        public string Name;
        public float Width;
        public float Height;
        public string CoverUrl;
        public string UpdateTime;
        public string LatestVersionId;
        public string SketchId;
        public string Type;

        public bool HasLayerData => !string.IsNullOrWhiteSpace(SketchId);

        public static LanhuDesignInfo Parse(JObject json)
        {
            return new LanhuDesignInfo
            {
                Id = ReadString(json["id"]),
                Name = ReadString(json["name"]),
                Width = ReadFloat(json["width"]),
                Height = ReadFloat(json["height"]),
                CoverUrl = ReadString(json["url"]),
                UpdateTime = ReadString(json["update_time"]),
                LatestVersionId = ReadString(json["latest_version"]),
                SketchId = ReadString(json["sketch_id"]),
                Type = ReadString(json["type"])
            };
        }

        internal static string ReadString(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString();
        }

        internal static float ReadFloat(JToken token, float fallback = 0f)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            if (float.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return fallback;
        }

        internal static bool ReadBool(JToken token, bool fallback = false)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return fallback;
            }

            return bool.TryParse(token.ToString(), out var value) ? value : fallback;
        }
    }

    internal sealed class LanhuDocument
    {
        public LanhuSourceReference Source;
        public LanhuDesignInfo Design;
        public string VersionId;
        public string VersionInfo;
        public string CoverUrl;
        public string JsonUrl;
        public LanhuNode Root;
        public readonly List<string> Warnings = new List<string>();

        public bool HasLayerData => Root != null;
        public int ExportedImageCount => Root == null ? 0 : Root.DescendantsAndSelf().Count(node => node.HasImage);
        public int TextCount => Root == null ? 0 : Root.DescendantsAndSelf().Count(node => node.IsText);
        public int HiddenCount => Root == null ? 0 : Root.DescendantsAndSelf().Count(node => !node.Visible);

        public static LanhuDocument Parse(
            LanhuSourceReference source,
            LanhuDesignInfo design,
            JObject detailJson,
            JObject layerJson)
        {
            var result = detailJson?["result"] as JObject;
            var version = (result?["versions"] as JArray)?.OfType<JObject>().FirstOrDefault();
            var document = new LanhuDocument
            {
                Source = source,
                Design = design,
                VersionId = LanhuDesignInfo.ReadString(version?["id"] ?? result?["latest_version"]),
                VersionInfo = LanhuDesignInfo.ReadString(version?["version_info"]),
                CoverUrl = LanhuDesignInfo.ReadString(version?["url"] ?? result?["url"] ?? design?.CoverUrl),
                JsonUrl = LanhuDesignInfo.ReadString(version?["json_url"])
            };

            if (layerJson == null)
            {
                document.Warnings.Add("This design has no layer JSON. The importer can only use its full-page cover.");
                return document;
            }

            var artboard = layerJson["artboard"] as JObject;
            if (artboard == null && layerJson["board"] is JObject board)
            {
                artboard = board["artboard"] as JObject ?? board;
            }

            if (artboard == null)
            {
                document.Warnings.Add("Layer JSON does not contain an artboard root.");
                return document;
            }

            var hostName = LanhuDesignInfo.ReadString(layerJson["meta"]?["host"]?["name"]);
            var pluginName = LanhuDesignInfo.ReadString(layerJson["meta"]?["plugin"]?["name"]);
            var usesPhotoshopShadowEncoding = hostName.IndexOf("photoshop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              pluginName.IndexOf("photoshop", StringComparison.OrdinalIgnoreCase) >= 0;
            document.Root = LanhuNode.Parse(artboard, string.Empty, true, usesPhotoshopShadowEncoding, document.Warnings);
            if (document.Root != null)
            {
                document.Root.Visible = true;
                var invalidFrames = document.Root.DescendantsAndSelf().Count(node => node.Frame.HadNegativeSize);
                if (invalidFrames > 0)
                {
                    document.Warnings.Add($"{invalidFrames} nodes contain negative frame dimensions from the Lanhu source. Their sizes were normalized, but old Photoshop uploads may need visual adjustment.");
                }
            }

            if (document.ExportedImageCount == 0)
            {
                document.Warnings.Add("蓝湖没有返回逐层图片。可选择只重建 TMP 文本和纯色形状；如需完整分层，请在源稿中把按钮、图标等标记为切片或导出资源后重新上传。");
            }

            return document;
        }
    }

    internal sealed class LanhuNode
    {
        public string Id;
        public string OriginId;
        public string Name;
        public string Type;
        public string Path;
        public bool Visible;
        public float Opacity;
        public LanhuFrame Frame;
        public string ImageUrl;
        public LanhuTextData Text;
        public LanhuVisualStyle Style;
        public readonly List<LanhuNode> Children = new List<LanhuNode>();

        public bool IsText => string.Equals(Type, "textLayer", StringComparison.OrdinalIgnoreCase) || Text != null;
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
        public bool HasSolidFill => Style != null && Style.FillColor.HasValue;

        public IEnumerable<LanhuNode> DescendantsAndSelf()
        {
            yield return this;
            foreach (var child in Children)
            {
                foreach (var descendant in child.DescendantsAndSelf())
                {
                    yield return descendant;
                }
            }
        }

        public static LanhuNode Parse(
            JObject json,
            string parentPath,
            bool isRoot,
            bool usesPhotoshopShadowEncoding,
            ICollection<string> warnings)
        {
            if (json == null)
            {
                return null;
            }

            var name = LanhuDesignInfo.ReadString(json["name"]);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = isRoot ? "Lanhu UI" : "Unnamed Node";
            }

            var id = LanhuDesignInfo.ReadString(json["id"]);
            var originId = LanhuDesignInfo.ReadString(json["originID"]);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = originId;
            }

            var pathPart = string.IsNullOrWhiteSpace(id) ? name : $"{name}#{id}";
            var path = string.IsNullOrWhiteSpace(parentPath) ? pathPart : $"{parentPath}/{pathPart}";
            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"path:{path}";
                warnings?.Add($"Node '{path}' has no source ID and uses its path as a fallback identity.");
            }

            var node = new LanhuNode
            {
                Id = id,
                OriginId = originId,
                Name = name,
                Type = LanhuDesignInfo.ReadString(json["type"]),
                Path = path,
                Visible = isRoot || LanhuDesignInfo.ReadBool(json["visible"], true),
                Opacity = ReadOpacity(json["opacity"], 1f),
                Frame = LanhuFrame.Parse((isRoot ? json["realFrame"] : null) as JObject ?? json["frame"] as JObject),
                ImageUrl = ReadImageUrl(json),
                Text = LanhuTextData.Parse(json["text"] as JObject),
                Style = LanhuVisualStyle.Parse(json["style"] as JObject, usesPhotoshopShadowEncoding)
            };

            if (json["layers"] is JArray layers)
            {
                foreach (var childJson in layers.OfType<JObject>())
                {
                    var child = Parse(childJson, path, false, usesPhotoshopShadowEncoding, warnings);
                    if (child != null)
                    {
                        node.Children.Add(child);
                    }
                }
            }

            return node;
        }

        private static float ReadOpacity(JToken token, float fallback)
        {
            var value = LanhuDesignInfo.ReadFloat(token, fallback);
            return Mathf.Clamp01(value > 1f ? value / 100f : value);
        }

        private static string ReadImageUrl(JObject json)
        {
            var image = json["image"] as JObject;
            var smartObject = json["smartObject"] as JObject;
            return FirstString(
                image?["imageUrl"],
                image?["url"],
                image?["downloadUrl"],
                smartObject?["imageUrl"],
                smartObject?["url"]);
        }

        private static string FirstString(params JToken[] values)
        {
            foreach (var value in values)
            {
                var text = LanhuDesignInfo.ReadString(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return string.Empty;
        }
    }

    internal struct LanhuFrame
    {
        public float Left;
        public float Top;
        public float Width;
        public float Height;
        public bool HadNegativeSize;

        public static LanhuFrame Parse(JObject json)
        {
            var left = LanhuDesignInfo.ReadFloat(json?["left"]);
            var top = LanhuDesignInfo.ReadFloat(json?["top"]);
            var width = LanhuDesignInfo.ReadFloat(json?["width"]);
            var height = LanhuDesignInfo.ReadFloat(json?["height"]);
            return new LanhuFrame
            {
                Left = width < 0f ? left + width : left,
                Top = height < 0f ? top + height : top,
                Width = Mathf.Abs(width),
                Height = Mathf.Abs(height),
                HadNegativeSize = width < 0f || height < 0f
            };
        }
    }

    internal sealed class LanhuTextData
    {
        public string Value;
        public LanhuTextStyle BaseStyle;
        public readonly List<LanhuTextStyle> Styles = new List<LanhuTextStyle>();

        public static LanhuTextData Parse(JObject json)
        {
            if (json == null)
            {
                return null;
            }

            var result = new LanhuTextData
            {
                Value = LanhuDesignInfo.ReadString(json["value"]),
                BaseStyle = LanhuTextStyle.Parse(json["style"] as JObject)
            };

            if (json["styles"] is JArray styles)
            {
                foreach (var styleJson in styles.OfType<JObject>())
                {
                    result.Styles.Add(LanhuTextStyle.Parse(styleJson));
                }
            }

            if (result.Styles.Count == 0 && result.BaseStyle != null)
            {
                result.Styles.Add(result.BaseStyle);
            }

            if (string.IsNullOrEmpty(result.Value))
            {
                result.Value = result.Styles.Count == 0
                    ? string.Empty
                    : string.Concat(result.Styles.Select(style => style.Content));
            }

            return result;
        }
    }

    internal sealed class LanhuTextStyle
    {
        public int From;
        public int To;
        public string Content;
        public LanhuColor? Color;
        public LanhuFontData Font;

        public static LanhuTextStyle Parse(JObject json)
        {
            if (json == null)
            {
                return null;
            }

            return new LanhuTextStyle
            {
                From = Mathf.RoundToInt(LanhuDesignInfo.ReadFloat(json["from"])),
                To = Mathf.RoundToInt(LanhuDesignInfo.ReadFloat(json["to"])),
                Content = LanhuDesignInfo.ReadString(json["content"]),
                Color = LanhuColor.ParseNullable(json["color"] as JObject),
                Font = LanhuFontData.Parse(json["font"] as JObject)
            };
        }
    }

    internal sealed class LanhuFontData
    {
        public string FamilyName;
        public string PostScriptName;
        public string Type;
        public string Align;
        public string VerticalAlignment;
        public float Size;
        public float LineHeight;
        public float LetterSpacing;
        public int Weight;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;

        public int EffectiveWeight => Weight > 0 ? Weight : Bold ? 700 : InferWeight(Type + " " + PostScriptName, 400);
        public bool IsBold => Bold || EffectiveWeight >= 600;

        public static LanhuFontData Parse(JObject json)
        {
            if (json == null)
            {
                return null;
            }

            var familyName = FirstString(json["name"], json["fontFamily"], json["familyName"]);
            var postScriptName = FirstString(json["postScriptName"], json["postscriptName"]);
            var type = FirstString(json["type"], json["style"], json["fontStyle"]);
            var descriptor = string.Join(" ", familyName, postScriptName, type, LanhuDesignInfo.ReadString(FirstToken(json["weight"], json["fontWeight"])));
            var size = Mathf.Max(1f, ReadMetric(json["size"], 14f));
            var weight = Mathf.RoundToInt(ReadMetric(FirstToken(json["weight"], json["fontWeight"]), 0f));
            weight = weight > 0 ? NormalizeWeight(weight) : InferWeight(descriptor, 400);
            var decoration = FirstString(json["textDecoration"], json["decoration"]);

            return new LanhuFontData
            {
                FamilyName = familyName,
                PostScriptName = postScriptName,
                Type = type,
                Align = LanhuDesignInfo.ReadString(json["align"]),
                VerticalAlignment = LanhuDesignInfo.ReadString(json["verticalAlignment"]),
                Size = size,
                LineHeight = ReadLineHeight(json["lineHeight"], size),
                LetterSpacing = ReadMetric(json["letterSpacing"]),
                Weight = weight,
                Bold = ReadFlag(json["bold"]),
                Italic = ReadFlag(json["italic"]) || ContainsAny(descriptor, "italic", "oblique"),
                Underline = ReadFlag(FirstToken(json["underline"], json["isUnderline"])) || ContainsAny(decoration, "underline"),
                Strikethrough = ReadFlag(FirstToken(json["strikethrough"], json["strikeThrough"], json["isStrikethrough"])) ||
                    ContainsAny(decoration, "line-through", "strikethrough", "strike-through")
            };
        }

        internal static int InferWeight(string descriptor, int fallback)
        {
            var value = (descriptor ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            if (ContainsAny(value, "thin", "hairline")) return 100;
            if (ContainsAny(value, "extralight", "ultralight")) return 200;
            if (ContainsAny(value, "light")) return 300;
            if (ContainsAny(value, "medium")) return 500;
            if (ContainsAny(value, "semibold", "demibold")) return 600;
            if (ContainsAny(value, "extrabold", "ultrabold", "heavy")) return 800;
            if (ContainsAny(value, "black")) return 900;
            if (ContainsAny(value, "bold")) return 700;
            if (ContainsAny(value, "regular", "normal", "book")) return 400;
            return fallback;
        }

        private static int NormalizeWeight(int value)
        {
            return Mathf.Clamp(Mathf.RoundToInt(value / 100f) * 100, 100, 900);
        }

        private static float ReadLineHeight(JToken token, float fontSize)
        {
            var value = ReadMetric(token);
            var unit = LanhuDesignInfo.ReadString((token as JObject)?["unit"]);
            if (value > 0f && (unit.Contains("%") || unit.IndexOf("percent", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return fontSize * value / 100f;
            }

            var raw = token?.Type == JTokenType.String ? token.ToString().Trim() : string.Empty;
            if (raw.EndsWith("%", StringComparison.Ordinal) &&
                float.TryParse(raw.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
            {
                return fontSize * percentage / 100f;
            }

            return value;
        }

        private static float ReadMetric(JToken token, float fallback = 0f)
        {
            if (token is JObject valueObject)
            {
                token = FirstToken(valueObject["value"], valueObject["amount"], valueObject["px"]);
            }

            return LanhuDesignInfo.ReadFloat(token, fallback);
        }

        private static bool ReadFlag(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (bool.TryParse(token.ToString(), out var boolean))
            {
                return boolean;
            }

            return Mathf.Abs(LanhuDesignInfo.ReadFloat(token)) > Mathf.Epsilon;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return !string.IsNullOrWhiteSpace(value) && terms.Any(term => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FirstString(params JToken[] tokens)
        {
            return LanhuDesignInfo.ReadString(FirstToken(tokens));
        }

        private static JToken FirstToken(params JToken[] tokens)
        {
            return tokens.FirstOrDefault(token => token != null && token.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(token.ToString()));
        }
    }

    internal sealed class LanhuVisualStyle
    {
        public LanhuColor? FillColor;
        public LanhuColor? OutlineColor;
        public float OutlineWidth;
        public LanhuColor? ShadowColor;
        public Vector2 ShadowOffset;
        public float ShadowBlur;
        public float ShadowSpread;

        public static LanhuVisualStyle Parse(JObject json, bool usesPhotoshopShadowEncoding = false)
        {
            if (json == null)
            {
                return null;
            }

            var result = new LanhuVisualStyle();
            var fill = (json["fills"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(item => LanhuDesignInfo.ReadBool(item["isEnabled"], true) && string.Equals(LanhuDesignInfo.ReadString(item["type"]), "color", StringComparison.OrdinalIgnoreCase));
            if (fill != null)
            {
                result.FillColor = LanhuColor.ParseNullable(fill["color"] as JObject, ReadOpacity(fill["opacity"], 1f));
            }

            var border = (json["borders"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(item => LanhuDesignInfo.ReadBool(item["isEnabled"], true));
            if (border != null)
            {
                result.OutlineColor = LanhuColor.ParseNullable(border["color"] as JObject, ReadOpacity(border["opacity"], 1f));
                result.OutlineWidth = Mathf.Max(0f, LanhuDesignInfo.ReadFloat(border["width"]));
            }

            var shadow = (json["shadows"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(item => LanhuDesignInfo.ReadBool(item["isEnabled"], true) && !LanhuDesignInfo.ReadBool(item["inset"]));
            if (shadow != null)
            {
                var offset = shadow["offset"] as JObject;
                var rawX = ReadMetric(shadow["x"], shadow["offsetX"], offset?["x"]);
                var rawY = ReadMetric(shadow["y"], shadow["offsetY"], offset?["y"]);
                var rawBlur = Mathf.Max(0f, ReadMetric(shadow["blur"], shadow["blurRadius"], shadow["radius"]));
                var rawSpread = ReadMetric(shadow["spread"], shadow["spreadRadius"]);
                result.ShadowColor = LanhuColor.ParseNullable(shadow["color"] as JObject, ReadOpacity(shadow["opacity"], 1f));
                if (usesPhotoshopShadowEncoding)
                {
                    var spreadRatio = Mathf.Clamp01(rawSpread / 100f);
                    var angleRadians = rawY * Mathf.Deg2Rad;
                    result.ShadowOffset = new Vector2(
                        -Mathf.Cos(angleRadians) * rawX,
                        -Mathf.Sin(angleRadians) * rawX);
                    result.ShadowSpread = rawBlur * spreadRatio;
                    result.ShadowBlur = rawBlur * (1f - spreadRatio);
                }
                else
                {
                    result.ShadowOffset = new Vector2(rawX, -rawY);
                    result.ShadowBlur = rawBlur;
                    result.ShadowSpread = rawSpread;
                }
            }

            return result;
        }

        private static float ReadOpacity(JToken token, float fallback)
        {
            var value = LanhuDesignInfo.ReadFloat(token, fallback);
            return Mathf.Clamp01(value > 1f ? value / 100f : value);
        }

        private static float ReadMetric(params JToken[] tokens)
        {
            foreach (var source in tokens)
            {
                if (source == null || source.Type == JTokenType.Null)
                {
                    continue;
                }

                var token = source;
                if (source is JObject valueObject)
                {
                    token = valueObject["value"] ?? valueObject["amount"] ?? valueObject["px"];
                }

                if (token != null && token.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(token.ToString()))
                {
                    return LanhuDesignInfo.ReadFloat(token);
                }
            }

            return 0f;
        }
    }

    internal struct LanhuColor
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public Color ToUnityColor(float opacity = 1f)
        {
            return new Color(R, G, B, Mathf.Clamp01(A * opacity));
        }

        public string ToHex()
        {
            return ColorUtility.ToHtmlStringRGBA(ToUnityColor());
        }

        public static LanhuColor? ParseNullable(JObject json, float opacity = 1f)
        {
            if (json == null)
            {
                return null;
            }

            var r = LanhuDesignInfo.ReadFloat(json["r"]);
            var g = LanhuDesignInfo.ReadFloat(json["g"]);
            var b = LanhuDesignInfo.ReadFloat(json["b"]);
            var divisor = Mathf.Max(r, Mathf.Max(g, b)) <= 1f ? 1f : 255f;
            var alpha = LanhuDesignInfo.ReadFloat(json["a"], 1f);
            var alphaDivisor = alpha <= 1f ? 1f : alpha <= 100f ? 100f : 255f;
            return new LanhuColor
            {
                R = Mathf.Clamp01(r / divisor),
                G = Mathf.Clamp01(g / divisor),
                B = Mathf.Clamp01(b / divisor),
                A = Mathf.Clamp01(alpha / alphaDivisor * opacity)
            };
        }
    }
}
