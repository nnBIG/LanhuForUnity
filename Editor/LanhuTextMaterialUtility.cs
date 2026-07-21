using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LanhuRuntimeSync.EditorTools
{
    internal static class LanhuTextMaterialUtility
    {
        private const string MaterialFolder = "Assets/LanhuRuntimeSync/Generated/TMP Materials";
        private const string UnderlayInnerKeyword = "UNDERLAY_INNER";
        private const string MobileDistanceFieldShader = "TextMeshPro/Mobile/Distance Field";
        private const string DistanceFieldShader = "TextMeshPro/Distance Field";

        public static void Apply(TextMeshProUGUI text, LanhuVisualStyle style)
        {
            if (!text)
            {
                return;
            }

            RemoveLegacyUiEffects(text.gameObject);

            var baseMaterial = text.font ? text.font.material : text.fontSharedMaterial;
            if (!baseMaterial)
            {
                return;
            }

            var outlineColor = style?.OutlineColor?.ToUnityColor() ?? Color.clear;
            var shadowColor = style?.ShadowColor?.ToUnityColor() ?? Color.clear;
            var hasOutline = style != null && style.OutlineWidth > 0f && outlineColor.a > 0f;
            var hasShadow = style != null && shadowColor.a > 0f;
            if (!hasOutline && !hasShadow)
            {
                AssignMaterial(text, baseMaterial);
                return;
            }

            var pixelsPerEffectUnit = GetPixelsPerEffectUnit(text, baseMaterial);
            var outlineWidth = hasOutline
                ? Mathf.Clamp01(style.OutlineWidth / pixelsPerEffectUnit)
                : 0f;
            var shadowOffset = hasShadow
                ? new Vector2(
                    Mathf.Clamp(style.ShadowOffset.x / pixelsPerEffectUnit, -1f, 1f),
                    Mathf.Clamp(style.ShadowOffset.y / pixelsPerEffectUnit, -1f, 1f))
                : Vector2.zero;
            var shadowDilate = hasShadow
                ? Mathf.Clamp(style.ShadowSpread / pixelsPerEffectUnit, -1f, 1f)
                : 0f;
            var shadowSoftness = hasShadow
                ? Mathf.Clamp01(style.ShadowBlur / pixelsPerEffectUnit)
                : 0f;

            var material = GetOrCreateMaterial(
                text,
                baseMaterial,
                hasOutline,
                outlineColor,
                outlineWidth,
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
