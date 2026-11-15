using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class SceneLoader : MonoBehaviour
{
    [SerializeField]
    private string menuScene;

    [SerializeField]
    private List<string> arenaScenes;

    private int currentArenaIndex = -1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        LoadMenu();
    }

    public void LoadMenu()
    {
        if (!SceneManager.GetSceneByName(menuScene).isLoaded)
            SceneManager.LoadScene(menuScene, LoadSceneMode.Additive);
    }

    public int GetArenaCount()
    {
        return arenaScenes.Count;
    }

    public void LoadArena(int index)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (index < 0 || index >= arenaScenes.Count)
        {
            Debug.LogError($"Invalid arena index: {index}");
            return;
        }
        
        var status = NetworkManager.Singleton.SceneManager.LoadScene(arenaScenes[index], LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning($"Could not load arena: {status}");
            return;
        }

        if (SceneManager.GetSceneByName(menuScene).isLoaded)
            SceneManager.UnloadSceneAsync(menuScene);

        currentArenaIndex = index;
    }
}
