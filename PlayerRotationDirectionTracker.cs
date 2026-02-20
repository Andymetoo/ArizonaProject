using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class PlayerRotationDirectionTracker : MonoBehaviour
{
    [Header("References")]
    public Transform playerTransform;
    public Transform cameraTransform;
    public Image fovConeImage;
    public Image fovConeGraphicImage;
    public Image gridImage;
    public SimpleFirstPersonController playerController; // ✅ Reference to get isInPrayerMode

    [Header("Fade Settings")]
    public float fadeDuration = 0.5f;
    public float visibleTime = 2f;

    [Header("Grid Alpha Range")]
    [Range(0f, 1f)] public float minGridAlpha = 0.2f;
    [Range(0f, 1f)] public float maxGridAlpha = 0.5f;

    [Header("Turn Sensitivity")]
    public float sensitivityThreshold = 0.5f;

    [Header("Rotation Tracking (For Shadow Minigame)")]
    [Tooltip("If enabled, this component will always track yaw turn direction each frame (even outside prayer mode), so PlayMaker can reliably read turn direction during the shadow minigame.")]
    public bool enableRotationTracking = true;

    [Tooltip("Optional override. If unset, defaults to playerTransform (if set) otherwise cameraTransform.")]
    public Transform yawTrackingTransform;

    [Tooltip("Minimum absolute yaw speed (deg/sec) required to count as a deliberate turn.")]
    public float minYawSpeedDegPerSec = 25f;

    [Tooltip("How long (seconds) the player must keep turning TOWARD the ghost side before 'toward confirmed' becomes true. This provides leeway so tiny micro-movements don't instantly kill.")]
    public float towardConfirmTime = 0.15f;

    [Tooltip("How long (seconds) the player must keep turning AWAY from the ghost side before 'away confirmed' becomes true.")]
    public float awayConfirmTime = 0.05f;

    [Tooltip("Ghost approach side: -1 = Left, +1 = Right, 0 = None. Typically set by your PlayMaker FSM when it picks left/right.")]
    public int ghostApproachSide = 0;

    [Header("Rotation Tracking Output (Read Only)")]
    [Tooltip("Yaw delta (degrees) used for tracking this frame.")]
    public float yawDeltaDeg;

    [Tooltip("Signed yaw speed (deg/sec): positive means turning RIGHT, negative means turning LEFT.")]
    public float yawSpeedDegPerSec;

    [Tooltip("Turn direction: -1 = Left, +1 = Right, 0 = Not turning (under threshold).")]
    public int yawTurnDirection;

    public bool isTurningLeft;
    public bool isTurningRight;

    [Tooltip("True while currently turning away from the ghost side (above speed threshold).")]
    public bool isTurningAwayFromGhost;

    [Tooltip("True while currently turning toward the ghost side (above speed threshold).")]
    public bool isTurningTowardGhost;

    [Tooltip("Seconds accumulated turning away (decays when not turning away).")]
    public float turningAwayTime;

    [Tooltip("Seconds accumulated turning toward (decays when not turning toward).")]
    public float turningTowardTime;

    [Tooltip("True if turning away has been sustained long enough to be 'confirmed'.")]
    public bool turningAwayConfirmed;

    [Tooltip("True if turning toward has been sustained long enough to be 'confirmed'.")]
    public bool turningTowardConfirmed;

    // PlayMaker-friendly properties (Get Property actions typically enumerate properties, not fields).
    public int GhostApproachSide
    {
        get => ghostApproachSide;
        set => ghostApproachSide = Mathf.Clamp(value, -1, 1);
    }

    public float YawDeltaDeg => yawDeltaDeg;
    public float YawSpeedDegPerSec => yawSpeedDegPerSec;
    public int YawTurnDirection => yawTurnDirection;
    public bool IsTurningLeft => isTurningLeft;
    public bool IsTurningRight => isTurningRight;
    public bool IsTurningAwayFromGhost => isTurningAwayFromGhost;
    public bool IsTurningTowardGhost => isTurningTowardGhost;
    public float TurningAwayTime => turningAwayTime;
    public float TurningTowardTime => turningTowardTime;
    public bool TurningAwayConfirmed => turningAwayConfirmed;
    public bool TurningTowardConfirmed => turningTowardConfirmed;

    // --- PlayMaker helpers ---
    public void SetGhostApproachLeft()
    {
        ghostApproachSide = -1;
    }

    public void SetGhostApproachRight()
    {
        ghostApproachSide = 1;
    }

    public void ClearGhostApproachSide()
    {
        ghostApproachSide = 0;
    }

    public void ResetTurnTracking()
    {
        turningAwayTime = 0f;
        turningTowardTime = 0f;
        turningAwayConfirmed = false;
        turningTowardConfirmed = false;
        yawTurnDirection = 0;
        isTurningLeft = false;
        isTurningRight = false;
        isTurningAwayFromGhost = false;
        isTurningTowardGhost = false;

        if (enableRotationTracking && yawTrackingTransform != null)
        {
            previousYawTrackingRotation = yawTrackingTransform.eulerAngles.y;
        }
    }

    [Header("Debug")]
    [Tooltip("Force the minimap visible even when not in prayer mode (useful to debug overlays).")]
    public bool debugForceMinimapVisible = false;

    [Header("Feedback Graphics")]
    public Image correctGraphic;
    public Image wrongItemGraphic;
    public Image wrongSquareGraphic;
    [Tooltip("Non-spoiler 'missed ritual' feedback. Place this graphic centered (do not reposition to a square).")]
    public Image missedRitualGraphic;

    [Header("Feedback Text (Optional)")]
    [Tooltip("Optional text that fades with feedback graphics.")]
    public TMP_Text feedbackText;
    public string correctText = "Correct";
    public string wrongSquareText = "Wrong square";
    public string missedRitualText = "Missed ritual";
    public string wrongItemText = "Wrong item";

    [Header("Wrong Item Sprites (Optional)")]
    [Tooltip("Optional: sprite to show for the item you placed incorrectly. Index 0 = itemType 1.")]
    public Sprite[] wrongItemTypeSprites;

    [Header("World References (Optional)")]
    [Tooltip("If assigned, used to distinguish missing-placement vs wrong-item-on-right-square.")]
    [SerializeField] private ObjectPlacer objectPlacer;
    public Transform[] gridPositions = new Transform[25]; // 0-24 for 5x5 grid
    public Image[] disabledSquareOverlays = new Image[25];
    [Tooltip("Optional: if set, disabledSquareOverlays will be auto-created from this prefab (one per grid position).")]
    public Image disabledSquareOverlayPrefab;
    [Tooltip("Optional parent for auto-created disabled overlays. If null, uses the prefab's parent or this object's transform.")]
    public Transform disabledSquareOverlayParent;
    [Tooltip("If true and disabledSquareOverlayPrefab is set, missing overlay entries will be instantiated automatically.")]
    public bool autoCreateDisabledSquareOverlays = true;
    public float feedbackDuration = 2f;
    [Range(0f, 1f)] public float maxFeedbackAlpha = 1f; // ✅ Max alpha for feedback graphics

    private float previousYRotation;
    private float previousYawTrackingRotation;
    private float lastTurnTime;
    private Coroutine fadeRoutine;
    private Coroutine feedbackRoutine;
    private bool isFeedbackActive = false; // ✅ Track if feedback is currently showing

    void Start()
    {
        if (playerTransform == null)
            playerTransform = transform;

        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        if (playerController == null)
            playerController = FindObjectOfType<SimpleFirstPersonController>();

        previousYRotation = cameraTransform.eulerAngles.y;

        if (yawTrackingTransform == null)
        {
            yawTrackingTransform = playerTransform != null ? playerTransform : cameraTransform;
        }
        if (yawTrackingTransform != null)
        {
            previousYawTrackingRotation = yawTrackingTransform.eulerAngles.y;
        }

        SetAlpha(fovConeGraphicImage, 0f);
        SetAlpha(gridImage, minGridAlpha);
        
        // ✅ Initialize feedback graphics to alpha 0
        SetAlpha(correctGraphic, 0f);
        SetAlpha(wrongItemGraphic, 0f);
        SetAlpha(wrongSquareGraphic, 0f);
        SetAlpha(missedRitualGraphic, 0f);
        SetTMPAlpha(feedbackText, 0f);

        EnsureDisabledOverlays();

        SetDisabledOverlaysAlpha(0f);
    }

    private void EnsureDisabledOverlays()
    {
        if (gridPositions == null || gridPositions.Length == 0) return;

        if (disabledSquareOverlays == null || disabledSquareOverlays.Length != gridPositions.Length)
        {
            disabledSquareOverlays = new Image[gridPositions.Length];
        }

        if (!autoCreateDisabledSquareOverlays || disabledSquareOverlayPrefab == null) return;

        Transform parent = disabledSquareOverlayParent;
        if (parent == null)
        {
            parent = disabledSquareOverlayPrefab.transform.parent;
        }
        if (parent == null)
        {
            parent = transform;
        }

        for (int i = 0; i < gridPositions.Length; i++)
        {
            if (disabledSquareOverlays[i] != null) continue;
            if (gridPositions[i] == null) continue;

            Image overlay = Instantiate(disabledSquareOverlayPrefab, parent);
            overlay.name = $"DisabledSquareOverlay_{i:00}";
            overlay.raycastTarget = false;
            overlay.gameObject.SetActive(false);
            overlay.rectTransform.position = gridPositions[i].position;
            overlay.rectTransform.rotation = gridPositions[i].rotation;
            disabledSquareOverlays[i] = overlay;
        }
    }

    void Update()
    {
        // Always track yaw direction first (independent of prayer/minimap UI).
        if (enableRotationTracking && yawTrackingTransform != null)
        {
            float currentYaw = yawTrackingTransform.eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) dt = 0.0001f;

            yawDeltaDeg = Mathf.DeltaAngle(previousYawTrackingRotation, currentYaw);
            previousYawTrackingRotation = currentYaw;

            yawSpeedDegPerSec = yawDeltaDeg / dt;

            float absSpeed = Mathf.Abs(yawSpeedDegPerSec);
            if (absSpeed >= minYawSpeedDegPerSec)
            {
                yawTurnDirection = yawSpeedDegPerSec > 0f ? 1 : -1;
            }
            else
            {
                yawTurnDirection = 0;
            }

            isTurningLeft = yawTurnDirection < 0;
            isTurningRight = yawTurnDirection > 0;

            // ghostApproachSide: -1 = Left, +1 = Right.
            // Turning away means turning opposite that side.
            isTurningAwayFromGhost = false;
            isTurningTowardGhost = false;
            if (ghostApproachSide == -1)
            {
                // Ghost on LEFT: away = RIGHT (+1), toward = LEFT (-1)
                isTurningAwayFromGhost = yawTurnDirection == 1;
                isTurningTowardGhost = yawTurnDirection == -1;
            }
            else if (ghostApproachSide == 1)
            {
                // Ghost on RIGHT: away = LEFT (-1), toward = RIGHT (+1)
                isTurningAwayFromGhost = yawTurnDirection == -1;
                isTurningTowardGhost = yawTurnDirection == 1;
            }

            // Accumulate timers with gentle decay when not actively turning that way.
            const float decayRate = 4f; // seconds of decay per second
            if (isTurningAwayFromGhost)
                turningAwayTime += dt;
            else
                turningAwayTime = Mathf.Max(0f, turningAwayTime - dt * decayRate);

            if (isTurningTowardGhost)
                turningTowardTime += dt;
            else
                turningTowardTime = Mathf.Max(0f, turningTowardTime - dt * decayRate);

            turningAwayConfirmed = turningAwayTime >= awayConfirmTime;
            turningTowardConfirmed = turningTowardTime >= towardConfirmTime;
        }

        // ❌ Don’t update unless in prayer mode
        if (!debugForceMinimapVisible && (playerController == null || !playerController.isInPrayerMode))
        {
            if (fovConeGraphicImage != null)
                SetAlpha(fovConeGraphicImage, 0f);
            if (gridImage != null && !isFeedbackActive) // ✅ Don't override grid during feedback
                SetAlpha(gridImage, 0f);

            SetDisabledOverlaysAlpha(0f);
            return;
        }

        float currentYRotation = cameraTransform.eulerAngles.y;
        float delta = Mathf.DeltaAngle(previousYRotation, currentYRotation);
        previousYRotation = currentYRotation;

        // Rotate the FOV cone pivot based on camera rotation
        if (fovConeImage != null)
        {
            float mappedRotation = (90f - currentYRotation) % 360f;
            fovConeImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, mappedRotation);
        }

        // ✅ Don't interfere with grid during feedback
        if (!isFeedbackActive)
        {
            if (Mathf.Abs(delta) > sensitivityThreshold)
            {
                lastTurnTime = Time.time;

                if (fadeRoutine != null)
                    StopCoroutine(fadeRoutine);

                fadeRoutine = StartCoroutine(FadeRoutine(true));

            }
            else if (Time.time > lastTurnTime + visibleTime)
            {
                if (fadeRoutine != null)
                    StopCoroutine(fadeRoutine);

                fadeRoutine = StartCoroutine(FadeRoutine(false));
            }
        }
    }

    private IEnumerator FadeRoutine(bool fadeIn)
    {
        float targetAlpha = fadeIn ? maxGridAlpha : minGridAlpha;

        float startFov = fovConeGraphicImage != null ? fovConeGraphicImage.color.a : 0f;
        float startGrid = gridImage != null ? gridImage.color.a : 0f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;

            float newAlpha = Mathf.Lerp(startFov, targetAlpha, t);

            if (fovConeGraphicImage != null)
                SetAlpha(fovConeGraphicImage, newAlpha);
            if (gridImage != null)
                SetAlpha(gridImage, newAlpha);

            SetDisabledOverlaysAlpha(newAlpha);

            yield return null;
        }

        SetAlpha(fovConeGraphicImage, targetAlpha);
        SetAlpha(gridImage, targetAlpha);
        SetDisabledOverlaysAlpha(targetAlpha);
        fadeRoutine = null;
    }


    private void SetAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = alpha;
        img.color = c;
    }

    private void SetDisabledOverlaysAlpha(float alpha)
    {
        if (disabledSquareOverlays == null) return;

        for (int i = 0; i < disabledSquareOverlays.Length; i++)
        {
            SetAlpha(disabledSquareOverlays[i], alpha);
        }
    }

    public void SetDisabledSquares(bool[] disabledMask)
    {
        if (disabledMask == null)
        {
            Debug.LogError("SetDisabledSquares called with null mask.");
            return;
        }

        if (disabledMask.Length != gridPositions.Length)
        {
            Debug.LogError($"SetDisabledSquares mask length mismatch. Expected {gridPositions.Length}, got {disabledMask.Length}.");
            return;
        }

        EnsureDisabledOverlays();

        if (disabledSquareOverlays == null || disabledSquareOverlays.Length != gridPositions.Length)
        {
            Debug.LogError($"disabledSquareOverlays must be assigned with {gridPositions.Length} entries, or provide disabledSquareOverlayPrefab and enable autoCreateDisabledSquareOverlays.");
            return;
        }

        for (int i = 0; i < disabledMask.Length; i++)
        {
            Image overlay = disabledSquareOverlays[i];
            if (overlay == null) continue;

            Transform targetPosition = gridPositions[i];
            if (targetPosition != null)
            {
                overlay.rectTransform.position = targetPosition.position;
                overlay.rectTransform.rotation = targetPosition.rotation;
            }
            overlay.gameObject.SetActive(disabledMask[i]);
        }
    }

    // ✅ The three methods for feedback display - now accept squareIndex as parameter
    public void PulseCorrect(int squareIndex)
    {
        int resolvedIndex = ResolveLikelyCorrectSquareIndex(squareIndex);
        StartFeedbackAtSquare(correctGraphic, resolvedIndex);
        SetFeedbackText(correctText);
    }

    public void PulseWrongItem(int squareIndex)
    {
        // This event currently covers two cases:
        // 1) Player placed nothing on a chosen square (spoiler if we reveal squareIndex)
        // 2) Player placed the wrong item ON the correct square (ok to reveal squareIndex)
        // We distinguish by checking whether there's any placed object on that square.
        if (objectPlacer == null)
        {
            objectPlacer = FindObjectOfType<ObjectPlacer>();
        }

        bool hasPlacedItem = false;
        int placedItemType = 0;

        if (objectPlacer != null)
        {
            hasPlacedItem = objectPlacer.TryGetPlacedObjectAtIndex(squareIndex, out _, out placedItemType);
        }

        if (!hasPlacedItem)
        {
            // Non-spoiler: show center-screen feedback only.
            PulseMissedRitual();
            SetFeedbackText(missedRitualText);
            return;
        }

        // Reveal that this was a correct square, but wrong item.
        ApplyWrongItemSpriteForType(placedItemType);
        StartFeedbackAtSquare(wrongItemGraphic, squareIndex);
        SetFeedbackText(wrongItemText);
    }

    public void PulseWrongSquare(int squareIndex)
    {
        StartFeedbackAtSquare(wrongSquareGraphic, squareIndex);
        SetFeedbackText(wrongSquareText);
    }

    // ✅ Non-spoiler: does NOT reveal which square was missed.
    public void PulseMissedRitual()
    {
        StartFeedbackNoSquare(missedRitualGraphic);
        SetFeedbackText(missedRitualText);
    }

    private void StartFeedbackAtSquare(Image graphic, int squareIndex)
    {
        if (feedbackRoutine != null)
        {
            StopCoroutine(feedbackRoutine);
        }
        feedbackRoutine = StartCoroutine(ShowFeedbackGraphicAtSquare(graphic, squareIndex));
    }

    private void StartFeedbackNoSquare(Image graphic)
    {
        if (feedbackRoutine != null)
        {
            StopCoroutine(feedbackRoutine);
        }
        feedbackRoutine = StartCoroutine(ShowFeedbackGraphicNoSquare(graphic));
    }

    private void HideAllFeedbackGraphics()
    {
        SetAlpha(correctGraphic, 0f);
        SetAlpha(wrongItemGraphic, 0f);
        SetAlpha(wrongSquareGraphic, 0f);
        SetAlpha(missedRitualGraphic, 0f);
        SetTMPAlpha(feedbackText, 0f);
    }

    private void ApplyWrongItemSpriteForType(int placedItemType)
    {
        if (wrongItemGraphic == null) return;
        if (wrongItemTypeSprites == null || wrongItemTypeSprites.Length == 0) return;

        int idx = placedItemType - 1;
        if (idx < 0 || idx >= wrongItemTypeSprites.Length) return;
        if (wrongItemTypeSprites[idx] == null) return;

        wrongItemGraphic.sprite = wrongItemTypeSprites[idx];
        wrongItemGraphic.preserveAspect = true;
    }

    private int ResolveLikelyCorrectSquareIndex(int requestedSquareIndex)
    {
        if (IsSquareIndexInRange(requestedSquareIndex) && IsSelectedSquareIndex(requestedSquareIndex))
        {
            return requestedSquareIndex;
        }

        GridManager gridManager = FindObjectOfType<GridManager>();
        if (gridManager == null)
        {
            return requestedSquareIndex;
        }

        Dictionary<int, int> selectedSquaresWithTypes = null;
        try
        {
            selectedSquaresWithTypes = gridManager.GetSelectedSquaresWithTypes();
        }
        catch
        {
            selectedSquaresWithTypes = null;
        }

        if (selectedSquaresWithTypes == null || selectedSquaresWithTypes.Count == 0)
        {
            return requestedSquareIndex;
        }

        // Prefer a square that is both selected AND currently has the correct item type placed.
        Dictionary<int, (GameObject, int)> placed = null;
        try
        {
            placed = gridManager.GetPlacedObjects();
        }
        catch
        {
            placed = null;
        }

        if (placed != null && placed.Count > 0)
        {
            int matchCount = 0;
            int matchedIndex = requestedSquareIndex;

            foreach (var kvp in placed)
            {
                int idx = kvp.Key;
                int placedType = kvp.Value.Item2;
                if (!selectedSquaresWithTypes.TryGetValue(idx, out int requiredType)) continue;
                if (placedType != requiredType) continue;

                matchCount++;
                matchedIndex = idx;
                if (matchCount > 1) break;
            }

            if (matchCount == 1 && IsSquareIndexInRange(matchedIndex))
            {
                return matchedIndex;
            }
        }

        // If there is exactly one selected square, fall back to that.
        if (selectedSquaresWithTypes.Count == 1)
        {
            foreach (var kvp in selectedSquaresWithTypes)
            {
                if (IsSquareIndexInRange(kvp.Key))
                {
                    return kvp.Key;
                }
                break;
            }
        }

        return requestedSquareIndex;
    }

    private bool IsSelectedSquareIndex(int squareIndex)
    {
        GridManager gridManager = FindObjectOfType<GridManager>();
        if (gridManager == null) return false;

        Dictionary<int, int> selectedSquaresWithTypes = null;
        try
        {
            selectedSquaresWithTypes = gridManager.GetSelectedSquaresWithTypes();
        }
        catch
        {
            selectedSquaresWithTypes = null;
        }

        return selectedSquaresWithTypes != null && selectedSquaresWithTypes.ContainsKey(squareIndex);
    }

    private bool IsSquareIndexInRange(int squareIndex)
    {
        return squareIndex >= 0 && gridPositions != null && squareIndex < gridPositions.Length;
    }

    private void SetFeedbackText(string message)
    {
        if (feedbackText == null) return;
        feedbackText.text = message;
    }

    private void SetTMPAlpha(TMP_Text tmp, float alpha)
    {
        if (tmp == null) return;
        Color c = tmp.color;
        c.a = alpha;
        tmp.color = c;
    }

    private IEnumerator ShowFeedbackGraphicAtSquare(Image graphic, int squareIndex)
    {
        if (graphic == null || squareIndex < 0 || squareIndex >= gridPositions.Length)
        {
            Debug.LogError($"Invalid graphic or square index: {squareIndex}");
            yield break;
        }

        // ✅ Set feedback active to override normal grid behavior
        isFeedbackActive = true;

        // Stop any existing fade routine
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        HideAllFeedbackGraphics();

        // ✅ Force grid to max alpha immediately
        SetAlpha(fovConeGraphicImage, maxGridAlpha);
        SetAlpha(gridImage, maxGridAlpha);
        SetDisabledOverlaysAlpha(maxGridAlpha);

        // Position the graphic at the correct grid position
        Transform targetPosition = gridPositions[squareIndex];
        if (targetPosition != null)
        {
            graphic.rectTransform.position = targetPosition.position;
            graphic.rectTransform.rotation = targetPosition.rotation;
        }

        // Show the graphic (lerp in quickly)
        SetAlpha(graphic, 0f);
        SetTMPAlpha(feedbackText, 0f);
        float elapsed = 0f;
        float fadeInTime = 0.25f;

        while (elapsed < fadeInTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, maxFeedbackAlpha, elapsed / fadeInTime);
            SetAlpha(graphic, alpha);
            SetTMPAlpha(feedbackText, alpha);
            yield return null;
        }

        SetAlpha(graphic, maxFeedbackAlpha);
        SetTMPAlpha(feedbackText, maxFeedbackAlpha);

        // Keep visible for feedback duration
        yield return new WaitForSeconds(feedbackDuration);

        // Fade out the graphic
        elapsed = 0f;
        float fadeOutTime = 0.5f;

        while (elapsed < fadeOutTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(maxFeedbackAlpha, 0f, elapsed / fadeOutTime);
            SetAlpha(graphic, alpha);
            SetTMPAlpha(feedbackText, alpha);
            yield return null;
        }

        SetAlpha(graphic, 0f);
        SetTMPAlpha(feedbackText, 0f);

        // ✅ Re-enable normal grid behavior
        isFeedbackActive = false;
        feedbackRoutine = null;
    }

    private IEnumerator ShowFeedbackGraphicNoSquare(Image graphic)
    {
        if (graphic == null)
        {
            Debug.LogError("PulseMissedRitual called but missedRitualGraphic is not assigned.");
            yield break;
        }

        isFeedbackActive = true;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        HideAllFeedbackGraphics();

        SetAlpha(fovConeGraphicImage, maxGridAlpha);
        SetAlpha(gridImage, maxGridAlpha);
        SetDisabledOverlaysAlpha(maxGridAlpha);

        SetAlpha(graphic, 0f);
        SetTMPAlpha(feedbackText, 0f);
        float elapsed = 0f;
        float fadeInTime = 0.25f;

        while (elapsed < fadeInTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(0f, maxFeedbackAlpha, elapsed / fadeInTime);
            SetAlpha(graphic, alpha);
            SetTMPAlpha(feedbackText, alpha);
            yield return null;
        }

        SetAlpha(graphic, maxFeedbackAlpha);
        SetTMPAlpha(feedbackText, maxFeedbackAlpha);
        yield return new WaitForSeconds(feedbackDuration);

        elapsed = 0f;
        float fadeOutTime = 0.5f;
        while (elapsed < fadeOutTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(maxFeedbackAlpha, 0f, elapsed / fadeOutTime);
            SetAlpha(graphic, alpha);
            SetTMPAlpha(feedbackText, alpha);
            yield return null;
        }

        SetAlpha(graphic, 0f);
        SetTMPAlpha(feedbackText, 0f);
        isFeedbackActive = false;
        feedbackRoutine = null;
    }
}
