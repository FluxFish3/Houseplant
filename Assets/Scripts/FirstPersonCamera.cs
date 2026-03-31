using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonCamera : MonoBehaviour
{
    [Header("References")]
    public Transform playerBody;
    public Transform camTarget; 

    [Header("Settings")]
    public float mouseSensitivity = 100f;
    public float yClamp = 85f;

    private float xRotation = 0f;
    private Vector2 lookInput;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookInput = context.ReadValue<Vector2>();
    }

    private void LateUpdate()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // Rotate player horizontally
        playerBody.Rotate(Vector3.up * mouseX);

        // Vertical rotation (camera only)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -yClamp, yClamp);

        // Combine rotations:
        Quaternion targetRot = Quaternion.Euler(xRotation, playerBody.eulerAngles.y, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 25f * Time.deltaTime);

        // Follow position
        Vector3 targetPosition = camTarget.position;
        transform.position = Vector3.Lerp(transform.position, targetPosition, 50f * Time.deltaTime);
    }
}