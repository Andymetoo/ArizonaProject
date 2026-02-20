using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class RoundManager : MonoBehaviour
{
    [SerializeField] private int gridSize = 5; // Set the grid size directly in the Inspector

    private ValidationSystem validationSystem;
    private GridManager gridManager;
    public int currentRound = 1;

    public bool readyForNextRound = false;

    public int wrongMeter = 0; // Tracks the total number of errors across rounds
    private Dictionary<int, int> randomizedItemAssignments; // Maps square type to item type

    // --- Endless score tracking (written on first failure) ---
    [Header("Endless (Score Tracking)")]
    [Tooltip("PlayerPrefs key written on the first failed validation in Endless.")]
    public string endlessRoundsCompletedPrefKey = "Endless_RoundsCompleted";

    [Tooltip("PlayerPrefs key written on the first failed validation in Endless.")]
    public string endlessTotalSquaresCompletedPrefKey = "Endless_TotalSquaresCompleted";

    private int endlessRoundsCompleted = 0;
    private int endlessTotalSquaresCompleted = 0;
    private bool endlessScoreWritten = false;

    [Tooltip("If true, SubmitRound_Endless will clear placements immediately on success. If false, call Endless_ApplyPendingSuccessCleanup() later (e.g., after the Traveler finishes checking).")]
    public bool endlessClearPlacementsImmediatelyOnSuccess = false;

    private bool endlessPendingSuccessCleanup = false;
    public bool EndlessPendingSuccessCleanup => endlessPendingSuccessCleanup;

    void Start()
    {
        gridManager = Object.FindFirstObjectByType<GridManager>();
        validationSystem = Object.FindFirstObjectByType<ValidationSystem>();

        RandomizeItemAssignments();
    }

    // --- Endless hooks (PlayMaker-friendly) ---
    // Call this at the start of an Endless run.
    public void Endless_ResetScoreTracking()
    {
        endlessRoundsCompleted = 0;
        endlessTotalSquaresCompleted = 0;
        endlessScoreWritten = false;
        endlessPendingSuccessCleanup = false;

        // Clear old values so the UI doesn't show stale scores if the player quits mid-run.
        if (!string.IsNullOrWhiteSpace(endlessRoundsCompletedPrefKey)) PlayerPrefs.DeleteKey(endlessRoundsCompletedPrefKey);
        if (!string.IsNullOrWhiteSpace(endlessTotalSquaresCompletedPrefKey)) PlayerPrefs.DeleteKey(endlessTotalSquaresCompletedPrefKey);
        PlayerPrefs.Save();
    }

    // Call this after the Traveler finishes the "checking" sequence.
    // Only does work if the last Endless validation succeeded.
    public void Endless_ApplyPendingSuccessCleanup()
    {
        if (!endlessPendingSuccessCleanup) return;

        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        if (objectPlacer != null)
        {
            objectPlacer.ClearAllPlacedObjects_Endless();
        }

        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }
        if (gridManager != null)
        {
            gridManager.Endless_ResetAllSquaresToIdle();
        }

        endlessPendingSuccessCleanup = false;
    }

    // Endless submit: clears placements on success, never locks items, and writes score on first failure.
    public void SubmitRound_Endless()
    {
        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        if (objectPlacer == null)
        {
            Debug.LogError("[RoundManager] SubmitRound_Endless failed: ObjectPlacer not found.");
            readyForNextRound = false;
            return;
        }

        Dictionary<Vector2Int, GameObject> placedObjects = objectPlacer.GetPlacedObjects();
        Dictionary<int, (GameObject, int)> convertedPlacedObjects = ConvertPlacedObjects(placedObjects);

        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }
        if (validationSystem == null)
        {
            validationSystem = Object.FindFirstObjectByType<ValidationSystem>();
        }
        if (gridManager == null || validationSystem == null)
        {
            Debug.LogError("[RoundManager] SubmitRound_Endless failed: missing GridManager or ValidationSystem.");
            readyForNextRound = false;
            return;
        }

        Dictionary<int, int> currentRoundSelections = gridManager.GetSelectedSquaresWithTypes();

        bool roundIsValid = validationSystem.ValidateCurrentRound(
            convertedPlacedObjects,
            currentRoundSelections,
            ref wrongMeter
        );

        if (roundIsValid)
        {
            // Score tracking (do not write to PlayerPrefs yet; only on first failure).
            endlessRoundsCompleted++;
            endlessTotalSquaresCompleted += (currentRoundSelections != null ? currentRoundSelections.Count : 0);

            // Endless behavior: clear placements AFTER the Traveler finishes checking.
            endlessPendingSuccessCleanup = true;
            if (endlessClearPlacementsImmediatelyOnSuccess)
            {
                Endless_ApplyPendingSuccessCleanup();
            }

            readyForNextRound = true;
            Debug.Log($"[Endless] Round complete. RoundsCompleted={endlessRoundsCompleted} TotalSquaresCompleted={endlessTotalSquaresCompleted}");
        }
        else
        {
            readyForNextRound = false;
            Debug.Log($"[Endless] Failed validation. Errors: {wrongMeter}");

            endlessPendingSuccessCleanup = false;

            if (!endlessScoreWritten)
            {
                if (!string.IsNullOrWhiteSpace(endlessRoundsCompletedPrefKey))
                {
                    PlayerPrefs.SetInt(endlessRoundsCompletedPrefKey, Mathf.Max(0, endlessRoundsCompleted));
                }

                if (!string.IsNullOrWhiteSpace(endlessTotalSquaresCompletedPrefKey))
                {
                    PlayerPrefs.SetInt(endlessTotalSquaresCompletedPrefKey, Mathf.Max(0, endlessTotalSquaresCompleted));
                }

                PlayerPrefs.Save();
                endlessScoreWritten = true;
                Debug.Log($"[Endless] Wrote score -> {endlessRoundsCompletedPrefKey}={endlessRoundsCompleted}, {endlessTotalSquaresCompletedPrefKey}={endlessTotalSquaresCompleted}");
            }
        }

        Debug.Log($"[Endless] Submit complete. Total errors so far: {wrongMeter}");
    }

    public void SubmitRound()
    {
        // Safety: some PlayMaker flows still call SubmitRound() even in Endless.
        // Route to Endless behavior to avoid LockItem/ceiling-locking and to enable Endless scoring.
        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }
        if (gridManager != null && gridManager.IsEndlessModeActive)
        {
            SubmitRound_Endless();
            return;
        }

        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        Dictionary<Vector2Int, GameObject> placedObjects = objectPlacer.GetPlacedObjects();

        // Convert placed objects to the required format
        Dictionary<int, (GameObject, int)> convertedPlacedObjects = new Dictionary<int, (GameObject, int)>();
        foreach (var kvp in placedObjects)
        {
            int index = kvp.Key.y * gridSize + kvp.Key.x;
            var item = kvp.Value.GetComponent<Item>();
            if (item != null)
            {
                convertedPlacedObjects[index] = (kvp.Value, item.GetItemType());
            }
            else
            {
                Debug.LogError($"Item component missing on placed object at index {index}");
            }
        }

        // Get the current round's selected squares
        Dictionary<int, int> currentRoundSelections = gridManager.GetSelectedSquaresWithTypes();

        // Validate the current round
        bool roundIsValid = validationSystem.ValidateCurrentRound(
            convertedPlacedObjects,
            currentRoundSelections,
            ref wrongMeter
        );

    if (roundIsValid)
    {
        // Lock all objects for this round if validation passed
        validationSystem.LockCurrentRoundObjects(convertedPlacedObjects, currentRoundSelections);
        Debug.Log($"Round {currentRound} Complete! All items locked.");
        readyForNextRound = true;
    }
    else
    {
        Debug.Log($"Round {currentRound} incomplete. Errors: {wrongMeter}");
        readyForNextRound = false;
    }


        Debug.Log($"Round submitted. Total errors so far: {wrongMeter}");
    }



    public void IncrementRound()
    {
        currentRound++;

        RandomizeItemAssignments();

        gridManager.GenerateRandomSquares(currentRound);
        gridManager.RecordRoundSelections(currentRound);
        readyForNextRound = false;
        Debug.Log($"Round incremented to {currentRound}. New puzzle generated.");
    }

    // Endless-only: advances the round counter and generates a new Endless pattern.
    // Safe for PlayMaker debug/testing; does not lock items and does not use the legacy generator.
    public void IncrementRound_Endless()
    {
        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }

        if (gridManager == null || !gridManager.IsEndlessModeActive)
        {
            Debug.LogWarning("[RoundManager] IncrementRound_Endless called while Endless is not active. Aborting.");
            return;
        }

        currentRound++;
        RandomizeItemAssignments();

        gridManager.GenerateEndlessSquares(currentRound);
        readyForNextRound = false;

        Debug.Log($"[Endless] Round incremented to {currentRound}. New Endless pattern generated.");
    }

    // Endless-only: regenerates the current round pattern without changing currentRound.
    public void GenerateCurrentRound_Endless()
    {
        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }

        if (gridManager == null || !gridManager.IsEndlessModeActive)
        {
            Debug.LogWarning("[RoundManager] GenerateCurrentRound_Endless called while Endless is not active. Aborting.");
            return;
        }

        RandomizeItemAssignments();
        gridManager.GenerateEndlessSquares(currentRound);
        readyForNextRound = false;
        Debug.Log($"[Endless] Regenerated current round pattern. currentRound={currentRound}");
    }

    private void RandomizeItemAssignments()
    {
        // Ensure dictionary is initialized before use
        if (randomizedItemAssignments == null)
        {
            randomizedItemAssignments = new Dictionary<int, int>();
        }
        else
        {
            randomizedItemAssignments.Clear(); // Clear existing assignments before refilling
        }

        // ✅ FIX: Log properly before assignments
        Debug.Log($"[RoundManager] Randomizing Assignments BEFORE: {(randomizedItemAssignments.Count > 0 ? string.Join(", ", randomizedItemAssignments.Select(kvp => $"{kvp.Key}: {kvp.Value}")) : "EMPTY")}");

        List<int> itemTypes = new List<int> { 1, 2, 3, 4 }; // 1 = Crucifix, 2 = Rosary, etc.
        List<int> squareTypes = new List<int> { 1, 2, 3, 4 };

        // Shuffle item types to ensure randomness
        itemTypes = itemTypes.OrderBy(x => Random.value).ToList();

        for (int i = 0; i < squareTypes.Count; i++)
        {
            randomizedItemAssignments[squareTypes[i]] = itemTypes[i]; // Assign shuffled items to square types
        }

        // 🚨 NEW CHECK: Ensure dictionary isn't empty after assignment
        if (randomizedItemAssignments.Count == 0)
        {
            Debug.LogError("[RoundManager] ERROR: randomizedItemAssignments is EMPTY after assignment!");
        }

        Debug.Log("=== PERMANENT ITEM ASSIGNMENTS ===");
        foreach (var kvp in randomizedItemAssignments)
        {
            string itemName = GetItemNameByType(kvp.Value);
            Debug.Log($"SquareType {kvp.Key} -> {itemName} (ItemType {kvp.Value})");
        }

        // 🚨 NEW FINAL CHECK: Log after assignments
        Debug.Log($"[RoundManager] Randomizing Assignments AFTER: {string.Join(", ", randomizedItemAssignments.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
    }

    public void ResetReadyForRoundFlag()
    {
        readyForNextRound = false;
    }

    private string GetItemNameByType(int itemType)
    {
        switch (itemType)
        {
            case 1: return "Crucifix";
            case 2: return "Rosary Beads";
            case 3: return "Bible";
            case 4: return "Candle";
            default: return "Unknown";
        }
    }

    // PlayMaker/debug friendly: public wrapper for item type -> display name.
    public string GetItemDisplayNameByItemType(int itemType)
    {
        return GetItemNameByType(itemType);
    }

    // Debug friendly: square type -> correct item name (based on current randomized assignments).
    public string GetCorrectItemDisplayNameForSquareType(int squareType)
    {
        int itemType = GetCorrectItemForSquareType(squareType);
        return GetItemNameByType(itemType);
    }

    public int GetCorrectItemForSquareType(int squareType)
    {
        if (randomizedItemAssignments != null && randomizedItemAssignments.TryGetValue(squareType, out int mappedItemType))
        {
            Debug.Log($"[RoundManager] Fetching correct type for SquareType {squareType} -> {mappedItemType}");
            return mappedItemType;
        }

        Debug.LogWarning($"[RoundManager] No correct item mapping found for SquareType {squareType}. Returning -1.");
        return -1;
    }

    private Dictionary<int, (GameObject, int)> ConvertPlacedObjects(Dictionary<Vector2Int, GameObject> placedObjects)
    {
        Dictionary<int, (GameObject, int)> convertedPlacedObjects = new Dictionary<int, (GameObject, int)>();

        foreach (var kvp in placedObjects)
        {
            int index = kvp.Key.y * gridSize + kvp.Key.x;
            var item = kvp.Value.GetComponent<Item>();
            if (item != null)
            {
                convertedPlacedObjects[index] = (kvp.Value, item.GetItemType());
            }
            else
            {
                Debug.LogError($"Item component missing on placed object at index {index}");
            }
        }

        return convertedPlacedObjects;
    }

    public void SetRound(int roundNumber)
    {
        currentRound = roundNumber;

        // Update the grid for the new round
        gridManager.GenerateRandomSquares(currentRound);
        gridManager.RecordRoundSelections(currentRound);
    }

    public Dictionary<int, int> GetItemAssignments()
    {
        if (randomizedItemAssignments == null || randomizedItemAssignments.Count == 0)
        {
            Debug.LogWarning("RoundManager: Item assignments not yet initialized!");
        }
        return randomizedItemAssignments;
    }
}
