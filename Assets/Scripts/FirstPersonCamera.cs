using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonCamera : MonoBehaviour
{
    [Header("References")]
    public Transform playerBody;
    public Transform camTarget;
    public PlayerMovement ownerMovement;

    [Header("Settings")]
    public float mouseSensitivity = 100f;
    public float yClamp = 85f;
    [Tooltip("When enabled, fall back to raw Mouse.current.delta if OnLook isn't being called.")]
    public bool useRawMouseFallback = true;

    private float xRotation = 0f;
    private float yRotation = 0f;   // Track Y ourselves instead of reading eulerAngles
    private Vector2 lookInput;

    private void Start()
    {
        // try to resolve owner/player references if not already set by PlayerMovement.OnSpawned
        TryAutoAssignOwner();

        if (playerBody != null)
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
        // Ignore input when not locked – raw deltas are garbage without pointer lock
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            lookInput = Vector2.zero;
            return;
        }
        lookInput = context.ReadValue<Vector2>();
    }

    private void LateUpdate()
    {
        // If spawn ordering left these null, try to resolve again.
        if (playerBody == null || camTarget == null || ownerMovement == null)
            TryAutoAssignOwner();

        if (playerBody == null || camTarget == null) return;

        bool isLocalOwner = ownerMovement != null && ownerMovement.isOwner;

        if (isLocalOwner)
        {
            // Local owner handles input & drives player rotation
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 delta = lookInput;

            // Fallback: poll raw mouse delta if OnLook isn't being triggered
            if (useRawMouseFallback && delta.sqrMagnitude < 0.000001f && Mouse.current != null)
            {
                delta = Mouse.current.delta.ReadValue();
            }

            float mouseX = delta.x * mouseSensitivity * Time.deltaTime;
            float mouseY = delta.y * mouseSensitivity * Time.deltaTime;

            yRotation += mouseX;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -yClamp, yClamp);

            // Apply local rotation immediately so the owner sees it
            playerBody.rotation = Quaternion.Euler(0f, yRotation, 0f);

            Quaternion targetRot = Quaternion.Euler(xRotation, yRotation, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 25f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, camTarget.position, 50f * Time.deltaTime);

            // Sync rotation to network (PlayerMovement will also apply locally inside SetLocalRotation)
            ownerMovement?.SetLocalRotation(xRotation, yRotation);

            // Clear lookInput so fallback polling doesn't accumulate the same delta twice next frame
            if (useRawMouseFallback)
                lookInput = Vector2.zero;
        }
        else
        {
            // Remote player: read synced rotations and apply
            float remoteY = ownerMovement != null ? ownerMovement.GetSyncedYRotation() : yRotation;
            float remoteX = ownerMovement != null ? ownerMovement.GetSyncedXRotation() : xRotation;

            playerBody.rotation = Quaternion.Euler(0f, remoteY, 0f);

            xRotation = remoteX;
            yRotation = remoteY;

            Quaternion targetRot = Quaternion.Euler(xRotation, yRotation, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 25f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, camTarget.position, 50f * Time.deltaTime);
        }
    }

    // Try to find the local owner PlayerMovement if it hasn't been assigned by OnSpawned.
    private void TryAutoAssignOwner()
    {
        if (ownerMovement != null)
        {
            if (playerBody == null) playerBody = ownerMovement.transform;
            if (camTarget == null)
                camTarget = ownerMovement.cameraAnchor ?? ownerMovement.transform.Find("CamTarget") ?? ownerMovement.transform.Find("CameraTarget");
            return;
        }

        var players = FindObjectsOfType<PlayerMovement>();
        for (int i = 0; i < players.Length; i++)
        {
            var pm = players[i];
            if (pm != null && pm.isOwner)
            {
                ownerMovement = pm;
                playerBody = pm.transform;
                camTarget = pm.cameraAnchor ?? pm.transform.Find("CamTarget") ?? pm.transform.Find("CameraTarget");
                yRotation = playerBody.eulerAngles.y;
                return;
            }
        }
    }
}