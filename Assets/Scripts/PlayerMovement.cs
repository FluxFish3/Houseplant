using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using PurrNet;

public class PlayerMovement : NetworkBehaviour
{
    [Header("References")]
    public Rigidbody rb;
    public Transform camTransform;
    public Transform groundCheck;
    [Tooltip("Optional. A child transform on the player used as the camera follow target (head/camera anchor). If empty we'll try to find a child named 'CamTarget'/'CameraTarget' or create one.")]
    public Transform cameraAnchor;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float acceleration = 10f; // higher = more responsive
    public float sprintMultiplier = 1.8f;
    public float fovMultiplier = 1.25f;

    private bool isLocked = false;

    [Header("FOV")]
    public float fovLerpSpeed = 8f;

    [Header("Speed Smoothing")]
    public float maxWalkSpeed = 10f;
    public float maxSprintSpeed = 16f;
    public float maxSpeedLerpRate = 8f;   // higher = snappier

    private float smoothedMaxSpeed;

    [Header("Jumping")]
    public LayerMask ground;
    public float jumpForce = 7f;
    public float jumpCooldown = 0.4f;
    public float groundCheckRadius = 0.2f;
    private float jumpCooldownTimer;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float stamina = 100f;
    public float sprintDrainPerSecond = 20f;
    public float jumpCost = 25f;
    public float staminaRegenPerSecond = 10f;
    public float staminaRegenDelay = 1.75f;
    private float regenDelayTimer;
    private Image staminaBar;
    public float staminaBarSmooth;

    [Header("Crouch")]
    [Tooltip("The transform that represents the player's visual mesh/root to shift down when crouching.")]
    public Transform meshRoot;
    [Tooltip("GameObject (e.g. feet) to hide when crouched.")]
    public GameObject feetObject;
    [Tooltip("Vertical amount (in local space) to shift the mesh down when crouching.")]
    public float crouchHeight = 0.5f;
    [Tooltip("Time (seconds) used by SmoothDamp to interpolate the mesh shift.")]
    public float crouchSmoothTime = 0.12f;
    public bool startCrouched = false;

    private Vector3 meshOriginalLocalPos = Vector3.zero;
    private Vector3 meshTargetLocalPos = Vector3.zero;
    private Vector3 meshVelocity = Vector3.zero;
    private bool isCrouching = false;

    public Vector2 moveInput;
    private bool isSprinting;
    private bool jumpPressed;
    public bool isGrounded;

    // Camera FOV handling
    private Camera cam;
    private float baseFov;

    // Network rotation sync
    private float syncedYRotation = 0f;
    private float syncedXRotation = 0f;

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        // Ensure physics mode is correct for server vs clients
        rb.isKinematic = !isServer;

