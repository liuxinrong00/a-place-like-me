using System.Collections;
using UnityEngine;

namespace APlaceLikeMe.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class BedroomPetController : MonoBehaviour
    {
        private const float MinimumAnimationDurationSeconds = 1f;
        private static readonly int SpeedParameterHash = Animator.StringToHash("speed");
        private static readonly int RandomIndexParameterHash = Animator.StringToHash("randomIndex");

        [Header("Animator States")]
        [SerializeField] private string idleStateName = "Idle";
        [SerializeField] private string idleTwoStateName = "Idle_2";
        [SerializeField] private string sleepingStateName = "Sleeping";
        [SerializeField] private string runStateName = "Run 0";

        [Header("Timing")]
        [SerializeField] private Vector2 idleDurationRange = new(5f, 12f);
        [SerializeField] private Vector2 runDurationRange = new(4f, 9f);
        [SerializeField] private Vector2 destinationPauseRange = new(0.15f, 0.6f);
        [SerializeField] private bool avoidImmediateRepeats = true;

        [Header("Facing")]
        [SerializeField] private bool randomizeFacing = true;
        [SerializeField] private bool faceRightWhileNightSleeping = true;
        [SerializeField] private float horizontalFacingThreshold = 0.05f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 1.35f;
        [SerializeField] private float destinationReachDistance = 0.06f;
        [SerializeField] private float minimumDestinationDistance = 0.8f;
        [SerializeField] private float movementBoundsPadding = 0.55f;
        [SerializeField] private Vector2 fallbackWanderSize = new(6f, 3f);
        [SerializeField] private float directionLockSeconds = 0.35f;
        [SerializeField] private float oppositeDirectionCooldownSeconds = 0.5f;
        [SerializeField] private float stuckDistanceThreshold = 0.025f;
        [SerializeField] private float stuckTimeBeforeNewDestination = 0.6f;

        [Header("Collision Avoidance")]
        [SerializeField] private LayerMask obstacleLayerMask = ~0;
        [SerializeField] private float obstacleProbeDistance = 0.42f;
        [SerializeField] private float obstacleProbeSkin = 0.03f;
        [SerializeField] private bool ignoreTriggerColliders = true;
        [SerializeField] private bool drawDebugRays;

        private Animator animator;
        private SpriteRenderer spriteRenderer;
        private Rigidbody2D body;
        private Collider2D[] ownColliders;
        private readonly RaycastHit2D[] obstacleHits = new RaycastHit2D[8];
        private Coroutine randomPlaybackRoutine;
        private bool hasSpeedParameter;
        private bool hasRandomIndexParameter;
        private int previousRandomAnimationIndex = -1;
        private PlaybackMode playbackMode = PlaybackMode.Unassigned;
        private Rect movementBounds;
        private Vector2 homePosition;
        private Vector2 destination;
        private bool hasMovementBounds;
        private bool isMoving;
        private bool hasHomePosition;
        private bool lastFacingRight = true;
        private CardinalDirection? lockedMoveDirection;
        private float lockedMoveDirectionUntilTime;
        private CardinalDirection? previousMoveDirection;
        private float previousMoveDirectionChangedTime;
        private Vector2 lastProgressPosition;
        private float stuckTimer;

        private enum CardinalDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        private enum PlaybackMode
        {
            Unassigned,
            NightSleep,
            Random
        }

        private struct DirectionProbeState
        {
            public bool LeftBlocked;
            public bool RightBlocked;
            public bool UpBlocked;
            public bool DownBlocked;

            public bool IsBlocked(CardinalDirection direction)
            {
                return direction switch
                {
                    CardinalDirection.Left => LeftBlocked,
                    CardinalDirection.Right => RightBlocked,
                    CardinalDirection.Up => UpBlocked,
                    CardinalDirection.Down => DownBlocked,
                    _ => true
                };
            }
        }

        public void SetNightSleep()
        {
            CacheComponents();
            playbackMode = PlaybackMode.NightSleep;
            StopRandomPlayback();

            ApplyFacing(faceRightWhileNightSleeping);
            PlayAnimation(sleepingStateName, 0, true);
        }

        public void BeginRandomPlayback()
        {
            BeginRandomPlayback(null);
        }

        public void BeginRandomPlayback(Rect? roomMovementBounds)
        {
            CacheComponents();
            ConfigureMovementBounds(roomMovementBounds);
            playbackMode = PlaybackMode.Random;
            StopRandomPlayback();
            ResetMovementMemory();
            randomPlaybackRoutine = StartCoroutine(RandomPlayback());
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void OnEnable()
        {
            if (playbackMode == PlaybackMode.Unassigned)
            {
                SetNightSleep();
                return;
            }

            if (playbackMode == PlaybackMode.Random)
            {
                BeginRandomPlayback();
            }
        }

        private void OnDisable()
        {
            StopRandomPlayback();
        }

        private IEnumerator RandomPlayback()
        {
            while (isActiveAndEnabled)
            {
                var randomAnimationIndex = PickRandomAnimationIndex();
                previousRandomAnimationIndex = randomAnimationIndex;

                switch (randomAnimationIndex)
                {
                    case 0:
                        yield return PlayStationaryAnimation(idleStateName, 0, idleDurationRange, false);
                        break;
                    case 1:
                        yield return PlayStationaryAnimation(idleTwoStateName, 1, idleDurationRange, false);
                        break;
                    case 2:
                        yield return PlayRunActivity(RandomRange(runDurationRange));
                        break;
                }
            }
        }

        private void FixedUpdate()
        {
            if (playbackMode != PlaybackMode.Random || !isMoving)
            {
                StopBodyVelocity();
                return;
            }

            var currentPosition = GetCurrentPosition();
            if (ShouldPickNewDestination(currentPosition))
            {
                destination = PickDestination();
                ResetMovementMemory(currentPosition);
            }

            var toDestination = destination - currentPosition;
            if (toDestination.sqrMagnitude <= destinationReachDistance * destinationReachDistance)
            {
                isMoving = false;
                StopBodyVelocity();
                ClearDirectionLock();
                return;
            }

            var moveDirection = ResolveMoveDirection(currentPosition, toDestination);
            if (moveDirection == Vector2.zero)
            {
                isMoving = false;
                StopBodyVelocity();
                ClearDirectionLock();
                return;
            }

            UpdateFacing(moveDirection);
            var stepDistance = GetStepDistance(toDestination, moveDirection);
            var nextPosition = currentPosition + moveDirection * stepDistance;
            nextPosition = ClampToMovementBounds(nextPosition);
            MoveTo(nextPosition);
        }

        private IEnumerator PlayStationaryAnimation(string stateName, int randomIndex, Vector2 durationRange, bool isSleeping)
        {
            isMoving = false;
            StopBodyVelocity();

            if (randomizeFacing && !isSleeping)
            {
                ApplyFacing(Random.value >= 0.5f);
            }

            PlayAnimation(stateName, randomIndex, isSleeping);
            yield return new WaitForSeconds(RandomRange(durationRange));
        }

        private IEnumerator PlayRunActivity(float durationSeconds)
        {
            var endTime = Time.time + Mathf.Max(MinimumAnimationDurationSeconds, durationSeconds);
            PlayAnimation(runStateName, 2, false);

            while (Time.time < endTime && playbackMode == PlaybackMode.Random && isActiveAndEnabled)
            {
                if (!isMoving)
                {
                    destination = PickDestination();
                    isMoving = true;
                    ResetMovementMemory();
                    PlayAnimation(runStateName, 2, false);
                    yield return new WaitForSeconds(RandomRange(destinationPauseRange, 0f));
                }

                yield return null;
            }

            isMoving = false;
            StopBodyVelocity();
        }

        private int PickRandomAnimationIndex()
        {
            var randomAnimationIndex = Random.Range(0, 3);
            if (avoidImmediateRepeats && previousRandomAnimationIndex >= 0 && randomAnimationIndex == previousRandomAnimationIndex)
            {
                randomAnimationIndex = (randomAnimationIndex + Random.Range(1, 3)) % 3;
            }

            return randomAnimationIndex;
        }

        private Vector2 PickDestination()
        {
            var bounds = GetMovementBounds();
            var currentPosition = GetCurrentPosition();
            var candidate = currentPosition;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                candidate = new Vector2(
                    Random.Range(bounds.xMin, bounds.xMax),
                    Random.Range(bounds.yMin, bounds.yMax));

                if ((candidate - currentPosition).sqrMagnitude >= minimumDestinationDistance * minimumDestinationDistance)
                {
                    return candidate;
                }
            }

            return candidate;
        }

        private Rect GetMovementBounds()
        {
            if (hasMovementBounds)
            {
                return movementBounds;
            }

            var center = hasHomePosition ? homePosition : GetCurrentPosition();
            return Rect.MinMaxRect(
                center.x - fallbackWanderSize.x * 0.5f,
                center.y - fallbackWanderSize.y * 0.5f,
                center.x + fallbackWanderSize.x * 0.5f,
                center.y + fallbackWanderSize.y * 0.5f);
        }

        private void ConfigureMovementBounds(Rect? roomMovementBounds)
        {
            if (!hasHomePosition)
            {
                homePosition = GetCurrentPosition();
                hasHomePosition = true;
            }

            if (!roomMovementBounds.HasValue)
            {
                hasMovementBounds = false;
                return;
            }

            var roomBounds = roomMovementBounds.Value;
            movementBounds = Rect.MinMaxRect(
                roomBounds.xMin + movementBoundsPadding,
                roomBounds.yMin + movementBoundsPadding,
                roomBounds.xMax - movementBoundsPadding,
                roomBounds.yMax - movementBoundsPadding);

            if (movementBounds.width <= 0f || movementBounds.height <= 0f)
            {
                hasMovementBounds = false;
                return;
            }

            hasMovementBounds = true;
        }

        private Vector2 ResolveMoveDirection(Vector2 currentPosition, Vector2 toDestination)
        {
            var desiredDirection = GetPrimaryDirection(toDestination);
            var probes = ProbeDirections(currentPosition);
            if (TryGetLockedMoveDirection(probes, out var lockedDirection))
            {
                return DirectionToVector(lockedDirection);
            }

            var selectedDirection = SelectMoveDirection(desiredDirection, toDestination, probes);
            if (!selectedDirection.HasValue)
            {
                ClearDirectionLock();
                return Vector2.zero;
            }

            selectedDirection = StabilizeDirection(selectedDirection.Value, probes);
            LockMoveDirection(selectedDirection.Value);
            return DirectionToVector(selectedDirection.Value);
        }

        private DirectionProbeState ProbeDirections(Vector2 currentPosition)
        {
            return new DirectionProbeState
            {
                LeftBlocked = IsDirectionBlocked(CardinalDirection.Left, currentPosition),
                RightBlocked = IsDirectionBlocked(CardinalDirection.Right, currentPosition),
                UpBlocked = IsDirectionBlocked(CardinalDirection.Up, currentPosition),
                DownBlocked = IsDirectionBlocked(CardinalDirection.Down, currentPosition)
            };
        }

        private bool IsDirectionBlocked(CardinalDirection direction, Vector2 currentPosition)
        {
            var directionVector = DirectionToVector(direction);
            if (hasMovementBounds && !movementBounds.Contains(currentPosition + directionVector * obstacleProbeDistance))
            {
                DrawProbe(directionVector, true);
                return true;
            }

            return RaycastHitsObstacle(directionVector);
        }

        private bool RaycastHitsObstacle(Vector2 direction)
        {
            var origin = GetProbeOrigin(direction);
            var hitCount = Physics2D.RaycastNonAlloc(origin, direction, obstacleHits, obstacleProbeDistance, obstacleLayerMask);
            for (var index = 0; index < hitCount; index++)
            {
                var hitCollider = obstacleHits[index].collider;
                if (hitCollider == null || IsOwnCollider(hitCollider))
                {
                    continue;
                }

                if (ignoreTriggerColliders && hitCollider.isTrigger)
                {
                    continue;
                }

                DrawProbe(direction, true);
                return true;
            }

            DrawProbe(direction, false);
            return false;
        }

        private CardinalDirection? SelectMoveDirection(CardinalDirection desiredDirection, Vector2 toDestination, DirectionProbeState probes)
        {
            if (!probes.IsBlocked(desiredDirection))
            {
                return desiredDirection;
            }

            var firstSideDirection = GetFirstSideDirection(desiredDirection, toDestination);
            var secondSideDirection = GetOppositeDirection(firstSideDirection);
            if (!probes.IsBlocked(firstSideDirection))
            {
                return firstSideDirection;
            }

            if (!probes.IsBlocked(secondSideDirection))
            {
                return secondSideDirection;
            }

            var oppositeDirection = GetOppositeDirection(desiredDirection);
            if (!probes.IsBlocked(oppositeDirection))
            {
                return oppositeDirection;
            }

            return null;
        }

        private bool TryGetLockedMoveDirection(DirectionProbeState probes, out CardinalDirection direction)
        {
            if (lockedMoveDirection.HasValue &&
                Time.time < lockedMoveDirectionUntilTime &&
                !probes.IsBlocked(lockedMoveDirection.Value))
            {
                direction = lockedMoveDirection.Value;
                return true;
            }

            direction = default;
            return false;
        }

        private CardinalDirection StabilizeDirection(CardinalDirection selectedDirection, DirectionProbeState probes)
        {
            if (!previousMoveDirection.HasValue ||
                selectedDirection != GetOppositeDirection(previousMoveDirection.Value) ||
                Time.time >= previousMoveDirectionChangedTime + oppositeDirectionCooldownSeconds ||
                probes.IsBlocked(previousMoveDirection.Value))
            {
                return selectedDirection;
            }

            return previousMoveDirection.Value;
        }

        private void LockMoveDirection(CardinalDirection direction)
        {
            if (!previousMoveDirection.HasValue || previousMoveDirection.Value != direction)
            {
                previousMoveDirection = direction;
                previousMoveDirectionChangedTime = Time.time;
            }

            lockedMoveDirection = direction;
            lockedMoveDirectionUntilTime = Time.time + directionLockSeconds;
        }

        private void ClearDirectionLock()
        {
            lockedMoveDirection = null;
            lockedMoveDirectionUntilTime = 0f;
        }

        private void ResetMovementMemory()
        {
            ResetMovementMemory(GetCurrentPosition());
        }

        private void ResetMovementMemory(Vector2 currentPosition)
        {
            ClearDirectionLock();
            lastProgressPosition = currentPosition;
            stuckTimer = 0f;
        }

        private bool ShouldPickNewDestination(Vector2 currentPosition)
        {
            if ((currentPosition - lastProgressPosition).sqrMagnitude >= stuckDistanceThreshold * stuckDistanceThreshold)
            {
                lastProgressPosition = currentPosition;
                stuckTimer = 0f;
                return false;
            }

            stuckTimer += Time.fixedDeltaTime;
            return stuckTimer >= stuckTimeBeforeNewDestination;
        }

        private CardinalDirection GetPrimaryDirection(Vector2 toDestination)
        {
            if (Mathf.Abs(toDestination.x) >= Mathf.Abs(toDestination.y))
            {
                return toDestination.x < 0f ? CardinalDirection.Left : CardinalDirection.Right;
            }

            return toDestination.y < 0f ? CardinalDirection.Down : CardinalDirection.Up;
        }

        private CardinalDirection GetFirstSideDirection(CardinalDirection desiredDirection, Vector2 toDestination)
        {
            if (desiredDirection == CardinalDirection.Left || desiredDirection == CardinalDirection.Right)
            {
                var preferUp = Mathf.Abs(toDestination.y) > horizontalFacingThreshold
                    ? toDestination.y > 0f
                    : lastFacingRight;
                return preferUp ? CardinalDirection.Up : CardinalDirection.Down;
            }

            var preferRight = Mathf.Abs(toDestination.x) > horizontalFacingThreshold
                ? toDestination.x > 0f
                : lastFacingRight;
            return preferRight ? CardinalDirection.Right : CardinalDirection.Left;
        }

        private static CardinalDirection GetOppositeDirection(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.Left => CardinalDirection.Right,
                CardinalDirection.Right => CardinalDirection.Left,
                CardinalDirection.Up => CardinalDirection.Down,
                CardinalDirection.Down => CardinalDirection.Up,
                _ => direction
            };
        }

        private static Vector2 DirectionToVector(CardinalDirection direction)
        {
            return direction switch
            {
                CardinalDirection.Left => Vector2.left,
                CardinalDirection.Right => Vector2.right,
                CardinalDirection.Up => Vector2.up,
                CardinalDirection.Down => Vector2.down,
                _ => Vector2.zero
            };
        }

        private float GetStepDistance(Vector2 toDestination, Vector2 moveDirection)
        {
            var maxStepDistance = moveSpeed * Time.fixedDeltaTime;
            var projectedDistance = Vector2.Dot(toDestination, moveDirection);
            return projectedDistance > 0f
                ? Mathf.Min(maxStepDistance, projectedDistance)
                : maxStepDistance;
        }

        private Vector2 ClampToMovementBounds(Vector2 position)
        {
            if (!hasMovementBounds)
            {
                return position;
            }

            position.x = Mathf.Clamp(position.x, movementBounds.xMin, movementBounds.xMax);
            position.y = Mathf.Clamp(position.y, movementBounds.yMin, movementBounds.yMax);
            return position;
        }

        private void PlayAnimation(string stateName, int randomIndex, bool isSleeping)
        {
            if (animator == null)
            {
                return;
            }

            if (hasSpeedParameter)
            {
                animator.SetFloat(SpeedParameterHash, isSleeping ? 1f : 0f);
            }

            if (hasRandomIndexParameter)
            {
                animator.SetInteger(RandomIndexParameterHash, randomIndex);
            }

            if (!string.IsNullOrWhiteSpace(stateName))
            {
                animator.Play(stateName, 0, 0f);
            }
        }

        private void ApplyFacing(bool faceRight)
        {
            lastFacingRight = faceRight;
            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = !faceRight;
            }
        }

        private void UpdateFacing(Vector2 moveDirection)
        {
            if (Mathf.Abs(moveDirection.x) <= horizontalFacingThreshold)
            {
                ApplyFacing(lastFacingRight);
                return;
            }

            ApplyFacing(moveDirection.x > 0f);
        }

        private Vector2 GetProbeOrigin(Vector2 direction)
        {
            var primaryCollider = GetPrimaryCollider();
            if (primaryCollider == null)
            {
                return GetCurrentPosition();
            }

            var bounds = primaryCollider.bounds;
            var origin = (Vector2)bounds.center;
            if (direction.x > 0f)
            {
                origin.x = bounds.max.x + obstacleProbeSkin;
            }
            else if (direction.x < 0f)
            {
                origin.x = bounds.min.x - obstacleProbeSkin;
            }
            else if (direction.y > 0f)
            {
                origin.y = bounds.max.y + obstacleProbeSkin;
            }
            else if (direction.y < 0f)
            {
                origin.y = bounds.min.y - obstacleProbeSkin;
            }

            return origin;
        }

        private Collider2D GetPrimaryCollider()
        {
            if (ownColliders == null)
            {
                return null;
            }

            for (var index = 0; index < ownColliders.Length; index++)
            {
                var ownCollider = ownColliders[index];
                if (ownCollider != null && ownCollider.enabled)
                {
                    return ownCollider;
                }
            }

            return null;
        }

        private bool IsOwnCollider(Collider2D otherCollider)
        {
            if (ownColliders == null)
            {
                return false;
            }

            for (var index = 0; index < ownColliders.Length; index++)
            {
                if (ownColliders[index] == otherCollider)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawProbe(Vector2 direction, bool blocked)
        {
            if (!drawDebugRays)
            {
                return;
            }

            Debug.DrawRay(
                GetProbeOrigin(direction),
                direction * obstacleProbeDistance,
                blocked ? Color.red : Color.green,
                Time.fixedDeltaTime);
        }

        private Vector2 GetCurrentPosition()
        {
            if (body != null)
            {
                return body.position;
            }

            return transform.position;
        }

        private void MoveTo(Vector2 position)
        {
            if (body != null)
            {
                body.MovePosition(position);
                return;
            }

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        private void StopBodyVelocity()
        {
            if (body == null)
            {
                return;
            }

            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private static float RandomRange(Vector2 range)
        {
            return RandomRange(range, MinimumAnimationDurationSeconds);
        }

        private static float RandomRange(Vector2 range, float minimumDuration)
        {
            var minimum = Mathf.Min(range.x, range.y);
            var maximum = Mathf.Max(range.x, range.y);
            return Mathf.Max(minimumDuration, Random.Range(minimum, maximum));
        }

        private void StopRandomPlayback()
        {
            if (randomPlaybackRoutine == null)
            {
                return;
            }

            StopCoroutine(randomPlaybackRoutine);
            randomPlaybackRoutine = null;
            isMoving = false;
            StopBodyVelocity();
        }

        private void CacheComponents()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
                CacheAnimatorParameters();
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.gravityScale = 0f;
                    body.freezeRotation = true;
                }
            }

            if (ownColliders == null || ownColliders.Length == 0)
            {
                ownColliders = GetComponents<Collider2D>();
            }
        }

        private void CacheAnimatorParameters()
        {
            hasSpeedParameter = false;
            hasRandomIndexParameter = false;

            if (animator == null)
            {
                return;
            }

            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedParameterHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    hasSpeedParameter = true;
                    continue;
                }

                if (parameter.nameHash == RandomIndexParameterHash && parameter.type == AnimatorControllerParameterType.Int)
                {
                    hasRandomIndexParameter = true;
                }
            }
        }
    }
}
