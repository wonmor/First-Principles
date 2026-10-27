#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 1.4 Bauhaus typography: one geometric sans, <b>Outfit</b> (OFL, Assets/Fonts/Outfit-OFL.txt), as real
/// Regular / SemiBold / Bold weights (static instances cut from the bundled variable font, whose default
/// instance is Thin) plus <b>Inter</b> (OFL) as the fallback for math symbols Outfit lacks (∑ ∫ ∂ ′ √ θ π).
/// Creates dynamic TMP assets under Resources/Fonts, makes Outfit the TMP default, and retargets every
/// TextMesh Pro component in the scenes and prefabs.
/// Batch: <c>-executeMethod BauhausFontSetup.RunBatch</c>.
/// </summary>
public static class BauhausFontSetup
{
    const string Dir = "Assets/Resources/Fonts/";
    public const string RegularAsset = Dir + "Outfit SDF.asset";
    const string SemiBoldAsset = Dir + "Outfit SemiBold SDF.asset";
    const string BoldAsset = Dir + "Outfit Bold SDF.asset";
    const string MathAsset = Dir + "Inter Math SDF.asset";
    const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [MenuItem("First Principles/Bauhaus/Apply fonts (Outfit + Inter math fallback)")]
    public static void Run() => Apply();

    public static void RunBatch() => EditorApplication.Exit(Apply() ? 0 : 1);

    static bool Apply()
    {
        AssetDatabase.Refresh();
        var math = GetOrCreate(MathAsset, "Assets/Fonts/Inter-VariableFont_opsz,wght.ttf", "Inter Math SDF", null);
        var regular = GetOrCreate(RegularAsset, "Assets/Fonts/Outfit-Regular.ttf", "Outfit SDF", math);
        var semi = GetOrCreate(SemiBoldAsset, "Assets/Fonts/Outfit-SemiBold.ttf", "Outfit SemiBold SDF", math);
        var bold = GetOrCreate(BoldAsset, "Assets/Fonts/Outfit-Bold.ttf", "Outfit Bold SDF", math);
        if (regular == null || semi == null || bold == null)
            return false;

        // Real weights for <b> / FontStyles.Bold and <font-weight=600>.
        var table = regular.fontWeightTable;
        table[4].regularTypeface = regular;
        table[6].regularTypeface = semi;
        table[7].regularTypeface = bold;
        EditorUtility.SetDirty(regular);

        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (settings == null)
        {
            Debug.LogError("[Bauhaus] TMP Settings missing at " + TmpSettingsPath);
            return false;
        }
        var so = new SerializedObject(settings);
        so.FindProperty("m_defaultFontAsset").objectReferenceValue = regular;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Retarget(regular);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bauhaus] Fonts applied: Outfit (Regular/SemiBold/Bold) + Inter math fallback.");
        return true;
    }

    static TMP_FontAsset GetOrCreate(string assetPath, string ttfPath, string name, TMP_FontAsset fallback)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (asset == null)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogError("[Bauhaus] Missing font " + ttfPath);
                return null;
            }
            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null)
                return null;
            asset.name = name;
            AssetDatabase.CreateAsset(asset, assetPath);
            // Atlas texture + material live inside the font asset file.
            if (asset.atlasTextures != null)
                foreach (var t in asset.atlasTextures)
                    if (t != null && !AssetDatabase.Contains(t)) { t.name = name + " Atlas"; AssetDatabase.AddObjectToAsset(t, asset); }
            if (asset.material != null && !AssetDatabase.Contains(asset.material))
            {
                asset.material.name = name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
        }

        if (fallback != null)
        {
            asset.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!asset.fallbackFontAssetTable.Contains(fallback))
                asset.fallbackFontAssetTable.Insert(0, fallback);
        }
        EditorUtility.SetDirty(asset);
        return asset;
    }

    static void Retarget(TMP_FontAsset font)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }))
        {
            var scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(guid), OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    tmp.font = font;
                    tmp.fontSharedMaterial = font.material;
                    tmp.outlineWidth = 0f;
                    EditorUtility.SetDirty(tmp);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool any = false;
                foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    tmp.font = font;
                    tmp.fontSharedMaterial = font.material;
                    any = true;
                }
                if (any)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
