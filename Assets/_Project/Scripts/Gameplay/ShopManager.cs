using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace APlaceLikeMe.Gameplay
{
    public sealed class ShopManager : MonoBehaviour
    {
        private const string InteractionMarkerRootName = "Interaction";
        private const string LegacyInteractionMarkerRootName = "Intraction";
        private const string ShoppingObjectName = "Shopping";
        private const string CashierDeskObjectName = "CashierDesk";
        private const string PreferredOutDoorObjectName = "OutDoor";
        private const string FallbackDoorObjectName = "EnterBedRoom";
        private const string NPCParentObjectName = "NPC";
        private const string PlayerObjectName = "LXR";

        public static ShopManager Instance { get; private set; }

        [Header("Scene Anchors")]
        [SerializeField] private Transform outDoor;
        [SerializeField] private Transform shopping;
        [SerializeField] private Transform cashierDesk;
        [SerializeField] private Transform player;

        [Header("Spawning")]
        [SerializeField] private bool spawnCustomers = true;
        [SerializeField] private List<GameObject> npcPrefabs = new();
        [SerializeField] private float firstSpawnDelay = 10f;
        [SerializeField] private float spawnInterval = 20f;
        [SerializeField] private int maxCustomersInStore = 5;
        [SerializeField] private int maxSpawnedCustomers = 5;

        [Header("Checkout")]
        [SerializeField] private int minCheckoutIncome = 3;
        [SerializeField] private int maxCheckoutIncome = 6;

        [Header("Queue")]
        [SerializeField] private float queueSpacing = 0.8f;
        [SerializeField] private Vector2 queueDirection = Vector2.down;
        [SerializeField] private Vector2 cashierQueueOffset = new(0f, -0.85f);
        [SerializeField] private float queueFrontClearance = 0.55f;

        [Header("Fallback Positions")]
        [SerializeField] private Vector2 fallbackOutDoorPosition = new(-4.4f, 0.1f);
        [SerializeField] private Vector2 fallbackShoppingPosition = new(-1.5f, 0f);
        [SerializeField] private Vector2 fallbackCashierPosition = new(0f, 0f);

        [Header("Pathfinding")]
        [SerializeField] private bool useAStarPathfinding = true;
        [SerializeField] private float pathGridSize = 0.25f;
        [SerializeField] private float pathObstacleProbeRadius = 0.32f;
        [SerializeField] private float movementObstacleProbeRadius = 0.18f;
        [SerializeField] private float pathBoundsPadding = 1.5f;
        [SerializeField] private float shoppingApproachDistance = 0.65f;
        [SerializeField] private int maxPathSearchCells = 20000;
        [SerializeField] private int maxGoalSnapCells = 24;

        private static readonly Vector3Int[] TilemapRegionDirections =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 1, 0),
            new(0, -1, 0)
        };

        private static readonly Vector2Int[] CardinalPathDirections =
        {
            new(1, 0),
            new(-1, 0),
            new(0, 1),
            new(0, -1)
        };

        private static readonly Vector2Int[] DiagonalPathDirections =
        {
            new(1, 1),
            new(1, -1),
            new(-1, 1),
            new(-1, -1)
        };

        private readonly List<GameObject> sceneNpcTemplates = new();
        private readonly List<NPCBehavior> activeNPCs = new();
        private readonly List<Vector2> shoppingPositions = new();
        private readonly List<Vector2> shoppingPositionBag = new();
        private Transform npcParent;
        private float spawnTimer;
        private int spawnedCustomerCount;
        private Rect pathBounds;
        private bool hasPathBounds;

        public List<NPCBehavior> queueList = new();

        public IReadOnlyList<NPCBehavior> QueueList => queueList;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapSceneWatcher()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureSceneInstance();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureSceneInstance();
        }

        private static void EnsureSceneInstance()
        {
            if (Instance != null)
            {
                return;
            }

            var npcParentObject = GameObject.Find(NPCParentObjectName);
            if (npcParentObject == null || GameObject.Find(ShoppingObjectName) == null || GameObject.Find(CashierDeskObjectName) == null)
            {
                return;
            }

            var managerObject = new GameObject(nameof(ShopManager));
            SceneManager.MoveGameObjectToScene(managerObject, npcParentObject.scene);
            managerObject.AddComponent<ShopManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveSceneReferences();
            ConfigureInteractionMarkerColliders();
            CacheShoppingPositions();
            RecalculatePathBounds();
            CacheSceneNpcTemplates();
            spawnTimer = Mathf.Max(0f, firstSpawnDelay);
            UpdateQueueTargets();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            UpdateCustomerSpawning();
        }

        public void RegisterNPC(NPCBehavior npc)
        {
            if (npc == null)
            {
                return;
            }

            if (sceneNpcTemplates.Contains(npc.gameObject))
            {
                return;
            }

            if (!activeNPCs.Contains(npc))
            {
                activeNPCs.Add(npc);
            }

            if (player == null)
            {
                player = FindSceneTransform(PlayerObjectName);
            }

            ConfigureNpcCollisionIgnores(npc);

            if (npc.transform.parent == null)
            {
                if (npcParent == null)
                {
                    npcParent = FindSceneTransform(NPCParentObjectName);
                }

                if (npcParent != null)
                {
                    npc.transform.SetParent(npcParent, true);
                }
            }
        }

        public void UnregisterNPC(NPCBehavior npc)
        {
            if (npc == null)
            {
                return;
            }

            if (queueList.Remove(npc))
            {
                UpdateQueueTargets();
            }

            activeNPCs.Remove(npc);
        }

        public void JoinQueue(NPCBehavior npc)
        {
            if (npc == null || queueList.Contains(npc))
            {
                return;
            }

            queueList.Add(npc);
            npc.SetQueueTarget(GetQueuePosition(queueList.Count - 1));
            UpdateQueueTargets();
        }

        public void FinishCheckout(NPCBehavior npc)
        {
            if (npc == null)
            {
                return;
            }

            if (queueList.Remove(npc))
            {
                UpdateQueueTargets();
            }
        }

        public bool IsFirstInQueue(NPCBehavior npc)
        {
            return queueList.Count > 0 && queueList[0] == npc;
        }

        public Vector2 GetDoorPosition()
        {
            return GetAnchorPosition(outDoor, fallbackOutDoorPosition);
        }

        public Vector2 GetShoppingPosition()
        {
            if (shoppingPositions.Count == 0)
            {
                CacheShoppingPositions();
            }

            if (shoppingPositions.Count == 0)
            {
                return GetNearestWalkablePosition(GetAnchorPosition(shopping, fallbackShoppingPosition));
            }

            if (shoppingPositionBag.Count == 0)
            {
                RefillShoppingPositionBag();
            }

            var bagIndex = shoppingPositionBag.Count - 1;
            var position = shoppingPositionBag[bagIndex];
            shoppingPositionBag.RemoveAt(bagIndex);
            return GetNearestWalkablePosition(position);
        }

        public Vector2 GetAlternativeShoppingPosition(Vector2 currentPosition, Vector2 blockedPosition)
        {
            return GetAlternativeShoppingPosition(currentPosition, blockedPosition, blockedPosition);
        }

        public Vector2 GetAlternativeShoppingPosition(Vector2 currentPosition, Vector2 blockedPosition, Vector2 currentTarget)
        {
            if (shoppingPositions.Count == 0)
            {
                CacheShoppingPositions();
            }

            if (shoppingPositions.Count == 0)
            {
                return GetNearestWalkablePosition(GetAnchorPosition(shopping, fallbackShoppingPosition));
            }

            var pathBuffer = new List<Vector2>();
            var bestPosition = GetNearestWalkablePosition(GetAnchorPosition(shopping, fallbackShoppingPosition));
            var bestScore = float.MaxValue;
            for (var index = 0; index < shoppingPositions.Count; index++)
            {
                var candidate = shoppingPositions[index];
                if (Vector2.SqrMagnitude(candidate - blockedPosition) < 0.75f * 0.75f)
                {
                    continue;
                }

                if (Vector2.SqrMagnitude(candidate - currentTarget) < 0.5f * 0.5f)
                {
                    continue;
                }

                if (!TryFindPath(currentPosition, candidate, pathBuffer, out var reachableCandidate))
                {
                    continue;
                }

                var blockedPenalty = 1f / Mathf.Max(0.1f, Vector2.Distance(reachableCandidate, blockedPosition));
                var score = CalculatePathDistance(currentPosition, pathBuffer) + blockedPenalty * 4f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestPosition = reachableCandidate;
            }

            return bestPosition;
        }

        public Vector2 GetCashierPosition()
        {
            return GetAnchorPosition(cashierDesk, fallbackCashierPosition);
        }

        public Vector2 GetQueuePosition(int index)
        {
            var normalizedQueueDirection = queueDirection == Vector2.zero ? Vector2.left : queueDirection.normalized;
            var desiredPosition = GetQueueStartPosition(normalizedQueueDirection) + normalizedQueueDirection * (index * queueSpacing);
            return GetQueueSlotPosition(desiredPosition, normalizedQueueDirection);
        }

        public bool TryCheckoutFrontNPC()
        {
            return TryCheckoutFrontNPC(out _);
        }

        public bool TryCheckoutFrontNPC(out int income)
        {
            income = 0;
            if (queueList.Count == 0)
            {
                return false;
            }

            var frontNPC = queueList[0];
            if (frontNPC == null || !frontNPC.IsWaitingForCheckout)
            {
                return false;
            }

            var minIncome = Mathf.Min(minCheckoutIncome, maxCheckoutIncome);
            var maxIncome = Mathf.Max(minCheckoutIncome, maxCheckoutIncome);
            income = Random.Range(minIncome, maxIncome + 1);
            frontNPC.CheckoutAndLeave();
            return true;
        }

        public bool TryFindPath(Vector2 start, Vector2 goal, List<Vector2> path, out Vector2 finalGoal)
        {
            finalGoal = goal;
            path?.Clear();

            if (!useAStarPathfinding || path == null)
            {
                return false;
            }

            RecalculatePathBounds(start, goal);

            var startCell = WorldToPathCell(start);
            var goalCell = WorldToPathCell(goal);
            if (!TryFindNearestWalkableCell(startCell, out startCell))
            {
                return false;
            }

            if (!TryFindNearestWalkableCell(goalCell, out goalCell))
            {
                finalGoal = GetNearestWalkablePosition(goal);
                path.Add(finalGoal);
                return true;
            }

            var snappedGoal = PathCellToWorld(goalCell);
            var canUseExactGoal = IsPointWalkable(goal) && HasClearPath(snappedGoal, goal, pathObstacleProbeRadius);
            finalGoal = canUseExactGoal ? goal : snappedGoal;

            if (HasClearPath(start, finalGoal, pathObstacleProbeRadius))
            {
                path.Add(finalGoal);
                return true;
            }

            var cellPath = FindPathCells(startCell, goalCell);
            if (cellPath.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < cellPath.Count; index++)
            {
                var point = PathCellToWorld(cellPath[index]);
                if (path.Count == 0 || Vector2.SqrMagnitude(path[^1] - point) > 0.0001f)
                {
                    path.Add(point);
                }
            }

            if (canUseExactGoal && Vector2.SqrMagnitude(path[^1] - finalGoal) > 0.0001f)
            {
                path.Add(finalGoal);
            }
            else
            {
                finalGoal = path[^1];
            }

            SimplifyPath(path);
            return path.Count > 0;
        }

        public Vector2 GetNearestWalkablePosition(Vector2 desiredPosition)
        {
            RecalculatePathBounds(desiredPosition, desiredPosition);

            var desiredCell = WorldToPathCell(desiredPosition);
            if (!TryFindNearestWalkableCell(desiredCell, out var nearestCell))
            {
                return desiredPosition;
            }

            var snappedPosition = PathCellToWorld(nearestCell);
            return IsPointWalkable(desiredPosition) && HasClearPath(snappedPosition, desiredPosition, pathObstacleProbeRadius)
                ? desiredPosition
                : snappedPosition;
        }

        public bool IsMovementSegmentClear(Vector2 start, Vector2 end)
        {
            RecalculatePathBounds(start, end);
            return HasClearPath(start, end, Mathf.Max(0.05f, movementObstacleProbeRadius));
        }

        public bool IsPathObstacle(Collider2D collider)
        {
            return IsPathBlockingCollider(collider);
        }

        public bool TryGetDetourPosition(Vector2 currentPosition, Vector2 blockedPosition, Vector2 finalTarget, out Vector2 detourPosition)
        {
            RecalculatePathBounds(currentPosition, finalTarget);

            var awayDirection = currentPosition - blockedPosition;
            if (awayDirection.sqrMagnitude <= 0.0001f)
            {
                awayDirection = currentPosition - finalTarget;
            }

            if (awayDirection.sqrMagnitude <= 0.0001f)
            {
                awayDirection = Vector2.down;
            }

            awayDirection.Normalize();
            var tangentDirection = new Vector2(-awayDirection.y, awayDirection.x);
            var distances = new[]
            {
                pathGridSize * 3f,
                pathGridSize * 5f,
                pathGridSize * 7f,
                pathGridSize * 9f
            };

            var bestPosition = currentPosition;
            var bestScore = float.MaxValue;
            var pathBuffer = new List<Vector2>();
            for (var index = 0; index < distances.Length; index++)
            {
                var distance = Mathf.Max(0.5f, distances[index]);
                EvaluateDetourCandidate(currentPosition, finalTarget, currentPosition + awayDirection * distance, pathBuffer, ref bestPosition, ref bestScore);
                EvaluateDetourCandidate(currentPosition, finalTarget, currentPosition + tangentDirection * distance + awayDirection * pathGridSize, pathBuffer, ref bestPosition, ref bestScore);
                EvaluateDetourCandidate(currentPosition, finalTarget, currentPosition - tangentDirection * distance + awayDirection * pathGridSize, pathBuffer, ref bestPosition, ref bestScore);
            }

            detourPosition = bestPosition;
            return bestScore < float.MaxValue;
        }

        public int GetDynamicSortingOrder(Vector2 position)
        {
            return Mathf.Clamp(Mathf.RoundToInt(-position.y * 10f), 0, 1);
        }

        private void UpdateCustomerSpawning()
        {
            if (!spawnCustomers || GetNpcTemplateCount() == 0)
            {
                return;
            }

            if (maxSpawnedCustomers > 0 && spawnedCustomerCount >= maxSpawnedCustomers)
            {
                return;
            }

            if (GetActiveCustomerCount() >= Mathf.Max(1, maxCustomersInStore))
            {
                spawnTimer = Mathf.Min(spawnTimer, 0.5f);
                return;
            }

            spawnTimer -= Time.deltaTime;
            if (spawnTimer > 0f)
            {
                return;
            }

            SpawnCustomer();
            spawnTimer = Mathf.Max(0.1f, spawnInterval);
        }

        private void SpawnCustomer()
        {
            var template = PickNpcTemplate();
            if (template == null)
            {
                return;
            }

            if (npcParent == null)
            {
                npcParent = FindSceneTransform(NPCParentObjectName);
            }

            var spawnPosition = GetDoorPosition();
            var instance = Instantiate(template, new Vector3(spawnPosition.x, spawnPosition.y, template.transform.position.z), Quaternion.identity, npcParent);
            instance.name = template.name;
            instance.SetActive(true);

            var behavior = instance.GetComponent<NPCBehavior>();
            if (behavior == null)
            {
                behavior = instance.AddComponent<NPCBehavior>();
            }

            behavior.BeginVisit();
            spawnedCustomerCount++;
        }

        private GameObject PickNpcTemplate()
        {
            if (npcPrefabs.Count > 0)
            {
                return npcPrefabs[Random.Range(0, npcPrefabs.Count)];
            }

            return sceneNpcTemplates.Count == 0 ? null : sceneNpcTemplates[Random.Range(0, sceneNpcTemplates.Count)];
        }

        private int GetNpcTemplateCount()
        {
            return npcPrefabs.Count > 0 ? npcPrefabs.Count : sceneNpcTemplates.Count;
        }

        private int GetActiveCustomerCount()
        {
            for (var index = activeNPCs.Count - 1; index >= 0; index--)
            {
                if (activeNPCs[index] == null)
                {
                    activeNPCs.RemoveAt(index);
                }
            }

            return activeNPCs.Count;
        }

        private void UpdateQueueTargets()
        {
            for (var index = queueList.Count - 1; index >= 0; index--)
            {
                var npc = queueList[index];
                if (npc == null)
                {
                    queueList.RemoveAt(index);
                    continue;
                }

                npc.SetQueueTarget(GetQueuePosition(index));
            }
        }

        private void ConfigureNpcCollisionIgnores(NPCBehavior npc)
        {
            var npcColliders = npc.GetComponentsInChildren<Collider2D>();
            if (npcColliders.Length == 0)
            {
                return;
            }

            if (player != null)
            {
                var playerColliders = player.GetComponentsInChildren<Collider2D>();
                IgnoreColliderPairs(npcColliders, playerColliders);
            }

            for (var index = 0; index < activeNPCs.Count; index++)
            {
                var otherNpc = activeNPCs[index];
                if (otherNpc == null || otherNpc == npc)
                {
                    continue;
                }

                var otherColliders = otherNpc.GetComponentsInChildren<Collider2D>();
                IgnoreColliderPairs(npcColliders, otherColliders);
            }
        }

        private static void IgnoreColliderPairs(Collider2D[] firstColliders, Collider2D[] secondColliders)
        {
            for (var firstIndex = 0; firstIndex < firstColliders.Length; firstIndex++)
            {
                var firstCollider = firstColliders[firstIndex];
                if (firstCollider == null)
                {
                    continue;
                }

                for (var secondIndex = 0; secondIndex < secondColliders.Length; secondIndex++)
                {
                    var secondCollider = secondColliders[secondIndex];
                    if (secondCollider != null)
                    {
                        Physics2D.IgnoreCollision(firstCollider, secondCollider, true);
                    }
                }
            }
        }

        private void EvaluateDetourCandidate(
            Vector2 currentPosition,
            Vector2 finalTarget,
            Vector2 candidate,
            List<Vector2> pathBuffer,
            ref Vector2 bestPosition,
            ref float bestScore)
        {
            var walkableCandidate = GetNearestWalkablePosition(candidate);
            if (!IsPointWalkable(walkableCandidate))
            {
                return;
            }

            if (!TryFindPath(currentPosition, walkableCandidate, pathBuffer, out var reachableCandidate))
            {
                return;
            }

            if (!TryFindPath(reachableCandidate, finalTarget, pathBuffer, out _))
            {
                return;
            }

            var score = Vector2.Distance(currentPosition, reachableCandidate)
                + Vector2.Distance(reachableCandidate, finalTarget) * 0.35f;
            if (score >= bestScore)
            {
                return;
            }

            bestScore = score;
            bestPosition = reachableCandidate;
        }

        private Vector2 GetQueueStartPosition(Vector2 normalizedQueueDirection)
        {
            var cashierPosition = GetCashierPosition();
            var desiredOffsetPosition = cashierPosition + cashierQueueOffset;
            var cashierBounds = GetAnchorBounds(cashierDesk);
            if (!cashierBounds.HasValue)
            {
                return desiredOffsetPosition;
            }

            var bounds = cashierBounds.Value;
            var extents = (Vector2)bounds.extents;
            var projectedExtent = Mathf.Abs(normalizedQueueDirection.x) * extents.x
                + Mathf.Abs(normalizedQueueDirection.y) * extents.y;
            var perpendicularDirection = GetPerpendicularDirection(normalizedQueueDirection);
            var lateralOffset = Vector2.Dot(desiredOffsetPosition - (Vector2)bounds.center, perpendicularDirection);
            return (Vector2)bounds.center
                + normalizedQueueDirection * (projectedExtent + Mathf.Max(pathGridSize, queueFrontClearance))
                + perpendicularDirection * lateralOffset;
        }

        private Vector2 GetQueueSlotPosition(Vector2 desiredPosition, Vector2 normalizedQueueDirection)
        {
            if (IsPointWalkable(desiredPosition))
            {
                return desiredPosition;
            }

            var gridSize = Mathf.Max(0.1f, pathGridSize);
            var perpendicularDirection = GetPerpendicularDirection(normalizedQueueDirection);
            for (var step = 1; step <= maxGoalSnapCells; step++)
            {
                var forwardCandidate = desiredPosition + normalizedQueueDirection * (step * gridSize);
                if (IsPointWalkable(forwardCandidate))
                {
                    return forwardCandidate;
                }

                var leftCandidate = desiredPosition + perpendicularDirection * (step * gridSize);
                if (IsPointWalkable(leftCandidate))
                {
                    return leftCandidate;
                }

                var rightCandidate = desiredPosition - perpendicularDirection * (step * gridSize);
                if (IsPointWalkable(rightCandidate))
                {
                    return rightCandidate;
                }
            }

            return GetNearestWalkablePosition(desiredPosition);
        }

        private void ResolveSceneReferences()
        {
            if (shopping == null)
            {
                shopping = FindSceneTransform(ShoppingObjectName);
            }

            if (cashierDesk == null)
            {
                cashierDesk = FindSceneTransform(CashierDeskObjectName);
            }

            if (outDoor == null)
            {
                outDoor = FindSceneTransform(PreferredOutDoorObjectName);
            }

            if (outDoor == null)
            {
                outDoor = FindSceneTransform(FallbackDoorObjectName);
            }

            if (player == null)
            {
                player = FindSceneTransform(PlayerObjectName);
            }

            fallbackShoppingPosition = GetAnchorPosition(shopping, fallbackShoppingPosition);
            fallbackCashierPosition = GetAnchorPosition(cashierDesk, fallbackCashierPosition);
            fallbackOutDoorPosition = GetAnchorPosition(outDoor, fallbackOutDoorPosition);
        }

        private void ConfigureInteractionMarkerColliders()
        {
            var scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return;
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                foreach (var tilemap in rootObject.GetComponentsInChildren<Tilemap>(true))
                {
                    if (!IsInteractionMarkerTilemap(tilemap))
                    {
                        continue;
                    }

                    if (!IsBlockingInteractionMarker(tilemap.name))
                    {
                        continue;
                    }

                    foreach (var collider in tilemap.GetComponents<Collider2D>())
                    {
                        collider.isTrigger = false;
                    }
                }
            }
        }

        private void CacheShoppingPositions()
        {
            shoppingPositions.Clear();
            shoppingPositionBag.Clear();

            if (shopping == null)
            {
                shoppingPositions.Add(fallbackShoppingPosition);
                RefillShoppingPositionBag();
                return;
            }

            foreach (var tilemap in shopping.GetComponentsInChildren<Tilemap>(true))
            {
                var regions = FindConnectedTileRegions(tilemap);
                for (var index = 0; index < regions.Count; index++)
                {
                    shoppingPositions.Add(GetShoppingApproachPosition(regions[index]));
                }
            }

            if (shoppingPositions.Count == 0)
            {
                shoppingPositions.Add(GetNearestWalkablePosition(GetAnchorPosition(shopping, fallbackShoppingPosition)));
            }

            shoppingPositions.Sort((left, right) =>
            {
                var yComparison = right.y.CompareTo(left.y);
                return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
            });

            RefillShoppingPositionBag();
        }

        private void RefillShoppingPositionBag()
        {
            shoppingPositionBag.Clear();
            shoppingPositionBag.AddRange(shoppingPositions);
            for (var index = 0; index < shoppingPositionBag.Count; index++)
            {
                var swapIndex = Random.Range(index, shoppingPositionBag.Count);
                (shoppingPositionBag[index], shoppingPositionBag[swapIndex]) = (shoppingPositionBag[swapIndex], shoppingPositionBag[index]);
            }
        }

        private Vector2 GetShoppingApproachPosition(TilemapRegion region)
        {
            var offset = Mathf.Max(pathGridSize, shoppingApproachDistance);
            var maxApproachDistance = offset + pathGridSize * 2f;
            var candidates = BuildShoppingApproachCandidates(region, offset);

            var bestPosition = GetNearestWalkablePosition(region.Center);
            var bestScore = float.MaxValue;
            var doorPosition = GetDoorPosition();
            var queuePosition = GetQueuePosition(0);
            var entryPath = new List<Vector2>();
            var exitPath = new List<Vector2>();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var walkableCandidate = GetNearestWalkablePosition(candidate);
                if (!IsPointWalkable(walkableCandidate))
                {
                    continue;
                }

                if (!TryFindPath(doorPosition, walkableCandidate, entryPath, out var reachableCandidate))
                {
                    continue;
                }

                if (!TryFindPath(reachableCandidate, queuePosition, exitPath, out _))
                {
                    continue;
                }

                var shelfDistancePenalty = GetDistanceToRegion(region, reachableCandidate);
                if (shelfDistancePenalty > maxApproachDistance)
                {
                    continue;
                }

                var snapPenalty = Vector2.SqrMagnitude(walkableCandidate - candidate);
                var score = CalculatePathDistance(doorPosition, entryPath)
                    + CalculatePathDistance(reachableCandidate, exitPath) * 0.15f
                    + snapPenalty * 8f
                    + shelfDistancePenalty * 3f
                    + index * 0.25f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestPosition = reachableCandidate;
            }

            if (bestScore < float.MaxValue)
            {
                return bestPosition;
            }

            bestScore = float.MaxValue;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var walkableCandidate = GetNearestWalkablePosition(candidate);
                if (!IsPointWalkable(walkableCandidate))
                {
                    continue;
                }

                var shelfDistancePenalty = GetDistanceToRegion(region, walkableCandidate);
                if (shelfDistancePenalty > maxApproachDistance)
                {
                    continue;
                }

                var snapPenalty = Vector2.SqrMagnitude(walkableCandidate - candidate);
                var score = snapPenalty * 8f + shelfDistancePenalty * 3f + index * 0.25f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestPosition = walkableCandidate;
            }

            return bestPosition;
        }

        private List<Vector2> BuildShoppingApproachCandidates(TilemapRegion region, float baseOffset)
        {
            var candidates = new List<Vector2>();
            var offsets = new[]
            {
                baseOffset,
                baseOffset + pathGridSize,
                baseOffset + pathGridSize * 2f
            };

            for (var offsetIndex = 0; offsetIndex < offsets.Length; offsetIndex++)
            {
                var offset = offsets[offsetIndex];
                candidates.Add(new Vector2(region.Center.x, region.Min.y - offset));
                candidates.Add(new Vector2(region.Min.x - offset, region.Center.y));
                candidates.Add(new Vector2(region.Max.x + offset, region.Center.y));
                candidates.Add(new Vector2(region.Center.x, region.Max.y + offset));
                candidates.Add(new Vector2(region.Min.x - offset, region.Min.y - offset));
                candidates.Add(new Vector2(region.Max.x + offset, region.Min.y - offset));
                candidates.Add(new Vector2(region.Min.x - offset, region.Max.y + offset));
                candidates.Add(new Vector2(region.Max.x + offset, region.Max.y + offset));
            }

            return candidates;
        }

        private static float GetDistanceToRegion(TilemapRegion region, Vector2 position)
        {
            var nearestX = Mathf.Clamp(position.x, region.Min.x, region.Max.x);
            var nearestY = Mathf.Clamp(position.y, region.Min.y, region.Max.y);
            return Vector2.Distance(position, new Vector2(nearestX, nearestY));
        }

        private static float CalculatePathDistance(Vector2 start, List<Vector2> path)
        {
            if (path == null || path.Count == 0)
            {
                return 0f;
            }

            var distance = 0f;
            var previous = start;
            for (var index = 0; index < path.Count; index++)
            {
                distance += Vector2.Distance(previous, path[index]);
                previous = path[index];
            }

            return distance;
        }

        private void CacheSceneNpcTemplates()
        {
            npcParent = FindSceneTransform(NPCParentObjectName);
            if (npcParent == null)
            {
                return;
            }

            sceneNpcTemplates.Clear();
            foreach (Transform child in npcParent)
            {
                var templateObject = child.gameObject;
                sceneNpcTemplates.Add(templateObject);
                if (templateObject.activeSelf)
                {
                    templateObject.SetActive(false);
                }

                if (templateObject.GetComponent<NPCBehavior>() == null)
                {
                    templateObject.AddComponent<NPCBehavior>();
                }
            }
        }

        private void RecalculatePathBounds()
        {
            RecalculatePathBounds(null, null);
        }

        private void RecalculatePathBounds(Vector2? includeA, Vector2? includeB)
        {
            var scene = gameObject.scene;
            var hasBounds = false;
            var min = Vector2.zero;
            var max = Vector2.zero;

            if (scene.IsValid())
            {
                foreach (var rootObject in scene.GetRootGameObjects())
                {
                    foreach (var tilemap in rootObject.GetComponentsInChildren<Tilemap>(true))
                    {
                        if (IsInteractionMarkerTilemap(tilemap))
                        {
                            continue;
                        }

                        var tilemapBounds = CalculateTilemapBounds(tilemap);
                        if (!tilemapBounds.HasValue)
                        {
                            continue;
                        }

                        if (!hasBounds)
                        {
                            min = tilemapBounds.Value.min;
                            max = tilemapBounds.Value.max;
                            hasBounds = true;
                            continue;
                        }

                        min = Vector2.Min(min, tilemapBounds.Value.min);
                        max = Vector2.Max(max, tilemapBounds.Value.max);
                    }
                }
            }

            IncludePointInBounds(includeA, ref hasBounds, ref min, ref max);
            IncludePointInBounds(includeB, ref hasBounds, ref min, ref max);

            if (!hasBounds)
            {
                min = Vector2.Min(fallbackOutDoorPosition, fallbackShoppingPosition);
                max = Vector2.Max(fallbackOutDoorPosition, fallbackShoppingPosition);
            }

            pathBounds = Rect.MinMaxRect(
                min.x - pathBoundsPadding,
                min.y - pathBoundsPadding,
                max.x + pathBoundsPadding,
                max.y + pathBoundsPadding);
            hasPathBounds = true;
        }

        private static void IncludePointInBounds(Vector2? point, ref bool hasBounds, ref Vector2 min, ref Vector2 max)
        {
            if (!point.HasValue)
            {
                return;
            }

            if (!hasBounds)
            {
                min = point.Value;
                max = point.Value;
                hasBounds = true;
                return;
            }

            min = Vector2.Min(min, point.Value);
            max = Vector2.Max(max, point.Value);
        }

        private List<Vector2Int> FindPathCells(Vector2Int startCell, Vector2Int goalCell)
        {
            var openCells = new List<Vector2Int> { startCell };
            var closedCells = new HashSet<Vector2Int>();
            var nodes = new Dictionary<Vector2Int, PathNode>
            {
                [startCell] = new(startCell, startCell, 0f, GetPathHeuristic(startCell, goalCell))
            };

            while (openCells.Count > 0 && closedCells.Count < maxPathSearchCells)
            {
                var currentCell = RemoveBestOpenCell(openCells, nodes);
                if (currentCell == goalCell)
                {
                    return ReconstructPathCells(nodes, currentCell);
                }

                closedCells.Add(currentCell);

                foreach (var direction in CardinalPathDirections)
                {
                    VisitNeighborCell(currentCell, direction, goalCell, openCells, closedCells, nodes);
                }

                foreach (var direction in DiagonalPathDirections)
                {
                    if (!IsPathCellWalkable(currentCell + new Vector2Int(direction.x, 0))
                        || !IsPathCellWalkable(currentCell + new Vector2Int(0, direction.y)))
                    {
                        continue;
                    }

                    VisitNeighborCell(currentCell, direction, goalCell, openCells, closedCells, nodes);
                }
            }

            return new List<Vector2Int>();
        }

        private void VisitNeighborCell(
            Vector2Int currentCell,
            Vector2Int direction,
            Vector2Int goalCell,
            List<Vector2Int> openCells,
            HashSet<Vector2Int> closedCells,
            Dictionary<Vector2Int, PathNode> nodes)
        {
            var neighborCell = currentCell + direction;
            if (closedCells.Contains(neighborCell) || !IsPathCellWalkable(neighborCell))
            {
                return;
            }

            var currentNode = nodes[currentCell];
            var moveCost = direction.x != 0 && direction.y != 0 ? 1.4142f : 1f;
            var tentativeCost = currentNode.GCost + moveCost;

            if (nodes.TryGetValue(neighborCell, out var existingNode) && tentativeCost >= existingNode.GCost)
            {
                return;
            }

            nodes[neighborCell] = new PathNode(
                neighborCell,
                currentCell,
                tentativeCost,
                tentativeCost + GetPathHeuristic(neighborCell, goalCell));

            if (!openCells.Contains(neighborCell))
            {
                openCells.Add(neighborCell);
            }
        }

        private static Vector2Int RemoveBestOpenCell(List<Vector2Int> openCells, Dictionary<Vector2Int, PathNode> nodes)
        {
            var bestIndex = 0;
            var bestNode = nodes[openCells[0]];
            for (var index = 1; index < openCells.Count; index++)
            {
                var candidateNode = nodes[openCells[index]];
                if (candidateNode.FCost < bestNode.FCost
                    || Mathf.Approximately(candidateNode.FCost, bestNode.FCost) && candidateNode.GCost > bestNode.GCost)
                {
                    bestIndex = index;
                    bestNode = candidateNode;
                }
            }

            var bestCell = openCells[bestIndex];
            openCells.RemoveAt(bestIndex);
            return bestCell;
        }

        private static List<Vector2Int> ReconstructPathCells(Dictionary<Vector2Int, PathNode> nodes, Vector2Int currentCell)
        {
            var path = new List<Vector2Int> { currentCell };
            while (nodes.TryGetValue(currentCell, out var node) && node.Parent != currentCell)
            {
                currentCell = node.Parent;
                path.Add(currentCell);
            }

            path.Reverse();
            return path;
        }

        private bool TryFindNearestWalkableCell(Vector2Int originCell, out Vector2Int walkableCell)
        {
            if (IsPathCellWalkable(originCell))
            {
                walkableCell = originCell;
                return true;
            }

            for (var radius = 1; radius <= maxGoalSnapCells; radius++)
            {
                var foundCell = false;
                var bestCell = originCell;
                var bestDistance = float.MaxValue;
                for (var x = -radius; x <= radius; x++)
                {
                    for (var y = -radius; y <= radius; y++)
                    {
                        if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        {
                            continue;
                        }

                        var candidateCell = originCell + new Vector2Int(x, y);
                        if (!IsPathCellWalkable(candidateCell))
                        {
                            continue;
                        }

                        var distance = (candidateCell - originCell).sqrMagnitude;
                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        foundCell = true;
                        bestDistance = distance;
                        bestCell = candidateCell;
                    }
                }

                if (foundCell)
                {
                    walkableCell = bestCell;
                    return true;
                }
            }

            walkableCell = originCell;
            return false;
        }

        private void SimplifyPath(List<Vector2> path)
        {
            if (path.Count <= 2)
            {
                return;
            }

            var simplifiedPath = new List<Vector2> { path[0] };
            var anchorIndex = 0;
            for (var testIndex = 2; testIndex < path.Count; testIndex++)
            {
                if (HasClearPath(path[anchorIndex], path[testIndex], pathObstacleProbeRadius))
                {
                    continue;
                }

                simplifiedPath.Add(path[testIndex - 1]);
                anchorIndex = testIndex - 1;
            }

            simplifiedPath.Add(path[^1]);
            path.Clear();
            path.AddRange(simplifiedPath);
        }

        private bool HasClearPath(Vector2 start, Vector2 end, float probeRadius)
        {
            var distance = Vector2.Distance(start, end);
            if (distance <= 0.001f)
            {
                return IsPointWalkable(end, probeRadius);
            }

            var stepDistance = Mathf.Max(0.1f, pathGridSize * 0.5f);
            var steps = Mathf.Max(1, Mathf.CeilToInt(distance / stepDistance));
            for (var index = 0; index <= steps; index++)
            {
                var point = Vector2.Lerp(start, end, index / (float)steps);
                if (!IsPointWalkable(point, probeRadius))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsPathCellWalkable(Vector2Int cell)
        {
            return IsPointInsidePathBounds(PathCellToWorld(cell)) && IsPointWalkable(PathCellToWorld(cell), pathObstacleProbeRadius);
        }

        private bool IsPointWalkable(Vector2 point)
        {
            return IsPointWalkable(point, pathObstacleProbeRadius);
        }

        private bool IsPointWalkable(Vector2 point, float probeRadius)
        {
            if (!IsPointInsidePathBounds(point))
            {
                return false;
            }

            var overlaps = Physics2D.OverlapCircleAll(point, Mathf.Max(0.05f, probeRadius));
            for (var index = 0; index < overlaps.Length; index++)
            {
                if (IsPathBlockingCollider(overlaps[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsPointInsidePathBounds(Vector2 point)
        {
            if (!hasPathBounds)
            {
                RecalculatePathBounds(point, point);
            }

            return pathBounds.Contains(point);
        }

        private bool IsPathBlockingCollider(Collider2D collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                return false;
            }

            if (collider.gameObject.scene != gameObject.scene)
            {
                return false;
            }

            if (collider.attachedRigidbody != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<NPCBehavior>() != null)
            {
                return false;
            }

            if (IsInteractionMarker(collider.transform))
            {
                var tilemap = collider.GetComponent<Tilemap>();
                return tilemap != null && IsBlockingInteractionMarker(tilemap.name);
            }

            var blockingTilemap = collider.GetComponent<Tilemap>();
            return blockingTilemap == null || !IsGroundTilemapName(blockingTilemap.name);
        }

        private Vector2Int WorldToPathCell(Vector2 position)
        {
            var gridSize = Mathf.Max(0.1f, pathGridSize);
            return new Vector2Int(
                Mathf.FloorToInt((position.x - pathBounds.xMin) / gridSize),
                Mathf.FloorToInt((position.y - pathBounds.yMin) / gridSize));
        }

        private Vector2 PathCellToWorld(Vector2Int cell)
        {
            var gridSize = Mathf.Max(0.1f, pathGridSize);
            return new Vector2(pathBounds.xMin + (cell.x + 0.5f) * gridSize, pathBounds.yMin + (cell.y + 0.5f) * gridSize);
        }

        private static float GetPathHeuristic(Vector2Int from, Vector2Int to)
        {
            var dx = Mathf.Abs(from.x - to.x);
            var dy = Mathf.Abs(from.y - to.y);
            return Mathf.Max(dx, dy) + (1.4142f - 1f) * Mathf.Min(dx, dy);
        }

        private static List<TilemapRegion> FindConnectedTileRegions(Tilemap tilemap)
        {
            var remainingCells = new HashSet<Vector3Int>();
            foreach (var cellPosition in tilemap.cellBounds.allPositionsWithin)
            {
                if (tilemap.HasTile(cellPosition))
                {
                    remainingCells.Add(cellPosition);
                }
            }

            var regions = new List<TilemapRegion>();
            while (remainingCells.Count > 0)
            {
                var startCell = default(Vector3Int);
                foreach (var cell in remainingCells)
                {
                    startCell = cell;
                    break;
                }

                var regionCells = FloodFillTileRegion(startCell, remainingCells);
                regions.Add(CreateTilemapRegion(tilemap, regionCells));
            }

            return regions;
        }

        private static List<Vector3Int> FloodFillTileRegion(Vector3Int startCell, HashSet<Vector3Int> remainingCells)
        {
            var regionCells = new List<Vector3Int>();
            var pendingCells = new Queue<Vector3Int>();
            pendingCells.Enqueue(startCell);
            remainingCells.Remove(startCell);

            while (pendingCells.Count > 0)
            {
                var currentCell = pendingCells.Dequeue();
                regionCells.Add(currentCell);

                foreach (var direction in TilemapRegionDirections)
                {
                    var neighborCell = currentCell + direction;
                    if (!remainingCells.Remove(neighborCell))
                    {
                        continue;
                    }

                    pendingCells.Enqueue(neighborCell);
                }
            }

            return regionCells;
        }

        private static TilemapRegion CreateTilemapRegion(Tilemap tilemap, List<Vector3Int> cells)
        {
            var minX = cells[0].x;
            var minY = cells[0].y;
            var maxX = cells[0].x;
            var maxY = cells[0].y;
            for (var index = 1; index < cells.Count; index++)
            {
                minX = Mathf.Min(minX, cells[index].x);
                minY = Mathf.Min(minY, cells[index].y);
                maxX = Mathf.Max(maxX, cells[index].x);
                maxY = Mathf.Max(maxY, cells[index].y);
            }

            var minWorld = tilemap.CellToWorld(new Vector3Int(minX, minY, 0));
            var maxWorld = tilemap.CellToWorld(new Vector3Int(maxX + 1, maxY + 1, 0));
            var min = Vector2.Min(minWorld, maxWorld);
            var max = Vector2.Max(minWorld, maxWorld);
            return new TilemapRegion(min, max);
        }

        private static Rect? CalculateTilemapBounds(Tilemap tilemap)
        {
            var hasTile = false;
            var minWorld = Vector2.zero;
            var maxWorld = Vector2.zero;

            foreach (var cellPosition in tilemap.cellBounds.allPositionsWithin)
            {
                if (!tilemap.HasTile(cellPosition))
                {
                    continue;
                }

                var cellMin = (Vector2)tilemap.CellToWorld(cellPosition);
                var cellMax = (Vector2)tilemap.CellToWorld(cellPosition + new Vector3Int(1, 1, 0));
                var cellWorldMin = Vector2.Min(cellMin, cellMax);
                var cellWorldMax = Vector2.Max(cellMin, cellMax);

                if (!hasTile)
                {
                    minWorld = cellWorldMin;
                    maxWorld = cellWorldMax;
                    hasTile = true;
                    continue;
                }

                minWorld = Vector2.Min(minWorld, cellWorldMin);
                maxWorld = Vector2.Max(maxWorld, cellWorldMax);
            }

            return hasTile ? Rect.MinMaxRect(minWorld.x, minWorld.y, maxWorld.x, maxWorld.y) : null;
        }

        private static Transform FindSceneTransform(string objectName)
        {
            var sceneObject = GameObject.Find(objectName);
            return sceneObject == null ? null : sceneObject.transform;
        }

        private static Vector2 GetAnchorPosition(Transform anchor, Vector2 fallbackPosition)
        {
            if (anchor == null)
            {
                return fallbackPosition;
            }

            var collider = anchor.GetComponent<Collider2D>();
            if (collider != null)
            {
                return collider.bounds.center;
            }

            var renderer = anchor.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds.center;
            }

            return anchor.position;
        }

        private static Bounds? GetAnchorBounds(Transform anchor)
        {
            if (anchor == null)
            {
                return null;
            }

            var collider = anchor.GetComponent<Collider2D>();
            if (collider != null)
            {
                return collider.bounds;
            }

            var renderer = anchor.GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds;
            }

            return null;
        }

        private static bool IsInteractionMarkerTilemap(Tilemap tilemap)
        {
            return tilemap != null && IsInteractionMarker(tilemap.transform);
        }

        private static bool IsInteractionMarker(Transform target)
        {
            var current = target;
            while (current != null)
            {
                if (current.name == InteractionMarkerRootName || current.name == LegacyInteractionMarkerRootName)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsGroundTilemapName(string tilemapName)
        {
            return tilemapName == "Ground" || tilemapName.StartsWith("Ground-");
        }

        private static Vector2 GetPerpendicularDirection(Vector2 direction)
        {
            return new Vector2(-direction.y, direction.x);
        }

        private static bool IsBlockingInteractionMarker(string markerName)
        {
            return markerName == ShoppingObjectName || markerName == CashierDeskObjectName;
        }

        private readonly struct PathNode
        {
            public PathNode(Vector2Int cell, Vector2Int parent, float gCost, float fCost)
            {
                Cell = cell;
                Parent = parent;
                GCost = gCost;
                FCost = fCost;
            }

            public Vector2Int Cell { get; }
            public Vector2Int Parent { get; }
            public float GCost { get; }
            public float FCost { get; }
        }

        private readonly struct TilemapRegion
        {
            public TilemapRegion(Vector2 min, Vector2 max)
            {
                Min = min;
                Max = max;
                Center = (min + max) * 0.5f;
            }

            public Vector2 Min { get; }
            public Vector2 Max { get; }
            public Vector2 Center { get; }
        }
    }
}
