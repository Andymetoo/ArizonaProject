using UnityEngine;
using System.Collections;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
public class SimpleFirstPersonController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 2f;
    public Transform cameraTransform;
    public float horizontalLookLimit = 45f;
    public float verticalLookLimit = 30f;

    [Header("Footstep Settings")]
    public AudioClip[] hardwoodFootsteps;
    public AudioClip[] carpetFootsteps;

    [Header("Footstep Overlay Zones")]
    [Tooltip("If assigned, overlay (wet/hollow) footsteps will play on this AudioSource. If null, uses the main AudioSource.")]
    public AudioSource footstepOverlayAudioSource;

    [Tooltip("Additional random clips to play when inside a trigger tagged Player_Hollow.")]
    public AudioClip[] hollowOverlayFootsteps;

    [Tooltip("Optional particle puff to play when a hollow overlay footstep is triggered.")]
    public ParticleSystem hollowFootstepPuff;

    [Tooltip("How many particles to emit for the hollow footstep puff (one-shot).")]
    [Min(1)]
    public int hollowPuffEmitCount = 1;

    [Tooltip("Additional random clips to play when inside a trigger tagged Player_Wet.")]
    public AudioClip[] wetOverlayFootsteps;

    [Tooltip("Volume multiplier for overlay (wet/hollow) footsteps.")]
    [Range(0f, 1f)]
    public float footstepOverlayVolume = 1.0f;

    public float baseFootstepInterval = 0.5f;  // Default interval when moving at normal speed
    public float sprintMultiplier = 0.75f;      // Adjusts interval for faster movement
    public float footstepVolume = 1.0f;        // Control volume of footsteps

    private float footstepTimer;

    // Use overlap counters instead of booleans so stacked/overlapping triggers behave reliably.
    private int carpetZoneCount = 0;
    private int hollowZoneCount = 0;
    private int wetZoneCount = 0;

    private AudioSource audioSource;
    private Rigidbody rb;
    private float verticalLookRotation = 0f;
    private float horizontalLookOffset = 0f;
    public bool isInPrayerMode = false;
    private Quaternion initialCameraRotation;

    [Header("Failsafe Settings")]
    public float maxVelocityMagnitude = 10f; // Maximum allowed velocity
    public float velocityResetThreshold = 8f; // Threshold for when to apply emergency brake
    public float stuckDetectionTime = 0.5f;   // How long before considering player "stuck"

    [Header("Sticky Input Failsafe")]
    [Tooltip("If enabled: when no WASD keys are held but horizontal velocity persists (rare stuck-input feel), force a horizontal brake.")]
    public bool enableNoInputBrakeFailsafe = true;

    [Tooltip("Seconds with no WASD input before the failsafe can trigger.")]
    public float noInputBrakeDelaySeconds = 0.12f;

    [Tooltip("If horizontal speed (XZ) is above this with no input, the failsafe will brake.")]
    public float noInputBrakeSpeedThreshold = 0.25f;

    private float noInputTimer = 0f;

    private Vector3 lastPosition;
    private float stuckTimer;
    private bool wasMovementRequested;


    [Header("Camera Wander Settings")]
    public float wanderStrength = 0.3f;
    public float wanderSpeed = 0.1f;
    public bool enableWander = true;  // Toggle for wander effect

    private Vector3 wanderOffset;
    private float wanderTime;
    public bool isMenuOpen = false;

    [Header("Camera Butter Settings")]
    private Vector3 currentVelocity = Vector3.zero;
    public float acceleration = 10f;
    public float deceleration = 8f;
    public float rotationSmoothing = 12f;
    private Vector2 smoothMouseInput;
    private Vector2 currentMouseInput;

    [Header("Head Bob")]
    public float bobSpeed = 4f;
    public bool enableHeadBobbing = true;
    public float bobIntensity = 0.07f;  // Strength of the bob
    public float returnSpeed = 6f; // How fast the camera returns to normal position
    private float bobTimer = 0f;
    private Vector3 originalCameraPosition;
    private Vector2 currentPrayerMouseInput;
    private Vector2 smoothPrayerMouseInput;

    [Header("Look UI")]
    public Slider lookDirectionSlider;
    public CanvasGroup lookSliderCanvasGroup;
    public float sliderMinAlpha = 0.1f;
    public float sliderMaxAlpha = 1f;
    public float sliderFadeSpeed = 3f;

    // Internal state
    private float lastYaw = 0f;
    private float idleFadeTimer = 0f;
    private float fadeCooldown = 0.5f; // Time before fade starts
    [HideInInspector]
    public bool inputIsFrozen = false;

    [Header("Ceiling Pull Death")]
    [Tooltip("When triggered, the camera pitches down while the player is pulled upward into the ceiling.")]
    public bool isDying = false;

    [Tooltip("Set true when the ceiling-pull death animation has completed. PlayMaker can poll this to finish death wrapup.")]
    public bool isFinishedDying = false;

    [Tooltip("Seconds to complete the pull sequence.")]
    [Min(0.1f)] public float ceilingPullDuration = 1.35f;

    [Tooltip("How far upward (meters) the player is pulled over the duration.")]
    [Min(0.1f)] public float ceilingPullHeight = 3.0f;

    [Tooltip("Target pitch (degrees) for the camera to look downward during the pull.")]
    [Range(0f, 89f)] public float ceilingPullLookDownPitch = 70f;

    [Tooltip("If true, disables Rigidbody collisions during the pull so the motion is uninterrupted.")]
    public bool disableCollisionsDuringDeath = true;

    [Header("Ceiling Pull Death (PlayMaker)")]
    [Tooltip("Optional PlayMaker GLOBAL event broadcast when the player reaches the target ceiling height.")]
    public string ceilingPullReachedHeightEvent = "";

    [Header("Input Robustness")]
    [Tooltip("If true, resets Unity input axes on focus/pause changes to avoid 'stuck key' movement after alt-tab or UI focus changes.")]
    public bool resetInputOnFocusChange = true;

    [Tooltip("If true, also resets input when menu/prayer/cursor lock state changes (helps clear stuck key states caused by UI interactions).")]
    public bool resetInputOnUiStateChange = true;

    [Tooltip("If true, stops movement whenever the cursor is not locked or is visible (helps avoid moving while UI/menus are active even if isMenuOpen wasn't set).")]
    public bool stopMovementWhenCursorUnlocked = true;

    [Header("Editor Input Robustness")]
    [Tooltip("Editor-only: if the Unity Game view loses focus while a key is held, Unity can miss the key-up and movement can feel 'stuck'. This forces a stop+reset when the Game view focus changes.")]
    public bool resetInputWhenGameViewLosesFocus = true;

    private Coroutine deathRoutine;

    private bool lastAppFocused;
    private bool lastMenuOpen;
    private bool lastPrayer;
    private CursorLockMode lastCursorLockState;
    private bool lastCursorVisible;

