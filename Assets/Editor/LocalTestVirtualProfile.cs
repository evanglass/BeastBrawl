using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Multiplayer Play Mode clones have their own active scene list and profile.
[InitializeOnLoad]
internal static class LocalTestVirtualProfile
{
    static LocalTestVirtualProfile()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += Configure;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == LocalTestSetup.ScenePath) Configure();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) Configure();
    }

    private static void Configure()
    {
        if (!EditorApplication.isPlaying && EditorSceneManager.GetActiveScene().path == LocalTestSetup.ScenePath)
            LocalTestSetup.ApplyProfile();
    }
}
