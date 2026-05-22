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
            UpdateAnimator();
            return;
        }

        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");
        moveInput = moveInput.normalized;

        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        if (rb == null || moveInput == Vector2.zero)
        {
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
