using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class Paralax : MonoBehaviour
{
    [SerializeField] private Camera mainCamera; // optional camera for standalone previews
   
    // paralax effect
    [SerializeField] private float horizontalEffect=0.5f; 
    [SerializeField] private float verticalEffect=0.5f;

    // Zoom to sync with the local gameplay camera.
    [SerializeField, Range(0f, 1f)] private float zoomEffect = 0.5f;


    private Vector3 startingBackgroundPosition;
    private Vector3 startingCameraPosition;
    private Vector3 startingScale;
    private float startingCameraZoom;
    private SpriteRenderer backgroundSprite;
    private bool isBackLayer;

    private Camera boundCamera;
    private Vector3 lastLayerPosition;
    private Vector3 lastLayerScale;
    private bool initialized;

    private Camera ResolveCamera()
    {
        Camera camera = SharedArenaCamera.ActiveCamera;
        if (camera != null) return camera;

        // Standalone previews can still use an authored camera. Wait for NGO's camera in a match.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return null;
        return mainCamera != null && mainCamera.isActiveAndEnabled ? mainCamera : Camera.main;
    }

    private void BindCamera(Camera camera)
    {
        boundCamera = camera;
        // Rebase from the last parallax pose, before the sky coverage correction.
        startingBackgroundPosition = initialized ? lastLayerPosition : transform.position;
        startingScale = initialized ? lastLayerScale : transform.localScale;
        startingCameraPosition = camera.transform.position;
        startingCameraZoom = camera.orthographicSize;
        if (initialized) return;
        initialized = true;

        backgroundSprite = GetComponent<SpriteRenderer>();
        if (backgroundSprite != null && backgroundSprite.sprite != null)
        {
            isBackLayer = true;
            foreach (var layer in FindObjectsByType<Paralax>(FindObjectsSortMode.None))
            {
                if (layer != this && layer.gameObject.scene == gameObject.scene &&
                    layer.GetComponent<SpriteRenderer>() != null && layer.transform.position.z > transform.position.z)
                    isBackLayer = false;
            }
        }
    }

    // Additive arenas can load before the gameplay camera is ready.

    void LateUpdate()
    {
#if UNITY_SERVER && !UNITY_EDITOR
        return;
#else
        Camera camera = ResolveCamera();
        if (camera == null || !camera.isActiveAndEnabled) return;
        if (boundCamera != camera) BindCamera(camera);

        // Calculate camera movement
        Vector3 cameraMovement =
            camera.transform.position - startingCameraPosition;

        // Apply movement to the background
        lastLayerPosition = new Vector3(
            startingBackgroundPosition.x + cameraMovement.x * horizontalEffect,
            startingBackgroundPosition.y + cameraMovement.y * verticalEffect,
            startingBackgroundPosition.z
        );

        // Scale the background in response to camera zoom
        float zoomRatio = camera.orthographicSize / Mathf.Max(.01f, startingCameraZoom);
        float zoomScale = Mathf.Lerp(1f, zoomRatio, zoomEffect);

        lastLayerScale = new Vector3(
            startingScale.x * zoomScale,
            startingScale.y * zoomScale,
            startingScale.z
        );

        transform.position = lastLayerPosition;
        transform.localScale = lastLayerScale;

        // The furthest existing art layer must cover the whole view as the camera moves.
        if (isBackLayer && backgroundSprite != null && backgroundSprite.sprite != null)
        {
            Bounds bounds = backgroundSprite.bounds;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            if (bounds.extents.x > .001f && bounds.extents.y > .001f)
            {
                float cover = Mathf.Max(1f, halfWidth * 1.05f / bounds.extents.x, halfHeight * 1.05f / bounds.extents.y);
                transform.localScale = new Vector3(transform.localScale.x * cover, transform.localScale.y * cover, transform.localScale.z);
                bounds = backgroundSprite.bounds;
                Vector3 position = transform.position;
                float dx = camera.transform.position.x - bounds.center.x;
                float dy = camera.transform.position.y - bounds.center.y;
                position.x += dx - Mathf.Clamp(dx, -(bounds.extents.x - halfWidth), bounds.extents.x - halfWidth);
                position.y += dy - Mathf.Clamp(dy, -(bounds.extents.y - halfHeight), bounds.extents.y - halfHeight);
                transform.position = position;
            }
        }
#endif
    }
}