#if UNITY_EDITOR
    private bool lastGameViewFocused;
#endif

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            Debug.LogError("Missing AudioSource on the player!");
        }

        if (cameraTransform == null)
        {
            Debug.LogError("Camera Transform not assigned!");
        }

        originalCameraPosition = cameraTransform.localPosition;
        initialCameraRotation = cameraTransform.localRotation;
        lastPosition = transform.position;

        wanderTime = Random.value * 1000f; // Random start time for Perlin noise

        // ✅ Force cursor lock at the start of the game
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        lookSliderCanvasGroup.alpha = 0f;

        lastAppFocused = Application.isFocused;
        lastMenuOpen = isMenuOpen;
        lastPrayer = isInPrayerMode;
        lastCursorLockState = Cursor.lockState;
        lastCursorVisible = Cursor.visible;

#if UNITY_EDITOR
        lastGameViewFocused = IsGameViewFocused();
#endif
    }

#if UNITY_EDITOR
    private static bool IsGameViewFocused()
    {
        // In editor, "application focus" stays true, but keyboard focus can move off the Game view.
        // When that happens while a key is down, Unity may never deliver the key-up to the Game view.
        var focused = EditorWindow.focusedWindow;
        if (focused == null) return false;
        return focused.GetType().Name == "GameView";
    }
