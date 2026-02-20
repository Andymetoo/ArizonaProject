using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using HutongGames.PlayMaker;
using UnityEngine.UI;

public class Traveler : MonoBehaviour
{
    private List<int> selectedSquares;
    private Vector2Int gridSize = new Vector2Int(5, 5);
    private List<Vector2Int> targetPositions; // Target positions as grid coordinates
    private Queue<Vector2Int> travelPath; // Queue to store the path the traveler will take
    private Vector2Int currentTarget; // Current target grid position
    private Vector2Int startGridPosition; // Starting grid position

    [Header("Debug Trace")]
    [SerializeField] private bool debugTrace = false;
    [SerializeField] private bool debugTraceMovement = true;
    [SerializeField] private bool debugTraceSelections = true;
    [SerializeField] private bool debugTraceTriggerColliders = true;

    public float speed = 5f;

    [Header("Validation Speed-Up (Optional)")]
    [UnityEngine.Tooltip("If enabled, allows the player to hold a key to speed up Traveler movement during validation/check phases.")]
    [SerializeField] private bool enableValidationSpeedUp = false;

    [UnityEngine.Tooltip("If true, speed-up requires an external runtime gate (SetValidationSpeedUpAllowed). If false, speed-up is available whenever enabled.")]
    [SerializeField] private bool validationSpeedUpRequiresExternalGate = true;

    [UnityEngine.Tooltip("Key that speeds up the Traveler while held (when allowed).")]
    [SerializeField] private KeyCode validationSpeedUpKey = KeyCode.Q;

    [Min(1f)]
    [SerializeField] private float validationSpeedUpMultiplier = 2f;

    [UnityEngine.Tooltip("Optional UI GameObject to show when speed-up is available.")]
    [SerializeField] private GameObject validationSpeedUpAvailableUI;

    [UnityEngine.Tooltip("Optional UI GameObject to show when the speed-up key is currently held (a 'depressed button' state). If assigned, this will be shown instead of the available UI while the key is held.")]
    [SerializeField] private GameObject validationSpeedUpHeldUI;

    [Header("Validation Speed-Up UI (Optional Sprite Swap)")]
    [UnityEngine.Tooltip("Optional: the Image component to swap sprites on while the speed-up key is held. If null, we will try to auto-find an Image under Validation Speed-Up Available UI at runtime.")]
    [SerializeField] private Image validationSpeedUpIconImage;

    [UnityEngine.Tooltip("Optional: sprite to show while the speed-up key is held. If set, the original sprite will be restored on release.")]
    [SerializeField] private Sprite validationSpeedUpHeldSprite;

    private bool triedResolveValidationSpeedUpIcon;
    private bool capturedValidationSpeedUpDefaultSprite;
    private Sprite validationSpeedUpDefaultSprite;

    [Header("Validation Speed-Up Status Text (Optional)")]
    [UnityEngine.Tooltip("Optional: a TMPTextFader to fade a status line (e.g. 'She is checking your placements') whenever validation speed-up is available.")]
    [SerializeField] private TMPTextFader validationCheckingStatusText;

    [SerializeField] private float validationCheckingStatusFadeInSeconds = 0.25f;
    [SerializeField] private float validationCheckingStatusFadeOutSeconds = 0.25f;

    private bool validationCheckingStatusInitialized;
    private bool lastValidationCheckingStatusVisible;

    [UnityEngine.Tooltip("Runtime gate (typically toggled by PlayMaker) for when speed-up is allowed.")]
    [SerializeField] public bool validationSpeedUpAllowed = false;

    public bool ValidationSpeedUpAllowed => validationSpeedUpAllowed;

    private bool IsValidationSpeedUpAvailable => enableValidationSpeedUp && (!validationSpeedUpRequiresExternalGate || validationSpeedUpAllowed);

    [Header("Validation Sniff Invalid Placements (Optional)")]
    [UnityEngine.Tooltip("If enabled, the Traveler can optionally route to squares that have placed objects on NON-selected squares during validation/check. Intended to make 'wrong placement' discovery consistent (opt-in + PlayMaker-gated).")]
    [SerializeField] private bool enableValidationSniffInvalidPlacements = false;

    [UnityEngine.Tooltip("If true, sniff requires an external runtime gate (SetValidationSniffAllowed). If false, sniff is active whenever enabled.")]
    [SerializeField] private bool validationSniffRequiresExternalGate = true;

    [UnityEngine.Tooltip("Runtime gate (typically toggled by PlayMaker) for when the sniff pass is allowed.")]
    [SerializeField] private bool validationSniffAllowed = false;

    private bool IsValidationSniffActive => enableValidationSniffInvalidPlacements && (!validationSniffRequiresExternalGate || validationSniffAllowed);

    private readonly HashSet<int> remainingSniffNodes = new HashSet<int>();
    private readonly HashSet<int> visitedSniffNodesThisRun = new HashSet<int>();
    public float pauseDuration = 1.0f; // Serialized pause duration
    private bool isInitialized = false;
    private bool isResetting = false;
    public bool isPatternComplete = false;
    public bool IsPatternComplete => isPatternComplete; // Expose this to Playmaker or other scripts

    // Incremented each time a new Traveler run begins.
    // Used to prevent delayed coroutines from resuming an old run after a new round starts.
    private int travelerRunToken = 0;

    private Vector2Int lastMoveDirection = Vector2Int.zero;

    // Current logical segment (tile-to-tile) the Traveler is moving along.
    // This avoids relying on rounded transform position for approach/away classification.
    private Vector2Int currentSegmentFromTile;
    private Vector2Int currentSegmentToTile;

    [Header("Turn SFX")]
    [UnityEngine.Tooltip("If enabled, emits a turn sound when the Traveler changes movement direction.")]
    [SerializeField] private bool enableTurnSfx = true;

    private enum TurnRelativeToPlayer
    {
        Unknown = 0,
        Toward = 1,
        Away = 2,
        Lateral = 3,
    }

    private TurnRelativeToPlayer GetRelativeToCenterTile(Vector2Int fromTile, Vector2Int toTile)
    {
        Vector2Int centerTile = new Vector2Int(gridSize.x / 2, gridSize.y / 2);

        int fromDist = Mathf.Abs(fromTile.x - centerTile.x) + Mathf.Abs(fromTile.y - centerTile.y);
        int toDist = Mathf.Abs(toTile.x - centerTile.x) + Mathf.Abs(toTile.y - centerTile.y);

        if (toDist < fromDist) return TurnRelativeToPlayer.Toward;
        if (toDist > fromDist) return TurnRelativeToPlayer.Away;
        return TurnRelativeToPlayer.Lateral;
    }

