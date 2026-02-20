using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ObjectPlacer : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject[] itemPrefabs; // Array of item prefabs (Item1, Item2, etc.)
    [SerializeField] private GameObject reticlePrefab; // Prefab of the reticle object

    [Header("Grid Settings")]
    [SerializeField] private float squareSize = 1f; // Match the grid square size
    [SerializeField] private int gridSize = 5; // Size of the grid
    [SerializeField] private Vector3 gridOffset = Vector3.zero; // Offset for the entire grid

    [Header("Audio Settings")]
    [SerializeField] private AudioClip[] placeSounds; // Array of placement sounds
    [SerializeField] private AudioClip[] removeSounds; // Array of removal sounds
    [SerializeField] private AudioSource audioSource; // AudioSource for playback

    [Header("UI Settings")]
    [SerializeField] private Image selectedItemIcon; // UI Image to update selected item icon
    [SerializeField] private Sprite[] itemIcons; // Array of item icons corresponding to item types

    [Header("Reticle Settings")]
    [SerializeField] private float reticleLerpSpeed = 10f; // Speed of interpolation
    //[SerializeField] private float positionThreshold = 0.1f; // Minimum movement before updating target
    private Vector3 targetReticlePosition; // Store target position
    [SerializeField] private HeldItemManager heldItemManager; // Assign in Inspector
    public bool canPlaceObjects = true;

    private int selectedItemType = 0; // Current item type the player is placing
    private int centralSquare;
    private GameObject reticleInstance;
    private Dictionary<int, (GameObject, int)> placedObjects = new Dictionary<int, (GameObject, int)>();
    public float maxRaycastDistance = 10f;
    private Vector2Int lastLockedGridPosition = new Vector2Int(int.MinValue, int.MinValue);
    //private float lockDuration = 0.1f; // Stay in the same square for at least 0.1s before switching
    //private float lockTimer = 0f;

    public static ObjectPlacer Instance { get; private set; }
    public bool releaseButtons = false;
    public bool tutHasGrabbedItem = false;
    [SerializeField] private SettingsMenuManager settingsMenuManager;
    [SerializeField] private GridManager gridManager;

    private bool ShouldIgnoreSquareRestrictions()
    {
        return gridManager != null && gridManager.IsEndlessModeActive;
    }

    [Header("Debug (Optional)")]
    [Tooltip("If enabled, allows scripts/UI buttons to programmatically place items for testing (e.g., Endless debug tools).")]
    [SerializeField] private bool debugAllowProgrammaticPlacement = false;

    public bool DebugAllowProgrammaticPlacement
    {
        get => debugAllowProgrammaticPlacement;
        set => debugAllowProgrammaticPlacement = value;
    }

    public void Debug_SetAllowProgrammaticPlacement(bool enabled)
    {
        debugAllowProgrammaticPlacement = enabled;
    }

    public void Debug_ClearAllPlacements(bool destroyLockedItems)
    {
        if (!debugAllowProgrammaticPlacement)
        {
            Debug.LogWarning("[ObjectPlacer][Debug] Programmatic placement is disabled. Enable DebugAllowProgrammaticPlacement to use this.");
            return;
        }

        ClearAllPlacedObjects(destroyLockedItems);
        Debug.Log($"[ObjectPlacer][Debug] Cleared all placements. destroyLockedItems={(destroyLockedItems ? 1 : 0)}");
    }

    public bool Debug_PlaceItemAtIndex(int squareIndex, int itemType, bool replaceExisting)
    {
        if (!debugAllowProgrammaticPlacement)
        {
            Debug.LogWarning("[ObjectPlacer][Debug] Programmatic placement is disabled. Enable DebugAllowProgrammaticPlacement to use this.");
            return false;
        }

        if (itemType <= 0 || itemPrefabs == null || itemType > itemPrefabs.Length)
        {
            Debug.LogError($"[ObjectPlacer][Debug] Invalid itemType={itemType}. Prefabs length={(itemPrefabs != null ? itemPrefabs.Length : 0)}");
            return false;
        }

        if (squareIndex < 0 || squareIndex >= gridSize * gridSize)
        {
            Debug.LogError($"[ObjectPlacer][Debug] Invalid squareIndex={squareIndex} for gridSize={gridSize}");
            return false;
        }

        centralSquare = (gridSize * gridSize) / 2;
        if (squareIndex == centralSquare)
        {
            Debug.LogWarning("[ObjectPlacer][Debug] Refusing to place on center square.");
            return false;
        }

        if (placedObjects.TryGetValue(squareIndex, out var existing))
        {
            if (!replaceExisting)
            {
                Debug.Log($"[ObjectPlacer][Debug] Placement already exists at index={squareIndex}; replaceExisting=0");
                return false;
            }

            if (existing.Item1 != null) Destroy(existing.Item1);
            placedObjects.Remove(squareIndex);
        }

        int x = squareIndex % gridSize;
        int y = squareIndex / gridSize;

        float snappedX = (x * squareSize) + (squareSize / 2f) + gridOffset.x;
        float snappedZ = (y * squareSize) + (squareSize / 2f) + gridOffset.z;
        Vector3 position = new Vector3(snappedX, 0.1f, snappedZ);

        Quaternion rotation = Quaternion.identity;
        if (Camera.main != null)
        {
            rotation = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f);
        }

        GameObject placedObject = Instantiate(itemPrefabs[itemType - 1], position, rotation);
        placedObjects[squareIndex] = (placedObject, itemType);

        var item = placedObject.GetComponent<Item>();
        if (item != null)
        {
            item.SetType(itemType);
        }
        else
        {
            Debug.LogError("[ObjectPlacer][Debug] Placed object does not have an Item component!");
        }

        Debug.Log($"[ObjectPlacer][Debug] Placed itemType={itemType} at squareIndex={squareIndex} (grid {x},{y}).");
        return true;
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        centralSquare = (gridSize * gridSize) / 2;

        if (gridManager == null)
        {
            gridManager = Object.FindFirstObjectByType<GridManager>();
        }

        // Initialize reticle
        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.SetActive(false);
        }
        else
        {
            Debug.LogError("Reticle Prefab is not assigned.");
        }

        // Initialize UI
        if (selectedItemIcon != null)
        {
            UpdateSelectedItemUI();
        }

        // Ensure AudioSource exists
        if (audioSource == null)
        {
            Debug.LogError("AudioSource is not assigned!");
        }
    }

    void Update()
    {
        if (canPlaceObjects)
        {
            if (heldItemManager != null)
            {
                heldItemManager.ShowHeldItem(); // Bring item back when placement is allowed
            }

            UpdateReticle();

            if (!releaseButtons)
            {
                if (Input.GetKeyDown(KeyCode.R)) PlaceObject();
                if (Input.GetKeyDown(KeyCode.T)) RemoveObject();
            }
            else
            {
                if (Input.GetMouseButtonDown(0)) PlaceObject(); // Left mouse button
                if (Input.GetMouseButtonDown(1)) RemoveObject(); // Right mouse button
            }
        }
        else
        {
            if (heldItemManager != null)
            {
                heldItemManager.HideHeldItem(); // Hide item when placement is disabled
            }
        }
    }

    void SetSelectedItem(int itemType)
    {
        if (itemType > 0 && itemType <= itemPrefabs.Length)
        {
            selectedItemType = itemType;
            UpdateSelectedItemUI();
            Debug.Log($"Selected item type: {selectedItemType}");
            SelectItem(itemType);
        }
    }

    public void SelectItemFromUI(int itemType)
    {
        SetSelectedItem(itemType);

        if (!tutHasGrabbedItem)
        {
            tutHasGrabbedItem = true;
        }
    }

    void UpdateSelectedItemUI()
    {
        if (selectedItemIcon != null && selectedItemType > 0 && selectedItemType <= itemIcons.Length)
        {
            selectedItemIcon.sprite = itemIcons[selectedItemType - 1];
        }
    }

    void UpdateReticle()
    {
        int layerMask = LayerMask.GetMask("Square_Layer");
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);

        // Ensure reticle starts inactive for this frame if no valid hit is found
        bool foundValidSquare = false;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, layerMask, QueryTriggerInteraction.Collide))
        {
            SquareTrigger square = hit.collider.GetComponent<SquareTrigger>();

            if (square != null)
            {
                Vector2Int newGridPosition = square.gridPosition;
                int index = newGridPosition.y * gridSize + newGridPosition.x;

                if (gridManager != null)
                {
                    bool allowed = ShouldIgnoreSquareRestrictions()
                        ? gridManager.IsSquareAllowedThisRound_IgnoreRestrictions(index)
                        : gridManager.IsSquareAllowedThisRound(index);

                    if (!allowed)
                    {
                        foundValidSquare = false;
                        reticleInstance.SetActive(false);
                        return;
                    }
                }

                Vector3 snappedPosition = square.transform.position + Vector3.up * 0.1f;

                // Always update position and activate the reticle when we have a valid square
                foundValidSquare = true;
                lastLockedGridPosition = newGridPosition;
                targetReticlePosition = snappedPosition;

                // Move reticle instantly for perimeter squares to eliminate lag
                if (IsPerimeterSquare(newGridPosition))
                {
                    reticleInstance.transform.position = snappedPosition;
                }
            }
        }

        // Update reticle visibility based on whether we found a valid square
        reticleInstance.SetActive(foundValidSquare);

        // Only perform lerp if the reticle is active
        if (reticleInstance.activeSelf)
        {
            // Use a higher lerp speed for smoother movement
            float currentSpeed = reticleLerpSpeed;
            reticleInstance.transform.position = Vector3.Lerp(
                reticleInstance.transform.position,
                targetReticlePosition,
                Time.deltaTime * currentSpeed
            );
        }
    }

    // Helper method to check if a grid position is on the perimeter
    private bool IsPerimeterSquare(Vector2Int pos)
    {
        return pos.x == 0 || pos.y == 0 || pos.x == gridSize - 1 || pos.y == gridSize - 1;
    }

    public void SelectItem(int itemType)
    {
        //Debug.Log($"Selecting ttteeeeest Item {itemType}");
        if (heldItemManager != null)
        {
            heldItemManager.SwapHeldItem(itemType);
        }
        else
        {
            //Debug.LogError("HeldItemManager reference is missing!");
        }
    }

    void PlaceObject()
    {
        if (settingsMenuManager.settingsMenu.activeSelf) return;

        if (selectedItemType == 0) // Prevent placing if no item is selected
        {
            //Debug.Log("No item selected. Pick an item first!");
            return;
        }

        if (!reticleInstance.activeSelf) return; // Prevent placement if the reticle is inactive
        if (!reticleInstance.activeSelf) return; // Prevent placement if the reticle is inactive

        Vector3 snappedPosition = targetReticlePosition; // Use the reticle's position directly
        Vector2Int gridPosition = lastLockedGridPosition; // Use the last locked grid position
        int index = gridPosition.y * gridSize + gridPosition.x;

        if (gridManager != null)
        {
            bool allowed = ShouldIgnoreSquareRestrictions()
                ? gridManager.IsSquareAllowedThisRound_IgnoreRestrictions(index)
                : gridManager.IsSquareAllowedThisRound(index);

            if (!allowed)
            {
                Debug.Log("Invalid placement: square is off-limits.");
                return;
            }
        }

        if (heldItemManager != null)
        {
            heldItemManager.AnimateItemHolder(); // Play the animation without swapping items
        }

        if (!IsWithinBounds(gridPosition) || index == centralSquare || placedObjects.ContainsKey(index))
        {
            Debug.Log("Invalid placement.");
            return;
        }

        // Instantiate the selected item prefab at the reticle's position
        GameObject placedObject = Instantiate(itemPrefabs[selectedItemType - 1], snappedPosition, Quaternion.identity);
        placedObject.transform.rotation = Quaternion.Euler(0, Camera.main.transform.eulerAngles.y, 0); // Apply player Y rotation


        // Store the placed object in the dictionary
        placedObjects[index] = (placedObject, selectedItemType);

        // Ensure the placed object has a valid item component
        var item = placedObject.GetComponent<Item>();
        if (item != null)
        {
            item.SetType(selectedItemType);
        }
        else
        {
            Debug.LogError("Placed object does not have an Item component!");
        }

        //Debug.Log($"Placed item at index {index} - GridPos: {gridPosition}, Snapped Pos: {snappedPosition}");

        // Play placement sound if available
        if (audioSource != null && placeSounds.Length >= selectedItemType)
        {
            audioSource.PlayOneShot(placeSounds[selectedItemType - 1]);
        }

        Invoke(nameof(DelayedHighlightCheck), 0.05f);

    }

    void DelayedHighlightCheck()
    {
        PlayerRaycastHighlight playerHighlight = Camera.main.GetComponent<PlayerRaycastHighlight>();
        if (playerHighlight != null)
        {
            playerHighlight.ForceHighlightCheck();
        }
    }

    void RemoveObject()
    {
        if (settingsMenuManager.settingsMenu.activeSelf) return;
        if (!reticleInstance.activeSelf) return; // Prevent removal if reticle is inactive

        Vector2Int gridPosition = lastLockedGridPosition; // Use the reticle's locked position
        int index = gridPosition.y * gridSize + gridPosition.x;

        if (heldItemManager != null)
        {
            heldItemManager.AnimateItemHolder(); // Play the animation without swapping items
        }

        if (placedObjects.TryGetValue(index, out var placedObject))
        {
            var itemComponent = placedObject.Item1.GetComponent<Item>();
            if (itemComponent != null && itemComponent.IsLocked())
            {
                Debug.Log($"Cannot interact with locked item at {gridPosition}.");
                return;
            }

            Destroy(placedObject.Item1);
            placedObjects.Remove(index);

            // Play removal sound
            if (audioSource != null && removeSounds.Length >= placedObject.Item2)
            {
                audioSource.PlayOneShot(removeSounds[placedObject.Item2 - 1]);
            }

            Debug.Log($"Removed object at {gridPosition}");
        }
    }


    private Vector3 SnapToGrid(Vector3 position)
    {
        float snappedX = Mathf.Floor((position.x - gridOffset.x) / squareSize) * squareSize + (squareSize / 2) + gridOffset.x;
        float snappedZ = Mathf.Floor((position.z - gridOffset.z) / squareSize) * squareSize + (squareSize / 2) + gridOffset.z;
        return new Vector3(snappedX, 0.1f, snappedZ);
    }

    public Vector2Int GetGridPosition(Vector3 position)
    {
        int gridX = Mathf.FloorToInt((position.x - gridOffset.x) / squareSize);
        int gridZ = Mathf.FloorToInt((position.z - gridOffset.z) / squareSize);
        return new Vector2Int(gridX, gridZ);
    }

    private bool IsWithinBounds(Vector2Int position)
    {
        return position.x >= 0 && position.x < gridSize && position.y >= 0 && position.y < gridSize;
    }

    public Dictionary<Vector2Int, GameObject> GetPlacedObjects()
    {
        Dictionary<Vector2Int, GameObject> result = new Dictionary<Vector2Int, GameObject>();
        foreach (var kvp in placedObjects)
        {
            int index = kvp.Key;
            Vector2Int gridPos = new Vector2Int(index % gridSize, index / gridSize);
            result[gridPos] = kvp.Value.Item1;
        }
        return result;
    }

    public bool TryGetPlacedObjectAtIndex(int squareIndex, out GameObject placedObject, out int itemType)
    {
        placedObject = null;
        itemType = 0;

        if (placedObjects == null) return false;
        if (!placedObjects.TryGetValue(squareIndex, out var value)) return false;

        placedObject = value.Item1;
        itemType = value.Item2;
        return placedObject != null;
    }

    // --- Endless hooks (PlayMaker-friendly) ---
    // Clears ALL current placements. Intended for Endless mode so placements disappear each successful round.
    public void ClearAllPlacedObjects_Endless()
    {
        ClearAllPlacedObjects(true);
    }

    public void ClearAllPlacedObjects(bool destroyLockedItems)
    {
        if (placedObjects == null || placedObjects.Count == 0) return;

        List<int> keys = new List<int>(placedObjects.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            int index = keys[i];
            if (!placedObjects.TryGetValue(index, out var entry)) continue;

            GameObject obj = entry.Item1;
            if (obj == null)
            {
                placedObjects.Remove(index);
                continue;
            }

            if (!destroyLockedItems)
            {
                var item = obj.GetComponent<Item>();
                if (item != null && item.IsLocked())
                {
                    continue;
                }
            }

            Destroy(obj);
            placedObjects.Remove(index);
        }
    }
}