#endif

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!resetInputOnFocusChange) return;

        if (hasFocus != lastAppFocused)
        {
            lastAppFocused = hasFocus;
            ForceStopAndResetInputAxes();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (!resetInputOnFocusChange) return;
        if (paused)
        {
            ForceStopAndResetInputAxes();
        }
    }

    private void ForceStopAndResetInputAxes()
    {
        StopHorizontalMovement();

        // ResetInputAxes helps clear stale key/button states in some focus-loss scenarios.
        // Safe to call even if you mainly rely on GetKey.
        Input.ResetInputAxes();
    }

    public void BeginCeilingPullDeath()
    {
        if (isDying) return;

        isDying = true;
        isFinishedDying = false;

        inputIsFrozen = true;
        StopHorizontalMovement();

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
        }
        deathRoutine = StartCoroutine(CeilingPullDeathRoutine());
    }

    private IEnumerator CeilingPullDeathRoutine()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();

        bool prevKinematic = false;
        bool prevDetectCollisions = true;
        if (rb != null)
        {
            prevKinematic = rb.isKinematic;
            prevDetectCollisions = rb.detectCollisions;
            rb.isKinematic = true;
            if (disableCollisionsDuringDeath)
            {
                rb.detectCollisions = false;
            }
        }

        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * ceilingPullHeight;

        Quaternion startCamRot = cameraTransform != null ? cameraTransform.localRotation : Quaternion.identity;
        Vector3 startEuler = startCamRot.eulerAngles;
        float startPitch = NormalizePitch(startEuler.x);
        float startYaw = startEuler.y;

        float duration = Mathf.Max(0.1f, ceilingPullDuration);
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);

            // Ease-in then accelerate: u^2 gives a nice "yanked" feeling.
            float posT = u * u;
            transform.position = Vector3.Lerp(startPos, endPos, posT);

            if (cameraTransform != null)
            {
                float pitch = Mathf.Lerp(startPitch, ceilingPullLookDownPitch, u);
                cameraTransform.localRotation = Quaternion.Euler(pitch, startYaw, 0f);
            }

            yield return null;
        }

        transform.position = endPos;
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(ceilingPullLookDownPitch, startYaw, 0f);
        }

        // Leave the player frozen; game-over flow will take over.
        if (rb != null)
        {
            // rb is kinematic during this sequence; setting velocity on kinematic bodies spams warnings.
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            // We intentionally do not restore physics state on death.
            // (If you ever reuse this for non-lethal sequences, restore prevKinematic/collisions here.)
        }

        if (!string.IsNullOrEmpty(ceilingPullReachedHeightEvent))
        {
            PlayMakerEventBridge.BroadcastGlobalEvent(ceilingPullReachedHeightEvent, this);
        }

        isFinishedDying = true;
        deathRoutine = null;
    }

    private static float NormalizePitch(float eulerX)
    {
        // Convert 0..360 to -180..180 so Lerp behaves predictably.
        if (eulerX > 180f) eulerX -= 360f;
        return eulerX;
    }

    private void OnEnable()
    {
        // If this component gets toggled by PlayMaker, make sure we start from a clean stop.
        if (rb == null) rb = GetComponent<Rigidbody>();
        StopHorizontalMovement();
    }

    private void OnDisable()
    {
        // Critical: if the controller is disabled while moving, velocity can persist and feel like a stuck key.
        StopHorizontalMovement();
    }

    void Update()
    {
#if UNITY_EDITOR
        if (resetInputWhenGameViewLosesFocus)
        {
            bool gameViewFocused = IsGameViewFocused();
            if (gameViewFocused != lastGameViewFocused)
            {
                lastGameViewFocused = gameViewFocused;
                if (!gameViewFocused)
                {
                    ForceStopAndResetInputAxes();
                }
            }
        }
#endif

        if (resetInputOnUiStateChange)
        {
            // Many "stuck key" reports happen after UI state flips while a key is down.
            // ResetInputAxes helps clear stale internal key/button states.
            if (isMenuOpen != lastMenuOpen || isInPrayerMode != lastPrayer ||
                Cursor.lockState != lastCursorLockState || Cursor.visible != lastCursorVisible)
            {
                lastMenuOpen = isMenuOpen;
                lastPrayer = isInPrayerMode;
                lastCursorLockState = Cursor.lockState;
                lastCursorVisible = Cursor.visible;

                ForceStopAndResetInputAxes();
            }
        }

        if (isMenuOpen || Time.timeScale < 0.0001f)
        {
            StopHorizontalMovement();
            return;
        }

        if (inputIsFrozen)
        {
            StopHorizontalMovement();
            return;
        }

        if (!isInPrayerMode)
        {
            HandleMouseLook();
            HandleFootsteps();
            ApplyHeadBobbing(); // ✅ Call this every frame
            lookSliderCanvasGroup.alpha = Mathf.MoveTowards(lookSliderCanvasGroup.alpha, 0f, Time.deltaTime * sliderFadeSpeed);
        }
        else
        {
            StopHorizontalMovement();
            HandlePrayerLook();
        }

        if (enableWander)
        {
            UpdateWanderOffset();
        }
    }


    void LateUpdate()
    {
        //Cursor.lockState = CursorLockMode.Locked;
        //Cursor.visible = false;
    }

    void FixedUpdate()
    {
        if (inputIsFrozen || isInPrayerMode || isMenuOpen)
        {
            StopHorizontalMovement();
            return;
        }

        if (stopMovementWhenCursorUnlocked && (Cursor.visible || Cursor.lockState != CursorLockMode.Locked))
        {
            StopHorizontalMovement();
            return;
        }

        HandleMovement();
        ApplyVelocityFailsafes();
        CheckForStuckState();
    }

    public void EnterPrayerMode()
    {
        isInPrayerMode = true;
        horizontalLookOffset = cameraTransform.localRotation.eulerAngles.y;
        verticalLookRotation = cameraTransform.localRotation.eulerAngles.x;

        //Cursor.lockState = CursorLockMode.Confined;
        //Cursor.visible = true;
    }

    public void ExitPrayerMode()
    {
        isInPrayerMode = false;
        horizontalLookOffset = transform.rotation.eulerAngles.y;
        verticalLookRotation = cameraTransform.localRotation.eulerAngles.x;

        //Cursor.lockState = CursorLockMode.Locked;
        //Cursor.visible = false;
    }

    private void UpdateWanderOffset()
    {
        wanderTime += Time.deltaTime * wanderSpeed;

        // Use different Perlin noise coordinates for each axis
        float xOffset = (Mathf.PerlinNoise(wanderTime, 0) - 0.5f) * 2f * wanderStrength;
        float yOffset = (Mathf.PerlinNoise(wanderTime + 100f, 0) - 0.5f) * 2f * wanderStrength;

        // Smoothly interpolate to new wander offset
        wanderOffset = Vector3.Lerp(wanderOffset, new Vector3(xOffset, yOffset, 0), Time.deltaTime * 2f);
    }

    private void ApplyHeadBobbing()
    {
        if (!enableHeadBobbing || cameraTransform == null) return;

        if (rb.linearVelocity.magnitude > 0.1f) // Only bob when moving
        {
            bobTimer += Time.deltaTime * bobSpeed;
            float bobAmountY = Mathf.Sin(bobTimer) * bobIntensity;
            float bobAmountX = Mathf.Cos(bobTimer / 2) * (bobIntensity * 0.5f); // Slight side-to-side
            cameraTransform.localPosition = originalCameraPosition + new Vector3(bobAmountX, bobAmountY, 0f);
        }
        else
        {
            cameraTransform.localPosition = Vector3.Lerp(cameraTransform.localPosition, originalCameraPosition, Time.deltaTime * returnSpeed);
            bobTimer = 0f;
        }
    }


    private void HandleMovement()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) return;
        if (rb.isKinematic)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        float moveForward = 0f;
        float moveSideways = 0f;

        if (Input.GetKey(KeyCode.W)) moveForward = 1f;
        if (Input.GetKey(KeyCode.S)) moveForward = -1f;
        if (Input.GetKey(KeyCode.D)) moveSideways = 1f;
        if (Input.GetKey(KeyCode.A)) moveSideways = -1f;

        bool anyMovementKeyHeld = (moveForward != 0f) || (moveSideways != 0f);

        Vector3 moveDirection = (transform.forward * moveForward + transform.right * moveSideways).normalized;
        wasMovementRequested = moveDirection.sqrMagnitude > 0.0001f;
        Vector3 targetVelocity = moveDirection * moveSpeed;

        if (moveDirection.magnitude > 0.1f)
        {
            // Accelerate towards target speed
            float adjustedDelta = Mathf.Min(Time.deltaTime, 0.033f); // Clamp to ~30 FPS max effect
            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, adjustedDelta * acceleration);
        }
        else
        {
            // Decelerate when not moving
            currentVelocity = Vector3.Lerp(currentVelocity, Vector3.zero, Time.fixedDeltaTime * deceleration);
        }

        rb.linearVelocity = new Vector3(currentVelocity.x, rb.linearVelocity.y, currentVelocity.z);

        if (enableNoInputBrakeFailsafe)
        {
            if (!anyMovementKeyHeld)
            {
                noInputTimer += Time.fixedDeltaTime;

                Vector3 v = rb.linearVelocity;
                float horizontalSpeed = new Vector2(v.x, v.z).magnitude;

                if (noInputTimer >= noInputBrakeDelaySeconds && horizontalSpeed >= noInputBrakeSpeedThreshold)
                {
                    StopHorizontalMovement();
                }
            }
            else
            {
                noInputTimer = 0f;
            }
        }

    }

    private void StopHorizontalMovement()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb == null) return;
        currentVelocity = Vector3.zero;
        if (rb.isKinematic) return;
        Vector3 v = rb.linearVelocity;
        rb.linearVelocity = new Vector3(0f, v.y, 0f);
    }

    private void ApplyVelocityFailsafes()
    {
        if (rb == null) return;
        if (rb.isKinematic) return;

        Vector3 v = rb.linearVelocity;
        float speed = v.magnitude;

        if (speed > maxVelocityMagnitude)
        {
            rb.linearVelocity = v.normalized * maxVelocityMagnitude;
        }

        // If something injects a large velocity spike, brake hard.
        Vector3 v2 = rb.linearVelocity;
        float horizontalSpeed = new Vector2(v2.x, v2.z).magnitude;
        if (!wasMovementRequested && horizontalSpeed >= velocityResetThreshold)
        {
            StopHorizontalMovement();
        }
    }

    private void HandleMouseLook()
    {
        if (isInPrayerMode || isMenuOpen) return;

        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        // Apply smoothing
        currentMouseInput = Vector2.Lerp(currentMouseInput, new Vector2(mouseX, mouseY), Time.deltaTime * rotationSmoothing);

        transform.Rotate(0f, currentMouseInput.x, 0f);
        verticalLookRotation -= currentMouseInput.y;
        verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);

        float wanderX = enableWander ? wanderOffset.x : 0f;
        float wanderY = enableWander ? wanderOffset.y : 0f;

        cameraTransform.localRotation = Quaternion.Euler(verticalLookRotation + wanderX, wanderY, 0f);
    }


    private void CheckForStuckState()
    {
        // Check if we're moving when we shouldn't be, or not moving when we should be
        float distanceMoved = Vector3.Distance(transform.position, lastPosition);

        if ((wasMovementRequested && distanceMoved < 0.01f) ||
            (!wasMovementRequested && distanceMoved > 0.1f))
        {
            stuckTimer += Time.unscaledDeltaTime;
            if (stuckTimer >= stuckDetectionTime)
            {
                //EmergencyBrake();
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPosition = transform.position;
    }

    private void EmergencyBrake()
    {
        currentVelocity = Vector3.zero;
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        Debug.Log("Emergency brake applied!");
    }

    private void HandlePrayerLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        Vector2 targetMouseInput = new Vector2(mouseX, mouseY);

        // ✅ Keep original smoothing behavior
        currentPrayerMouseInput = Vector2.Lerp(currentPrayerMouseInput, targetMouseInput, Time.deltaTime * rotationSmoothing);

        horizontalLookOffset = Mathf.Clamp(horizontalLookOffset + currentPrayerMouseInput.x, -horizontalLookLimit, horizontalLookLimit);
        verticalLookRotation = Mathf.Clamp(verticalLookRotation - currentPrayerMouseInput.y, -verticalLookLimit, verticalLookLimit);

        Quaternion yawRotation = Quaternion.Euler(0f, horizontalLookOffset + wanderOffset.y, 0f);
        Quaternion pitchRotation = Quaternion.Euler(verticalLookRotation + wanderOffset.x, 0f, 0f);

        cameraTransform.localRotation = initialCameraRotation * yawRotation * pitchRotation;

        // 🎯 --- UI Slider Update ---
        if (lookDirectionSlider != null)
        {
            float progress = Mathf.InverseLerp(-horizontalLookLimit, horizontalLookLimit, horizontalLookOffset);
            lookDirectionSlider.value = progress;
        }

        // 🎯 --- Alpha Lerp ---
        if (lookSliderCanvasGroup != null)
        {
            float movement = Mathf.Abs(currentPrayerMouseInput.x); // use smoothed input
            bool isMoving = movement > 0.01f;

            if (isMoving)
            {
                lookSliderCanvasGroup.alpha = Mathf.MoveTowards(lookSliderCanvasGroup.alpha, sliderMaxAlpha, Time.deltaTime * sliderFadeSpeed);
                idleFadeTimer = 0f;
            }
            else
            {
                idleFadeTimer += Time.deltaTime;
                if (idleFadeTimer >= fadeCooldown)
                {
                    lookSliderCanvasGroup.alpha = Mathf.MoveTowards(lookSliderCanvasGroup.alpha, sliderMinAlpha, Time.deltaTime * sliderFadeSpeed);
                }
            }
        }
    }


    private void HandleFootsteps()
    {
        if (rb.linearVelocity.magnitude > 0.1f) // Only play footsteps when moving
        {
            footstepTimer -= Time.deltaTime;
            if (footstepTimer <= 0f)
            {
                PlayFootstep();
                float speedMultiplier = Mathf.Clamp(rb.linearVelocity.magnitude / moveSpeed, 0.5f, 1.5f);
                footstepTimer = baseFootstepInterval * (1f / speedMultiplier);
            }
        }
    }

    private void PlayFootstep()
    {
        if (audioSource == null) return;

        bool isOnCarpet = carpetZoneCount > 0;
        AudioClip[] footstepPool = isOnCarpet ? carpetFootsteps : hardwoodFootsteps;
        if (footstepPool != null && footstepPool.Length > 0)
        {
            AudioClip selectedClip = footstepPool[Random.Range(0, footstepPool.Length)];
            audioSource.PlayOneShot(selectedClip, footstepVolume);
        }

        // Overlay layers (can stack if multiple zones overlap).
        AudioSource overlaySource = footstepOverlayAudioSource != null ? footstepOverlayAudioSource : audioSource;
        if (overlaySource == null) return;

        if (hollowZoneCount > 0 && hollowOverlayFootsteps != null && hollowOverlayFootsteps.Length > 0)
        {
            AudioClip overlayClip = hollowOverlayFootsteps[Random.Range(0, hollowOverlayFootsteps.Length)];
            overlaySource.PlayOneShot(overlayClip, footstepOverlayVolume);

            if (hollowFootstepPuff != null)
            {
                // Emit is a reliable one-shot even if the object is always active.
                hollowFootstepPuff.Emit(Mathf.Max(1, hollowPuffEmitCount));
            }
        }

        if (wetZoneCount > 0 && wetOverlayFootsteps != null && wetOverlayFootsteps.Length > 0)
        {
            AudioClip overlayClip = wetOverlayFootsteps[Random.Range(0, wetOverlayFootsteps.Length)];
            overlaySource.PlayOneShot(overlayClip, footstepOverlayVolume);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player_Carpet")) carpetZoneCount++;
        if (other.CompareTag("Player_Hollow")) hollowZoneCount++;
        if (other.CompareTag("Player_Wet")) wetZoneCount++;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player_Carpet")) carpetZoneCount = Mathf.Max(0, carpetZoneCount - 1);
        if (other.CompareTag("Player_Hollow")) hollowZoneCount = Mathf.Max(0, hollowZoneCount - 1);
        if (other.CompareTag("Player_Wet")) wetZoneCount = Mathf.Max(0, wetZoneCount - 1);
    }

    public void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