    public float CurrentMoveToward01
    {
        get
        {
            if (currentSegmentFromTile == currentSegmentToTile) return 0.5f;
            TurnRelativeToPlayer rel = GetRelativeToCenterTile(currentSegmentFromTile, currentSegmentToTile);
            if (rel == TurnRelativeToPlayer.Toward) return 1f;
            if (rel == TurnRelativeToPlayer.Away) return 0f;
            return 0.5f;
        }
    }

    public bool IsCurrentlyMovingTowardPlayer => CurrentMoveToward01 > 0.5f;
    public bool IsCurrentlyMovingAwayFromPlayer => CurrentMoveToward01 < 0.5f;

    [UnityEngine.Tooltip("If enabled, broadcasts a PlayMaker event when turning (legacy behavior).")]
    [SerializeField] private bool broadcastTurnEvent = false;

    [SerializeField] private string turnEventName = "AUDIOTurnSound";

    [UnityEngine.Tooltip("Optional: play a turn sound directly from this script (in addition to, or instead of, PlayMaker).")]
    [SerializeField] private bool playTurnClipDirectly = true;

    [SerializeField] private AudioSource turnAudioSource;
    [SerializeField] private AudioClip[] turnClips;

    [Header("Turn SFX (Toward/Away)")]
    [UnityEngine.Tooltip("Optional. If set, these clips are used when a turn results in the Traveler stepping closer to the player/center tile.")]
    [SerializeField] private AudioClip[] turnTowardPlayerClips;

    [UnityEngine.Tooltip("Optional. If set, these clips are used when a turn results in the Traveler stepping farther from the player/center tile.")]
    [SerializeField] private AudioClip[] turnAwayFromPlayerClips;

    [UnityEngine.Tooltip("Optional. Used when the next step is neither closer nor farther (distance unchanged). If empty, falls back to Turn Clips.")]
    [SerializeField] private AudioClip[] turnLateralClips;
    [Range(0f, 1f)]
    [SerializeField] private float turnClipVolume = 1f;

    [UnityEngine.Tooltip("Prevents rapid double-triggering if multiple turn checks happen close together.")]
    [Min(0f)]
    [SerializeField] private float minSecondsBetweenTurnSounds = 0.2f;

    private float lastTurnSoundTime = -999f;
    [SerializeField] private int lastTurnAngle;

    [SerializeField] private TurnRelativeToPlayer lastTurnRelativeToPlayer;
    public bool LastTurnWasTowardPlayer => lastTurnRelativeToPlayer == TurnRelativeToPlayer.Toward;
    public bool LastTurnWasAwayFromPlayer => lastTurnRelativeToPlayer == TurnRelativeToPlayer.Away;
    public bool LastTurnWasLateralToPlayer => lastTurnRelativeToPlayer == TurnRelativeToPlayer.Lateral;

    public int LastTurnAngle => lastTurnAngle;

    public bool isPausing = false; // Tracks if the traveler is currently pausing

    [Header("Round-to-Round Start Behavior")]
    [UnityEngine.Tooltip("If enabled, when a round completes and the Traveler is still on the grid, the next InitTraveler() will start the new path from the Traveler's current tile (no corner warp). If disabled, each new round starts from a corner as usual.")]
    [SerializeField] private bool continueFromCurrentTileBetweenRounds = true;
    [SerializeField] private float onGridTolerance = 0.35f;

    private bool lastRoundCompleted;
    private bool startFromCurrentTileThisInit;
    private bool forceCornerStartNextInit;

    // PlayMaker-friendly hook:
    // Call this when the Traveler is about to come out of (or has just returned from) the attic/hatch.
    // It forces the next InitTraveler() to pick a corner start instead of continuing from the last tile.
    public void ForceNextInitFromCorner()
    {
        forceCornerStartNextInit = true;
    }

    // Convenience entrypoint for PlayMaker: use this instead of InitTraveler() for hatch/attic starts.
    public void InitTravelerFromHatch()
    {
        forceCornerStartNextInit = true;
        InitTraveler();
    }

    [Header("Traveler Visual Settings")]
    public Transform travelerVisual; // Reference to the child visual object
    public float rotationSpeed = 5f; // Speed at which the child rotates to face forward movement
    public Follower follower; // Assign in Inspector
    public bool isFollowerReady = false;
    private bool hasCustomStart = false;
    public float followerDetatchDelay = 1.5f;
    public float pauseTurnWaitTime = 1.0f;

    [Header("Debug Force Start (Optional)")]
    [UnityEngine.Tooltip("If enabled, Traveler will start at the forced start index (0-24). Off by default.")]
    [SerializeField] private bool debugForceStartIndex = false;
    [Range(0, 24)]
    [SerializeField] private int debugStartIndex = 0;

    [Header("Trigger Collider Activation")]
    [UnityEngine.Tooltip("If enabled, disables trigger colliders during initialization and enables them after a short delay. Helps prevent immediate trigger-based audio firing when the Traveler takes over.")]
    [SerializeField] private bool delayTriggerColliderEnable = true;
    [SerializeField] private float triggerColliderEnableDelaySeconds = 0.75f;
    [UnityEngine.Tooltip("If empty, all trigger Colliders on this Traveler and children will be managed.")]
    [SerializeField] private Collider[] triggerCollidersToManage;

    private GridManager cachedGridManager;

    [Header("Hint Visiting")]
    [UnityEngine.Tooltip("If enabled, each selected square will only be 'visited'/paused once per Traveler run. If the Traveler passes through a future hint on the way to another, that hint is consumed and won't be targeted later.")]
    [SerializeField] private bool consumeHintsOnFirstVisit = true;

    [UnityEngine.Tooltip("If enabled, the Traveler will NOT target squares that are currently correct (GridManager solved flag). This is intended for re-validation attempts so the ghost can beeline to unresolved squares.")]
    [SerializeField] private bool skipCurrentlySolvedHints = false;

    [UnityEngine.Tooltip("If enabled, the next hint target is chosen randomly from remaining hints (instead of nearest-first).")]
    [SerializeField] private bool randomizeNextHintTarget = false;

    [Header("Start Position (Optional)")]
    [UnityEngine.Tooltip("If enabled, the Traveler will avoid choosing a corner start that is adjacent to any hint square (Manhattan distance <= 1). This reduces cases where the first target is immediately next to the starting corner.")]
    [SerializeField] private bool avoidCornerStartAdjacentToFirstHint = false;

    private readonly HashSet<int> remainingHintNodes = new HashSet<int>();

