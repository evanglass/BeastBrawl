using UnityEngine;

// An arena can opt into an upper ring-out without changing the shared lower boundary.
public sealed class ArenaRingOutCeiling : MonoBehaviour
{
    [SerializeField] private float height = 26f;
    private static ArenaRingOutCeiling active;

    private void OnEnable() => active = this;
    private void OnDisable()
    {
        if (active == this) active = null;
    }

    internal static bool IsAbove(float y) => active != null && y > active.height;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 center = new Vector3(transform.position.x, height, transform.position.z);
        Gizmos.DrawLine(center - Vector3.right * 55f, center + Vector3.right * 55f);
    }
}
