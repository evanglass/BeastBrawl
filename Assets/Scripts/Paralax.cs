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
        float zoomRatio = mainCamera.orthographicSize / startingCameraZoom;
        float zoomScale = Mathf.Lerp(1f, zoomRatio, zoomEffect);

        transform.localScale = new Vector3(
            startingScale.x * zoomScale,
            startingScale.y * zoomScale,
            startingScale.z
        );
    }
}