    void Update()
    {
        UpdateValidationSpeedUpUI();
        UpdateFollowerValidationSpeedUp();
        if (!isInitialized || isPausing) return;

        if (!isResetting)
        {
            MoveToTarget();
        }
    }

    private void UpdateFollowerValidationSpeedUp()
    {
        if (follower == null) return;

        float multiplier = 1f;
        if (IsValidationSpeedUpAvailable && Input.GetKey(validationSpeedUpKey))
        {
            multiplier = validationSpeedUpMultiplier;
        }

        follower.SetSpeedMultiplier(multiplier);
    }

    // PlayMaker-friendly toggle.
    public void SetValidationSniffAllowed(bool allowed)
    {
        validationSniffAllowed = allowed;

        if (!validationSniffAllowed)
        {
            remainingSniffNodes.Clear();
            visitedSniffNodesThisRun.Clear();
            return;
        }

        RefreshSniffNodes();
    }

    public void InitTraveler()
    {
        // New run: invalidate any pending delayed work from prior runs.
        travelerRunToken++;

        isFollowerReady = false;
        isPatternComplete = false;

        // IMPORTANT: reset per-init start selection state.
        // Without this, a prior init's startGridPosition can persist and prevent corner re-randomization
        // when the Traveler re-enters from the hatch/attic.
        hasCustomStart = false;

        // New run: sniff targets should be visited at most once per run.
        remainingSniffNodes.Clear();
        visitedSniffNodesThisRun.Clear();

        // Between "round complete" and "next round start" we want the Traveler considered paused
        // (e.g., for footsteps logic driven externally).
        isPausing = true;

        bool canContinueFromCurrentTile = continueFromCurrentTileBetweenRounds && lastRoundCompleted && IsCurrentlyOnGrid();
        if (forceCornerStartNextInit)
        {
            canContinueFromCurrentTile = false;
            forceCornerStartNextInit = false;
        }

        startFromCurrentTileThisInit = canContinueFromCurrentTile;
        lastRoundCompleted = false;

        if (follower != null)
        {
            // Only reset to hatch when we intend to do a hatch-style spawn.
            if (!startFromCurrentTileThisInit)
            {
                follower.ResetToHatch(instant: true);
            }
        }

        if (cachedGridManager == null)
        {
            cachedGridManager = FindObjectOfType<GridManager>();
        }

        if (debugTrace && debugTraceTriggerColliders)
        {
            Collider[] cols = GetManagedTriggerColliders();
            Debug.Log($"[Traveler] InitTraveler: managingTriggerColliders={cols.Length} delayEnable={(delayTriggerColliderEnable ? 1 : 0)} delaySeconds={triggerColliderEnableDelaySeconds:0.###}", this);
        }

        DisableManagedTriggerColliders();
        //Debug.Log("Resetting FOLLOWER.");
        StartCoroutine(InitializeTraveler());
    }

