using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class LANController : MonoBehaviour
{
    [SerializeField]
    private ushort port = 7777;

    private NetworkManager nm;

    private void Awake()
    {
        nm = NetworkManager.Singleton;
    }

    public void HostGame()
    {
        nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", port, "0.0.0.0");
        bool started = nm.StartHost();
    }
}
