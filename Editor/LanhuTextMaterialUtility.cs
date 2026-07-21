using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LanhuRuntimeSync.EditorTools
{
    internal static class LanhuTextMaterialUtility
    {
        private const string MaterialFolder = "Assets/LanhuRuntimeSync/Generated/TMP Materials";
        private const string EffectFontFolder = "Assets/LanhuRuntimeSync/Generated/TMP Fonts";
        private const string UnderlayInnerKeyword = "UNDERLAY_INNER";
        private const string MobileDistanceFieldShader = "TextMeshPro/Mobile/Distance Field";
        private const string DistanceFieldShader = "TextMeshPro/Distance Field";
        private const int EffectFontAtlasPadding = 32;
        private const int EffectFontAtlasSize = 2048;
        private const float EffectThicknessCorrection = 0.5f;

        public static void Apply(TextMeshProUGUI text, LanhuVisualStyle style)
        {
            if (!text)
            {
                return;
            }

            RemoveLegacyUiEffects(text.gameObject);

            var outlineColor = ToTextEffectColorSpace(style?.OutlineColor?.ToUnityColor() ?? Color.clear);
            var shadowColor = ToTextEffectColorSpace(style?.ShadowColor?.ToUnityColor() ?? Color.clear);
            var hasOutline = style != null && style.OutlineWidth > 0f && outlineColor.a > 0f;
            var hasShadow = style != null && shadowColor.a > 0f;

            if (hasOutline || hasShadow)
            {
                AssignEffectFont(text);
            }

            var baseMaterial = text.font ? text.font.material : text.fontSharedMaterial;
            if (!baseMaterial)
            {
                return;
            }

            if (!hasOutline && !hasShadow)
            {
                AssignMaterial(text, baseMaterial);
                return;
            }

            var pixelsPerEffectUnit = GetPixelsPerEffectUnit(text, baseMaterial);
            var outlineWidth = hasOutline
                ? Mathf.Clamp01(style.OutlineWidth * EffectThicknessCorrection / pixelsPerEffectUnit)
                : 0f;
            var faceDilate = hasOutline
                ? ResolveFaceDilate(style.OutlineAlignment, outlineWidth)
                : 0f;
            var shadowOffset = hasShadow
                ? new Vector2(
                    Mathf.Clamp(style.ShadowOffset.x / pixelsPerEffectUnit, -1f, 1f),
                    Mathf.Clamp(style.ShadowOffset.y / pixelsPerEffectUnit, -1f, 1f))
                : Vector2.zero;
            var shadowDilate = hasShadow
                ? Mathf.Clamp(style.ShadowSpread * EffectThicknessCorrection / pixelsPerEffectUnit, -1f, 1f)
                : 0f;
            var shadowSoftness = hasShadow
                ? Mathf.Clamp01(style.ShadowBlur * EffectThicknessCorrection / pixelsPerEffectUnit)
                : 0f;

            var material = GetOrCreateMaterial(
                text,
                baseMaterial,
                hasOutline,
                outlineColor,
                outlineWidth,
                faceDilate,
                hasShadow,
                shadowColor,
                shadowOffset,
                shadowDilate,
                shadowSoftness);
            if (!material)
            {
                AssignMaterial(text, baseMaterial);
                return;
            }

            ConfigureMaterial(
                material,
                baseMaterial,
                hasOutline,
                outlineColor,
                outlineWidth,
                faceDilate,
                hasShadow,
                shadowColor,
                shadowOffset,
                shadowDilate,
                shadowSoftness);
            AssignMaterial(text, material);
            EditorUtility.SetDirty(material);
        }

        private static Material GetOrCreateMaterial(
            TMP_Text text,
            Material baseMaterial,
            bool hasOutline,
            Color outlineColor,
            float outlineWidth,
            float faceDilate,
            bool hasShadow,
            Color shadowColor,
            Vector2 shadowOffset,
            float shadowDilate,
            float shadowSoftness)
        {
            var signature = BuildSignature(
                text,
                baseMaterial,
                hasOutline,
                outlineColor,
                outlineWidth,
                faceDilate,
                hasShadow,
                shadowColor,
                shadowOffset,
                shadowDilate,
                shadowSoftness);
            var effectName = $"{SafeName(text.font ? text.font.name : baseMaterial.name)}_{(hasOutline ? "Outline" : "NoOutline")}_{(hasShadow ? "Shadow" : "NoShadow")}_{StableHash(signature)}";
            EnsureAssetFolder(MaterialFolder);
            var assetPath = $"{MaterialFolder}/{effectName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material)
            {
                return material;
            }

            material = new Material(baseMaterial)
            {
                name = effectName
            };
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        private static void ConfigureMaterial(
            Material material,
            Material baseMaterial,
            bool hasOutline,
            Color outlineColor,
            float outlineWidth,
            float faceDilate,
            bool hasShadow,
            Color shadowColor,
            Vector2 shadowOffset,
            float shadowDilate,
            float shadowSoftness)
        {
            material.CopyPropertiesFromMaterial(baseMaterial);
            material.shader = Shader.Find(MobileDistanceFieldShader) ??
                              Shader.Find(DistanceFieldShader) ??
                              baseMaterial.shader;

            SetKeyword(material, ShaderUtilities.Keyword_Outline, hasOutline);
            SetColor(material, ShaderUtilities.ID_OutlineColor, hasOutline ? outlineColor : Color.clear);
            SetFloat(material, ShaderUtilities.ID_OutlineWidth, hasOutline ? outlineWidth : 0f);
            SetFloat(material, ShaderUtilities.ID_OutlineSoftness, 0f);
            SetFloat(material, ShaderUtilities.ID_FaceDilate, hasOutline ? faceDilate : 0f);

            SetKeyword(material, ShaderUtilities.Keyword_Underlay, hasShadow);
            material.DisableKeyword(UnderlayInnerKeyword);
            SetColor(material, ShaderUtilities.ID_UnderlayColor, hasShadow ? shadowColor : Color.clear);
            SetFloat(material, ShaderUtilities.ID_UnderlayOffsetX, hasShadow ? shadowOffset.x : 0f);
            SetFloat(material, ShaderUtilities.ID_UnderlayOffsetY, hasShadow ? shadowOffset.y : 0f);
            SetFloat(material, ShaderUtilities.ID_UnderlayDilate, hasShadow ? shadowDilate : 0f);
            SetFloat(material, ShaderUtilities.ID_UnderlaySoftness, hasShadow ? shadowSoftness : 0f);

            DisableAutoScaleRatios(material);
            ShaderUtilities.UpdateShaderRatios(material);
            DisableAutoScaleRatios(material);
        }

        private static string BuildSignature(
            TMP_Text text,
            Material baseMaterial,
            bool hasOutline,
            Color outlineColor,
            float outlineWidth,
            float faceDilate,
            bool hasShadow,
            Color shadowColor,
            Vector2 shadowOffset,
            float shadowDilate,
            float shadowSoftness)
        {
            var fontPath = text.font ? AssetDatabase.GetAssetPath(text.font) : string.Empty;
            var fontKey = string.IsNullOrWhiteSpace(fontPath)
                ? baseMaterial.name
                : AssetDatabase.AssetPathToGUID(fontPath);
            var builder = new StringBuilder(fontKey);
            builder.Append(hasOutline ? "|OL|" : "|NoOL|");
            if (hasOutline)
            {
                builder.Append(ColorUtility.ToHtmlStringRGBA(outlineColor));
                builder.Append('|').Append(Number(outlineWidth));
                builder.Append('|').Append(Number(faceDilate));
            }

            builder.Append(hasShadow ? "|UL|" : "|NoUL|");
            if (hasShadow)
            {
                builder.Append(ColorUtility.ToHtmlStringRGBA(shadowColor));
                builder.Append('|').Append(Number(shadowOffset.x));
                builder.Append('|').Append(Number(shadowOffset.y));
                builder.Append('|').Append(Number(shadowDilate));
                builder.Append('|').Append(Number(shadowSoftness));
            }

            return builder.ToString();
        }

        private static float GetPixelsPerEffectUnit(TMP_Text text, Material baseMaterial)
        {
            var fontSize = Mathf.Max(1f, text.fontSize);
            var pointSize = text.font ? Mathf.Max(1f, text.font.faceInfo.pointSize) : fontSize;
            var gradientScale = baseMaterial.HasProperty(ShaderUtilities.ID_GradientScale)
                ? Mathf.Max(1f, baseMaterial.GetFloat(ShaderUtilities.ID_GradientScale))
                : text.font && text.font.atlasPadding > 0
                    ? text.font.atlasPadding + 1f
                    : 5f;
            return Mathf.Max(0.001f, gradientScale * fontSize / pointSize);
        }

        private static float ResolveFaceDilate(string alignment, float outlineWidth)
        {
            if (string.Equals(alignment, "outside", StringComparison.OrdinalIgnoreCase))
            {
                return outlineWidth;
            }

            if (string.Equals(alignment, "inside", StringComparison.OrdinalIgnoreCase))
            {
                return -outlineWidth;
            }

            return 0f;
        }

        private static void AssignEffectFont(TextMeshProUGUI text)
        {
            var sourceAsset = text.font;
            if (!sourceAsset || sourceAsset.atlasPadding >= EffectFontAtlasPadding || !sourceAsset.sourceFontFile)
            {
                return;
            }

            var sourceFontPath = AssetDatabase.GetAssetPath(sourceAsset.sourceFontFile);
            var sourceFontGuid = AssetDatabase.AssetPathToGUID(sourceFontPath);
            if (string.IsNullOrWhiteSpace(sourceFontGuid))
            {
                return;
            }

            EnsureAssetFolder(EffectFontFolder);
            var fontName = $"{SafeName(sourceAsset.name)}_LanhuEffectP{EffectFontAtlasPadding}_{StableHash(sourceFontGuid).Substring(0, 8)}";
            var assetPath = $"{EffectFontFolder}/{fontName}.asset";
            var effectAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (!effectAsset)
            {
                var pointSize = Mathf.Max(16, Mathf.RoundToInt(sourceAsset.faceInfo.pointSize));
                effectAsset = TMP_FontAsset.CreateFontAsset(
                    sourceAsset.sourceFontFile,
                    pointSize,
                    EffectFontAtlasPadding,
                    GlyphRenderMode.SDFAA,
                    EffectFontAtlasSize,
                    EffectFontAtlasSize,
                    AtlasPopulationMode.Dynamic,
                    true);
                if (!effectAsset)
                {
                    return;
                }

                effectAsset.name = fontName;
                effectAsset.normalStyle = sourceAsset.normalStyle;
                effectAsset.normalSpacingOffset = sourceAsset.normalSpacingOffset;
                effectAsset.boldStyle = sourceAsset.boldStyle;
                effectAsset.boldSpacing = sourceAsset.boldSpacing;
                effectAsset.fallbackFontAssetTable = sourceAsset.fallbackFontAssetTable;
                effectAsset.atlasTextures[0].name = fontName + " Atlas";
                effectAsset.material.name = fontName + " Material";
                effectAsset.material.SetFloat(ShaderUtilities.ID_WeightNormal, effectAsset.normalStyle);
                effectAsset.material.SetFloat(ShaderUtilities.ID_WeightBold, effectAsset.boldStyle);

                AssetDatabase.CreateAsset(effectAsset, assetPath);
                PersistFontSubAssets(effectAsset);
            }

            string parsedText;
            try
            {
                text.ForceMeshUpdate(true, true);
                parsedText = text.GetParsedText();
            }
            catch
            {
                parsedText = string.Empty;
            }

            if (!string.IsNullOrEmpty(parsedText))
            {
                effectAsset.TryAddCharacters(parsedText, out _);
                PersistFontSubAssets(effectAsset);
            }

            if (text.font != effectAsset)
            {
                text.font = effectAsset;
            }

            EditorUtility.SetDirty(effectAsset);
        }

        private static void PersistFontSubAssets(TMP_FontAsset fontAsset)
        {
            foreach (var texture in fontAsset.atlasTextures ?? Array.Empty<Texture2D>())
            {
                if (!texture || AssetDatabase.Contains(texture))
                {
                    continue;
                }

                texture.name = string.IsNullOrWhiteSpace(texture.name)
                    ? fontAsset.name + " Atlas"
                    : texture.name;
                AssetDatabase.AddObjectToAsset(texture, fontAsset);
                EditorUtility.SetDirty(texture);
            }

            if (fontAsset.material && !AssetDatabase.Contains(fontAsset.material))
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                EditorUtility.SetDirty(fontAsset.material);
            }
        }

        private static Color ToTextEffectColorSpace(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        private static void AssignMaterial(TMP_Text text, Material material)
        {
            if (text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
            }

            text.UpdateMeshPadding();
            text.SetVerticesDirty();
            text.SetMaterialDirty();
            EditorUtility.SetDirty(text);
        }

        private static void RemoveLegacyUiEffects(GameObject gameObject)
        {
            foreach (var effect in gameObject.GetComponents<Shadow>())
            {
                if (effect && (effect.GetType() == typeof(Shadow) || effect.GetType() == typeof(Outline)))
                {
                    UnityEngine.Object.DestroyImmediate(effect);
                }
            }
        }

        private static void DisableAutoScaleRatios(Material material)
        {
            material.EnableKeyword(ShaderUtilities.Keyword_Ratios);
            SetFloat(material, ShaderUtilities.ID_ScaleRatio_A, 1f);
            SetFloat(material, ShaderUtilities.ID_ScaleRatio_B, 1f);
            SetFloat(material, ShaderUtilities.ID_ScaleRatio_C, 1f);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }

        private static void SetFloat(Material material, int propertyId, float value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetFloat(propertyId, value);
            }
        }

        private static void SetColor(Material material, int propertyId, Color value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetColor(propertyId, value);
            }
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
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

        private static string SafeName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string((value ?? "TMP")
                .Select(character => invalid.Contains(character) || character == '/' || character == '\\' || character == ':' ? '_' : character)
                .ToArray())
                .Trim(' ', '.', '_');
            if (safe.Length > 48)
            {
                safe = safe.Substring(0, 48).TrimEnd(' ', '.', '_');
            }

            return string.IsNullOrWhiteSpace(safe) ? "TMP" : safe;
        }

        private static string Number(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= 16777619;
                }

                return hash.ToString("x8");
            }
        }
    }
}
