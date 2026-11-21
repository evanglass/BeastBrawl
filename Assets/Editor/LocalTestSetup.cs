using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class LocalTestSetup
{
    internal const string ScenePath = LANController.LocalEditorScenePath;
    private const string ProfilePath = "Assets/Settings/Build Profiles/Local Multiplayer Test.asset";

    [MenuItem("Tools/Feral Fighter/Open Local Test")]
    private static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ApplyProfile();
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath);
        Selection.activeGameObject = Object.FindFirstObjectByType<GameGlobals>()?.gameObject;
    }

    internal static void ApplyProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
        if (profile != null && BuildProfile.GetActiveBuildProfile() != profile)
            BuildProfile.SetActiveBuildProfile(profile);
    }
}
