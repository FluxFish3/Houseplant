using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Rigidbody rb;
    public Transform camTransform;
    public Transform groundCheck;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 1.8f;

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

    private Vector2 moveInput;
    private bool isSprinting;
    private bool jumpPressed;
    private bool isGrounded;

    private void Start()
    {
        Cursor.visible = false;
        stamina = maxStamina;
        smoothedMaxSpeed = maxWalkSpeed;
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
    }

    private void FixedUpdate()
    {
        MovePlayer();
        LimitSpeed();
    }
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        isSprinting = context.ReadValueAsButton();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed)
            jumpPressed = true;
    }
    private void MovePlayer()
    {
        Vector3 forward = camTransform.forward;
        Vector3 right = camTransform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = forward * moveInput.y + right * moveInput.x;

        float currentSpeed = (isSprinting && stamina > 0f) ? moveSpeed * sprintMultiplier : moveSpeed;
        rb.AddForce(moveDirection * currentSpeed, ForceMode.Force);
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
    }
}