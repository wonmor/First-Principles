using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates Assets/Scenes/Space3D.unity (one GameObject with <see cref="Space3DScene"/>; everything else is
/// built at runtime) and makes sure it is in the build settings. Idempotent.
///   Unity -batchmode -quit -projectPath "First Principles" -executeMethod Space3DSceneSetup.Create
/// </summary>
public static class Space3DSceneSetup
{
    public const string ScenePath = "Assets/Scenes/Space3D.unity";

    [MenuItem("Build/Create Space3D scene")]
    public static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Space3D");
            go.AddComponent<Space3DScene>();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("Space3DSceneSetup: created " + ScenePath);
        }

        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == ScenePath))
        {
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Space3DSceneSetup: added to build settings");
        }

        AssetDatabase.SaveAssets();
    }
}
