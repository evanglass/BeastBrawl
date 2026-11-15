using Unity.Netcode;
using UnityEngine;

public class EagleVisual : NetworkBehaviour
{
    public enum Phase
    {
        Inactive,
        FlyingToCorner,
        Aiming,
        FreeFlight
    }

    private NetworkVariable<Phase> phase = new(Phase.Inactive);
    private NetworkVariable<float> aimAngle = new(-90f);
    private NetworkVariable<int> cornerFacing = new(0);
    private EagleBeast eagle;
    private LineRenderer cone;

    [SerializeField]
    private ParticleSystem soundWavePrefab;

    private ParticleSystem soundWave;

    public int FacingOverride => phase.Value == Phase.Aiming ? cornerFacing.Value : 0;

    private void Awake()
    {
        eagle = GetComponent<EagleBeast>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsClient)
            return;

        GameObject coneObject = new GameObject("Eagle Sonic Boom Cone");
        coneObject.transform.SetParent(transform, false);
        cone = coneObject.AddComponent<LineRenderer>();
        cone.useWorldSpace = true;
        cone.positionCount = 3;
        cone.startWidth = 0.08f;
        cone.endWidth = 0.08f;
        cone.startColor = Color.cyan;
        cone.endColor = Color.cyan;
        cone.sortingOrder = 20;
        cone.sharedMaterial = GetComponent<SpriteRenderer>().sharedMaterial;
        cone.enabled = false;

        if (soundWavePrefab != null)
            soundWave = Instantiate(soundWavePrefab, transform);
    }

    public override void OnNetworkDespawn()
    {
        if (cone != null)
            Destroy(cone.gameObject);
        if (soundWave != null)
            Destroy(soundWave.gameObject);
        cone = null;
        soundWave = null;
    }

    private void Update()
    {
        if (!IsSpawned || !IsClient || cone == null)
            return;

        if (phase.Value == Phase.Aiming)
        {
            DrawCone(transform.position, aimAngle.Value);
        }
        else
        {
            cone.enabled = false;
        }
    }

    public void SetAim(Phase nextPhase, float angle)
    {
        if (!IsServer || !IsSpawned)
            return;

        if (nextPhase == Phase.FlyingToCorner)
            cornerFacing.Value = angle > -90f ? 1 : -1;

        phase.Value = nextPhase;
        aimAngle.Value = angle;
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    public void PlayBoomRpc(Vector2 origin, float angle)
    {
        if (soundWave == null)
            return;

        soundWave.transform.position = origin;
        soundWave.Play();
        float lifetime = Mathf.Max(0.01f, soundWave.main.startLifetime.constantMax);
        for (int i = 0; i < 41; i++)
        {
            float direction = Mathf.Lerp(angle - eagle.BoomHalfAngle, angle + eagle.BoomHalfAngle, i / 40f);
            float radians = direction * Mathf.Deg2Rad;
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams();
            particle.position = origin;
            particle.velocity = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * (eagle.BoomRange / lifetime);
            soundWave.Emit(particle, 1);
        }
    }

    private void DrawCone(Vector2 origin, float angle)
    {
        float left = (angle - eagle.BoomHalfAngle) * Mathf.Deg2Rad;
        float right = (angle + eagle.BoomHalfAngle) * Mathf.Deg2Rad;
        Vector2 leftEnd = origin + new Vector2(Mathf.Cos(left), Mathf.Sin(left)) * eagle.BoomRange;
        Vector2 rightEnd = origin + new Vector2(Mathf.Cos(right), Mathf.Sin(right)) * eagle.BoomRange;

        cone.SetPosition(0, leftEnd);
        cone.SetPosition(1, origin);
        cone.SetPosition(2, rightEnd);
        cone.enabled = true;
    }
}
