using UnityEngine;

public class Paralax : MonoBehaviour
{
    [SerializeField] private Camera mainCamera; // camera reference
   
    // paralax effect
    [SerializeField] private float horizontalEffect=0.5f; 
    [SerializeField] private float verticalEffect=0.5f;

    //zoom to sync with the camera tracking the 4 player character
    [SerializeField, Range(0f, 1f)] private float zoomEffect = 0.5f;


    private Vector3 startingBackgroundPosition;
    private Vector3 startingCameraPosition;
    private Vector3 startingScale;
    private float startingCameraZoom;
    private SpriteRenderer backgroundSprite;
    private bool isBackLayer;

     // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //find the camera
        if (mainCamera ==null)
        {
            mainCamera = Camera.main;
        }

        if(mainCamera== null)
        {
            Debug.LogError("ParallaxBackground: No camera assigned!");
            enabled = false;
            return;
        }

        // Record the starting positions, scale, and zoom
        startingBackgroundPosition = transform.position;
        startingCameraPosition = mainCamera.transform.position;
        startingCameraZoom = mainCamera.orthographicSize;
        startingScale = transform.localScale;
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




    // Update is called once per frame

    void LateUpdate()
    {
        // Calculate camera movement
        Vector3 cameraMovement =
            mainCamera.transform.position - startingCameraPosition;

        // Apply movement to the background
        transform.position = new Vector3(
            startingBackgroundPosition.x + cameraMovement.x * horizontalEffect,
            startingBackgroundPosition.y + cameraMovement.y * verticalEffect,
            startingBackgroundPosition.z
        );

        // Scale the background in response to camera zoom
        float zoomRatio = mainCamera.orthographicSize / Mathf.Max(.01f, startingCameraZoom);
        float zoomScale = Mathf.Lerp(1f, zoomRatio, zoomEffect);

        transform.localScale = new Vector3(
            startingScale.x * zoomScale,
            startingScale.y * zoomScale,
            startingScale.z
        );

        // The furthest existing art layer must cover the whole view as the camera moves.
        if (isBackLayer && backgroundSprite != null && backgroundSprite.sprite != null)
        {
            Bounds bounds = backgroundSprite.bounds;
            float halfHeight = mainCamera.orthographicSize;
            float halfWidth = halfHeight * mainCamera.aspect;
            if (bounds.extents.x > .001f && bounds.extents.y > .001f)
            {
                float cover = Mathf.Max(1f, halfWidth * 1.05f / bounds.extents.x, halfHeight * 1.05f / bounds.extents.y);
                transform.localScale = new Vector3(transform.localScale.x * cover, transform.localScale.y * cover, transform.localScale.z);
                bounds = backgroundSprite.bounds;
                Vector3 position = transform.position;
                float dx = mainCamera.transform.position.x - bounds.center.x;
                float dy = mainCamera.transform.position.y - bounds.center.y;
                position.x += dx - Mathf.Clamp(dx, -(bounds.extents.x - halfWidth), bounds.extents.x - halfWidth);
                position.y += dy - Mathf.Clamp(dy, -(bounds.extents.y - halfHeight), bounds.extents.y - halfHeight);
                transform.position = position;
            }
        }
    }
}