        // Discover meshRoot if not explicitly assigned (common cases)
        if (meshRoot == null)
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
                meshRoot = smr.transform;
            else
            {
                var mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null)
                    meshRoot = mr.transform;
            }
        }

        // Cache original local position to use as the uncloached baseline
        if (meshRoot != null)
        {
            meshOriginalLocalPos = meshRoot.localPosition;
            meshTargetLocalPos = meshOriginalLocalPos;
        }

        // Ensure feet object initial visibility matches startCrouched
        isCrouching = startCrouched;
        if (feetObject != null)
            feetObject.SetActive(!isCrouching);

        // Only local owner needs to hook tick and the main camera
        if (isOwner)
        {
            if (networkManager != null)
                networkManager.onTick += OnTick;

            // Try to find local camera
            cam = Camera.main;
            if (cam != null)
            {
                // this is the camera transform used by movement (movement is camera-relative)
                camTransform = cam.transform;
                baseFov = cam.fieldOfView;

                // Configure the FirstPersonCamera instance on the main camera
                FirstPersonCamera fpCam = cam.GetComponent<FirstPersonCamera>();
                if (fpCam != null)
                {
                    // Link player body so camera can rotate the player
                    fpCam.playerBody = transform;
                    fpCam.ownerMovement = this;

                    // Determine a proper camTarget on the player:
                    // prefer `cameraAnchor` set on the prefab, else try child names, else create a child anchor at a reasonable head height.
                    Transform target = cameraAnchor;
                    if (target == null)
                    {
                        target = transform.Find("CamTarget") ?? transform.Find("CameraTarget");
                        if (target == null)
                        {
                            GameObject go = new GameObject("CamTarget");
                            go.transform.SetParent(transform, false);
                            go.transform.localPosition = new Vector3(0f, 1.6f, 0f); // default head height
                            target = go.transform;
                            cameraAnchor = target;
                        }
                    }

                    fpCam.camTarget = target;

                    // Only enable first-person camera controls for the local owner.
                    fpCam.enabled = true;
                }
            }
            else
            {
                baseFov = 60f; // fallback
            }

            Cursor.visible = false;
            stamina = maxStamina;
            smoothedMaxSpeed = maxWalkSpeed;
            isLocked = false;
            staminaBar = FindObjectOfType<Canvas>()?.transform.Find("StaminaBar")?.GetComponent<Image>();
        }
        else
        {
            // For non-owners, try to discover a cameraAnchor on the prefab so remote camera-targeting (if needed) is present.
            if (cameraAnchor == null)
            {
                cameraAnchor = transform.Find("CamTarget") ?? transform.Find("CameraTarget");
            }
        }

        // Apply initial crouch position if starting crouched
        UpdateTargetMeshPosition();
        if (meshRoot != null)
            meshRoot.localPosition = meshTargetLocalPos;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (isOwner && networkManager != null)
            networkManager.onTick -= OnTick;
    }

    private void OnTick(bool asServer)
    {
        if (asServer) { return; }
        MovePlayer();
        LimitSpeed();
    }

    private void Update()
    {
        // Ground check
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, ground);

        // Jump cooldown timer
        if (jumpCooldownTimer > 0f)
            jumpCooldownTimer -= Time.deltaTime;

        HandleJump();
        HandleStamina();

        // Smoothly update camera FOV each frame (only for owner)
        UpdateFOV();

        // Smoothly move mesh root toward target local position (visual crouch)
        if (meshRoot != null)
        {
            meshRoot.localPosition = Vector3.SmoothDamp(meshRoot.localPosition, meshTargetLocalPos, ref meshVelocity, Mathf.Max(0.001f, crouchSmoothTime));
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        // If locked, ignore input updates (keep inputs at zero).
        if (isLocked)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (isLocked)
        {
            isSprinting = false;
            return;
        }
        isSprinting = context.ReadValueAsButton();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (isLocked)
        {
            jumpPressed = false;
            return;
        }
        if (context.performed)
            jumpPressed = true;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        // This is handled by FirstPersonCamera, but we need to implement it here to receive the input callback and mark it as used.
        cam.GetComponent<FirstPersonCamera>().OnLook(context); 
    }

    public void OnLock(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            isLocked = !isLocked;

            // When locking, immediately clear all movement inputs
            if (isLocked)
            {
                moveInput = Vector2.zero;
                isSprinting = false;
                jumpPressed = false;
                SetLocalCrouch(true); 
            }
            else
            {
                // When unlocking, sample the current device state so movement resumes
                // if the player is still holding move keys / stick.
                moveInput = SampleCurrentMoveInput();
                isSprinting = SampleCurrentSprintInput();
                SetLocalCrouch(false); 
                // Intentionally do not auto-trigger jumpPressed on unlock to avoid accidental jumps.
            }
        }
    }

    /// <summary>
    /// Samples current devices (keyboard + gamepad left stick) to produce a movement Vector2.
    /// Returns a normalized vector when magnitude > 1 to mimic joystick/axis behavior.
    /// </summary>
    private Vector2 SampleCurrentMoveInput()
    {
        Vector2 sampled = Vector2.zero;

        // Gamepad left stick has higher priority if present
        var gp = Gamepad.current;
        if (gp != null)
        {
            sampled = gp.leftStick.ReadValue();
        }

        // Keyboard fallback/augmentation (WASD / arrows)
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) sampled.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) sampled.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) sampled.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) sampled.x += 1f;
        }

        // Clamp length to 1 (in case keyboard + stick both add up)
        if (sampled.sqrMagnitude > 1f)
            sampled.Normalize();

        return sampled;
    }

    /// <summary>
    /// Samples current devices to determine sprint state (Shift on keyboard or shoulder on gamepad).
    /// </summary>
    private bool SampleCurrentSprintInput()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
            return true;

        var gp = Gamepad.current;
        if (gp != null && (gp.leftShoulder.isPressed || gp.rightShoulder.isPressed))
            return true;

        return false;
    }

    /// <summary>
    /// Locally set crouch state and notify server to replicate to observers.
    /// </summary>
    private void SetLocalCrouch(bool crouch)
    {
        if (isCrouching == crouch) return;

        isCrouching = crouch;

        // Hide feet locally immediately
        if (feetObject != null)
            feetObject.SetActive(!isCrouching);

        // Update mesh target
        UpdateTargetMeshPosition();

        // Propagate to server/others
        SyncCrouchToServer(isCrouching);
    }

    private void UpdateTargetMeshPosition()
    {
        if (meshRoot == null) return;

        meshTargetLocalPos = meshOriginalLocalPos + (isCrouching ? Vector3.down * crouchHeight : Vector3.zero);
    }

    [ServerRpc]
    private void SyncCrouchToServer(bool crouch)
    {
        // Apply on server instance
        isCrouching = crouch;
        if (feetObject != null)
            feetObject.SetActive(!isCrouching);
        UpdateTargetMeshPosition();

        // Broadcast to observers
        SyncCrouchToClients(crouch);
    }

    [ObserversRpc]
    private void SyncCrouchToClients(bool crouch)
    {
        // Owners already applied locally; observers should apply now.
        if (isOwner) return;

        isCrouching = crouch;
        if (feetObject != null)
            feetObject.SetActive(!isCrouching);
        UpdateTargetMeshPosition();
    }

    [ServerRpc]
    private void MovePlayer()
    {
        // camTransform must be set on the owner (movement uses camera-relative directions)
        if (camTransform == null) return;

        Vector3 forward = camTransform.forward;
        Vector3 right = camTransform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;

        float currentSpeed = (isSprinting && stamina > 0f) ? moveSpeed * sprintMultiplier : moveSpeed;

        Vector3 targetVelocity = moveDirection * currentSpeed;
        Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        Vector3 velocityChange = (targetVelocity - flatVelocity) * acceleration;

        rb.AddForce(velocityChange, ForceMode.Force);
    }

    private void LimitSpeed()
    {
        float targetMaxSpeed = (isSprinting && stamina > 0f) ? maxSprintSpeed : maxWalkSpeed;

        // Smoothly blend max speed
        smoothedMaxSpeed = Mathf.Lerp(
            smoothedMaxSpeed,
            targetMaxSpeed,
            maxSpeedLerpRate * Time.fixedDeltaTime
        );

        Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        if (flatVelocity.magnitude > smoothedMaxSpeed)
        {
            Vector3 limited = flatVelocity.normalized * smoothedMaxSpeed;
            rb.linearVelocity = new Vector3(limited.x, rb.linearVelocity.y, limited.z);
        }
    }

    private void HandleJump()
    {
        if (!jumpPressed) return;

        if (isGrounded && jumpCooldownTimer <= 0f && stamina >= jumpCost)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

            stamina -= jumpCost;
            regenDelayTimer = staminaRegenDelay;
            jumpCooldownTimer = jumpCooldown;
        }

        jumpPressed = false;
    }

    private void HandleStamina()
    {
        bool usingStamina = false;

        // Sprint drain
        if (isSprinting && moveInput.magnitude > 0.1f && stamina > 0f)
        {
            stamina -= sprintDrainPerSecond * Time.deltaTime;
            usingStamina = true;
        }

        // If stamina was used, reset delay
        if (usingStamina)
        {
            regenDelayTimer = staminaRegenDelay;
        }
        else
        {
            // Countdown delay
            if (regenDelayTimer > 0f)
            {
                regenDelayTimer -= Time.deltaTime;
            }
            else
            {
                stamina += staminaRegenPerSecond * Time.deltaTime;
            }
        }

        stamina = Mathf.Clamp(stamina, 0f, maxStamina);
        if (staminaBar != null)
            staminaBar.fillAmount = Mathf.Lerp(staminaBar.fillAmount, stamina / maxStamina, staminaBarSmooth * Time.deltaTime);
    }

    // Smoothly blends camera FOV to sprint or base value
    void UpdateFOV()
    {
        if (!isOwner || cam == null) return;

        // Match sprint condition used for movement visuals
        bool sprintActive = isSprinting && moveInput.magnitude > 0.1f && stamina > 0f;

        float targetFov = sprintActive ? baseFov * fovMultiplier : baseFov;

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Called by FirstPersonCamera to sync local player rotation to the network.
    /// </summary>
    public void SetLocalRotation(float xRotation, float yRotation)
    {
        syncedXRotation = xRotation;
        syncedYRotation = yRotation;
        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        SyncRotationToServer(xRotation, yRotation);
    }

    [ServerRpc]
    private void SyncRotationToServer(float xRotation, float yRotation)
    {
        syncedXRotation = xRotation;
        syncedYRotation = yRotation;
        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
        SyncRotationToClients(xRotation, yRotation);
    }

    [ObserversRpc]
    private void SyncRotationToClients(float xRotation, float yRotation)
    {
        if (isOwner) return; // Owner controls their own rotation via camera

        syncedXRotation = xRotation;
        syncedYRotation = yRotation;
        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    /// <summary>
    /// Get the synced Y rotation for remote players.
    /// </summary>
    public float GetSyncedYRotation()
    {
        return syncedYRotation;
    }

    /// <summary>
    /// Get the synced X rotation for remote players.
    /// </summary>
    public float GetSyncedXRotation()
    {
        return syncedXRotation;
    }
}