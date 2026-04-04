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
    private float yRotation = 0f;   // Track Y ourselves instead of reading eulerAngles
    private Vector2 lookInput;

    private void Start()
    {
        // Seed yRotation from the body's starting orientation
        yRotation = playerBody.eulerAngles.y;
        RequestPointerLock();
    }

    private void Update()
    {
        // Re-lock on click (required by browsers after focus loss)
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                RequestPointerLock();
        }
    }

    private void RequestPointerLock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        // Ignore input when not locked — raw deltas are garbage without pointer lock
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            lookInput = Vector2.zero;
            return;
        }
        lookInput = context.ReadValue<Vector2>();
    }

    private void LateUpdate()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // Accumulate Y ourselves — never read back from eulerAngles
        yRotation += mouseX;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -yClamp, yClamp);

        // Apply to player body (horizontal only)
        playerBody.rotation = Quaternion.Euler(0f, yRotation, 0f);

        // Camera uses our clean accumulated values
        Quaternion targetRot = Quaternion.Euler(xRotation, yRotation, 0f);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 25f * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, camTarget.position, 50f * Time.deltaTime);
    }
}