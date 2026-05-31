using APlaceLikeMe.UI;
using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    [Header("Move Speed")]
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Animator animator;
    private Rect? movementBounds;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection = new(0, -1);

    public void ConfigureBounds(Rect bounds)
    {
        movementBounds = bounds;
    }

    public void ClearBounds()
    {
        movementBounds = null;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (PrototypeGameController.Active != null && PrototypeGameController.Active.AreWorldControlsLocked)
        {
            moveInput = Vector2.zero;
            StopRigidbody();
            UpdateAnimator();
            return;
        }

        moveInput = ReadMovementInput();

        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        if (moveInput == Vector2.zero)
        {
            StopRigidbody();
            return;
        }

        var nextPosition = rb.position + moveInput * moveSpeed * Time.fixedDeltaTime;
        if (movementBounds.HasValue)
        {
            var bounds = movementBounds.Value;
            nextPosition.x = Mathf.Clamp(nextPosition.x, bounds.xMin, bounds.xMax);
            nextPosition.y = Mathf.Clamp(nextPosition.y, bounds.yMin, bounds.yMax);
        }

        rb.MovePosition(nextPosition);
    }

    private static Vector2 ReadMovementInput()
    {
        var movement = Vector2.zero;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            movement.x -= 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            movement.x += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            movement.y -= 1f;
        }

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            movement.y += 1f;
        }

        return movement.normalized;
    }

    private void StopRigidbody()
    {
        if (rb == null)
        {
            return;
        }

        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;
    }

    private void UpdateAnimator()
    {
        if (animator == null)
        {
            return;
        }

        if (moveInput != Vector2.zero)
        {
            lastMoveDirection = moveInput;
        }

        animator.SetFloat("Horizontal", lastMoveDirection.x);
        animator.SetFloat("Vertical", lastMoveDirection.y);
        animator.SetFloat("Speed", moveInput.sqrMagnitude);
    }
}
