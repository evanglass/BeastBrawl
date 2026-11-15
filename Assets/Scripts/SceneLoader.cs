using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
using System;

public class SceneLoader : MonoBehaviour
{
    [SerializeField]
    private string menuScene;

    [SerializeField]
    private List<string> arenaScenes;

    private int currentArenaIndex = -1;
    private NetworkSceneManager pendingSceneManager;
    private string pendingArena;
    private string pendingUnload;
    private int pendingArenaIndex = -1;

    public event Action<bool> ArenaLoadFinished;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
#if !UNITY_SERVER || UNITY_EDITOR
        LoadMenu();
#endif
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsClient && !networkManager.IsServer && arenaScenes.Contains(scene.name))
            UnloadMenu();
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

    public bool LoadArena(int index)
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer)
            return false;

        if (pendingSceneManager != null)
        {
            Debug.LogWarning("An arena is already loading.");
            return false;
        }

        if (index < 0 || index >= arenaScenes.Count)
        {
            Debug.LogError($"Invalid arena index: {index}");
            return false;
        }
        
        pendingSceneManager = networkManager.SceneManager;
        pendingArena = arenaScenes[index];
        pendingArenaIndex = index;

        if (currentArenaIndex >= 0)
        {
            Scene previous = SceneManager.GetSceneByName(arenaScenes[currentArenaIndex]);
            if (previous.IsValid() && previous.isLoaded)
            {
                pendingUnload = previous.name;
                pendingSceneManager.OnUnloadEventCompleted += OnArenaUnloadCompleted;
                var unloadStatus = pendingSceneManager.UnloadScene(previous);
                if (unloadStatus == SceneEventProgressStatus.Started)
                {
                    FindFirstObjectByType<RoundManager>()?.ClearArena();
                    return true;
                }

                Debug.LogWarning($"Could not unload arena: {unloadStatus}");
                StopWaitingForArena();
                return false;
            }
        }

        return StartArenaLoad();
    }

    private bool StartArenaLoad()
    {
        pendingSceneManager.OnLoadEventCompleted += OnArenaLoadCompleted;
        var status = pendingSceneManager.LoadScene(pendingArena, LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            StopWaitingForArena();
            Debug.LogWarning($"Could not load arena: {status}");
            return false;
        }

        FindFirstObjectByType<RoundManager>()?.ClearArena();
        UnloadMenu();
        currentArenaIndex = pendingArenaIndex;
        return true;
    }

    private void OnArenaUnloadCompleted(string sceneName, LoadSceneMode loadMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != pendingUnload)
            return;

        pendingSceneManager.OnUnloadEventCompleted -= OnArenaUnloadCompleted;
        pendingUnload = null;
        currentArenaIndex = -1;

        if (clientsTimedOut.Count > 0)
        {
            StopWaitingForArena();
            ArenaLoadFinished?.Invoke(false);
            return;
        }

        StartCoroutine(LoadAfterUnload());
    }

    private IEnumerator LoadAfterUnload()
    {
        yield return null;

        if (pendingSceneManager != null && !StartArenaLoad())
            ArenaLoadFinished?.Invoke(false);
    }

    public void UnloadMenu()
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.name == menuScene)
                SceneManager.UnloadSceneAsync(scene);
        }
    }

    public void ReturnToMenu()
    {
        StartCoroutine(ReturnToMenuRoutine());
    }

    private IEnumerator ReturnToMenuRoutine()
    {
        StopWaitingForArena();
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        while (networkManager != null && networkManager.IsListening)
            yield return null;

        foreach (string arenaName in arenaScenes)
        {
            Scene arena = SceneManager.GetSceneByName(arenaName);
            if (arena.IsValid() && arena.isLoaded)
                yield return SceneManager.UnloadSceneAsync(arena);
        }

        currentArenaIndex = -1;
        LoadMenu();
    }

    private void OnArenaLoadCompleted(string sceneName, LoadSceneMode loadMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != pendingArena || loadMode != LoadSceneMode.Additive)
            return;

        StopWaitingForArena();
        if (clientsTimedOut.Count > 0)
        {
            Debug.LogWarning($"Arena {sceneName} did not finish loading for every player.");
            ArenaLoadFinished?.Invoke(false);
            return;
        }

        var roundManager = FindFirstObjectByType<RoundManager>();
        if (roundManager == null)
        {
            Debug.LogError("Cannot prepare arena: no RoundManager was found.");
            ArenaLoadFinished?.Invoke(false);
            return;
        }

        bool arenaReady = roundManager.SetArena(SceneManager.GetSceneByName(sceneName));
        ArenaLoadFinished?.Invoke(arenaReady);
    }

    private void StopWaitingForArena()
    {
        if (pendingSceneManager != null)
        {
            pendingSceneManager.OnLoadEventCompleted -= OnArenaLoadCompleted;
            pendingSceneManager.OnUnloadEventCompleted -= OnArenaUnloadCompleted;
        }
        pendingSceneManager = null;
        pendingArena = null;
        pendingUnload = null;
        pendingArenaIndex = -1;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        StopWaitingForArena();
    }
}
