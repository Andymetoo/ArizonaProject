using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Collections;
using UnityEngine.SceneManagement; // ✅ Required for SceneManager

public class GridManager : MonoBehaviour
{
    private int gridSize = 5;
    private List<int> selectedSquares;
    private int centralSquare;
    public GameObject squarePrefab; // Assign in the Inspector
    public float squareSize = 1f; // Set desired square size in world units
    [SerializeField] public int numberOfSelectedSquares = 3;
    public List<int> GetSelectedSquares() => selectedSquares;
    private Dictionary<int, int> selectedSquaresWithTypes;
    [SerializeField] public int maxTypes = 1;
    private Dictionary<int, Dictionary<int, int>> allRoundSelections = new Dictionary<int, Dictionary<int, int>>();
    private HashSet<int> solvedNodes = new HashSet<int>(); // Keeps track of solved node indices
    private bool centerIsInitialized = false;
    Color lightYellow = new Color(1f, 1f, 0.8f, .15f); // Light Yellow 
    Color lightRed = new Color(1f, 0.8f, 0.8f, .15f); // Ligh Red

    [Header("Selectable Squares Restriction")]
    [SerializeField] private bool restrictSelectableSquares = false;
    [SerializeField, Range(1, 24)] private int allowedSelectableSquaresCount = 24;
    [SerializeField] private bool randomizeAllowedSquaresEachRound = false;

    [Header("Selectable Squares Restriction (Profile, Optional)")]
    [SerializeField] private bool useSelectableSquaresRestrictionProfile = false;
    [SerializeField] private SelectableSquaresRestrictionProfile selectableSquaresRestrictionProfile;

    [Header("Custom Night (Optional)")]
    [Tooltip("If enabled, CustomNight_ApplyAndGenerateRound can drive numberOfSelectedSquares/maxTypes per round from a CustomNightScenario. Legacy FSM flow is unaffected unless you call the CustomNight methods.")]
    [SerializeField] private bool useCustomNightScenario = false;
    [SerializeField] private CustomNightScenario customNightScenario;

    [Header("Custom Night Timeline (Optional, JSON)")]
    [Tooltip("If enabled, CustomNightTimeline_ApplyAndGenerateRound can drive numberOfSelectedSquares/maxTypes per round from the saved JSON timeline config.")]
    [SerializeField] private bool useCustomNightTimeline = false;

    [Tooltip("Optional: PlayerPrefs key used by menus to indicate Custom Night mode. If set and equals 1, CustomNightTimeline_LoadSaved will enable timeline mode when a config exists.")]
    [SerializeField] private string customNightPrefKey = "IsCustomNight";

    [SerializeField] private bool customNightTimelineLoaded = false;
    [SerializeField] private int customNightTimelineTotalRounds = 0;
    [SerializeField] private bool customNightTimelineWinState = false;
    [SerializeField] private string customNightTimelineLastStatus = string.Empty;

    [SerializeField] private int customNightTimelineCurrentRound = 0;

    [Header("Endless (Optional)")]
    [Tooltip("If enabled (or if the PlayerPrefs key below is set to 1), Endless helper methods can be used by FSMs without affecting the legacy flow.")]
    [SerializeField] private bool useEndlessMode = false;

    [Tooltip("Optional: PlayerPrefs key used by menus/FSMs to indicate Endless mode.")]
    [SerializeField] private string endlessModePrefKey = "IsEndlessMode";

    [Header("Endless (Debug)")]
    [Tooltip("If true, Endless generation will always include square index 0 in the selected set each round (when possible). Useful for quickly testing repeat-square behavior.")]
    [SerializeField] private bool endlessDebugForceIncludeSquareIndex0EveryRound = false;

    public bool EndlessDebugForceIncludeSquareIndex0EveryRound
    {
        get => endlessDebugForceIncludeSquareIndex0EveryRound;
        set => endlessDebugForceIncludeSquareIndex0EveryRound = value;
    }

    // PlayMaker/UI-friendly toggle
    public void Endless_Debug_SetForceIncludeSquareIndex0EveryRound(bool enabled)
    {
        endlessDebugForceIncludeSquareIndex0EveryRound = enabled;
    }

    [Header("Custom Night Timeline (Read Only)")]
    [SerializeField] private bool customNightTimelinePlacementStageInterruptions = true;
    [SerializeField] private float customNightTimelineForgivenessLevel = 0f;
    [SerializeField] private float customNightTimelineAtticMin = 0f;
    [SerializeField] private float customNightTimelineAtticMax = 0f;
    [SerializeField] private bool customNightTimelineNoSelectableSquaresRemaining = false;

    // Runtime-only (loaded from JSON)
    private CustomNightTimelineConfig customNightTimelineConfig;

    [Header("Custom Night (Read Only)")]
    [SerializeField] private bool customNightWinState = false;
    [SerializeField] private bool customNightLastScenarioValid = false;
    [SerializeField] private int customNightLastAppliedSelectableSquares = 0;
    [SerializeField] private int customNightLastAppliedMaxTypes = 0;
    [SerializeField] private int customNightLastGeneratedSquares = 0;
    [SerializeField] private string customNightLastStatus = string.Empty;

    // PlayMaker-friendly: public properties so FSMs can use Set Property to assign a ScriptableObject asset.
    public bool UseSelectableSquaresRestrictionProfile
    {
        get => useSelectableSquaresRestrictionProfile;
        set => useSelectableSquaresRestrictionProfile = value;
    }

    public SelectableSquaresRestrictionProfile SelectableSquaresRestrictionProfile
    {
        get => selectableSquaresRestrictionProfile;
        set => selectableSquaresRestrictionProfile = value;
    }

    // --- Custom Night (PlayMaker-friendly) ---
    // Use Set Property to assign CustomNightScenario + enable UseCustomNightScenario,
    // then Call Method -> CustomNight_ApplyAndGenerateRound(int currentRound).
    public bool UseCustomNightScenario
    {
        get => useCustomNightScenario;
        set => useCustomNightScenario = value;
    }

    public CustomNightScenario CustomNightScenario
    {
        get => customNightScenario;
        set => customNightScenario = value;
    }

    public bool CustomNightWinState => customNightWinState;
    public bool CustomNightLastScenarioValid => customNightLastScenarioValid;
    public int CustomNightLastAppliedSelectableSquares => customNightLastAppliedSelectableSquares;
    public int CustomNightLastAppliedMaxTypes => customNightLastAppliedMaxTypes;
    public int CustomNightLastGeneratedSquares => customNightLastGeneratedSquares;
    public string CustomNightLastStatus => customNightLastStatus;

    // --- Custom Night Timeline (PlayMaker-friendly) ---
    public bool UseCustomNightTimeline
    {
        get => useCustomNightTimeline;
        set => useCustomNightTimeline = value;
    }

    public bool CustomNightTimelineLoaded => customNightTimelineLoaded;
    public int CustomNightTimelineTotalRounds => customNightTimelineTotalRounds;
    public int CustomNightTimelineMaxRounds => customNightTimelineTotalRounds;
    public bool CustomNightTimelineWinState => customNightTimelineWinState;
    public string CustomNightTimelineLastStatus => customNightTimelineLastStatus;
    public int CustomNightTimelineCurrentRound => customNightTimelineCurrentRound;

