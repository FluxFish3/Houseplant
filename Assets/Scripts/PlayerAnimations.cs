using UnityEngine;
using DG.Tweening;

public class PlayerAnimations : MonoBehaviour
{
    public PlayerMovement pm;

    public float moveThreshhold = 0.1f;
    public float bounceHeight = 0.2f;
    public float bounceDuration = 0.25f;
    public float returnTilt = 0.2f;
    public float tiltAngle = 15f;

    public Ease tiltEase = Ease.InOutSine;

    private Tween bounceTween;
    private Tween tiltTween;

    private Vector2 flatVel;

    private float startY;
    private int nextTiltDirection = 1;
    private int lastTiltDirection = 1;
    private int stepCount = 0;

    private void Start()
    {
        startY = transform.localPosition.y;
    }

    private void DoTilt()
    {
        tiltTween?.Kill();
        tiltTween = transform.DOLocalRotate(
            new Vector3(0f, 0f, tiltAngle * nextTiltDirection),
            bounceDuration * 2
        ).SetEase(tiltEase);

        lastTiltDirection = nextTiltDirection;
        nextTiltDirection *= -1;
    }

    private void Update()
    {
        flatVel = new Vector2(pm.rb.linearVelocity.x, pm.rb.linearVelocity.z);

        float relativeSpeed = flatVel.magnitude / pm.moveSpeed;

        bool isMoving = flatVel.magnitude > moveThreshhold && pm.isGrounded;

        if (isMoving)
        {
            if (bounceTween == null)
            {
                stepCount = 0;

                // Determine initial tilt direction
                if (Mathf.Abs(flatVel.x) > Mathf.Abs(flatVel.y))
                {
                    // Moving more horizontally (left/right)
                    nextTiltDirection = flatVel.x > 0 ? 1 : -1;
                }
                else
                {
                    // Moving more vertically (forward/backward) -> continue alternating
                    nextTiltDirection = lastTiltDirection;
                }

                DoTilt();

                bounceTween = transform.DOLocalMoveY(startY + bounceHeight, bounceDuration)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutCubic)
                    .OnStepComplete(() =>
                    {
                        stepCount++;
                        if (stepCount % 2 == 0)
                        {
                            DoTilt();
                        }
                    });
            }
        }
        else if (bounceTween != null)
        {
            bounceTween.Kill();
            tiltTween?.Kill();

            transform.DOLocalMoveY(startY, bounceDuration).SetEase(Ease.OutCubic);
            transform.DOLocalRotate(Vector3.zero, returnTilt).SetEase(Ease.OutSine);

            bounceTween = null;
            tiltTween = null;
        }
    }
}