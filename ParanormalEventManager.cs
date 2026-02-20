using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ParanormalEventManager : MonoBehaviour
{
    [Header("Enable")]
    [Tooltip("Master enable for the paranormal system. This is script-owned; avoid driving it from PlayMaker.")]
    [SerializeField] private bool masterEnabled = true;

    [Tooltip("Runtime state (computed): only active during placement, after first successful validation.")]
    public bool isActive = false;

    [Header("Phase Gating")]
    [Tooltip("Paranormal events only start after the first round has been successfully validated.")]
    [SerializeField] private bool startAfterFirstSuccessfulRound = true;

    [Tooltip("If true, placement is detected via ObjectPlacer.canPlaceObjects.")]
    [SerializeField] private bool requirePlacementPhase = true;

    [Tooltip("If true, suppresses paranormal triggers while player is praying (SimpleFirstPersonController.isInPrayerMode).")]
    [SerializeField] private bool suppressWhilePraying = true;

    [Tooltip("If true, suppresses paranormal triggers when Time.timeScale is ~0 (tutorial/menu pause).")]
    [SerializeField] private bool suppressWhileTimePaused = true;

    [Header("Timing")]
    [SerializeField] private float minInterval = 5f;
    [SerializeField] private float maxInterval = 15f;
    [Tooltip("When we leave the placement phase, reset the timer so it doesn't fire immediately on return.")]
    [SerializeField] private bool resetTimerWhenDeactivated = true;

    public enum AngerSource
    {
        ManualValue,
        RoundWrongMeter
    }

    [Header("Probability (Ghost Anger)")]
    [Tooltip("Where the paranormal system reads 'anger' from. No PlayMaker dependency.")]
    [SerializeField] private AngerSource angerSource = AngerSource.RoundWrongMeter;

    [Tooltip("Used when Anger Source = ManualValue.")]
    [SerializeField] private float manualAnger = 0f;

    [Tooltip("Anger value that maps to maxChance.")]
    [SerializeField] private float maxAnger = 22f;

    [Tooltip("Chance at anger = 0.")]
    [Range(0f, 1f)]
    [SerializeField] private float minChance = 0.02f;

    [Tooltip("Chance at anger >= maxAnger.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxChance = 0.5f;

    [Header("Event Mix (Weights)")]
    [SerializeField] private float weightObjectThrow = 1f;
    [SerializeField] private float weightLightFlicker = 1f;
    [SerializeField] private float weightFurnitureSound = 1f;

    [Header("Event: Object Throw")]
    [Tooltip("Objects to affect (e.g., book flying off).")]
    [SerializeField] private List<ParanormalObject> objectsToAffect = new List<ParanormalObject>();

    [Tooltip("If false, each ParanormalObject can only be triggered once per run.")]
    [SerializeField] private bool allowRepeatObjectActivations = false;

    [Header("Event: Light Flicker")]
    [Tooltip("First light object to toggle during flicker (e.g., bulb mesh/light).")]
    [SerializeField] private GameObject lightObject1;

    [Tooltip("Second light object to toggle during flicker (e.g., ambient light source).")]
    [SerializeField] private GameObject lightObject2;

    // Legacy fallback (optional): if you previously wired on/off groups, we still support it.
    [Tooltip("(Legacy) GameObject(s) that represent the light ON state (active when light is on).")]
    [SerializeField] private GameObject lightOnGroup;

    [Tooltip("(Legacy) GameObject(s) that represent the light OFF state (active when light is off).")]
    [SerializeField] private GameObject lightOffGroup;

    [SerializeField] private Vector2Int flickerCountRange = new Vector2Int(2, 4);
    [SerializeField] private Vector2 flickerStepDurationRange = new Vector2(0.05f, 0.12f);

    [Header("Event: Light Flicker SFX")]
    [SerializeField] private AudioSource lightFlickerSfxSource;
    [SerializeField] private AudioClip lightFlickerSfx;
    [Range(0f, 1f)]
    [SerializeField] private float lightFlickerSfxVolume = 1f;

    [System.Serializable]
    public struct FurnitureOneShot
    {
        public AudioSource audioSource;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    [Header("Event: Furniture Sound")]
    [SerializeField] private FurnitureOneShot[] furnitureOneShots;

    [Header("Debug")]
    [SerializeField] private bool enableDebugKey = true;
    [SerializeField] private KeyCode debugTriggerKey = KeyCode.M;
    [SerializeField] private bool debugBypassesStateGuards = true;

    // Runtime refs
    [SerializeField] private ObjectPlacer objectPlacer;
    [SerializeField] private RoundManager roundManager;
    [SerializeField] private SimpleFirstPersonController playerController;

    private readonly HashSet<ParanormalObject> activatedObjects = new HashSet<ParanormalObject>();
    private float nextEventTimer = 0f;
    private bool hasUnlockedAfterFirstSuccess = false;
    private bool wasActiveLastFrame = false;
    private Coroutine lightFlickerCoroutine;
    private bool? cachedLightBaselineOn;
    private bool? cachedLight1Baseline;
    private bool? cachedLight2Baseline;

    public void ActivateSystem()
    {
        masterEnabled = true;
        ScheduleNextEvent();
    }

    public void DeactivateSystem()
    {
        masterEnabled = false;
        isActive = false;

        if (lightFlickerCoroutine != null)
        {
            StopCoroutine(lightFlickerCoroutine);
            lightFlickerCoroutine = null;
        }

        RestoreLightState();
    }

    public void UnlockAfterFirstRoundCompletion()
    {
        hasUnlockedAfterFirstSuccess = true;
    }

    public void SetManualAnger(float anger)
    {
        manualAnger = anger;
    }

    private void Awake()
    {
        // Lazily find references if not wired in inspector.
        if (objectPlacer == null) objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
        if (roundManager == null) roundManager = Object.FindFirstObjectByType<RoundManager>();
        if (playerController == null) playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();

        ScheduleNextEvent();
    }

    private void Update()
    {
        if (enableDebugKey && Input.GetKeyDown(debugTriggerKey))
        {
            TriggerRandomEvent(debugBypassesStateGuards);
        }

        UpdateUnlockState();

        bool shouldBeActive = masterEnabled && ShouldRunNow();
        isActive = shouldBeActive;

        if (!isActive)
        {
            if (wasActiveLastFrame && resetTimerWhenDeactivated)
            {
                ScheduleNextEvent();
            }

            wasActiveLastFrame = false;
            return;
        }

        wasActiveLastFrame = true;

        nextEventTimer -= Time.deltaTime;
        if (nextEventTimer > 0f) return;

        TryTriggerAutomaticEvent();
        ScheduleNextEvent();
    }

    private void UpdateUnlockState()
    {
        if (!startAfterFirstSuccessfulRound)
        {
            hasUnlockedAfterFirstSuccess = true;
            return;
        }

        if (hasUnlockedAfterFirstSuccess) return;

        // Primary unlock should come from GridManager calling UnlockAfterFirstRoundCompletion()
        // (when it generates round 2). As a safety net, we also unlock if RoundManager indicates
        // we are past round 1.
        if (roundManager == null) roundManager = Object.FindFirstObjectByType<RoundManager>();
        if (roundManager != null && roundManager.currentRound >= 2)
        {
            hasUnlockedAfterFirstSuccess = true;
        }
    }

    private bool ShouldRunNow()
    {
        if (!hasUnlockedAfterFirstSuccess) return false;

        if (suppressWhileTimePaused && Time.timeScale < 0.0001f) return false;

        if (suppressWhilePraying)
        {
            if (playerController == null) playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
            if (playerController != null && playerController.isInPrayerMode) return false;
            if (playerController != null && playerController.isMenuOpen) return false;
        }

        if (requirePlacementPhase)
        {
            if (objectPlacer == null) objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>();
            if (objectPlacer == null || !objectPlacer.canPlaceObjects) return false;
        }

        return true;
    }

    private void ScheduleNextEvent()
    {
        float safeMin = Mathf.Max(0.05f, minInterval);
        float safeMax = Mathf.Max(safeMin, maxInterval);
        nextEventTimer = Random.Range(safeMin, safeMax);
    }

    private void TryTriggerAutomaticEvent()
    {
        float chance = ComputeActivationChanceFromAnger();
        if (Random.value >= chance) return;

        TriggerRandomEvent(false);
    }

    private float ComputeActivationChanceFromAnger()
    {
        float angerLevel = 0f;
        switch (angerSource)
        {
            case AngerSource.ManualValue:
                angerLevel = manualAnger;
                break;
            case AngerSource.RoundWrongMeter:
                if (roundManager == null) roundManager = Object.FindFirstObjectByType<RoundManager>();
                angerLevel = roundManager != null ? roundManager.wrongMeter : 0f;
                break;
        }

        float t = (maxAnger <= 0.0001f) ? 1f : Mathf.Clamp01(angerLevel / maxAnger);
        return Mathf.Lerp(minChance, maxChance, t);
    }

    private enum EventType
    {
        ObjectThrow,
        LightFlicker,
        FurnitureSound
    }

    public void TriggerRandomEvent(bool bypassGuards)
    {
        if (!bypassGuards && !ShouldRunNow()) return;

        // Avoid overlapping flickers.
        if (lightFlickerCoroutine != null)
        {
            StopCoroutine(lightFlickerCoroutine);
            lightFlickerCoroutine = null;
            RestoreLightState();
        }

        EventType? chosen = ChooseEventType();
        if (chosen == null) return;

        switch (chosen.Value)
        {
            case EventType.ObjectThrow:
                ActivateRandomParanormalObject();
                break;
            case EventType.LightFlicker:
                lightFlickerCoroutine = StartCoroutine(LightFlickerRoutine(bypassGuards));
                break;
            case EventType.FurnitureSound:
                PlayRandomFurnitureOneShot();
                break;
        }
    }

    private EventType? ChooseEventType()
    {
        // Build availability.
        bool canThrow = HasAvailableParanormalObject();
        bool canFlicker = HasLightFlickerTargets();
        bool canFurniture = (furnitureOneShots != null && furnitureOneShots.Length > 0);

        float wThrow = canThrow ? Mathf.Max(0f, weightObjectThrow) : 0f;
        float wFlicker = canFlicker ? Mathf.Max(0f, weightLightFlicker) : 0f;
        float wFurniture = canFurniture ? Mathf.Max(0f, weightFurnitureSound) : 0f;

        float total = wThrow + wFlicker + wFurniture;
        if (total <= 0.0001f) return null;

        float r = Random.value * total;
        if (r < wThrow) return EventType.ObjectThrow;
        r -= wThrow;
        if (r < wFlicker) return EventType.LightFlicker;
        return EventType.FurnitureSound;
    }

    private bool HasAvailableParanormalObject()
    {
        if (objectsToAffect == null || objectsToAffect.Count == 0) return false;

        if (allowRepeatObjectActivations) return true;

        foreach (var obj in objectsToAffect)
        {
            if (obj == null) continue;
            if (!activatedObjects.Contains(obj)) return true;
        }

        return false;
    }

    private bool HasLightFlickerTargets()
    {
        bool hasPair = (lightObject1 != null && lightObject2 != null);
        bool hasLegacy = (lightOnGroup != null && lightOffGroup != null);
        return hasPair || hasLegacy;
    }

    private void ActivateRandomParanormalObject()
    {
        if (objectsToAffect == null || objectsToAffect.Count == 0) return;

        List<ParanormalObject> available = new List<ParanormalObject>();
        foreach (var obj in objectsToAffect)
        {
            if (obj == null) continue;
            if (allowRepeatObjectActivations || !activatedObjects.Contains(obj))
            {
                available.Add(obj);
            }
        }

        if (available.Count == 0)
        {
            Debug.LogWarning("[ParanormalEventManager] No available objects left to activate.");
            return;
        }

        ParanormalObject chosen = available[Random.Range(0, available.Count)];
        if (!allowRepeatObjectActivations)
        {
            activatedObjects.Add(chosen);
        }

        Debug.Log($"[ParanormalEventManager] Object throw: {chosen.gameObject.name}");
        chosen.ActivateObject();
    }

    private IEnumerator LightFlickerRoutine(bool bypassGuards)
    {
        if (!HasLightFlickerTargets())
        {
            lightFlickerCoroutine = null;
            yield break;
        }

        // Determine baseline state.
        bool baselineLightOn = GetBaselineLightOnState();
        CacheLightBaselines(baselineLightOn);

        ApplyLightVisualState(baselineLightOn);

        int minCount = Mathf.Min(flickerCountRange.x, flickerCountRange.y);
        int maxCount = Mathf.Max(flickerCountRange.x, flickerCountRange.y);
        int flickers = Random.Range(minCount, maxCount + 1);

        for (int i = 0; i < flickers; i++)
        {
            if (!bypassGuards && !ShouldRunNow())
            {
                break;
            }

            // Toggle
            bool toggled = !GetCurrentLightOnState();
            ApplyLightVisualState(toggled);
            PlayLightFlickerSfx();
            yield return new WaitForSeconds(Random.Range(flickerStepDurationRange.x, flickerStepDurationRange.y));

            if (!bypassGuards && !ShouldRunNow())
            {
                break;
            }

            // Toggle back
            ApplyLightVisualState(!toggled);
            PlayLightFlickerSfx();
            yield return new WaitForSeconds(Random.Range(flickerStepDurationRange.x, flickerStepDurationRange.y));
        }

        // Restore to authoritative baseline.
        ApplyLightVisualState(baselineLightOn);
        ClearLightBaselines();

        lightFlickerCoroutine = null;
    }

    private bool GetCurrentLightOnState()
    {
        if (lightObject1 != null && lightObject2 != null)
        {
            return lightObject1.activeSelf || lightObject2.activeSelf;
        }

        if (lightOnGroup != null) return lightOnGroup.activeSelf;
        if (lightOffGroup != null) return !lightOffGroup.activeSelf;

        return true;
    }

    private void PlayLightFlickerSfx()
    {
        if (lightFlickerSfxSource == null || lightFlickerSfx == null) return;
        lightFlickerSfxSource.PlayOneShot(lightFlickerSfx, lightFlickerSfxVolume);
    }

    private void ApplyLightVisualState(bool lightOn)
    {
        // Preferred mode: toggle two objects together.
        if (lightObject1 != null && lightObject2 != null)
        {
            lightObject1.SetActive(lightOn);
            lightObject2.SetActive(lightOn);
            return;
        }

        // Legacy fallback: on/off groups.
        if (lightOnGroup != null) lightOnGroup.SetActive(lightOn);
        if (lightOffGroup != null) lightOffGroup.SetActive(!lightOn);
    }

    private void RestoreLightState()
    {
        if (!HasLightFlickerTargets()) return;

        // If we cached baselines for the pair mode, restore exact per-object state.
        if (lightObject1 != null && lightObject2 != null && cachedLight1Baseline.HasValue && cachedLight2Baseline.HasValue)
        {
            lightObject1.SetActive(cachedLight1Baseline.Value);
            lightObject2.SetActive(cachedLight2Baseline.Value);
            return;
        }

        // Otherwise restore to the cached combined baseline if we have it.
        if (cachedLightBaselineOn.HasValue)
        {
            ApplyLightVisualState(cachedLightBaselineOn.Value);
            return;
        }

        // Best-effort: keep whatever is currently displayed.
        if (lightObject1 != null && lightObject2 != null)
        {
            ApplyLightVisualState(lightObject1.activeSelf || lightObject2.activeSelf);
            return;
        }

        ApplyLightVisualState(lightOnGroup != null ? lightOnGroup.activeSelf : true);
    }

    private bool GetBaselineLightOnState()
    {
        // Preferred mode: if either is on, treat baseline as "on" so we re-sync them.
        if (lightObject1 != null && lightObject2 != null)
        {
            return lightObject1.activeSelf || lightObject2.activeSelf;
        }

        // Legacy fallback.
        if (lightOnGroup != null) return lightOnGroup.activeSelf;
        return true;
    }

    private void CacheLightBaselines(bool baselineLightOn)
    {
        cachedLightBaselineOn = baselineLightOn;
        if (lightObject1 != null && lightObject2 != null)
        {
            cachedLight1Baseline = lightObject1.activeSelf;
            cachedLight2Baseline = lightObject2.activeSelf;
        }
        else
        {
            cachedLight1Baseline = null;
            cachedLight2Baseline = null;
        }
    }

    private void ClearLightBaselines()
    {
        cachedLightBaselineOn = null;
        cachedLight1Baseline = null;
        cachedLight2Baseline = null;
    }

    private void PlayRandomFurnitureOneShot()
    {
        if (furnitureOneShots == null || furnitureOneShots.Length == 0) return;

        // Filter usable entries.
        List<int> usable = new List<int>();
        for (int i = 0; i < furnitureOneShots.Length; i++)
        {
            if (furnitureOneShots[i].audioSource == null) continue;
            if (furnitureOneShots[i].clip == null) continue;
            usable.Add(i);
        }

        if (usable.Count == 0) return;

        int idx = usable[Random.Range(0, usable.Count)];
        var entry = furnitureOneShots[idx];

        float vol = entry.volume <= 0f ? 1f : entry.volume;
        entry.audioSource.PlayOneShot(entry.clip, vol);
    }
}
