using System.Collections.Generic;
using UnityEngine;

namespace APlaceLikeMe.Gameplay
{
    public enum NPCBehaviorState
    {
        Entering,
        Shopping,
        Queuing,
        WaitingForCheckout,
        Leaving,
        Finished
    }

    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class NPCBehavior : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 1.7f;
        [SerializeField] private float stoppingDistance = 0.05f;
        [SerializeField] private float shoppingArrivalDistance = 0.25f;
        [SerializeField] private float queueArrivalDistance = 0.2f;
        [SerializeField] private float shoppingDuration = 2.5f;
        [SerializeField] private float pathWaypointDistance = 0.12f;
        [SerializeField] private float stuckRepathDelay = 1.0f;
        [SerializeField] private float stuckDistance = 0.03f;
        [SerializeField] private float collisionRepathCooldown = 0.08f;

        private Rigidbody2D body;
        private Animator animator;
        private bool hasHorizontalParameter;
        private bool hasVerticalParameter;
        private bool hasSpeedParameter;
        private bool hasLastHorizontalParameter;
        private bool hasLastVerticalParameter;
        private NPCBehaviorState state = NPCBehaviorState.Entering;
        private Vector2 targetPosition;
        private Vector2 finalTargetPosition;
        private Vector2 shoppingTargetPosition;
        private Vector2 lastMoveDirection = Vector2.down;
        private readonly List<Vector2> path = new();
        private int pathIndex;
        private Vector2 lastProgressPosition;
        private float stuckTimer;
        private float nextCollisionRepathTime;
        private float stateTimer;
        private bool isRegisteredInQueue;
        private bool hasBegunVisit;

        public NPCBehaviorState State => state;
        public Vector2 TargetPosition => targetPosition;
        public bool IsWaitingForCheckout => state == NPCBehaviorState.WaitingForCheckout;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            animator = GetComponent<Animator>();
            CacheAnimatorParameters();