    public bool CustomNightTimelinePlacementStageInterruptions => customNightTimelinePlacementStageInterruptions;
    public float CustomNightTimelineForgivenessLevel => customNightTimelineForgivenessLevel;
    public float CustomNightTimelineAtticMin => customNightTimelineAtticMin;
    public float CustomNightTimelineAtticMax => customNightTimelineAtticMax;
    public bool CustomNightTimelineNoSelectableSquaresRemaining => customNightTimelineNoSelectableSquaresRemaining;

    // --- Endless (PlayMaker-friendly) ---
    public bool UseEndlessMode
    {
        get => useEndlessMode;
        set => useEndlessMode = value;
    }

    public bool IsEndlessModeActive
    {
        get
        {
            // Safety: Custom Night and Endless are mutually exclusive.
            // If Custom Night is active, never treat Endless as active (prevents SubmitRound() routing
            // into Endless behavior which intentionally does not lock items).
            if (!string.IsNullOrWhiteSpace(customNightPrefKey) && PlayerPrefs.GetInt(customNightPrefKey, 0) == 1)
            {
                return false;
            }

            // If the PlayerPrefs key exists, treat it as the source of truth.
            // This prevents Endless behavior from "leaking" into normal modes if useEndlessMode was
            // accidentally left enabled in a scene/prefab.
            if (!string.IsNullOrWhiteSpace(endlessModePrefKey) && PlayerPrefs.HasKey(endlessModePrefKey))
            {
                return PlayerPrefs.GetInt(endlessModePrefKey, 0) == 1;
            }

            // Fallback: allow manual enable when no PlayerPrefs key is present.
            return useEndlessMode;
        }
    }

    // Alias for PlayMaker "Get Property" discoverability.
    public bool UseEndlessModeEffective => IsEndlessModeActive;