    public IEnumerator InitializeTraveler()
    {
        int myToken = travelerRunToken;

        // Wait for GridManager to finish its setup
        GridManager gridManager = cachedGridManager != null ? cachedGridManager : FindObjectOfType<GridManager>();
        cachedGridManager = gridManager;

        while (gridManager == null || gridManager.GetSelectedSquares() == null || gridManager.GetSelectedSquares().Count == 0)
        {
            if (myToken != travelerRunToken) yield break;
            yield return null; // Wait until GridManager is ready
        }

        if (myToken != travelerRunToken) yield break;

        // Retrieve the selected squares for the current round
        selectedSquares = new List<int>(gridManager.GetSelectedSquares());
        targetPositions = selectedSquares.Select(index => new Vector2Int(index % gridSize.x, index / gridSize.x)).ToList();

        remainingHintNodes.Clear();
        ValidationSystem validationSystem = Object.FindFirstObjectByType<ValidationSystem>();
        if (selectedSquares != null)
        {
            for (int i = 0; i < selectedSquares.Count; i++)
            {
                int idx = selectedSquares[i];

                // Optional skip: if the system already knows this square is correct,
                // we don't need to visit it again on a later validation pass.
                // IMPORTANT: this is only safe to skip if the square is actually LOCKED (cannot change).
                // Otherwise, a player can remove a previously-correct relic and we'd miss the "now wrong" feedback.
                if (skipCurrentlySolvedHints && gridManager != null && gridManager.IsNodeSolved(idx))
                {
                    if (validationSystem != null && validationSystem.IsSquareLocked(idx))
                    {
                        continue;
                    }
                }

                remainingHintNodes.Add(idx);
            }
        }

        if (debugTrace && debugTraceSelections)
        {
            string squares = selectedSquares != null ? string.Join(",", selectedSquares) : "(null)";
            Debug.Log($"[Traveler] InitializeTraveler: selectedSquares=[{squares}] count={(selectedSquares != null ? selectedSquares.Count : 0)}", this);
        }

        if (targetPositions.Count > 0)
        {
            if (startFromCurrentTileThisInit)
            {
                startGridPosition = GetCurrentGridPositionSnapped();
                hasCustomStart = true;

                if (debugTrace && debugTraceSelections)
                {
                    Debug.Log($"[Traveler] InitializeTraveler: continuing from current tile startGridPosition={startGridPosition}", this);
                }
            }

            // Define custom valid start positions (corners only)
            // Step 1: Define preferred corner squares
            List<int> preferredStartPositions = new List<int> { 0, 4, 20, 24 };

            // Always allow corner starts, even if they overlap selected squares.
            // The predictability is more important than avoiding overlap.
            List<int> chosenStartPool = preferredStartPositions;

            // Optional: avoid corner starts that are adjacent to the closest hint.
            // This is opt-in to preserve legacy behavior + PlayMaker tuning.
            if (avoidCornerStartAdjacentToFirstHint && !hasCustomStart && !debugForceStartIndex && targetPositions != null && targetPositions.Count > 0)
            {
                List<int> filteredCorners = new List<int>();
                for (int i = 0; i < chosenStartPool.Count; i++)
                {
                    int cornerIndex = chosenStartPool[i];
                    Vector2Int cornerPos = new Vector2Int(cornerIndex % gridSize.x, cornerIndex / gridSize.x);

                    int nearestManhattan = int.MaxValue;
                    for (int j = 0; j < targetPositions.Count; j++)
                    {
                        Vector2Int hintPos = targetPositions[j];
                        int dist = Mathf.Abs(cornerPos.x - hintPos.x) + Mathf.Abs(cornerPos.y - hintPos.y);
                        if (dist < nearestManhattan) nearestManhattan = dist;
                        if (nearestManhattan <= 1) break;
                    }

                    if (nearestManhattan > 1)
                    {
                        filteredCorners.Add(cornerIndex);
                    }
                }

                if (filteredCorners.Count > 0)
                {
                    chosenStartPool = filteredCorners;
                }
            }

            // Step 4: Fallback if none of the preferred options are usable at all
            if (chosenStartPool.Count == 0)
            {
                chosenStartPool = Enumerable.Range(0, gridSize.x * gridSize.y)
                    .Where(index => index != (gridSize.x * gridSize.y) / 2) // Exclude center
                    .ToList();
            }

            // Step 5: Pick one randomly
            if (debugForceStartIndex)
            {
                startGridPosition = new Vector2Int(debugStartIndex % gridSize.x, debugStartIndex / gridSize.x);
                hasCustomStart = true;

                if (debugTrace && debugTraceSelections)
                {
                    Debug.Log($"[Traveler] DEBUG forcing startIndex={debugStartIndex} startGridPosition={startGridPosition}", this);
                }
            }
            else if (!hasCustomStart)
            {
                int randomStartIndex = chosenStartPool[Random.Range(0, chosenStartPool.Count)];
                startGridPosition = new Vector2Int(randomStartIndex % gridSize.x, randomStartIndex / gridSize.x);
                hasCustomStart = true;
            }

            int startNodeIndex = startGridPosition.y * gridSize.x + startGridPosition.x;
            bool startIsSelected = selectedSquares != null && selectedSquares.Contains(startNodeIndex);

            if (debugTrace && debugTraceSelections)
            {
                Debug.Log($"[Traveler] InitializeTraveler: startGridPosition={startGridPosition} startNodeIndex={startNodeIndex} startIsSelected={(startIsSelected ? 1 : 0)}", this);

                if (startIsSelected)
                {
                    Debug.LogWarning("[Traveler] Start tile is one of the selected squares. GenerateTravelPath() does not enqueue the start tile, so this selected square may never be 'visited'/paused unless handled elsewhere. This can present as a rare missing sound.", this);
                }
            }

            // Snap the traveler's position to the randomized starting point

            //Debug.Log($"Traveler Start Position: {startGridPosition}");

            if (!startFromCurrentTileThisInit)
            {
                // Notify the follower to move to the traveler's start position
                if (follower != null)
                {
                    follower.MoveToTravelerStart(startGridPosition);
                }

                // Wait for the Follower to reach the target position before proceeding
                while (!isFollowerReady)
                {
                    if (myToken != travelerRunToken) yield break;
                    yield return null;
                }
            }
            else
            {
                // No hatch/follower approach between rounds: start immediately from current tile.
                isFollowerReady = true;
            }

            // Sort target positions for traversal
            transform.position = new Vector3(startGridPosition.x, transform.position.y, startGridPosition.y);

            targetPositions = targetPositions.OrderBy(pos => Vector2.Distance(startGridPosition, pos)).ToList();

            if (!startFromCurrentTileThisInit)
            {
                float waitTime = 1f; // Adjust this value for desired wait duration
                yield return new WaitForSeconds(waitTime);
                if (myToken != travelerRunToken) yield break;
            }

            // Generate initial travel path to the first hint.
            GenerateTravelPathFromCurrent();

            // Seed the "current segment" as start -> first target (or start -> start if starting on a hint).
            currentSegmentFromTile = startGridPosition;
            currentSegmentToTile = currentTarget;

            // If our start tile is also a selected square, treat it as the first target.
            // This ensures the normal "reached target" flow runs (including pause logic) instead of silently skipping it.
            if (startIsSelected)
            {
                // GenerateTravelPath() sets currentTarget by dequeuing the first step.
                // Push that step back so we don't lose it.
                if (travelPath != null)
                {
                    Queue<Vector2Int> restored = new Queue<Vector2Int>();
                    restored.Enqueue(currentTarget);
                    foreach (var p in travelPath) restored.Enqueue(p);
                    travelPath = restored;
                }

                currentTarget = startGridPosition;
                lastMoveDirection = Vector2Int.zero;

                currentSegmentFromTile = startGridPosition;
                currentSegmentToTile = startGridPosition;
            }
            else
            {
                lastMoveDirection = currentTarget - startGridPosition;

                currentSegmentFromTile = startGridPosition;
                currentSegmentToTile = currentTarget;
            }

            if (debugTrace && debugTraceMovement)
            {
                Debug.Log($"[Traveler] TravelPath generated. steps={(travelPath != null ? travelPath.Count : 0)} currentTarget={currentTarget} next={(travelPath != null && travelPath.Count > 0 ? travelPath.Peek().ToString() : "(none)")}", this);
            }

            // Start moving only after triggers are re-enabled.
            // This keeps PlayMaker's OnTriggerEnter reliable (no "moved off start tile while colliders disabled").
            isInitialized = false;
            isPatternComplete = false;

            if (delayTriggerColliderEnable)
            {
                StartCoroutine(EnableManagedTriggerCollidersAndStartAfterDelay(triggerColliderEnableDelaySeconds));
            }
            else
            {
                EnableManagedTriggerColliders();
                isInitialized = true;
                isPatternComplete = false;
                isPausing = false;
            }
        }
        else
        {
            Debug.LogError("No valid target positions found after initialization!");
        }
    }

    private void DisableManagedTriggerColliders()
    {
        if (!delayTriggerColliderEnable) return;

        Collider[] colliders = GetManagedTriggerColliders();
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = false;
        }