            if (body != null)
            {
                body.gravityScale = 0f;
                body.freezeRotation = true;
            }
        }

        private void OnEnable()
        {
            ShopManager.Instance?.RegisterNPC(this);
        }

        private void Start()
        {
            if (ShopManager.Instance == null)
            {
                return;
            }

            if (!hasBegunVisit)
            {
                BeginVisit();
            }
        }

        private void OnDisable()
        {
            if (ShopManager.Instance != null)
            {
                ShopManager.Instance.UnregisterNPC(this);
            }
        }

        private void Update()
        {
            if (state == NPCBehaviorState.Shopping)
            {
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    SetState(NPCBehaviorState.Queuing);
                }
            }
        }

        private void FixedUpdate()
        {
            if (state == NPCBehaviorState.Shopping || state == NPCBehaviorState.WaitingForCheckout || state == NPCBehaviorState.Finished)
            {
                StopMovement();
                UpdateAnimator(Vector2.zero);
                return;
            }

            MoveTowardTarget();
        }

        public void SetQueueTarget(Vector2 queueTarget)
        {
            SetTargetPosition(queueTarget);
            if (state == NPCBehaviorState.WaitingForCheckout)
            {
                SetState(NPCBehaviorState.Queuing);
            }
        }

        public void CheckoutAndLeave()
        {
            if (state != NPCBehaviorState.WaitingForCheckout)
            {
                return;
            }

            ShopManager.Instance?.FinishCheckout(this);
            SetState(NPCBehaviorState.Leaving);
            SetTargetPosition(ShopManager.Instance == null ? (Vector2)transform.position : ShopManager.Instance.GetDoorPosition());
        }

        public void BeginVisit()
        {
            hasBegunVisit = true;
            SetState(NPCBehaviorState.Entering);
            shoppingTargetPosition = ShopManager.Instance == null ? (Vector2)transform.position : ShopManager.Instance.GetShoppingPosition();
            SetTargetPosition(shoppingTargetPosition);
        }

        private void MoveTowardTarget()
        {
            if (!hasBegunVisit)
            {
                UpdateAnimator(Vector2.zero);
                return;
            }

            var currentPosition = body == null ? (Vector2)transform.position : body.position;
            var toTarget = targetPosition - currentPosition;
            var currentStoppingDistance = GetStoppingDistanceForState();
            if (toTarget.sqrMagnitude <= currentStoppingDistance * currentStoppingDistance)
            {
                if (Vector2.SqrMagnitude(targetPosition - finalTargetPosition) > currentStoppingDistance * currentStoppingDistance)
                {
                    SetTargetPosition(finalTargetPosition, finalTargetPosition);
                    return;
                }

                ArriveAtTarget();
                StopMovement();
                UpdateAnimator(Vector2.zero);
                return;
            }

            var direction = toTarget.normalized;
            var moveTarget = GetCurrentMoveTarget(currentPosition);
            var toMoveTarget = moveTarget - currentPosition;
            if (toMoveTarget.sqrMagnitude > 0.0001f)
            {
                direction = toMoveTarget.normalized;
            }

            var nextPosition = Vector2.MoveTowards(currentPosition, moveTarget, moveSpeed * Time.fixedDeltaTime);
            if (ShopManager.Instance != null && !ShopManager.Instance.IsMovementSegmentClear(currentPosition, nextPosition))
            {
                RequestImmediateRepath(moveTarget);
                StopMovement();
                UpdateAnimator(Vector2.zero);
                return;
            }

            if (body == null)
            {
                transform.position = new Vector3(nextPosition.x, nextPosition.y, transform.position.z);
            }
            else
            {
                body.MovePosition(nextPosition);
            }

            UpdateAnimator(direction);
            UpdateSortingOrder();
            UpdateStuckRepath(currentPosition);
        }

        private void LateUpdate()
        {
            UpdateSortingOrder();
        }

        private void ArriveAtTarget()
        {
            switch (state)
            {
                case NPCBehaviorState.Entering:
                    SetState(NPCBehaviorState.Shopping);
                    break;
                case NPCBehaviorState.Queuing:
                    if (ShopManager.Instance != null && ShopManager.Instance.IsFirstInQueue(this))
                    {
                        SetState(NPCBehaviorState.WaitingForCheckout);
                        FaceCashierDesk();
                        break;
                    }

                    FaceCashierDesk();
                    break;
                case NPCBehaviorState.Leaving:
                    SetState(NPCBehaviorState.Finished);
                    Destroy(gameObject);
                    break;
            }
        }

        private void SetState(NPCBehaviorState nextState)
        {
            if (state == nextState)
            {
                if (state == NPCBehaviorState.WaitingForCheckout)
                {
                    FaceCashierDesk();
                }

                return;
            }

            state = nextState;

            switch (state)
            {
                case NPCBehaviorState.Entering:
                    isRegisteredInQueue = false;
                    break;
                case NPCBehaviorState.Shopping:
                    stateTimer = Mathf.Max(0f, shoppingDuration);
                    StopMovement();
                    break;
                case NPCBehaviorState.Queuing:
                    if (!isRegisteredInQueue)
                    {
                        isRegisteredInQueue = true;
                        ShopManager.Instance?.JoinQueue(this);
                    }

                    break;
                case NPCBehaviorState.WaitingForCheckout:
                    FaceCashierDesk();
                    break;
                case NPCBehaviorState.Leaving:
                case NPCBehaviorState.Finished:
                    isRegisteredInQueue = false;
                    break;
            }
        }

        private void FaceCashierDesk()
        {
            if (ShopManager.Instance == null)
            {
                UpdateAnimator(Vector2.up);
                return;
            }

            var currentPosition = body == null ? (Vector2)transform.position : body.position;
            var cashierDirection = ShopManager.Instance.GetCashierPosition() - currentPosition;
            if (cashierDirection.sqrMagnitude <= 0.0001f)
            {
                cashierDirection = Vector2.up;
            }

            UpdateAnimator(GetCardinalDirection(cashierDirection));
        }

        private void SetTargetPosition(Vector2 nextTargetPosition)
        {
            SetTargetPosition(nextTargetPosition, nextTargetPosition);
        }

        private void SetTargetPosition(Vector2 nextTargetPosition, Vector2 nextFinalTargetPosition)
        {
            targetPosition = nextTargetPosition;
            finalTargetPosition = nextFinalTargetPosition;
            path.Clear();
            pathIndex = 0;
            stuckTimer = 0f;
            lastProgressPosition = body == null ? (Vector2)transform.position : body.position;

            if (ShopManager.Instance == null)
            {
                return;
            }

            var startPosition = body == null ? (Vector2)transform.position : body.position;
            if (ShopManager.Instance.TryFindPath(startPosition, nextTargetPosition, path, out var finalTarget))
            {
                targetPosition = finalTarget;
                TrimReachedPathPoints(startPosition);
                return;
            }

            targetPosition = ShopManager.Instance.GetNearestWalkablePosition(nextTargetPosition);
        }

        private Vector2 GetCurrentMoveTarget(Vector2 currentPosition)
        {
            TrimReachedPathPoints(currentPosition);
            return pathIndex < path.Count ? path[pathIndex] : targetPosition;
        }

        private void TrimReachedPathPoints(Vector2 currentPosition)
        {
            var threshold = Mathf.Max(0.02f, pathWaypointDistance);
            var thresholdSqr = threshold * threshold;
            while (pathIndex < path.Count && Vector2.SqrMagnitude(path[pathIndex] - currentPosition) <= thresholdSqr)
            {
                pathIndex++;
            }
        }

        private void UpdateStuckRepath(Vector2 previousPosition)
        {
            if (state == NPCBehaviorState.Shopping || state == NPCBehaviorState.WaitingForCheckout || state == NPCBehaviorState.Finished)
            {
                return;
            }

            var currentPosition = body == null ? (Vector2)transform.position : body.position;
            if (Vector2.SqrMagnitude(currentPosition - lastProgressPosition) > stuckDistance * stuckDistance)
            {
                lastProgressPosition = currentPosition;
                stuckTimer = 0f;
                return;
            }

            if (Vector2.SqrMagnitude(currentPosition - previousPosition) > stuckDistance * stuckDistance)
            {
                return;
            }

            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer < Mathf.Max(0.2f, stuckRepathDelay))
            {
                return;
            }

            stuckTimer = 0f;
            RequestImmediateRepath(targetPosition);
        }

        private void RequestImmediateRepath(Vector2 blockedPosition)
        {
            if (state == NPCBehaviorState.Shopping || state == NPCBehaviorState.WaitingForCheckout || state == NPCBehaviorState.Finished)
            {
                return;
            }

            if (Time.time < nextCollisionRepathTime)
            {
                return;
            }

            nextCollisionRepathTime = Time.time + Mathf.Max(0.05f, collisionRepathCooldown);
            var currentPosition = body == null ? (Vector2)transform.position : body.position;
            if (ShopManager.Instance != null)
            {
                if (state == NPCBehaviorState.Entering)
                {
                    if (ShopManager.Instance.TryGetDetourPosition(currentPosition, blockedPosition, shoppingTargetPosition, out var shoppingDetourPosition))
                    {
                        SetTargetPosition(shoppingDetourPosition, shoppingTargetPosition);
                        return;
                    }

                    SetTargetPosition(shoppingTargetPosition);
                    return;
                }

                if (ShopManager.Instance.TryGetDetourPosition(currentPosition, blockedPosition, finalTargetPosition, out var detourPosition))
                {
                    SetTargetPosition(detourPosition, finalTargetPosition);
                    return;
                }
            }

            SetTargetPosition(finalTargetPosition);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (ShopManager.Instance != null && ShopManager.Instance.IsPathObstacle(collision.collider))
            {
                RequestImmediateRepath(collision.GetContact(0).point);
            }
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (ShopManager.Instance != null && ShopManager.Instance.IsPathObstacle(collision.collider))
            {
                RequestImmediateRepath(collision.GetContact(0).point);
            }
        }

        private float GetStoppingDistanceForState()
        {
            return state switch
            {
                NPCBehaviorState.Entering => Mathf.Max(stoppingDistance, shoppingArrivalDistance),
                NPCBehaviorState.Queuing => Mathf.Max(stoppingDistance, queueArrivalDistance),
                _ => stoppingDistance
            };
        }

        private void StopMovement()
        {
            if (body == null)
            {
                return;
            }

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private void UpdateAnimator(Vector2 moveDirection)
        {
            if (animator == null)
            {
                return;
            }

            if (moveDirection != Vector2.zero)
            {
                lastMoveDirection = moveDirection;
            }

            SetAnimatorFloat(hasHorizontalParameter, "Horizontal", lastMoveDirection.x);
            SetAnimatorFloat(hasVerticalParameter, "Vertical", lastMoveDirection.y);
            SetAnimatorFloat(hasLastHorizontalParameter, "LastHorizontal", lastMoveDirection.x);
            SetAnimatorFloat(hasLastVerticalParameter, "LastVertical", lastMoveDirection.y);
            SetAnimatorFloat(hasSpeedParameter, "Speed", moveDirection.sqrMagnitude);
        }

        private void UpdateSortingOrder()
        {
            if (ShopManager.Instance == null)
            {
                return;
            }

            var sortingOrder = ShopManager.Instance.GetDynamicSortingOrder(transform.position);
            foreach (var renderer in GetComponentsInChildren<SpriteRenderer>())
            {
                renderer.sortingOrder = sortingOrder;
            }
        }

        private static Vector2 GetCardinalDirection(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                return direction.x >= 0f ? Vector2.right : Vector2.left;
            }

            return direction.y >= 0f ? Vector2.up : Vector2.down;
        }

        private void CacheAnimatorParameters()
        {
            if (animator == null)
            {
                return;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Float)
                {
                    continue;
                }

                switch (parameter.name)
                {
                    case "Horizontal":
                        hasHorizontalParameter = true;
                        break;
                    case "Vertical":
                        hasVerticalParameter = true;
                        break;
                    case "Speed":
                        hasSpeedParameter = true;
                        break;
                    case "LastHorizontal":
                        hasLastHorizontalParameter = true;
                        break;
                    case "LastVertical":
                        hasLastVerticalParameter = true;
                        break;
                }
            }
        }

        private void SetAnimatorFloat(bool hasParameter, string parameterName, float value)
        {
            if (hasParameter)
            {
                animator.SetFloat(parameterName, value);
            }
        }
    }
}