    // Optional: convenience wrappers so FSMs can toggle Endless via Call Method.
    public void Endless_SetPlayerPrefEnabled(bool enabled)
    {
        if (string.IsNullOrWhiteSpace(endlessModePrefKey)) return;
        PlayerPrefs.SetInt(endlessModePrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void Endless_ReadPlayerPrefIntoUseEndlessMode()
    {
        if (string.IsNullOrWhiteSpace(endlessModePrefKey))
        {
            useEndlessMode = false;
            return;
        }

        useEndlessMode = PlayerPrefs.GetInt(endlessModePrefKey, 0) == 1;
    }

    // Endless always wants all 24 non-center squares selectable.
    public bool IsSquareAllowedThisRound_IgnoreRestrictions(int squareIndex)
    {
        return squareIndex != centralSquare;
    }

    // Enables placement colliders + minimap for all non-center squares.
    public void Endless_RefreshAllowedSquaresMask_AllEnabled()
    {
        int gridCount = transform.childCount;
        for (int i = 0; i < gridCount; i++)
        {
            bool isCenter = i == centralSquare;

            Transform squareRoot = transform.GetChild(i);
            if (squareRoot == null) continue;

            SquareController controller = squareRoot.GetComponent<SquareController>();
            if (controller != null)
            {
                controller.SetOffLimits(false);
            }

            foreach (var squareTrigger in squareRoot.GetComponentsInChildren<SquareTrigger>(true))
            {
                squareTrigger.SetPlacementEnabled(!isCenter);
            }
        }

        if (playerRotationDirectionTracker == null)
        {
            playerRotationDirectionTracker = Object.FindFirstObjectByType<PlayerRotationDirectionTracker>();
        }

        if (playerRotationDirectionTracker != null)
        {
            bool[] disabledMask = new bool[gridSize * gridSize];
            // All false = nothing disabled.
            playerRotationDirectionTracker.SetDisabledSquares(disabledMask);
        }
    }

    public void Endless_ResetAllSquaresToIdle()
    {
        // Hard gate: this method should only have effects in Endless mode.
        if (!IsEndlessModeActive) return;

        int gridCount = transform.childCount;
        for (int i = 0; i < gridCount; i++)
        {
            Transform squareTransform = transform.GetChild(i);
            if (squareTransform == null) continue;

            SquareController controller = squareTransform.GetComponent<SquareController>();
            if (controller == null) continue;

            // Leave the center alone.
            if (i == centralSquare) continue;

            // Clear any leftover selection flag from prior rounds (Endless can re-use squares).
            controller.SetSelectedSquareFlag(false);

            // Force the FSM back to its idle role, rather than relying on OnRefresh to fully reset.
            controller.SetState("Idle");
        }
    }

    // --- Grid completion helpers (PlayMaker-friendly) ---
    // Total unique squares selected across all rounds (excludes the center square).
    public int TotalUniqueSelectedSquaresCount
    {
        get
        {
            if (allRoundSelections == null || allRoundSelections.Count == 0) return 0;

            return allRoundSelections
                .SelectMany(r => r.Value != null ? r.Value.Keys : Enumerable.Empty<int>())
                .Where(i => i != centralSquare)
                .Distinct()
                .Count();
        }
    }

    // True when all non-center squares are selected at least once.
    // Useful as a post-validation safety check to end the run.
    public bool IsGridFullySelected => TotalUniqueSelectedSquaresCount >= (gridSize * gridSize - 1);

    public void CustomNightTimeline_ResetOutcome()
    {
        customNightTimelineWinState = false;
        customNightTimelineLastStatus = string.Empty;
        customNightTimelineNoSelectableSquaresRemaining = false;
    }

    public void CustomNightTimeline_Disable()
    {
        useCustomNightTimeline = false;
    }

    public void CustomNightTimeline_LoadSaved()
    {
        customNightTimelineWinState = false;
        customNightTimelineLastStatus = string.Empty;
        customNightTimelineNoSelectableSquaresRemaining = false;
        customNightTimelineCurrentRound = 0;

        // Default values if we can't load.
        customNightTimelinePlacementStageInterruptions = false;
        customNightTimelineForgivenessLevel = 0f;
        customNightTimelineAtticMin = 0f;
        customNightTimelineAtticMax = 0f;

        // If your menus use a PlayerPrefs flag, respect it here.
        if (!string.IsNullOrWhiteSpace(customNightPrefKey) && PlayerPrefs.GetInt(customNightPrefKey, 0) != 1)
        {
            customNightTimelineLoaded = false;
            customNightTimelineConfig = null;
            customNightTimelineTotalRounds = 0;
            useCustomNightTimeline = false;
            customNightTimelineLastStatus = "Custom Night flag not set.";
            return;
        }

        if (CustomNightConfigStore.TryLoad(out CustomNightTimelineConfig cfg, out string error))
        {
            customNightTimelineConfig = cfg;
            customNightTimelineLoaded = true;
            customNightTimelineTotalRounds = Mathf.Max(0, cfg != null ? cfg.TotalRounds : 0);
            useCustomNightTimeline = customNightTimelineTotalRounds > 0;

            // Cache global options for PlayMaker/UI.
            if (cfg != null)
            {
                customNightTimelinePlacementStageInterruptions = cfg.placementStageInterruptions;
                customNightTimelineForgivenessLevel = cfg.forgivenessLevel;
                customNightTimelineAtticMin = cfg.atticMin;
                customNightTimelineAtticMax = cfg.atticMax;
            }

            customNightTimelineLastStatus = useCustomNightTimeline ? "Custom Night timeline loaded." : "Custom Night timeline was empty.";
            return;
        }

        customNightTimelineLoaded = false;
        customNightTimelineConfig = null;
        customNightTimelineTotalRounds = 0;
        useCustomNightTimeline = false;
        customNightTimelineLastStatus = $"Failed to load custom night timeline: {error}";
    }

    // PlayMaker-friendly helpers to avoid wiring bugs with the round variable.
    public void CustomNightTimeline_BeginAndGenerateRound1()
    {
        customNightTimelineCurrentRound = 1;
        CustomNightTimeline_ApplyAndGenerateRound(customNightTimelineCurrentRound);
    }

    public void CustomNightTimeline_GenerateNextRound()
    {
        if (customNightTimelineCurrentRound < 1)
        {
            customNightTimelineCurrentRound = 1;
        }
        else
        {
            customNightTimelineCurrentRound += 1;
        }

        CustomNightTimeline_ApplyAndGenerateRound(customNightTimelineCurrentRound);
    }

    // Call Method-friendly entry point.
    // Assumes CustomNightTimeline_LoadSaved was called earlier (e.g., in MainInit custom branch).
    public void CustomNightTimeline_ApplyAndGenerateRound(int currentRound)
    {
        customNightTimelineWinState = false;
        customNightTimelineNoSelectableSquaresRemaining = false;

        if (!useCustomNightTimeline)
        {
            customNightTimelineLastStatus = "Custom night timeline not enabled.";
            return;
        }

        if (!customNightTimelineLoaded || customNightTimelineConfig == null)
        {
            customNightTimelineLastStatus = "Custom night timeline not loaded.";
            return;
        }

        int totalRounds = customNightTimelineConfig.TotalRounds;
        customNightTimelineTotalRounds = totalRounds;

        int clampedRound = Mathf.Max(1, currentRound);
        customNightTimelineCurrentRound = clampedRound;
        if (clampedRound > totalRounds)
        {
            customNightTimelineWinState = true;
            customNightTimelineLastStatus = "Reached end of custom night timeline (round > totalRounds).";
            return;
        }

        if (!customNightTimelineConfig.Validate(out string msg, gridSize * gridSize - 1, 4, false))
        {
            customNightTimelineLastStatus = $"Custom night timeline invalid: {msg}";
            return;
        }

        int idx = clampedRound - 1;
        if (customNightTimelineConfig.rounds == null || idx < 0 || idx >= customNightTimelineConfig.rounds.Count)
        {
            customNightTimelineLastStatus = "Custom night timeline missing round data.";
            return;
        }

        var round = customNightTimelineConfig.rounds[idx];
        if (round == null)
        {
            customNightTimelineLastStatus = "Custom night timeline round was null.";
            return;
        }

        int addSquaresThisRound = Mathf.Clamp(round.addSquaresThisRound, 0, gridSize * gridSize - 1);
        int maxTypesThisRound = Mathf.Clamp(round.maxTypesThisRound, 1, 4);

        // Compute candidate count to avoid impossible states (clamp squares-to-select).
        RefreshAllowedSquaresMask(clampedRound);

        HashSet<int> previouslySelectedSquares = allRoundSelections
            .SelectMany(r => r.Value.Keys)
            .ToHashSet();

        int candidateCount = Enumerable.Range(0, gridSize * gridSize)
            .Where(i => i != centralSquare)
            .Where(i => !previouslySelectedSquares.Contains(i))
            .Count(i => allowedSquaresThisRound.Contains(i));

        if (debugCustomNightTimeline)
        {
            bool restrictionEnabled = restrictSelectableSquares;
            int allowedCountSetting = allowedSelectableSquaresCount;
            bool randomizeEachRound = randomizeAllowedSquaresEachRound;
            if (useSelectableSquaresRestrictionProfile && selectableSquaresRestrictionProfile != null)
            {
                restrictionEnabled = selectableSquaresRestrictionProfile.restrictSelectableSquares;
                allowedCountSetting = selectableSquaresRestrictionProfile.allowedSelectableSquaresCount;
                randomizeEachRound = selectableSquaresRestrictionProfile.randomizeAllowedSquaresEachRound;
            }

            Debug.Log(
                $"[CustomNightTimeline] Apply round: currentRound={currentRound} clampedRound={clampedRound} idx={idx} " +
                $"addSquaresThisRound={addSquaresThisRound} maxTypesThisRound={maxTypesThisRound} " +
                $"previouslySelectedSquares={previouslySelectedSquares.Count} allowedSquaresThisRound={allowedSquaresThisRound.Count} " +
                $"restrictionEnabled={restrictionEnabled} allowedCountSetting={allowedCountSetting} randomizeAllowedSquaresEachRound={randomizeEachRound} " +
                $"candidateCount={candidateCount}");
        }

        int clampedSquaresToSelect = Mathf.Min(addSquaresThisRound, candidateCount);
        if (clampedSquaresToSelect <= 0)
        {
            customNightTimelineWinState = true;
            customNightTimelineNoSelectableSquaresRemaining = true;
            customNightTimelineLastStatus = "No selectable squares remaining (exhausted or restricted).";
            return;
        }

        // Apply and generate via existing pipeline.
        numberOfSelectedSquares = clampedSquaresToSelect;
        maxTypes = maxTypesThisRound;

        if (debugCustomNightTimeline)
        {
            Debug.Log($"[CustomNightTimeline] Generating: round={clampedRound} numberOfSelectedSquares={numberOfSelectedSquares} maxTypes={maxTypes}");
        }
        GenerateRandomSquares(clampedRound);

        customNightTimelineLastStatus = clampedRound >= totalRounds
            ? "Generated final custom night round."
            : "Generated custom night round.";
    }

    public void CustomNight_ResetOutcome()
    {
        customNightWinState = false;
        customNightLastScenarioValid = false;
        customNightLastAppliedSelectableSquares = 0;
        customNightLastAppliedMaxTypes = 0;
        customNightLastGeneratedSquares = 0;
        customNightLastStatus = string.Empty;
    }

    // Custom Night entry point (Call Method-friendly signature).
    // Applies/clamps settings for the given round and generates squares.
    // Sets CustomNightWinState=true when:
    // - currentRound is beyond totalRounds, OR
    // - no selectable squares remain, OR
    // - all available non-center, non-permanently-blocked squares have been exhausted.
    public void CustomNight_ApplyAndGenerateRound(int currentRound)
    {
        // Do not affect legacy flow unless explicitly enabled.
        if (!useCustomNightScenario || customNightScenario == null)
        {
            customNightLastScenarioValid = false;
            customNightLastStatus = "Custom night scenario not enabled/assigned.";
            customNightLastGeneratedSquares = 0;
            customNightWinState = false;
            return;
        }

        customNightWinState = false;
        customNightLastGeneratedSquares = 0;

        customNightLastScenarioValid = customNightScenario.Validate(out string validationMessage);
        if (!customNightLastScenarioValid)
        {
            customNightLastStatus = $"Scenario invalid: {validationMessage}";
            return;
        }

        int clampedRound = Mathf.Max(1, currentRound);
        if (clampedRound > customNightScenario.totalRounds)
        {
            customNightWinState = true;
            customNightLastStatus = "Reached end of scenario (round > totalRounds).";
            return;
        }

        // If the scenario provides a restriction profile, apply it.
        // We intentionally do not auto-disable profiles when null to avoid surprising side effects.
        if (customNightScenario.restrictionProfile != null)
        {
            selectableSquaresRestrictionProfile = customNightScenario.restrictionProfile;
            useSelectableSquaresRestrictionProfile = true;
        }

        // Hard limits in this project.
        const int maxHardTypes = 4;
        int maxHardSelectableSquares = gridSize * gridSize - 1; // 24 (center excluded)

        int desiredSquares = Mathf.Clamp(customNightScenario.GetDesiredSelectableSquaresForRound(clampedRound), 0, maxHardSelectableSquares);
        int desiredTypes = Mathf.Clamp(customNightScenario.GetDesiredMaxTypesForRound(clampedRound), 1, maxHardTypes);

        customNightLastAppliedMaxTypes = desiredTypes;

        // Ensure allowedSquaresThisRound is computed for this round before we count candidates.
        RefreshAllowedSquaresMask(clampedRound);

        HashSet<int> previouslySelectedSquares = allRoundSelections
            .SelectMany(round => round.Value.Keys)
            .ToHashSet();

        int candidateCount = Enumerable.Range(0, gridSize * gridSize)
            .Where(i => i != centralSquare)
            .Where(i => !previouslySelectedSquares.Contains(i))
            .Count(i => allowedSquaresThisRound.Contains(i));

        int clampedSquaresToSelect = Mathf.Min(desiredSquares, candidateCount);
        customNightLastAppliedSelectableSquares = clampedSquaresToSelect;

        if (clampedSquaresToSelect <= 0)
        {
            customNightWinState = true;
            customNightLastStatus = "No selectable squares remaining for this scenario (all exhausted or restricted).";
            return;
        }

        // Apply the computed round settings and generate normally.
        numberOfSelectedSquares = clampedSquaresToSelect;
        maxTypes = desiredTypes;
        GenerateRandomSquares(clampedRound);

        customNightLastGeneratedSquares = selectedSquares != null ? selectedSquares.Count : 0;

        // Win if all possible non-center, non-permanently-blocked squares are used up.
        // Note: this intentionally ignores per-round random allowed-count limits; it treats only permanently blocked as truly unavailable.
        int permanentlyBlockedCount = Mathf.Clamp(permanentlyBlockedSquaresSet.Count, 0, maxHardSelectableSquares);
        int maxUniquePossible = Mathf.Clamp(maxHardSelectableSquares - permanentlyBlockedCount, 0, maxHardSelectableSquares);
        int totalUniqueSelected = allRoundSelections
            .SelectMany(r => r.Value.Keys)
            .Distinct()
            .Count();

        if (totalUniqueSelected >= maxUniquePossible)
        {
            customNightWinState = true;
            customNightLastStatus = "All available squares exhausted (unique selected >= max possible).";
            return;
        }

        customNightLastStatus = clampedRound >= customNightScenario.totalRounds
            ? "Generated final scenario round."
            : "Generated scenario round.";
    }

    [Tooltip("These squares are guaranteed to be inaccessible when restrictions are enabled. Indices are 0-24 for a 5x5 grid.")]
    [SerializeField] private List<int> permanentlyBlockedSquares = new List<int>();

    private readonly HashSet<int> allowedSquaresThisRound = new HashSet<int>();
    private readonly HashSet<int> permanentlyBlockedSquaresSet = new HashSet<int>();
    private readonly HashSet<int> forcedAllowedSquaresSet = new HashSet<int>();
    private int lastAllowedSquaresRound = -1;

    [Header("Minimap (Optional)")]
    [SerializeField] private PlayerRotationDirectionTracker playerRotationDirectionTracker;

    [Header("Debug (Optional)")]
    [Tooltip("If enabled, GenerateRandomSquares will use the forced indices/types for the specified round.")]
    [SerializeField] private bool debugForceSelections = false;
    [Tooltip("If enabled, Custom Night Timeline will log which round settings are applied and how many candidates are available.")]
    [SerializeField] private bool debugCustomNightTimeline = false;
    [Tooltip("Only applies debug forcing when currentRound matches this value.")]
    [Min(1)]
    [SerializeField] private int debugForceRound = 1;
    [Tooltip("Forced square indices (0-24). Center (12) is allowed but will be ignored with a warning.")]
    [SerializeField] private List<int> debugForcedSquareIndices = new List<int> { 0, 10 };
    [Tooltip("Optional forced types (same count as indices). If missing/out of range, type will be chosen randomly from activeTypes.")]
    [SerializeField] private List<int> debugForcedTypes = new List<int>();
    [Tooltip("If enabled, forced indices will be used even if they violate allowed-squares restrictions or repeat a previously-used square.")]
    [SerializeField] private bool debugIgnoreRestrictionAndHistory = true;


    void Start()
    {
        centralSquare = (gridSize * gridSize) / 2; // Central index (12 in 5x5)
        ResetGridManager();
        StartCoroutine(InitializeGridWithDelay());
    }

    private IEnumerator InitializeGridWithDelay()
    {
        yield return new WaitForSeconds(0f); // Delay for initialization

        centralSquare = (gridSize * gridSize) / 2; // Central index (12 in 5x5)
        List<SquareController> controllers = new List<SquareController>();

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                int index = y * gridSize + x; // Flattened index for consistency

                // Calculate position based on squareSize
                float adjustedX = x * squareSize;
                float adjustedY = y * squareSize;
                Vector3 position = new Vector3(adjustedX, 0, adjustedY);

                // Instantiate the square
                GameObject square = Instantiate(squarePrefab, position, Quaternion.identity, transform);

                SquareController controller = square.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetIndex(index); // Assign the index to the square
                    controllers.Add(controller); // Store controller for deferred initialization
                }

                // Add TMP component if square has one as a child
                var tmp = square.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmp != null)
                {
                    tmp.text = index.ToString(); // Set index as text
                }

                // Highlight central square
                if (index == centralSquare)
                {
                    square.GetComponent<Renderer>().material.color = lightRed; // Highlight center
                    square.tag = "Center"; // Optional tag

                    if (controller != null)
                    {
                        //Debug.Log("Central square detected. Sending OnCenter event.");
                        controller.SetState("Center");
                    }
                    else
                    {
                        Debug.LogError("SquareController not found on central square.");
                    }
                }
            }
        }

        // After all squares are initialized, check and disable colliders
        //foreach (var controller in controllers)
        //{
        //  controller.CheckAndDisableCollider();
        //}
    }


    public void GenerateRandomSquares(int currentRound)
    {
        Debug.Log($"GenerateRandomSquares called for Round {currentRound}");

        // Solved-state is per-round/per-validation attempt. Reset when generating a new round.
        solvedNodes?.Clear();

        // Paranormal system unlock hook: when the game advances to round 2,
        // that implies round 1 was completed successfully.
        if (currentRound == 2)
        {
            ParanormalEventManager paranormalEventManager = Object.FindFirstObjectByType<ParanormalEventManager>();
            if (paranormalEventManager != null)
            {
                paranormalEventManager.UnlockAfterFirstRoundCompletion();
            }
        }

        RefreshAllowedSquaresMask(currentRound);

        if (selectedSquaresWithTypes == null)
        {
            Debug.LogWarning("[GridManager] WARNING: selectedSquaresWithTypes was NULL, initializing...");
            selectedSquaresWithTypes = new Dictionary<int, int>();
        }
        else
        {
            Debug.Log($"[GridManager] BEFORE Randomization - {string.Join(", ", selectedSquaresWithTypes.Select(kvp => $"Index: {kvp.Key}, Type: {kvp.Value}"))}");
        }

        selectedSquares = new List<int>();
        selectedSquaresWithTypes.Clear();  // ✅ Instead of reinitializing, just clear it

        int totalAvailableTypes = 4; // Maximum number of types in the game
        int typesToSelect = Mathf.Clamp(maxTypes, 1, totalAvailableTypes);
        // Start with 2, add 1 per round

        // ✅ Step 1: Maintain previously selected types
        HashSet<int> previouslySelectedTypes = allRoundSelections
            .SelectMany(round => round.Value.Values)
            .ToHashSet();

        // ✅ Step 2: If it's the first round, pick two random types
        if (currentRound == 1)
        {
            List<int> allTypes = Enumerable.Range(1, totalAvailableTypes).ToList();
            previouslySelectedTypes = new HashSet<int>(allTypes.OrderBy(x => Random.value).Take(2));
        }
        else if (previouslySelectedTypes.Count < typesToSelect)
        {
            // Only add a new type if we're under the allowed count
            List<int> possibleNewTypes = Enumerable.Range(1, totalAvailableTypes)
                .Except(previouslySelectedTypes)
                .ToList();

            if (possibleNewTypes.Count > 0)
            {
                int newType = possibleNewTypes[Random.Range(0, possibleNewTypes.Count)];
                previouslySelectedTypes.Add(newType);
            }
        }

        // ✅ Step 4: Convert HashSet to a shuffled List for random selection
        List<int> activeTypes = previouslySelectedTypes.ToList();
        if (activeTypes.Count > typesToSelect)
        {
            activeTypes = activeTypes.OrderBy(x => Random.value).Take(typesToSelect).ToList();
        }


        // ✅ Step 5: Assign random squares while maintaining previous selections
        int squaresToSelect = numberOfSelectedSquares; // Increase squares per round
        HashSet<int> previouslySelectedSquares = allRoundSelections
            .SelectMany(round => round.Value.Keys)
            .ToHashSet();

        int maxSelectableSquares = gridSize * gridSize - 1; // 24
        squaresToSelect = Mathf.Min(squaresToSelect, maxSelectableSquares - previouslySelectedSquares.Count);

        List<int> candidateSquares = Enumerable.Range(0, gridSize * gridSize)
            .Where(i => i != centralSquare)
            .Where(i => !previouslySelectedSquares.Contains(i))
            .Where(i => allowedSquaresThisRound.Contains(i))
            .OrderBy(_ => Random.value)
            .ToList();

        // Debug override: force specific indices for a given round.
        if (debugForceSelections && currentRound == debugForceRound && debugForcedSquareIndices != null && debugForcedSquareIndices.Count > 0)
        {
            List<int> forced = new List<int>();
            for (int i = 0; i < debugForcedSquareIndices.Count; i++)
            {
                int idx = debugForcedSquareIndices[i];
                if (idx < 0 || idx >= gridSize * gridSize)
                {
                    Debug.LogWarning($"[GridManager] Debug forced index out of range: {idx}.", this);
                    continue;
                }
                if (idx == centralSquare)
                {
                    Debug.LogWarning($"[GridManager] Debug forced index is center ({centralSquare}); skipping.", this);
                    continue;
                }

                if (!debugIgnoreRestrictionAndHistory)
                {
                    if (previouslySelectedSquares.Contains(idx))
                    {
                        Debug.LogWarning($"[GridManager] Debug forced index {idx} was previously selected; skipping (debugIgnoreRestrictionAndHistory=false).", this);
                        continue;
                    }
                    if (!allowedSquaresThisRound.Contains(idx))
                    {
                        Debug.LogWarning($"[GridManager] Debug forced index {idx} is not allowed this round; skipping (debugIgnoreRestrictionAndHistory=false).", this);
                        continue;
                    }
                }

                forced.Add(idx);
            }

            // De-dup while preserving order
            forced = forced.Distinct().ToList();

            candidateSquares = forced;
            squaresToSelect = candidateSquares.Count;
            Debug.Log($"[GridManager] DEBUG forcing selections for round {currentRound}: [{string.Join(",", candidateSquares)}]", this);
        }

        squaresToSelect = Mathf.Min(squaresToSelect, candidateSquares.Count);

        for (int i = 0; i < squaresToSelect; i++)
        {
            int chosenSquare = candidateSquares[i];
            selectedSquares.Add(chosenSquare);

            int randomType;
            if (debugForceSelections && currentRound == debugForceRound && debugForcedTypes != null && i < debugForcedTypes.Count)
            {
                int forcedType = debugForcedTypes[i];
                randomType = forcedType;
            }
            else
            {
                randomType = activeTypes[Random.Range(0, activeTypes.Count)]; // ✅ Randomly assign from active types
            }
            selectedSquaresWithTypes[chosenSquare] = randomType;

            // Apply visual changes
            Transform square = transform.GetChild(chosenSquare);
            if (square != null)
            {
                square.GetComponent<Renderer>().material.color = lightYellow;
                var tmp = square.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmp != null)
                {
                    tmp.text += $" (Type {randomType})";
                }

                // Notify FSM via SquareController
                SquareController controller = square.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetSelectedSquareFlag(true);
                    controller.SetState("InPlay");
                    controller.SetType(randomType);
                }
                else
                {
                    Debug.LogError($"SquareController not found on square {chosenSquare}");
                }
            }
        }

        // ✅ Step 6: Ensure the center square is initialized
        GameObject centerSquare = GameObject.FindGameObjectWithTag("Center");
        if (centerSquare != null && !centerIsInitialized)
        {
            SquareController centerController = centerSquare.GetComponent<SquareController>();
            if (centerController != null)
            {
                centerController.SetState("Center");
                centerIsInitialized = true;
            }
        }
        else if (centerSquare == null)
        {
            Debug.LogError("Central tag not found on central square.");
        }

        // ✅ Step 7: Store selections for future rounds
        RecordRoundSelections(currentRound);
        Debug.Log($"Selected Squares and Types: {string.Join(", ", selectedSquaresWithTypes.Select(kvp => $"Index: {kvp.Key}, Type: {kvp.Value}"))}");

        // Diagnostics: confirm uniqueness and flag same-type selections (often misinterpreted as "only one sound")
        if (selectedSquares != null)
        {
            int distinct = selectedSquares.Distinct().Count();
            if (distinct != selectedSquares.Count)
            {
                Debug.LogError($"[GridManager] Duplicate square indices detected! selectedSquares.Count={selectedSquares.Count}, distinct={distinct}");
            }
        }

        if (selectedSquaresWithTypes != null)
        {
            var byType = selectedSquaresWithTypes
                .GroupBy(kvp => kvp.Value)
                .Select(g => new { Type = g.Key, Count = g.Count(), Indices = string.Join(",", g.Select(x => x.Key)) })
                .OrderByDescending(x => x.Count)
                .ToList();

            var duplicates = byType.Where(x => x.Count > 1).ToList();
            if (duplicates.Count > 0)
            {
                Debug.LogWarning($"[GridManager] Multiple squares share the same type this round: {string.Join(" | ", duplicates.Select(d => $"Type {d.Type} x{d.Count} (indices {d.Indices})"))}. If downstream audio plays once per TYPE, you'll hear only one sound.");
            }
        }
    }

    // Parallel generator for Endless mode.
    // - Squares CAN repeat across rounds (no history filtering)
    // - Restrictions are ignored (all 24 non-center squares)
    public void GenerateEndlessSquares(int currentRound)
    {
        Debug.Log($"GenerateEndlessSquares called for Round {currentRound}");

        // If the previous Endless validation succeeded, apply the deferred cleanup now.
        // This makes relics disappear between rounds even if the scene FSM still uses legacy transitions.
        RoundManager roundManager = Object.FindFirstObjectByType<RoundManager>();
        if (roundManager != null && roundManager.EndlessPendingSuccessCleanup)
        {
            roundManager.Endless_ApplyPendingSuccessCleanup();
        }

        // Solved-state is per-round/per-validation attempt. Reset when generating a new round.
        solvedNodes?.Clear();

        // Endless allows repeats across rounds, so we must reset square FSM state each round.
        // This also ensures any trigger colliders disabled by the previous round's OnSolved state
        // are re-enabled before the Traveler can collide with them again.
        Endless_ResetAllSquaresToIdle();

        // Keep parity with normal mode unlock hook.
        if (currentRound == 2)
        {
            ParanormalEventManager paranormalEventManager = Object.FindFirstObjectByType<ParanormalEventManager>();
            if (paranormalEventManager != null)
            {
                paranormalEventManager.UnlockAfterFirstRoundCompletion();
            }
        }

        // Ensure placement/minimap are fully enabled in Endless.
        Endless_RefreshAllowedSquaresMask_AllEnabled();

        if (selectedSquaresWithTypes == null)
        {
            Debug.LogWarning("[GridManager] WARNING: selectedSquaresWithTypes was NULL, initializing...");
            selectedSquaresWithTypes = new Dictionary<int, int>();
        }

        selectedSquares = new List<int>();
        selectedSquaresWithTypes.Clear();

        int totalAvailableTypes = 4;
        int typesToSelect = Mathf.Clamp(maxTypes, 1, totalAvailableTypes);

        // Choose the active type set fresh each round (Endless doesn't need type history).
        List<int> activeTypes = Enumerable.Range(1, totalAvailableTypes)
            .OrderBy(_ => Random.value)
            .Take(typesToSelect)
            .ToList();

        // Endless rule: 1 relic on round 1, 2 on round 2, ... up to 24, then stay at 24 forever.
        int squaresToSelect = Mathf.Clamp(currentRound, 1, gridSize * gridSize - 1);

        List<int> candidateSquares = Enumerable.Range(0, gridSize * gridSize)
            .Where(i => i != centralSquare)
            .OrderBy(_ => Random.value)
            .ToList();

        // Debug helper: guarantee square 0 is selected each round (when it isn't the center).
        if (endlessDebugForceIncludeSquareIndex0EveryRound)
        {
            if (centralSquare == 0)
            {
                Debug.LogWarning("[Endless][Debug] Force-include index 0 requested, but index 0 is the center square in this grid. Skipping.", this);
            }
            else
            {
                candidateSquares.Remove(0);
                candidateSquares.Insert(0, 0);
                Debug.Log("[Endless][Debug] Forcing index 0 to be selected this round.", this);
            }
        }

        squaresToSelect = Mathf.Min(squaresToSelect, candidateSquares.Count);

        for (int i = 0; i < squaresToSelect; i++)
        {
            int chosenSquare = candidateSquares[i];
            selectedSquares.Add(chosenSquare);

            int randomType = activeTypes[Random.Range(0, activeTypes.Count)];
            selectedSquaresWithTypes[chosenSquare] = randomType;

            Transform square = transform.GetChild(chosenSquare);
            if (square != null)
            {
                square.GetComponent<Renderer>().material.color = lightYellow;

                SquareController controller = square.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetState("InPlay");
                    controller.SetType(randomType);
                }
                else
                {
                    Debug.LogError($"SquareController not found on square {chosenSquare}");
                }
            }
        }

        // Ensure the center square is initialized.
        GameObject centerSquareObj = GameObject.FindGameObjectWithTag("Center");
        if (centerSquareObj != null && !centerIsInitialized)
        {
            SquareController centerController = centerSquareObj.GetComponent<SquareController>();
            if (centerController != null)
            {
                centerController.SetState("Center");
                centerIsInitialized = true;
            }
        }
        else if (centerSquareObj == null)
        {
            Debug.LogError("Central tag not found on central square.");
        }

        Debug.Log($"[Endless] Selected Squares and Types: {string.Join(", ", selectedSquaresWithTypes.Select(kvp => $"Index: {kvp.Key}, Type: {kvp.Value}"))}");
    }

    public void RefreshAllowedSquaresMask(int currentRound)
    {
        RecomputeAllowedSquaresThisRound(currentRound);
        ApplyAllowedSquaresToPlacementColliders();
        ApplyAllowedSquaresToMinimap();
    }

    private void RecomputeAllowedSquaresThisRound(int currentRound)
    {
        allowedSquaresThisRound.Clear();

        bool restrictionEnabled = restrictSelectableSquares;
        int allowedCountSetting = allowedSelectableSquaresCount;
        bool randomizeEachRound = randomizeAllowedSquaresEachRound;

        permanentlyBlockedSquaresSet.Clear();
        forcedAllowedSquaresSet.Clear();

        if (useSelectableSquaresRestrictionProfile && selectableSquaresRestrictionProfile != null)
        {
            restrictionEnabled = selectableSquaresRestrictionProfile.restrictSelectableSquares;
            allowedCountSetting = selectableSquaresRestrictionProfile.allowedSelectableSquaresCount;
            randomizeEachRound = selectableSquaresRestrictionProfile.randomizeAllowedSquaresEachRound;

            if (selectableSquaresRestrictionProfile.permanentlyBlockedSquares != null)
            {
                foreach (int idx in selectableSquaresRestrictionProfile.permanentlyBlockedSquares)
                {
                    permanentlyBlockedSquaresSet.Add(idx);
                }
            }

            if (selectableSquaresRestrictionProfile.forcedAllowedSquares != null)
            {
                foreach (int idx in selectableSquaresRestrictionProfile.forcedAllowedSquares)
                {
                    forcedAllowedSquaresSet.Add(idx);
                }
            }
        }
        else
        {
            if (permanentlyBlockedSquares != null)
            {
                foreach (int idx in permanentlyBlockedSquares)
                {
                    permanentlyBlockedSquaresSet.Add(idx);
                }
            }
        }

        permanentlyBlockedSquaresSet.Remove(centralSquare);

        if (!restrictionEnabled)
        {
            for (int i = 0; i < gridSize * gridSize; i++)
            {
                if (i == centralSquare) continue;
                if (permanentlyBlockedSquaresSet.Contains(i)) continue;
                allowedSquaresThisRound.Add(i);
            }

            lastAllowedSquaresRound = currentRound;
            return;
        }

        int maxPossible = (gridSize * gridSize - 1) - permanentlyBlockedSquaresSet.Count;
        int clampedAllowedCount = Mathf.Clamp(allowedCountSetting, 1, Mathf.Max(1, maxPossible));

        bool shouldRecompute;
        if (randomizeEachRound)
        {
            shouldRecompute = lastAllowedSquaresRound != currentRound;
        }
        else
        {
            shouldRecompute = lastAllowedSquaresRound == -1;
        }

        if (!shouldRecompute && allowedSquaresThisRound.Count == clampedAllowedCount)
        {
            return;
        }

        foreach (int forced in forcedAllowedSquaresSet)
        {
            if (forced == centralSquare) continue;
            if (forced < 0 || forced >= gridSize * gridSize) continue;
            if (permanentlyBlockedSquaresSet.Contains(forced)) continue;
            allowedSquaresThisRound.Add(forced);
        }

        List<int> candidates = Enumerable.Range(0, gridSize * gridSize)
            .Where(i => i != centralSquare)
            .Where(i => !permanentlyBlockedSquaresSet.Contains(i))
            .Where(i => !allowedSquaresThisRound.Contains(i))
            .OrderBy(_ => Random.value)
            .ToList();

        int remainingSlots = Mathf.Max(0, clampedAllowedCount - allowedSquaresThisRound.Count);
        for (int i = 0; i < remainingSlots && i < candidates.Count; i++)
        {
            allowedSquaresThisRound.Add(candidates[i]);
        }

        lastAllowedSquaresRound = currentRound;
    }

    public bool IsSquareAllowedThisRound(int squareIndex)
    {
        if (squareIndex == centralSquare) return false;
        if (permanentlyBlockedSquaresSet.Contains(squareIndex)) return false;

        bool restrictionEnabled = restrictSelectableSquares;
        if (useSelectableSquaresRestrictionProfile && selectableSquaresRestrictionProfile != null)
        {
            restrictionEnabled = selectableSquaresRestrictionProfile.restrictSelectableSquares;
        }

        if (!restrictionEnabled) return true;
        return allowedSquaresThisRound.Contains(squareIndex);
    }

    private void ApplyAllowedSquaresToPlacementColliders()
    {
        int gridCount = transform.childCount;
        for (int i = 0; i < gridCount; i++)
        {
            bool placementEnabled = IsSquareAllowedThisRound(i);

            bool restrictionEnabled = restrictSelectableSquares;
            if (useSelectableSquaresRestrictionProfile && selectableSquaresRestrictionProfile != null)
            {
                restrictionEnabled = selectableSquaresRestrictionProfile.restrictSelectableSquares;
            }

            bool isOffLimits = restrictionEnabled && i != centralSquare && !allowedSquaresThisRound.Contains(i);

            Transform squareRoot = transform.GetChild(i);
            if (squareRoot == null) continue;

            SquareController controller = squareRoot.GetComponent<SquareController>();
            if (controller != null)
            {
                controller.SetOffLimits(isOffLimits);
            }

            foreach (var squareTrigger in squareRoot.GetComponentsInChildren<SquareTrigger>(true))
            {
                squareTrigger.SetPlacementEnabled(placementEnabled);
            }
        }
    }

    private void ApplyAllowedSquaresToMinimap()
    {
        if (playerRotationDirectionTracker == null)
        {
            playerRotationDirectionTracker = Object.FindFirstObjectByType<PlayerRotationDirectionTracker>();
        }

        if (playerRotationDirectionTracker == null) return;

        bool[] disabledMask = new bool[gridSize * gridSize];
        for (int i = 0; i < disabledMask.Length; i++)
        {
            bool restrictionEnabled = restrictSelectableSquares;
            if (useSelectableSquaresRestrictionProfile && selectableSquaresRestrictionProfile != null)
            {
                restrictionEnabled = selectableSquaresRestrictionProfile.restrictSelectableSquares;
            }

            disabledMask[i] = restrictionEnabled && i != centralSquare && !allowedSquaresThisRound.Contains(i);
        }

        playerRotationDirectionTracker.SetDisabledSquares(disabledMask);
    }

    // --- PlayMaker-friendly helpers ---
    // PlayMaker's built-in Set Property action often won't show private [SerializeField] fields,
    // and it can be awkward to pass custom ScriptableObject types as parameters.
    // These wrappers accept UnityEngine.Object so FSMs can use an "Object" variable to assign the asset.

    public void SetSelectableSquaresRestrictionProfileAsset(UnityEngine.Object profileAsset)
    {
        selectableSquaresRestrictionProfile = profileAsset as SelectableSquaresRestrictionProfile;
    }

    public void SetUseSelectableSquaresRestrictionProfile(bool useProfile)
    {
        useSelectableSquaresRestrictionProfile = useProfile;
    }

    public void ApplySelectableSquaresRestrictionProfileForRound(UnityEngine.Object profileAsset, int currentRound, bool useProfile)
    {
        selectableSquaresRestrictionProfile = profileAsset as SelectableSquaresRestrictionProfile;
        useSelectableSquaresRestrictionProfile = useProfile && selectableSquaresRestrictionProfile != null;
        RefreshAllowedSquaresMask(currentRound);
    }

    // PlayMaker Call Method-friendly (no ScriptableObject parameter types):
    // Use Set Property to set SelectableSquaresRestrictionProfile, then call this.
    public void ApplySelectableSquaresRestrictionProfileForRound(int currentRound)
    {
        useSelectableSquaresRestrictionProfile = selectableSquaresRestrictionProfile != null;
        RefreshAllowedSquaresMask(currentRound);
    }


    public void ResetGridManager()
    {
        Debug.Log($"Before Reset: allRoundSelections count: {allRoundSelections.Count}");
        selectedSquares?.Clear();
        selectedSquaresWithTypes?.Clear();
        allRoundSelections?.Clear();
        solvedNodes?.Clear();
        centerIsInitialized = false;

        Debug.Log("GridManager reset on scene reload.");
    }

    public Dictionary<int, int> GetSelectedSquaresWithTypes()
    {
        return selectedSquaresWithTypes;
    }

    public Dictionary<int, int> GetRoundSelections(int roundNumber)
    {
        if (allRoundSelections.TryGetValue(roundNumber, out var roundSelections))
        {
            return new Dictionary<int, int>(roundSelections);
        }

        Debug.LogError($"No selections found for round {roundNumber}");
        return new Dictionary<int, int>();
    }

    public Dictionary<int, (GameObject, int)> GetPlacedObjects()
    {
        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        if (objectPlacer != null)
        {
            Dictionary<Vector2Int, GameObject> placedObjects = objectPlacer.GetPlacedObjects();
            return placedObjects.ToDictionary(
                kvp =>
                {
                    int index = kvp.Key.y * gridSize + kvp.Key.x;
                    var item = kvp.Value.GetComponent<Item>();
                    int type = item.GetItemType();
                    Debug.Log($"Converting placed object at position ({kvp.Key.x}, {kvp.Key.y}) to index {index} with type {type}");
                    return index;
                },
                kvp =>
                {
                    var item = kvp.Value.GetComponent<Item>();
                    int type = item.GetItemType();
                    Debug.Log($"Getting item component from object {kvp.Value.name} - reports type: {type}");
                    return (kvp.Value, type);
                }
            );
        }

        Debug.LogError("ObjectPlacer not found while trying to get placed objects.");
        return new Dictionary<int, (GameObject, int)>();
    }

    public Dictionary<int, Dictionary<int, int>> GetAllRoundSelections()
    {
        return allRoundSelections;
    }

    // True if this index was ever selected as a target square in any recorded round.
    // Note: this is NOT the same as IsNodeSolved(), which is intentionally cleared when generating a new round.
    public bool WasSquareEverSelected(int nodeIndex)
    {
        if (allRoundSelections == null || allRoundSelections.Count == 0) return false;

        foreach (var kvp in allRoundSelections)
        {
            Dictionary<int, int> selections = kvp.Value;
            if (selections != null && selections.ContainsKey(nodeIndex)) return true;
        }

        return false;
    }

    public void RecordRoundSelections(int roundNumber)
    {
        allRoundSelections[roundNumber] = new Dictionary<int, int>(selectedSquaresWithTypes);
        //Debug.Log($"Recorded round {roundNumber} selections.");
    }

    public int GetNumberOfSelectedSquares()
    {
        return selectedSquares.Count;
    }

    public void SetNumberOfSelectedSquares(int newCount)
    {
        numberOfSelectedSquares = Mathf.Clamp(newCount, 1, gridSize * gridSize - 1);
        GenerateRandomSquares(1); // Pass a valid round number, defaulted to 1 if none provided.

        // Reset the traveler
        Traveler traveler = Object.FindFirstObjectByType<Traveler>();
        if (traveler != null)
        {
            traveler.ResetTraveler();
        }
    }

    public void SetMaxTypes(int maxTypes)
    {
        this.maxTypes = maxTypes;
    }

    public void AddMissingSquaresToCurrentRound(Dictionary<int, int> missingSquares)
    {
        foreach (var square in missingSquares)
        {
            if (!selectedSquaresWithTypes.ContainsKey(square.Key))
            {
                selectedSquaresWithTypes[square.Key] = square.Value;
                Debug.Log($"Added missing square {square.Key} (Type {square.Value}) to current round.");
            }
        }
    }

    public void MarkNodeAsSolved(int nodeIndex)
    {
        if (!solvedNodes.Contains(nodeIndex))
        {
            solvedNodes.Add(nodeIndex);
            Debug.Log($"Node {nodeIndex} marked as solved.");

            // Notify the FSM to change the state to "Solved"
            Transform squareTransform = transform.GetChild(nodeIndex);
            if (squareTransform != null)
            {
                SquareController controller = squareTransform.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetState("Solved");
                }
                else
                {
                    Debug.LogError($"SquareController not found on square {nodeIndex}");
                }
            }
        }
    }

    // Lightweight solved-flag API (no visual/FSM side effects). Used for re-validation routing.
    public void SetNodeSolvedFlag(int nodeIndex, bool solved)
    {
        if (solved)
        {
            if (!solvedNodes.Contains(nodeIndex))
            {
                solvedNodes.Add(nodeIndex);
            }
        }
        else
        {
            if (solvedNodes.Contains(nodeIndex))
            {
                solvedNodes.Remove(nodeIndex);
            }
        }
    }


    public void UnmarkNodeAsSolved(int nodeIndex)
    {
        if (solvedNodes.Contains(nodeIndex))
        {
            solvedNodes.Remove(nodeIndex);
            Debug.Log($"Node {nodeIndex} unmarked as solved.");

            // Notify the FSM to change the state back to "Idle" or "InPlay"
            Transform squareTransform = transform.GetChild(nodeIndex);
            if (squareTransform != null)
            {
                SquareController controller = squareTransform.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetState("InPlay"); // Adjust based on your game logic
                }
                else
                {
                    Debug.LogError($"SquareController not found on square {nodeIndex}");
                }
            }
        }
    }


    public bool IsNodeSolved(int nodeIndex)
    {
        return solvedNodes.Contains(nodeIndex);
    }

    public void RestartScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GenerateTutorialSquares()
    {
        // Ensure placement restrictions are initialized for the tutorial.
        // Without this, IsSquareAllowedThisRound() can return false for everything when restrictions are enabled,
        // which prevents ObjectPlacer from activating the reticle/placing relics during tutorial.
        RefreshAllowedSquaresMask(currentRound: 1);

        selectedSquares = new List<int> { 21, 1 };
        selectedSquaresWithTypes = new Dictionary<int, int>
    {
        { 21, 1 },
        { 1, 1 }
    };

        // Force-allow tutorial squares regardless of the current restriction mask.
        allowedSquaresThisRound.Add(21);
        allowedSquaresThisRound.Add(1);
        ApplyAllowedSquaresToPlacementColliders();
        ApplyAllowedSquaresToMinimap();

        allRoundSelections[1] = new Dictionary<int, int>(selectedSquaresWithTypes); // Optional: pretend it's round 1

        foreach (int index in selectedSquares)
        {
            Transform square = transform.GetChild(index);
            if (square != null)
            {
                square.GetComponent<Renderer>().material.color = lightYellow;

                var tmp = square.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmp != null)
                {
                    tmp.text += $" (Type 1)";
                }

                SquareController controller = square.GetComponent<SquareController>();
                if (controller != null)
                {
                    controller.SetState("InPlay");
                    controller.SetType(1);
                }
                else
                {
                    Debug.LogError($"SquareController not found on square {index}");
                }
            }
        }

        // Ensure center square is marked
        GameObject centerSquare = GameObject.FindGameObjectWithTag("Center");
        if (centerSquare != null && !centerIsInitialized)
        {
            SquareController centerController = centerSquare.GetComponent<SquareController>();
            if (centerController != null)
            {
                centerController.SetState("Center");
                centerIsInitialized = true;
            }
        }
    }

}
