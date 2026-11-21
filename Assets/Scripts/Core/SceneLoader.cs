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

    private readonly List<Camera> suspendedMenuCameras = new();

    private int currentArenaIndex = -1;
    private NetworkSceneManager pendingSceneManager;
    private string pendingArena;
    private string pendingUnload;
    private int pendingArenaIndex = -1;

    public bool ReturningToMenu { get; private set; }
    public const string EnchantedForestScene = "Arena_05_EnchantedForest";
    public static readonly string[] CustomArenas = { "Arena_001", "Arena_02", "Arena_03", "Arena_04_Preview", EnchantedForestScene };
    public event Action<bool> ArenaLoadFinished;

    private void Awake()
    {
        // Add the fifth arena to the standard rotation. Custom games still replace this list.
        if (arenaScenes != null && !arenaScenes.Contains(EnchantedForestScene))
            arenaScenes.Add(EnchantedForestScene);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
#if !UNITY_SERVER || UNITY_EDITOR
        LoadMenu();
#endif
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var networkManager = NetworkManager.Singleton;
        bool isArena = Array.IndexOf(CustomArenas, scene.name) >= 0;
        if (SharedArenaCamera.ActiveCamera != null &&
            (isArena || (scene.name == menuScene && HasLoadedArena())))
            SuspendMenuCameras();
        if (scene.name == "Arena_04_Preview")
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.name == "Forest Preview Camera") camera.enabled = false;
        }
        if (networkManager == null || !networkManager.IsClient || networkManager.IsServer ||
            (!arenaScenes.Contains(scene.name) && !isArena)) return;

        var match = FindFirstObjectByType<MatchManager>();
        match?.GetComponent<RoundManager>()?.CacheRecoveryGeometry(scene);
        if (match == null || !match.HasLobbyMember(networkManager.LocalClientId)) return;
        UnloadMenu();
    }

    // The overlay menu may still be loaded while a client finishes joining an arena.
    private void SuspendMenuCameras()
    {
        Scene menu = SceneManager.GetSceneByName(menuScene);
        if (!menu.isLoaded) return;
        suspendedMenuCameras.RemoveAll(camera => camera == null);
        foreach (var root in menu.GetRootGameObjects())
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.enabled || camera == SharedArenaCamera.ActiveCamera) continue;
                camera.enabled = false;
                suspendedMenuCameras.Add(camera);
            }
    }

    private bool HasLoadedArena()
    {
        foreach (string arena in CustomArenas)
            if (SceneManager.GetSceneByName(arena).isLoaded) return true;
        return false;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (suspendedMenuCameras.Count == 0 || HasLoadedArena()) return;
        RestoreMenuCameras();
    }

    private void RestoreMenuCameras()
    {
        foreach (var camera in suspendedMenuCameras)
            if (camera != null) camera.enabled = true;
        suspendedMenuCameras.Clear();
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

    internal void SetCustomArenas(List<string> scenes)
    {
        arenaScenes = new List<string>(scenes);
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
        if (!ReturningToMenu) StartCoroutine(ReturnToMenuRoutine());
    }

    private IEnumerator ReturnToMenuRoutine()
    {
        ReturningToMenu = true;
        StopWaitingForArena();
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        while (networkManager != null && networkManager.IsListening)
            yield return null;

        var loadedArenaNames = new HashSet<string>(arenaScenes);
        loadedArenaNames.UnionWith(CustomArenas);
        foreach (string arenaName in loadedArenaNames)
        {
            Scene arena = SceneManager.GetSceneByName(arenaName);
            if (arena.IsValid() && arena.isLoaded)
                yield return SceneManager.UnloadSceneAsync(arena);
        }

        currentArenaIndex = -1;
        LoadMenu();
        var menu = FindFirstObjectByType<LANConnect>();
        if (menu != null && menu.LocalMainMenu != null) menu.LocalMainMenu.SetActive(true);
        ReturningToMenu = false;
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
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        RestoreMenuCameras();
        StopWaitingForArena();
    }
}
