using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CeilingHandMinigame : MonoBehaviour
{
    private const string DefaultGrabbedPlayMakerEvent = "GAME/Attic_Arm_Minigame_Death";

    [Header("Audio (Encounter) ")]
    [Tooltip("Optional: if assigned, random one-shots will be played from this AudioSource during the whole encounter.")]
    public AudioSource encounterAudioSource;

    [Tooltip("Random one-shot clips to play intermittently during the encounter.")]
    public AudioClip[] encounterOneShotClips;

    [Tooltip("Random interval range (seconds) between one-shots.")]
    public Vector2 encounterOneShotIntervalRange = new Vector2(5f, 10f);

    [Tooltip("Volume for encounter one-shots.")]
    [Range(0f, 1f)] public float encounterOneShotVolume = 0.8f;

    [Header("Audio (Encounter Loop)")]
    [Tooltip("Optional: looping bed audio for the entire encounter.")]
    public AudioSource encounterLoopAudioSource;

    [Tooltip("Looping clip to play for the duration of the encounter.")]
    public AudioClip encounterLoopClip;

    [Tooltip("Volume for the looping encounter bed.")]
    [Range(0f, 1f)] public float encounterLoopVolume = 0.65f;

    [Tooltip("Seconds to fade the encounter loop out when the event ends.")]
    [Min(0f)] public float encounterLoopFadeOutSeconds = 1.0f;

    [Header("Player Hit (Grab)")]
    [Tooltip("Optional one-shot SFX played when the hand grabs the player.")]
    public AudioSource grabbedAudioSource;

    [Tooltip("Optional grabbed SFX clip.")]
    public AudioClip grabbedClip;

    [Range(0f, 1f)]
    public float grabbedVolume = 1.0f;

    [Tooltip("PlayMaker GLOBAL event broadcast when the hand hits the player. Default matches your existing flow.")]
    public string onGrabbedPlayMakerEvent = DefaultGrabbedPlayMakerEvent;

    [Header("References")]
    [Tooltip("Curated spawn points on/near the ceiling. The hand/warning will spawn at this transform's position & rotation.")]
    public Transform[] spawnPoints;

    [Tooltip("Optional warning prefab (eg. black tear/hole). Spawned at the selected spawn point during the warning window.")]
    public GameObject warningPrefab;

    [Tooltip("Hand prefab that contains a trigger collider and a CeilingHandStrike script.")]
    public CeilingHandStrike handPrefab;

    [Tooltip("Optional override. If null, will FindFirstObjectByType<SimpleFirstPersonController>().")]
    public SimpleFirstPersonController playerController;

    [Header("Prayer Trigger")]
    [Tooltip("Optional: a prayer trigger GameObject to disable while this minigame is active, then restore when finished/cancelled.")]
    public GameObject prayerTriggerObject;

    [Header("Flicker Override")]
    [Tooltip("Candles to temporarily override flicker during each strike.")]
    public FlickeringCandle[] candlesToOverride;

    [Tooltip("Minimum intensity used for override flicker.")]
    public float overrideMinIntensity = 0.2f;

    [Tooltip("Maximum intensity used for override flicker.")]
    public float overrideMaxIntensity = 2.2f;

    [Tooltip("Additional boost applied at max proximity factor for override flicker.")]
    public float overrideIntensityBoost = 0.0f;

    [Tooltip("Slowest flicker speed for override (seconds).")]
    public float overrideBaseFlickerSpeed = 0.12f;

    [Tooltip("Fastest flicker speed for override (seconds).")]
    public float overrideFastestFlickerSpeed = 0.03f;

    [Tooltip("Proximity factor for override mode (0..1). 1 = maximum intensity/speed.")]
    [Range(0f, 1f)] public float overrideProximityFactor = 1f;

    [Header("Ceiling Light Flicker (Optional)")]
    [Tooltip("If assigned, these two objects will be flickered (SetActive on/off) during each strike. This does not modify any FSM variables.")]
    public GameObject ceilingLightObject1;
    public GameObject ceilingLightObject2;

    [Tooltip("If true, ceiling lights will only be flickered if they were ON at the start of the strike.")]
    public bool flickerCeilingLightsOnlyIfInitiallyOn = true;

    [Tooltip("Random flicker interval range (seconds).")]
    public Vector2 ceilingLightFlickerIntervalRange = new Vector2(0.05f, 0.18f);

    [Header("Sequence")]
    [Min(1)] public int defaultStrikeCount = 4;

    [Tooltip("Seconds the warning stays up before the hand reaches down.")]
    [Min(0f)] public float warningTime = 0.9f;

    [Tooltip("Seconds between the end of one strike and the next warning.")]
    [Min(0f)] public float downtimeTime = 0.9f;

    [Header("Hand Motion")]
    [Tooltip("How far (in meters) the hand reaches down from its spawn point.")]
    [Min(0.1f)] public float reachDistance = 2.3f;

    [Tooltip("Downward reach speed (m/s).")]
    [Min(0.1f)] public float reachSpeed = 4.0f;

    [Tooltip("Upward retreat speed (m/s).")]
    [Min(0.1f)] public float retreatSpeed = 6.5f;

    [Tooltip("How long (seconds) the hand stays fully extended before retracting.")]
    [Min(0f)] public float holdExtendedTime = 0.75f;

    [Header("Escalation")]
    [Tooltip("Each strike multiplies reach speed by this factor.")]
    [Min(1f)] public float reachSpeedMultiplierPerStrike = 1.12f;

    [Tooltip("Each strike multiplies warning time by this factor (values < 1 mean shorter warning each time).")]
    [Range(0.1f, 1f)] public float warningTimeMultiplierPerStrike = 0.92f;

    [Tooltip("Minimum warning time after escalation.")]
    [Min(0f)] public float minWarningTime = 0.45f;

    [Header("Debug")]
    public bool enableDebugHotkey = true;
    public KeyCode debugHotkey = KeyCode.V;

    [Header("State (Read Only)")]
    [SerializeField] private bool isActive;
    [SerializeField] private int currentStrikeIndex;

    private Coroutine sequenceRoutine;
    private int lastSpawnIndex = -1;

    private GameObject activeWarningInstance;
    private CeilingHandStrike activeHandInstance;
    private Coroutine ceilingLightFlickerRoutine;
    private Coroutine encounterAudioRoutine;
    private Coroutine encounterLoopFadeRoutine;
    private bool recordedCeilingLightStates;
    private bool ceilingLight1WasActive;
    private bool ceilingLight2WasActive;

    private bool recordedPrayerTriggerState;
    private bool prayerTriggerWasActive;

    public bool IsActive => isActive;
    public int CurrentStrikeIndex => currentStrikeIndex;

    private void OnDisable()
    {
        // Safety: if this component gets disabled/destroyed mid-encounter, restore the prayer trigger.
        RestorePrayerTriggerIfNeeded();
    }

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        }

        if (encounterAudioSource == null)
        {
            encounterAudioSource = GetComponent<AudioSource>();
        }

        if (encounterLoopAudioSource == null)
        {
            // Default to the same AudioSource if you don't want to set up a second one.
            // PlayOneShot won't interrupt the loop.
            encounterLoopAudioSource = encounterAudioSource;
        }

        if (grabbedAudioSource == null)
        {
            grabbedAudioSource = encounterAudioSource;
        }

        // Unity serialization quirk: adding a new serialized field later can leave existing scene instances as
        // empty/null instead of using the C# initializer. Ensure we still default to the intended event.
        if (string.IsNullOrEmpty(onGrabbedPlayMakerEvent))
        {
            onGrabbedPlayMakerEvent = DefaultGrabbedPlayMakerEvent;
        }
    }

    private void Update()
    {
        if (!enableDebugHotkey) return;

        if (Input.GetKeyDown(debugHotkey))
        {
            if (!isActive)
            {
                BeginSequence();
            }
            else
            {
                CancelSequence();
            }
        }
    }

    // PlayMaker-friendly entry points
    public void BeginSequence()
    {
        BeginSequenceWithStrikeCount(defaultStrikeCount);
    }

    public void BeginSequenceWithStrikeCount(int strikeCount)
    {
        if (isActive) return;

        if (strikeCount <= 0) strikeCount = 1;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[CeilingHandMinigame] No spawnPoints assigned.", this);
            return;
        }

        if (handPrefab == null)
        {
            Debug.LogError("[CeilingHandMinigame] No handPrefab assigned.", this);
            return;
        }

        if (playerController == null)
        {
            playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        }

        // If the player is praying/paused/etc, you probably don't want this firing.
        // (You said you handle stipulations, but this prevents accidental debug triggers.)
        if (playerController != null && (playerController.isInPrayerMode || playerController.isMenuOpen || playerController.inputIsFrozen))
        {
            Debug.Log("[CeilingHandMinigame] Not starting because player is in a blocked state (prayer/menu/frozen).", this);
            return;
        }

        isActive = true;
        currentStrikeIndex = 0;

        DisablePrayerTriggerIfAssigned();

        StartEncounterAudio();
        StartEncounterLoopAudio();

        if (sequenceRoutine != null) StopCoroutine(sequenceRoutine);
        sequenceRoutine = StartCoroutine(SequenceRoutine(strikeCount));
    }

    public void CancelSequence()
    {
        if (!isActive) return;

        isActive = false;
        currentStrikeIndex = 0;

        RestorePrayerTriggerIfNeeded();

        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }

        StopEncounterAudio();
        StopEncounterLoopAudio(fadeOut: true);

        CleanupActiveStrikeObjects();
    }

    private IEnumerator SequenceRoutine(int strikeCount)
    {
        for (int i = 0; i < strikeCount; i++)
        {
            if (!isActive) yield break;

            currentStrikeIndex = i + 1;

            Transform spawn = PickSpawnPoint();
            if (spawn == null)
            {
                Debug.LogError("[CeilingHandMinigame] PickSpawnPoint returned null.", this);
                break;
            }

            float effectiveWarning = Mathf.Max(minWarningTime, warningTime * Mathf.Pow(warningTimeMultiplierPerStrike, i));
            float effectiveReachSpeed = reachSpeed * Mathf.Pow(reachSpeedMultiplierPerStrike, i);

            // Keep the warning visible for the entire time the hand is present.
            // (Warning starts now and will be destroyed after the hand finishes.)

            GameObject warningInstance = null;
            if (warningPrefab != null)
            {
                warningInstance = Instantiate(warningPrefab, spawn.position, spawn.rotation);
            }

            activeWarningInstance = warningInstance;

            BeginStrikeFlickerOverride();

            float t = 0f;
            while (t < effectiveWarning)
            {
                if (!isActive) break;
                t += Time.deltaTime;
                yield return null;
            }

            if (!isActive) yield break;

            CeilingHandStrike hand = Instantiate(handPrefab, spawn.position, spawn.rotation);
            hand.Configure(this, reachDistance, effectiveReachSpeed, retreatSpeed, holdExtendedTime);

            activeHandInstance = hand;

            // Wait for strike to finish (or be cancelled)
            while (isActive && !hand.IsFinished)
            {
                yield return null;
            }

            EndStrikeFlickerOverride();

            if (warningInstance != null)
            {
                StopWarningGracefully(warningInstance);
            }

            if (hand != null)
            {
                Destroy(hand.gameObject);
            }

            activeHandInstance = null;
            activeWarningInstance = null;

            if (!isActive) yield break;

            float dt = 0f;
            while (dt < downtimeTime)
            {
                if (!isActive) break;
                dt += Time.deltaTime;
                yield return null;
            }
        }

        isActive = false;
        currentStrikeIndex = 0;
        sequenceRoutine = null;

        RestorePrayerTriggerIfNeeded();

        StopEncounterAudio();
        StopEncounterLoopAudio(fadeOut: true);

        CleanupActiveStrikeObjects();
    }

    private Transform PickSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;

        // Choose the spawn point closest to the player.
        Vector3 playerPos;
        if (!TryGetPlayerPosition(out playerPos))
        {
            // Fallback to the previous random behavior if we can't resolve a player position.
            if (spawnPoints.Length == 1)
            {
                lastSpawnIndex = 0;
                return spawnPoints[0];
            }

            int idx = Random.Range(0, spawnPoints.Length);
            if (idx == lastSpawnIndex)
            {
                idx = (idx + 1 + Random.Range(0, spawnPoints.Length - 1)) % spawnPoints.Length;
            }

            lastSpawnIndex = idx;
            return spawnPoints[idx];
        }

        int bestIndex = -1;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform sp = spawnPoints[i];
            if (sp == null) continue;

            float sqr = (sp.position - playerPos).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return null;

        lastSpawnIndex = bestIndex;
        return spawnPoints[bestIndex];
    }

    private bool TryGetPlayerPosition(out Vector3 playerPos)
    {
        if (playerController == null)
        {
            playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        }

        if (playerController != null)
        {
            playerPos = playerController.transform.position;
            return true;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            playerPos = cam.transform.position;
            return true;
        }

        playerPos = default;
        return false;
    }

    private void DisablePrayerTriggerIfAssigned()
    {
        if (prayerTriggerObject == null) return;

        if (!recordedPrayerTriggerState)
        {
            prayerTriggerWasActive = prayerTriggerObject.activeSelf;
            recordedPrayerTriggerState = true;
        }

        prayerTriggerObject.SetActive(false);
    }

    private void RestorePrayerTriggerIfNeeded()
    {
        if (prayerTriggerObject == null) return;
        if (!recordedPrayerTriggerState) return;

        prayerTriggerObject.SetActive(prayerTriggerWasActive);
        recordedPrayerTriggerState = false;
    }

    // Called by CeilingHandStrike when it touches the player
    public void OnHandHitPlayer()
    {
        if (!isActive) return;

        Debug.Log("[CeilingHandMinigame] Hand hit player. Triggering ceiling-pull death.");

        if (grabbedAudioSource != null && grabbedClip != null)
        {
            grabbedAudioSource.PlayOneShot(grabbedClip, grabbedVolume);
        }

        if (!string.IsNullOrEmpty(onGrabbedPlayMakerEvent))
        {
            bool sent = PlayMakerEventBridge.BroadcastGlobalEvent(onGrabbedPlayMakerEvent, this);
            if (!sent)
            {
                Debug.LogWarning($"[CeilingHandMinigame] Failed to broadcast PlayMaker event '{onGrabbedPlayMakerEvent}'.", this);
            }
        }

        if (playerController == null)
        {
            playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
        }

        if (playerController != null)
        {
            playerController.BeginCeilingPullDeath();
        }

        // Stop the minigame sequence; PlayMaker can handle the rest using isFinishedDying.
        CancelSequence();
    }

    private void BeginStrikeFlickerOverride()
    {
        // Candle override
        if (candlesToOverride != null)
        {
            for (int i = 0; i < candlesToOverride.Length; i++)
            {
                FlickeringCandle candle = candlesToOverride[i];
                if (candle == null) continue;
                candle.SetExternalFlickerOverride(
                    true,
                    overrideMinIntensity,
                    overrideMaxIntensity,
                    overrideIntensityBoost,
                    overrideBaseFlickerSpeed,
                    overrideFastestFlickerSpeed,
                    overrideProximityFactor);
            }
        }

        // Ceiling light on/off flicker
        recordedCeilingLightStates = false;
        if (ceilingLightFlickerRoutine != null)
        {
            StopCoroutine(ceilingLightFlickerRoutine);
            ceilingLightFlickerRoutine = null;
        }

        if (ceilingLightObject1 != null || ceilingLightObject2 != null)
        {
            ceilingLight1WasActive = ceilingLightObject1 != null && ceilingLightObject1.activeSelf;
            ceilingLight2WasActive = ceilingLightObject2 != null && ceilingLightObject2.activeSelf;
            recordedCeilingLightStates = true;

            bool shouldFlicker1 = ceilingLightObject1 != null && (!flickerCeilingLightsOnlyIfInitiallyOn || ceilingLight1WasActive);
            bool shouldFlicker2 = ceilingLightObject2 != null && (!flickerCeilingLightsOnlyIfInitiallyOn || ceilingLight2WasActive);

            if (shouldFlicker1 || shouldFlicker2)
            {
                ceilingLightFlickerRoutine = StartCoroutine(CeilingLightFlickerRoutine(shouldFlicker1, shouldFlicker2));
            }
        }
    }

    private void EndStrikeFlickerOverride()
    {
        if (candlesToOverride != null)
        {
            for (int i = 0; i < candlesToOverride.Length; i++)
            {
                FlickeringCandle candle = candlesToOverride[i];
                if (candle == null) continue;
                candle.SetExternalFlickerOverride(false, 0f, 0f, 0f, 0.1f, 0.05f, 1f);
            }
        }

        if (ceilingLightFlickerRoutine != null)
        {
            StopCoroutine(ceilingLightFlickerRoutine);
            ceilingLightFlickerRoutine = null;
        }

        RestoreCeilingLights();
    }

    private IEnumerator CeilingLightFlickerRoutine(bool flicker1, bool flicker2)
    {
        while (isActive)
        {
            float interval = Random.Range(
                Mathf.Max(0.01f, ceilingLightFlickerIntervalRange.x),
                Mathf.Max(0.01f, ceilingLightFlickerIntervalRange.y));

            // Quick off pulse
            if (flicker1 && ceilingLightObject1 != null) ceilingLightObject1.SetActive(false);
            if (flicker2 && ceilingLightObject2 != null) ceilingLightObject2.SetActive(false);

            yield return new WaitForSeconds(interval * 0.5f);

            if (flicker1 && ceilingLightObject1 != null) ceilingLightObject1.SetActive(true);
            if (flicker2 && ceilingLightObject2 != null) ceilingLightObject2.SetActive(true);

            yield return new WaitForSeconds(interval * 0.5f);
        }
    }

    private void RestoreCeilingLights()
    {
        if (!recordedCeilingLightStates) return;

        if (ceilingLightObject1 != null) ceilingLightObject1.SetActive(ceilingLight1WasActive);
        if (ceilingLightObject2 != null) ceilingLightObject2.SetActive(ceilingLight2WasActive);
    }

    private void CleanupActiveStrikeObjects()
    {
        // Stop flicker first so we restore light states.
        EndStrikeFlickerOverride();

        if (activeHandInstance != null)
        {
            Destroy(activeHandInstance.gameObject);
            activeHandInstance = null;
        }

        if (activeWarningInstance != null)
        {
            StopWarningGracefully(activeWarningInstance);
            activeWarningInstance = null;
        }
    }

    private void StartEncounterAudio()
    {
        if (encounterAudioRoutine != null)
        {
            StopCoroutine(encounterAudioRoutine);
            encounterAudioRoutine = null;
        }

        if (encounterAudioSource == null) return;
        if (encounterOneShotClips == null || encounterOneShotClips.Length == 0) return;

        encounterAudioRoutine = StartCoroutine(EncounterAudioRoutine());
    }

    private void StopEncounterAudio()
    {
        if (encounterAudioRoutine != null)
        {
            StopCoroutine(encounterAudioRoutine);
            encounterAudioRoutine = null;
        }
    }

    private void StartEncounterLoopAudio()
    {
        if (encounterLoopAudioSource == null) return;
        if (encounterLoopClip == null) return;

        if (encounterLoopFadeRoutine != null)
        {
            StopCoroutine(encounterLoopFadeRoutine);
            encounterLoopFadeRoutine = null;
        }

        encounterLoopAudioSource.clip = encounterLoopClip;
        encounterLoopAudioSource.loop = true;
        encounterLoopAudioSource.volume = encounterLoopVolume;
        if (!encounterLoopAudioSource.isPlaying)
        {
            encounterLoopAudioSource.Play();
        }
    }

    private void StopEncounterLoopAudio(bool fadeOut)
    {
        if (encounterLoopAudioSource == null) return;

        if (encounterLoopFadeRoutine != null)
        {
            StopCoroutine(encounterLoopFadeRoutine);
            encounterLoopFadeRoutine = null;
        }

        if (!fadeOut || encounterLoopFadeOutSeconds <= 0f)
        {
            if (encounterLoopAudioSource.isPlaying)
            {
                encounterLoopAudioSource.Stop();
            }
            return;
        }

        encounterLoopFadeRoutine = StartCoroutine(FadeOutAndStopLoopRoutine());
    }

    private IEnumerator FadeOutAndStopLoopRoutine()
    {
        if (encounterLoopAudioSource == null) yield break;

        float startVolume = encounterLoopAudioSource.volume;
        float duration = Mathf.Max(0.01f, encounterLoopFadeOutSeconds);
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            encounterLoopAudioSource.volume = Mathf.Lerp(startVolume, 0f, u);
            yield return null;
        }

        encounterLoopAudioSource.volume = 0f;
        encounterLoopAudioSource.Stop();

        // Restore the configured volume for the next run.
        encounterLoopAudioSource.volume = encounterLoopVolume;

        encounterLoopFadeRoutine = null;
    }

    private IEnumerator EncounterAudioRoutine()
    {
        // Small initial delay so the very first frame doesn't stinger.
        yield return null;

        while (isActive)
        {
            float min = Mathf.Max(0.01f, encounterOneShotIntervalRange.x);
            float max = Mathf.Max(min, encounterOneShotIntervalRange.y);
            float wait = Random.Range(min, max);

            float t = 0f;
            while (isActive && t < wait)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!isActive) yield break;
            if (encounterAudioSource == null) continue;
            if (encounterOneShotClips == null || encounterOneShotClips.Length == 0) continue;

            AudioClip clip = encounterOneShotClips[Random.Range(0, encounterOneShotClips.Length)];
            if (clip != null)
            {
                encounterAudioSource.PlayOneShot(clip, encounterOneShotVolume);
            }
        }
    }

    private void StopWarningGracefully(GameObject warningInstance)
    {
        if (warningInstance == null) return;

        ParticleSystem[] systems = warningInstance.GetComponentsInChildren<ParticleSystem>(true);
        if (systems == null || systems.Length == 0)
        {
            Destroy(warningInstance);
            return;
        }

        float maxLifetime = 0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps == null) continue;

            // Stop emission but let existing particles die naturally.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            ParticleSystem.MainModule main = ps.main;
            float duration = main.duration;
            float startDelay = GetMaxCurveValue(main.startDelay);
            float lifetime = GetMaxCurveValue(main.startLifetime);

            maxLifetime = Mathf.Max(maxLifetime, duration + startDelay + lifetime);
        }

        Destroy(warningInstance, maxLifetime + 0.25f);
    }

    private static float GetMaxCurveValue(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return curve.constant;
            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Max(curve.constantMin, curve.constantMax);
            case ParticleSystemCurveMode.Curve:
                return curve.curve != null ? curve.curve.keys[curve.curve.length - 1].value : 0f;
            case ParticleSystemCurveMode.TwoCurves:
                float a = curve.curveMin != null ? curve.curveMin.keys[curve.curveMin.length - 1].value : 0f;
                float b = curve.curveMax != null ? curve.curveMax.keys[curve.curveMax.length - 1].value : 0f;
                return Mathf.Max(a, b);
            default:
                return 0f;
        }
    }
}
