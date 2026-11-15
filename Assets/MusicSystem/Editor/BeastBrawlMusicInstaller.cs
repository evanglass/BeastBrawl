#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BeastBrawl.Music;

namespace BeastBrawl.Music.Editor
{
    public static class BeastBrawlMusicInstaller
    {
        private const string Folder = "Assets/MusicSystem";
        private const string ScenePath = Folder + "/Scenes/Music_Test.unity";

        [MenuItem("Tools/BeastBrawl/Install Music Test Scene")]
        public static void Install()
        {
            MakeFolder("Assets", "MusicSystem");
            MakeFolder(Folder, "Scenes");
            MakeFolder(Folder, "Resources");
            MakeFolder(Folder + "/Resources", "Music");
            MakeFolder(Folder + "/Resources/Music", "Grooves");
            MakeFolder(Folder + "/Resources/Music", "Masters");
            MakeFolder(Folder + "/Resources/Music", "Melodies");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                Debug.Log("Music test scene already exists; leaving it unchanged: " + ScenePath);
                return;
            }

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject cameraObject = new GameObject("Main Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true;
                camera.backgroundColor = new Color(0.07f, 0.07f, 0.12f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                cameraObject.AddComponent<AudioListener>();
                cameraObject.transform.position = new Vector3(0, 0, -10);

                GameObject manager = new GameObject("MusicManager");
                manager.AddComponent<BeastBrawlMusicManager>();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new System.Exception("Unable to save Music_Test scene");
                Debug.Log("Created music test scene: " + ScenePath);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.Refresh();
        }

        private static void MakeFolder(string parent, string name)
        {
            string child = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