        if (debugTrace && debugTraceTriggerColliders)
        {
            Debug.Log($"[Traveler] Disabled trigger colliders count={colliders.Length}", this);
        }
    }

    private void EnableManagedTriggerColliders()
    {
        Collider[] colliders = GetManagedTriggerColliders();
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = true;
        }

        if (debugTrace && debugTraceTriggerColliders)
        {
            Debug.Log($"[Traveler] Enabled trigger colliders count={colliders.Length}", this);
        }

        TravelerTriggerTrace trace = GetComponent<TravelerTriggerTrace>();
        if (trace != null)
        {
            trace.ProbeOverlapsNow("AFTER_ENABLE");
        }
    }

    private IEnumerator EnableManagedTriggerCollidersAndStartAfterDelay(float seconds)
    {
        int myToken = travelerRunToken;
        if (seconds > 0f)
        {
            yield return new WaitForSeconds(seconds);
        }

        if (myToken != travelerRunToken) yield break;

        EnableManagedTriggerColliders();
        isInitialized = true;
        isPatternComplete = false;
        isPausing = false;

        if (debugTrace)
        {
            Debug.Log($"[Traveler] Movement start after trigger enable. currentTarget={currentTarget} travelPathRemaining={(travelPath != null ? travelPath.Count : 0)}", this);
        }
    }

    private IEnumerator EnableManagedTriggerCollidersAfterDelay(float seconds)
    {
        int myToken = travelerRunToken;
        if (seconds > 0f)
        {
            yield return new WaitForSeconds(seconds);
        }

        if (myToken != travelerRunToken) yield break;

        EnableManagedTriggerColliders();
    }

    private Collider[] GetManagedTriggerColliders()
    {
        if (triggerCollidersToManage != null && triggerCollidersToManage.Length > 0)
        {
            return triggerCollidersToManage;
        }

        // Auto-manage only trigger colliders (safe default).
        return GetComponentsInChildren<Collider>(true).Where(c => c != null && c.isTrigger).ToArray();
    }


    private void GenerateTravelPath()
    {
        // Legacy entry point. Keep for older call sites.
        GenerateTravelPathFromCurrent();
    }

    private void GenerateTravelPathFromCurrent()
    {
        Vector2Int currentGridPos = GetCurrentGridPositionSnapped();
        GenerateTravelPathToNextHintFrom(currentGridPos);
    }

    private static int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private void LogPatternCompleteDebug(string source)
    {
        if (!debugTrace) return;

        int selectedCount = selectedSquares != null ? selectedSquares.Count : 0;
        Debug.Log(
            $"[Traveler] PatternComplete ({source}): current={GetCurrentGridPositionSnapped()} currentTarget={currentTarget} " +
            $"consumeHints={(consumeHintsOnFirstVisit ? 1 : 0)} remainingHints={remainingHintNodes.Count}/{selectedCount} " +
            $"sniffActive={(IsValidationSniffActive ? 1 : 0)} remainingSniff={remainingSniffNodes.Count} visitedSniff={visitedSniffNodesThisRun.Count} " +
            $"startFromCurrentThisInit={(startFromCurrentTileThisInit ? 1 : 0)}",
            this
        );
    }

    private void RefreshSniffNodes()
    {
        remainingSniffNodes.Clear();
        if (!IsValidationSniffActive) return;

        GridManager gridManager = cachedGridManager != null ? cachedGridManager : FindObjectOfType<GridManager>();
        cachedGridManager = gridManager;
        if (gridManager == null) return;

        Dictionary<int, int> selected = gridManager.GetSelectedSquaresWithTypes();
        if (selected == null) return;

        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        if (objectPlacer == null) return;

        int total = gridSize.x * gridSize.y;
        for (int i = 0; i < total; i++)
        {
            // We only sniff "wrong placements" (placed on squares that are NOT selected this round).
            if (selected.ContainsKey(i)) continue;

            // Never sniff squares that were part of ANY previous round.
            // Those are historical round targets (likely locked relic placements) and should not be treated
            // as stray/wrong placements in later rounds.
            if (gridManager.WasSquareEverSelected(i)) continue;

            // Don't sniff squares that are already solved/locked from previous rounds.
            // Those are correct historical placements and should not be treated as wrong placements.
            if (gridManager.IsNodeSolved(i)) continue;

            // Don't re-add sniff targets we've already visited this run.
            if (visitedSniffNodesThisRun.Contains(i)) continue;

            if (objectPlacer.TryGetPlacedObjectAtIndex(i, out GameObject placedObject, out _)
                && placedObject != null)
            {
                remainingSniffNodes.Add(i);
            }
        }

        if (debugTrace)
        {
            Debug.Log($"[Traveler] RefreshSniffNodes: enabled={(enableValidationSniffInvalidPlacements ? 1 : 0)} active={(IsValidationSniffActive ? 1 : 0)} gateRequired={(validationSniffRequiresExternalGate ? 1 : 0)} gateAllowed={(validationSniffAllowed ? 1 : 0)} sniffCount={remainingSniffNodes.Count}", this);
        }
    }

    private void GenerateTravelPathToTargetIndexFrom(Vector2Int start, int chosenIndex)
    {
        travelPath = new Queue<Vector2Int>();

        Vector2Int target = new Vector2Int(chosenIndex % gridSize.x, chosenIndex / gridSize.x);
        Vector2Int currentPosition = start;

        // Add horizontal movement
        while (currentPosition.x != target.x)
        {
            currentPosition.x += (currentPosition.x < target.x) ? 1 : -1;
            travelPath.Enqueue(new Vector2Int(currentPosition.x, currentPosition.y));
        }

        // Add vertical movement
        while (currentPosition.y != target.y)
        {
            currentPosition.y += (currentPosition.y < target.y) ? 1 : -1;
            travelPath.Enqueue(new Vector2Int(currentPosition.x, currentPosition.y));
        }

        // Reset current target to the first position in the path.
        if (travelPath.Count > 0)
        {
            currentTarget = travelPath.Dequeue();
        }
        else
        {
            // Already at target.
            currentTarget = target;
        }
    }

    private bool TryContinueWithSniffTargets(Vector2Int start)
    {
        if (!IsValidationSniffActive) return false;

        // Only sniff after all hint nodes have been consumed/visited.
        if (consumeHintsOnFirstVisit && remainingHintNodes.Count > 0) return false;

        RefreshSniffNodes();

        int startIndex = start.y * gridSize.x + start.x;
        remainingSniffNodes.Remove(startIndex); // if we're already standing on one
        if (remainingSniffNodes.Count == 0) return false;

        // Nearest-first.
        int chosenIndex = remainingSniffNodes
            .OrderBy(i => ManhattanDistance(start, new Vector2Int(i % gridSize.x, i / gridSize.x)))
            .First();

        remainingSniffNodes.Remove(chosenIndex);
        GenerateTravelPathToTargetIndexFrom(start, chosenIndex);

        Vector2Int newDirection = currentTarget - start;
        if (newDirection != Vector2Int.zero)
        {
            lastMoveDirection = newDirection;
        }

        currentSegmentFromTile = start;
        currentSegmentToTile = currentTarget;

        if (debugTrace)
        {
            Debug.Log($"[Traveler] Sniff routing to index={chosenIndex} from={start} currentTarget={currentTarget} remainingSniffNodes={remainingSniffNodes.Count} travelPathRemaining={(travelPath != null ? travelPath.Count : 0)}", this);
        }

        return true;
    }

    private void GenerateTravelPathToNextHintFrom(Vector2Int start)
    {
        travelPath = new Queue<Vector2Int>();

        if (selectedSquares == null || selectedSquares.Count == 0)
        {
            // No hints at all.
            travelPath.Clear();
            currentTarget = start;
            return;
        }

        // If we're consuming hints, only route to remaining ones.
        List<int> candidates = consumeHintsOnFirstVisit
            ? remainingHintNodes.ToList()
            : new List<int>(selectedSquares);

        if (candidates.Count == 0)
        {
            travelPath.Clear();
            currentTarget = start;
            return;
        }

        int chosenIndex;
        if (randomizeNextHintTarget)
        {
            chosenIndex = candidates[Random.Range(0, candidates.Count)];
        }
        else
        {
            // Nearest-first.
            chosenIndex = candidates
                .OrderBy(i => ManhattanDistance(start, new Vector2Int(i % gridSize.x, i / gridSize.x)))
                .First();
        }

        if (debugTrace)
        {
            Debug.Log(
                $"[Traveler] Hint routing to index={chosenIndex} from={start} " +
                $"consumeHints={(consumeHintsOnFirstVisit ? 1 : 0)} randomize={(randomizeNextHintTarget ? 1 : 0)} " +
                $"candidates={candidates.Count} remainingHints={remainingHintNodes.Count}",
                this
            );
        }

        GenerateTravelPathToTargetIndexFrom(start, chosenIndex);
    }

    public void MoveToTarget()
    {
        Vector3 targetWorldPosition = new Vector3(currentTarget.x, transform.position.y, currentTarget.y);
        Vector3 currentPosition = transform.position;

        // Calculate the direction of movement
        Vector3 movementDirection = targetWorldPosition - currentPosition;

        // Only rotate the visual if movement is occurring
        if (movementDirection.magnitude > 0.05f)
        {
            // Rotate the visual representation to face the direction of movement
            if (travelerVisual != null)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movementDirection, Vector3.up);
                travelerVisual.rotation = Quaternion.Slerp(
                    travelerVisual.rotation,
                    targetRotation,
                    rotationSpeed * Time.deltaTime
                );
            }
        }

        float effectiveSpeed = speed;
        if (IsValidationSpeedUpAvailable && Input.GetKey(validationSpeedUpKey))
        {
            effectiveSpeed *= validationSpeedUpMultiplier;
        }

        // Move towards the target position
        transform.position = Vector3.MoveTowards(currentPosition, targetWorldPosition, effectiveSpeed * Time.deltaTime);

        // Check if we reached the current target
        if (Vector3.Distance(transform.position, targetWorldPosition) < 0.05f)
        {
            int nodeIndex = currentTarget.y * gridSize.x + currentTarget.x;

            if (IsValidationSniffActive)
            {
                remainingSniffNodes.Remove(nodeIndex);
                visitedSniffNodesThisRun.Add(nodeIndex);
            }

            GridManager gridManager = cachedGridManager != null ? cachedGridManager : FindObjectOfType<GridManager>();
            cachedGridManager = gridManager;

            if (debugTrace && debugTraceMovement)
            {
                int type = -1;
                bool hasType = false;
                bool solved = false;
                if (gridManager != null)
                {
                    var dict = gridManager.GetSelectedSquaresWithTypes();
                    if (dict != null && dict.ContainsKey(nodeIndex))
                    {
                        hasType = true;
                        type = dict[nodeIndex];
                        solved = gridManager.IsNodeSolved(nodeIndex);
                    }
                }

                Debug.Log($"[Traveler] Reached nodeIndex={nodeIndex} grid={currentTarget} isSelected={(selectedSquares != null && selectedSquares.Contains(nodeIndex) ? 1 : 0)} hasType={(hasType ? 1 : 0)} type={type} solved={(solved ? 1 : 0)} travelPathRemaining={(travelPath != null ? travelPath.Count : 0)}", this);
            }

            if (gridManager != null)
            {
                if (gridManager.GetSelectedSquaresWithTypes().ContainsKey(nodeIndex))
                {
                    if (!gridManager.IsNodeSolved(nodeIndex))
                    {
                        Debug.Log($"Performing action on unsolved node {nodeIndex}");
                    }
                    else
                    {
                        Debug.Log($"Skipping solved node {nodeIndex}");
                    }
                }
            }

            bool shouldPauseForHint = false;
            if (consumeHintsOnFirstVisit)
            {
                shouldPauseForHint = remainingHintNodes.Contains(nodeIndex);
            }
            else
            {
                shouldPauseForHint = selectedSquares.Contains(nodeIndex);
            }

            // Pause if it's a selected square (hint)
            if (shouldPauseForHint)
            {
                if (consumeHintsOnFirstVisit)
                {
                    remainingHintNodes.Remove(nodeIndex);
                }

                if (debugTrace && debugTraceMovement)
                {
                    Debug.Log($"[Traveler] Pausing on selected square nodeIndex={nodeIndex} pauseDuration={pauseDuration:0.###}", this);
                }
                StartCoroutine(PauseBeforeNextTarget());
                return;
            }

            // If we're consuming hints, we don't want to follow a precomputed full path.
            // Always move along the current segment, and if it runs out, pick the next remaining hint.
            if (travelPath.Count > 0)
            {
                Vector2Int nextTarget = travelPath.Peek();

                // Use logical grid targets (not rounded transform position) to avoid false turn detection.
                Vector2Int nextDirection = nextTarget - currentTarget;

                // Check if this is a turn (direction change)
                if (lastMoveDirection != Vector2Int.zero && nextDirection != lastMoveDirection)
                {
                    Debug.Log($"Turn detected! Previous: {lastMoveDirection}, Next: {nextDirection}");
                    StartCoroutine(PauseForTurn(nextDirection));
                    return; // Don't move to next target yet, wait for turn pause to complete
                }

                // No turn, move to next target normally
                Vector2Int fromTile = currentTarget;
                Vector2Int toTile = travelPath.Dequeue();
                currentTarget = toTile;
                lastMoveDirection = nextDirection;

                currentSegmentFromTile = fromTile;
                currentSegmentToTile = toTile;
            }
            else
            {
                if (consumeHintsOnFirstVisit && remainingHintNodes.Count > 0)
                {
                    GenerateTravelPathFromCurrent();
                    // Do not zero lastMoveDirection here; we want to accurately detect turns between segments.
                }
                else
                {
                    Vector2Int fromTile = GetCurrentGridPositionSnapped();
                    if (TryContinueWithSniffTargets(fromTile))
                    {
                        // Continue moving; pattern completes only after sniff targets are processed.
                        return;
                    }

                    LogPatternCompleteDebug("MoveToTarget");
                    Debug.Log("Traveler has reached all targets.");
                    isInitialized = false;
                    isPatternComplete = true;
                    isPausing = true;
                    lastRoundCompleted = true;

                    if (debugTrace && debugTraceMovement)
                    {
                        Debug.Log("[Traveler] Pattern complete.", this);
                    }
                }
            }
        }
    }

    // PlayMaker-friendly toggle.
    public void SetValidationSpeedUpAllowed(bool allowed)
    {
        validationSpeedUpAllowed = allowed;
        UpdateValidationSpeedUpUI();
    }

    private void UpdateValidationSpeedUpUI()
    {
        bool available = IsValidationSpeedUpAvailable;
        bool held = available && Input.GetKey(validationSpeedUpKey);

        UpdateValidationCheckingStatusText(available);

        if (validationSpeedUpHeldUI != null)
        {
            validationSpeedUpHeldUI.SetActive(held);
        }

        if (validationSpeedUpAvailableUI != null)
        {
            // If a held UI exists, hide the normal one while held.
            validationSpeedUpAvailableUI.SetActive(available && (!held || validationSpeedUpHeldUI == null));
        }

        // Optional: swap the icon sprite while held.
        // This supports a single UI object with TMP + Image children, where we just depress the button visually.
        if (!triedResolveValidationSpeedUpIcon)
        {
            triedResolveValidationSpeedUpIcon = true;

            if (validationSpeedUpIconImage == null && validationSpeedUpAvailableUI != null)
            {
                validationSpeedUpIconImage = validationSpeedUpAvailableUI.GetComponentInChildren<Image>(true);
            }

            if (validationSpeedUpIconImage != null && !capturedValidationSpeedUpDefaultSprite)
            {
                capturedValidationSpeedUpDefaultSprite = true;
                validationSpeedUpDefaultSprite = validationSpeedUpIconImage.sprite;
            }
        }

        if (validationSpeedUpIconImage != null && validationSpeedUpHeldSprite != null)
        {
            if (!available)
            {
                // Ensure we don't leave the held sprite stuck if the UI is disabled while held.
                if (capturedValidationSpeedUpDefaultSprite)
                {
                    validationSpeedUpIconImage.sprite = validationSpeedUpDefaultSprite;
                }
            }
            else
            {
                validationSpeedUpIconImage.sprite = held ? validationSpeedUpHeldSprite : validationSpeedUpDefaultSprite;
            }
        }
    }

    private void UpdateValidationCheckingStatusText(bool available)
    {
        if (validationCheckingStatusText == null || validationCheckingStatusText.tmpText == null) return;

        if (!validationCheckingStatusInitialized)
        {
            validationCheckingStatusInitialized = true;
            lastValidationCheckingStatusVisible = available;
            validationCheckingStatusText.SetAlphaInstant(available ? 1f : 0f);
            return;
        }

        if (available == lastValidationCheckingStatusVisible) return;
        lastValidationCheckingStatusVisible = available;

        validationCheckingStatusText.FadeTo(available ? 1f : 0f, available ? validationCheckingStatusFadeInSeconds : validationCheckingStatusFadeOutSeconds);
    }

    private IEnumerator PauseBeforeNextTarget()
    {
        int myToken = travelerRunToken;
        isPausing = true;
        yield return new WaitForSeconds(pauseDuration);

        if (myToken != travelerRunToken) yield break;

        // IMPORTANT:
        // Keep isPausing=true until we've chosen the next target and (optionally) handled a turn pause.
        // Otherwise Update() can run in-between, see the Traveler "arrive" again, and cause off-by-one
        // behavior where the turn pause/sound happens one tile late.

        // After pausing on a hint, pick the next remaining hint and regenerate a segment.
        if (consumeHintsOnFirstVisit)
        {
            if (remainingHintNodes.Count > 0)
            {
                Vector2Int fromTile = GetCurrentGridPositionSnapped();
                GenerateTravelPathFromCurrent();

                // If leaving this hint tile changes direction vs the direction we arrived with,
                // play the turn sound/pause HERE (on the turning square), not one step later.
                Vector2Int newDirection = currentTarget - fromTile;
                if (lastMoveDirection != Vector2Int.zero && newDirection != Vector2Int.zero && newDirection != lastMoveDirection)
                {
                    EmitTurnSfx(newDirection);
                    yield return new WaitForSeconds(pauseTurnWaitTime);
                    if (myToken != travelerRunToken) yield break;
                }

                // Now the next move direction becomes our "last" direction for turn detection.
                if (newDirection != Vector2Int.zero)
                {
                    lastMoveDirection = newDirection;
                }
            }
            else
            {
                Vector2Int fromTile = GetCurrentGridPositionSnapped();
                if (TryContinueWithSniffTargets(fromTile))
                {
                    isPausing = false;
                    yield break;
                }

                LogPatternCompleteDebug("PauseBeforeNextTarget-consumeHints");
                Debug.Log("Traveler has reached all targets.");
                isInitialized = false;
                isPatternComplete = true;
                isPausing = true;
                lastRoundCompleted = true;
            }

            isPausing = false;
            yield break;
        }

        // Legacy behavior: move to next queued position.
        if (travelPath.Count > 0)
        {
            currentTarget = travelPath.Dequeue();
        }
        else
        {
            LogPatternCompleteDebug("PauseBeforeNextTarget-legacy");
            Debug.Log("Traveler has reached all targets.");
            isInitialized = false;
            isPatternComplete = true;
            isPausing = true;
            lastRoundCompleted = true;
        }

        isPausing = false;
    }

    public void PauseTraveler()
    {
        isPausing = true; // Stop the traveler indefinitely
        //Debug.Log("Pausing Traveler");
    }

    public void UnpauseTraveler()
    {
        if (isPausing)
        {
            isPausing = false;
            Debug.Log("Traveler unpaused and resuming movement.");
        }
    }

    public void ResetTraveler()
    {
        if (!isInitialized)
        {
            Debug.LogWarning("Traveler not initialized. Cannot reset.");
            return;
        }

        isResetting = true;

        // Reset position and regenerate the path
        transform.position = new Vector3(startGridPosition.x, transform.position.y, startGridPosition.y);
        GenerateTravelPath();

        isResetting = false;
        isPatternComplete = false; // Reset the pattern completion state
        isPausing = false;
        lastRoundCompleted = false;
    }

    private bool IsCurrentlyOnGrid()
    {
        // Consider the Traveler "on the grid" if its XZ position is close to any valid tile.
        float x = transform.position.x;
        float z = transform.position.z;

        float minX = 0f - onGridTolerance;
        float maxX = (gridSize.x - 1) + onGridTolerance;
        float minZ = 0f - onGridTolerance;
        float maxZ = (gridSize.y - 1) + onGridTolerance;

        return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    private Vector2Int GetCurrentGridPositionSnapped()
    {
        int gx = Mathf.Clamp(Mathf.RoundToInt(transform.position.x), 0, gridSize.x - 1);
        int gy = Mathf.Clamp(Mathf.RoundToInt(transform.position.z), 0, gridSize.y - 1);
        return new Vector2Int(gx, gy);
    }

    public Vector2Int GetStartGridPosition()
    {
        return startGridPosition;
    }

    public void FollowerCompleted()
    {
        StartCoroutine(FollowerCompletedDelayed());
    }

    private IEnumerator FollowerCompletedDelayed()
    {
        int myToken = travelerRunToken;
        yield return new WaitForSeconds(followerDetatchDelay);

        if (myToken != travelerRunToken) yield break;
        isFollowerReady = true;
        Debug.Log("Follower completed its movement. Traveler can now start.");
    }

    public void RereadyFollower()
    {
        if (isPatternComplete) // 🚀 Only reset if the Traveler has fully completed its movement
        {
            Debug.Log("Resetting Follower Readiness.");
            isFollowerReady = false;
        }
    }

    public void InitTravelerTutorialPath()
    {
        StartCoroutine(InitTutorialRoutine());
    }

    private IEnumerator PauseForTurn(Vector2Int newDirection)
    {
        int myToken = travelerRunToken;
        isPausing = true;

        EmitTurnSfx(newDirection);

        yield return new WaitForSeconds(pauseTurnWaitTime);

    if (myToken != travelerRunToken) yield break;

        if (travelPath.Count > 0)
        {
            Vector2Int fromTile = currentTarget;
            Vector2Int toTile = travelPath.Dequeue();
            currentTarget = toTile;
            lastMoveDirection = newDirection;

            currentSegmentFromTile = fromTile;
            currentSegmentToTile = toTile;
        }

        isPausing = false;
    }

    private void EmitTurnSfx(Vector2Int newDirection)
    {
        if (!enableTurnSfx) return;

        // Determine whether the next step (after the turn) moves closer to or farther from the player.
        // Current design assumes the player is on the center tile (e.g., index 12 for a 5x5 grid).
        // This gives a deterministic "toward" vs "away" signal without needing world-rotation heuristics.
        TurnRelativeToPlayer relative = TurnRelativeToPlayer.Unknown;
        if (newDirection != Vector2Int.zero)
        {
            Vector2Int fromTile = currentTarget;
            Vector2Int toTile = currentTarget + newDirection;
            relative = GetRelativeToCenterTile(fromTile, toTile);
        }

        lastTurnRelativeToPlayer = relative;

        // Compute angle for debugging/branching if needed.
        int turnAngle = 0;
        if (lastMoveDirection != Vector2Int.zero && newDirection != Vector2Int.zero)
        {
            if (newDirection == -lastMoveDirection) turnAngle = 180;
            else if (newDirection != lastMoveDirection) turnAngle = 90;
        }

        lastTurnAngle = turnAngle;

        if (Time.time < lastTurnSoundTime + minSecondsBetweenTurnSounds)
        {
            return;
        }
        lastTurnSoundTime = Time.time;

        if (broadcastTurnEvent && !string.IsNullOrEmpty(turnEventName))
        {
            PlayMakerFSM.BroadcastEvent(turnEventName);
            Debug.Log($"[Traveler] Turn event broadcast '{turnEventName}' angle={turnAngle}");
        }

        if (playTurnClipDirectly)
        {
            if (turnAudioSource == null) turnAudioSource = GetComponent<AudioSource>();
            if (turnAudioSource == null) return;

            AudioClip[] pool = null;
            if (relative == TurnRelativeToPlayer.Toward && turnTowardPlayerClips != null && turnTowardPlayerClips.Length > 0)
            {
                pool = turnTowardPlayerClips;
            }
            else if (relative == TurnRelativeToPlayer.Away && turnAwayFromPlayerClips != null && turnAwayFromPlayerClips.Length > 0)
            {
                pool = turnAwayFromPlayerClips;
            }
            else if (relative == TurnRelativeToPlayer.Lateral && turnLateralClips != null && turnLateralClips.Length > 0)
            {
                pool = turnLateralClips;
            }
            else
            {
                pool = turnClips;
            }

            if (pool == null || pool.Length == 0) return;

            AudioClip clip = pool[Random.Range(0, pool.Length)];
            if (clip != null)
            {
                turnAudioSource.PlayOneShot(clip, turnClipVolume);
            }
        }
    }

    private IEnumerator InitTutorialRoutine()
    {
        int myToken = travelerRunToken;
        isInitialized = false;
        isPatternComplete = false;
        isPausing = false;
        isResetting = false;

        gridSize = new Vector2Int(5, 5);

        List<int> tutorialPathIndices = new List<int> { 14, 24, 21, 1 };
        selectedSquares = new List<int>(tutorialPathIndices);

        targetPositions = tutorialPathIndices
            .Select(index => new Vector2Int(index % gridSize.x, index / gridSize.x))
            .ToList();

        startGridPosition = targetPositions[0];

        if (follower != null)
        {
            isFollowerReady = false;
            follower.MoveToTravelerStart(startGridPosition);

            // Wait until the follower sets itself ready
            while (!isFollowerReady)
            {
                if (myToken != travelerRunToken) yield break;
                yield return null;
            }

            yield return new WaitForSeconds(1f);
            if (myToken != travelerRunToken) yield break;
        }

        transform.position = new Vector3(startGridPosition.x, transform.position.y, startGridPosition.y);
        travelPath = new Queue<Vector2Int>();
        Vector2Int currentPosition = startGridPosition;

        for (int i = 1; i < targetPositions.Count; i++)
        {
            Vector2Int target = targetPositions[i];

            while (currentPosition.x != target.x)
            {
                currentPosition.x += (currentPosition.x < target.x) ? 1 : -1;
                travelPath.Enqueue(new Vector2Int(currentPosition.x, currentPosition.y));
            }

            while (currentPosition.y != target.y)
            {
                currentPosition.y += (currentPosition.y < target.y) ? 1 : -1;
                travelPath.Enqueue(new Vector2Int(currentPosition.x, currentPosition.y));
            }
        }

        if (travelPath.Count > 0)
        {
            currentTarget = travelPath.Dequeue();
        }

        isInitialized = true;
        Debug.Log($"Traveler tutorial path initialized. Starting at {startGridPosition}, with {travelPath.Count} steps.");
    }

    public void ForceNewRandomStart()
    {
        hasCustomStart = false;
    }
}
