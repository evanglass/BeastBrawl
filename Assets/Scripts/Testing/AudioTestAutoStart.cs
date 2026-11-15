using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Automatic host/client startup, used only by the audio multiplayer test scene.</summary>
public sealed class AudioTestAutoStart : MonoBehaviour
{
    [SerializeField, Min(0f)] private float hostDelaySeconds = .5f;
    [SerializeField, Min(0f)] private float clientDelaySeconds = 3f;

    private IEnumerator Start()
    {
#if UNITY_EDITOR
        bool host = Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
        yield return new WaitForSecondsRealtime(host ? hostDelaySeconds : clientDelaySeconds);
        var network = NetworkManager.Singleton;
        if (network == null)
        {
            Debug.LogError("Audio test could not find its NetworkManager.");
            yield break;
        }
        if (network.IsListening) yield break;
        bool started = host ? network.StartHost() : network.StartClient();
        if (!started) Debug.LogError(host ? "Audio test host could not start." : "Audio test client could not start.");
        else Debug.Log(host ? "Audio test: host started." : "Audio test: client connecting.");
#else
        yield break;
#endif
    }
}
