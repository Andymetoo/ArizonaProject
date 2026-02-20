using UnityEngine;

/// <summary>
/// Directly drives AudioLowPassFilter / AudioHighPassFilter / AudioReverbFilter
/// on a set of referenced GameObjects based on:
/// - Traveler ring: Inner vs Outer (from TravelerGridCueProfileDriver)
/// - Listener angle: Front / Side / Back bands
///
/// This is meant to be a simple, "layman's slider" system and can be used instead of AudioCueColorizer.
/// </summary>
[DisallowMultipleComponent]
public class GridInnerOuterAngleFilterDriver : MonoBehaviour
{
    public enum AngleBand
    {
        Back,
        Side,
        Front
    }

    [System.Serializable]
    public class Target
    {
        [Tooltip("Root object to affect. If filters are null, they will be auto-found on this object.")]
        public GameObject gameObject;

        [Header("Optional overrides")]
        public AudioLowPassFilter lowPass;
        public AudioHighPassFilter highPass;
        public AudioReverbFilter reverb;

        [Tooltip("If true, prevents AudioCueColorizer on this target from driving filters/angle shaping to avoid fighting. (Volume/external gain can remain active.)")]
        public bool disableColorizer = true;

        [Tooltip("If true, disables TravelerFacingCue's low-pass shaping on this target (if present) to avoid fighting over AudioLowPassFilter.cutoffFrequency.")]
        public bool disableTravelerFacingCueLowPassShaping = false;

        [Tooltip("If true, attempts to find filters/colorizer on children too (GetComponentInChildren). Useful when AudioSource+filters live on a child object.")]
        public bool searchInChildren = false;

        [Tooltip("If true, forces filter components to stay enabled while this driver is active.")]
        public bool forceEnableFilters = false;

        [HideInInspector] public bool initialized;
    }

    [Header("References")]
    [SerializeField] private TravelerGridCueProfileDriver gridKnower;
    [Tooltip("If null, uses Camera.main.")]
    [SerializeField] private Transform listener;
    [Tooltip("The world position we treat as the sound emitter for angle checks. If null, uses gridKnower's GameObject transform.")]
    [SerializeField] private Transform emitter;

    [Header("Targets")]
    [SerializeField] private Target[] targets;

    [Header("Ring (Inner vs Outer) - Layman sliders")]
    [Tooltip("0 = crisp/dry, 1 = muffled.")]
    [Range(0f, 1f)]
    [SerializeField] private float innerMuffle = 0.10f;
    [Tooltip("0 = crisp/dry, 1 = muffled.")]
    [Range(0f, 1f)]
    [SerializeField] private float outerMuffle = 0.65f;

    [Tooltip("0 = no reverb, 1 = noticeable (still subtle).")]
    [Range(0f, 1f)]
    [SerializeField] private float innerReverb = 0.05f;
    [Tooltip("0 = no reverb, 1 = noticeable (still subtle).")]
    [Range(0f, 1f)]
    [SerializeField] private float outerReverb = 0.35f;

    [Header("Angle (Front/Side/Back)")]
    [Tooltip("Strength of the angle effect (front/back extremes). 0 disables angle shaping.")]
    [Range(0f, 1f)]
    [SerializeField] private float angleStrength = 0.85f;

    [Tooltip("Angles <= this are treated as FRONT.")]
    [Range(0f, 90f)]
    [SerializeField] private float frontConeDegrees = 35f;

    [Tooltip("Angles >= (180 - this) are treated as BACK.")]
    [Range(0f, 90f)]
    [SerializeField] private float backConeDegrees = 35f;

    [Header("Front/Side/Back filter tuning")]
    [Tooltip("How muffled BACK is relative to the ring base. 0 = none, 1 = extreme.")]
    [Range(0f, 1f)]
    [SerializeField] private float backMuffle = 1.0f;

    [Tooltip("How thin/bright FRONT is relative to the ring base. 0 = none, 1 = extreme.")]
    [Range(0f, 1f)]
    [SerializeField] private float frontPresence = 0.85f;

    [Header("Filter extremes (advanced, but you can ignore)")]
    [SerializeField] private float lpfOpenHz = 22000f;
    [SerializeField] private float lpfClosedHz = 900f;

    [SerializeField] private float hpfLowHz = 20f;
    [SerializeField] private float hpfHighHz = 450f;

    [Header("Reverb mapping")]
    [Tooltip("AudioReverbFilter.reverbLevel at 0 reverb slider (typically effectively off).")]
    [SerializeField] private float reverbMinLevel = -10000f;
    [Tooltip("AudioReverbFilter.reverbLevel at 1 reverb slider (subtle, not a bathroom).")]
    [SerializeField] private float reverbMaxLevel = -2200f;

    [Header("Smoothing")]
    [Min(0f)]
    [SerializeField] private float lerpSeconds = 0.10f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool debugLogTargetInit = false;
    [SerializeField] private bool debugDetectFighting = true;
    [Min(0f)]
    [SerializeField] private float debugMismatchToleranceHz = 5f;
    [Min(0f)]
    [SerializeField] private float debugMismatchToleranceReverb = 10f;

    private bool warnedMissingRefs;

    private struct ExpectedDriven
    {
        public bool has;
        public float lpf;
        public float hpf;
        public float rev;
        public bool lpfEnabled;
        public bool hpfEnabled;
        public bool revEnabled;
    }

    private ExpectedDriven[] expected;

    private float currentLPF;
    private float currentHPF;
    private float currentReverb;

    private void Awake()
    {
        if (gridKnower == null)
        {
            gridKnower = FindFirstObjectByType<TravelerGridCueProfileDriver>();
        }

        if (listener == null && Camera.main != null)
        {
            listener = Camera.main.transform;
        }

        if (emitter == null)
        {
            emitter = gridKnower != null ? gridKnower.transform : transform;
        }

        InitializeTargets();

        expected = targets != null ? new ExpectedDriven[targets.Length] : null;

        // Initialize to a sane baseline so filters don't jump on first frame.
        currentLPF = lpfOpenHz;
        currentHPF = hpfLowHz;
        currentReverb = reverbMinLevel;
    }

    private void OnEnable()
    {
        InitializeTargets();

        // Reset fighting detection so we don't log stale mismatches.
        if (expected != null)
        {
            for (int i = 0; i < expected.Length; i++) expected[i].has = false;
        }
    }

    private void InitializeTargets()
    {
        if (targets == null) return;

        for (int i = 0; i < targets.Length; i++)
        {
            Target t = targets[i];
            if (t == null || t.gameObject == null) continue;

            bool facingCueLowPassSuppressed = false;

            // Allow re-init if we previously couldn't find components.
            if (t.initialized && t.lowPass != null && t.highPass != null && t.reverb != null)
            {
                continue;
            }

            if (t.lowPass == null)
            {
                t.lowPass = t.gameObject.GetComponent<AudioLowPassFilter>();
                if (t.lowPass == null && t.searchInChildren) t.lowPass = t.gameObject.GetComponentInChildren<AudioLowPassFilter>(true);
            }
            if (t.highPass == null)
            {
                t.highPass = t.gameObject.GetComponent<AudioHighPassFilter>();
                if (t.highPass == null && t.searchInChildren) t.highPass = t.gameObject.GetComponentInChildren<AudioHighPassFilter>(true);
            }
            if (t.reverb == null)
            {
                t.reverb = t.gameObject.GetComponent<AudioReverbFilter>();
                if (t.reverb == null && t.searchInChildren) t.reverb = t.gameObject.GetComponentInChildren<AudioReverbFilter>(true);
            }

            if (t.disableColorizer)
            {
                AudioCueColorizer c = t.gameObject.GetComponent<AudioCueColorizer>();
                if (c == null && t.searchInChildren) c = t.gameObject.GetComponentInChildren<AudioCueColorizer>(true);
                if (c != null)
                {
                    // Avoid fighting: this driver owns the filters. Keep the colorizer enabled so
                    // it can still drive AudioSource.volume (and respond to externalGain).
                    c.SetDriveLowPass(false);
                    c.SetDriveHighPass(false);
                    c.SetDriveReverb(false);
                    c.SetFrontBackShapingEnabled(false);
                }
            }

            if (t.disableTravelerFacingCueLowPassShaping)
            {
                TravelerFacingCue facingCue = t.gameObject.GetComponent<TravelerFacingCue>();
                if (facingCue == null && t.searchInChildren) facingCue = t.gameObject.GetComponentInChildren<TravelerFacingCue>(true);
                if (facingCue != null)
                {
                    facingCue.SetLowPassShapingEnabled(false);
                    facingCueLowPassSuppressed = true;
                }
            }

            t.initialized = true;

            if (debugLogTargetInit)
            {
                string lp = t.lowPass != null ? $"LPF(en={(t.lowPass.enabled ? 1 : 0)} cutoff={t.lowPass.cutoffFrequency:0})" : "LPF(null)";
                string hp = t.highPass != null ? $"HPF(en={(t.highPass.enabled ? 1 : 0)} cutoff={t.highPass.cutoffFrequency:0})" : "HPF(null)";
                string rv = t.reverb != null ? $"Rev(en={(t.reverb.enabled ? 1 : 0)} level={t.reverb.reverbLevel:0})" : "Rev(null)";
                AudioCueColorizer cz = t.gameObject.GetComponent<AudioCueColorizer>();
                if (cz == null && t.searchInChildren) cz = t.gameObject.GetComponentInChildren<AudioCueColorizer>(true);
                string czs = cz != null ? $"Colorizer(en={(cz.enabled ? 1 : 0)})" : "Colorizer(null)";
                string fcs = $"FacingCueLPFOff={(facingCueLowPassSuppressed ? 1 : 0)}";
                Debug.Log($"[GridInnerOuterAngleFilterDriver] TargetInit[{i}] obj={t.gameObject.name} searchInChildren={(t.searchInChildren ? 1 : 0)} forceEnable={(t.forceEnableFilters ? 1 : 0)} {lp} {hp} {rv} {czs} {fcs}", this);
            }
        }
    }

    private void Update()
    {
        if (gridKnower == null || emitter == null)
        {
            if (!warnedMissingRefs && debugLog)
            {
                warnedMissingRefs = true;
                Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] Missing refs. gridKnower={(gridKnower != null ? gridKnower.name : "(null)")} emitter={(emitter != null ? emitter.name : "(null)")}", this);
            }
            return;
        }

        if (debugDetectFighting)
        {
            DetectFighting();
        }

        var ring = gridKnower.GetCurrentRingType();

        bool isInner = ring == TravelerGridCueProfileDriver.RingType.Inner;
        bool isOuter = ring == TravelerGridCueProfileDriver.RingType.Outer;

        // If center/out-of-bounds: treat as inner (safer/less confusing)
        float ringMuffle = isOuter ? outerMuffle : innerMuffle;
        float ringReverb = isOuter ? outerReverb : innerReverb;

        // Ring -> base filters
        float baseLPF = Mathf.Lerp(lpfOpenHz, lpfClosedHz, Mathf.Clamp01(ringMuffle));
        float baseHPF = Mathf.Lerp(hpfLowHz, hpfHighHz, Mathf.Clamp01(ringMuffle * 0.60f));
        float baseReverb = Mathf.Lerp(reverbMinLevel, reverbMaxLevel, Mathf.Clamp01(ringReverb));

        // Angle band shaping
        float targetLPF = baseLPF;
        float targetHPF = baseHPF;

        if (angleStrength > 0f && listener != null)
        {
            AngleBand band = GetAngleBand(listener, emitter);

            // Back: more muffled. Front: more presence (HPF up, LPF open).
            float lpfAngle = baseLPF;
            float hpfAngle = baseHPF;

            if (band == AngleBand.Back)
            {
                float extra = Mathf.Lerp(0f, 1f, backMuffle);
                lpfAngle = Mathf.Lerp(baseLPF, lpfClosedHz, extra);
                hpfAngle = Mathf.Lerp(baseHPF, hpfLowHz, extra * 0.50f);
            }
            else if (band == AngleBand.Front)
            {
                float extra = Mathf.Lerp(0f, 1f, frontPresence);
                lpfAngle = Mathf.Lerp(baseLPF, lpfOpenHz, extra);
                hpfAngle = Mathf.Lerp(baseHPF, hpfHighHz, extra);
            }
            else
            {
                // Side: keep base.
                lpfAngle = baseLPF;
                hpfAngle = baseHPF;
            }

            targetLPF = Mathf.Lerp(baseLPF, lpfAngle, angleStrength);
            targetHPF = Mathf.Lerp(baseHPF, hpfAngle, angleStrength);
        }

        float t = lerpSeconds <= 0f ? 1f : Mathf.Clamp01(Time.deltaTime / lerpSeconds);
        currentLPF = Mathf.Lerp(currentLPF, targetLPF, t);
        currentHPF = Mathf.Lerp(currentHPF, targetHPF, t);
        currentReverb = Mathf.Lerp(currentReverb, baseReverb, t);

        ApplyToTargets(currentLPF, currentHPF, currentReverb);

        if (debugLog)
        {
            Debug.Log($"[GridInnerOuterAngleFilterDriver] ring={ring} LPF={currentLPF:0} HPF={currentHPF:0} Reverb={currentReverb:0}");
        }
    }

    private void ApplyToTargets(float lpfHz, float hpfHz, float reverbLevel)
    {
        if (targets == null) return;

        lpfHz = Mathf.Clamp(lpfHz, 10f, 22000f);
        hpfHz = Mathf.Clamp(hpfHz, 10f, 22000f);

        for (int i = 0; i < targets.Length; i++)
        {
            Target t = targets[i];
            if (t == null || t.gameObject == null) continue;

            // If targets were assigned after Awake or were missing components on first init, try again.
            if (!t.initialized || t.lowPass == null || t.highPass == null || t.reverb == null)
            {
                InitializeTargets();
            }

            if (t.forceEnableFilters)
            {
                if (t.lowPass != null && !t.lowPass.enabled) t.lowPass.enabled = true;
                if (t.highPass != null && !t.highPass.enabled) t.highPass.enabled = true;
                if (t.reverb != null && !t.reverb.enabled) t.reverb.enabled = true;
            }

            if (t.lowPass != null) t.lowPass.cutoffFrequency = lpfHz;
            if (t.highPass != null) t.highPass.cutoffFrequency = hpfHz;
            if (t.reverb != null) t.reverb.reverbLevel = reverbLevel;

            if (expected != null && i < expected.Length)
            {
                expected[i].has = true;
                expected[i].lpf = lpfHz;
                expected[i].hpf = hpfHz;
                expected[i].rev = reverbLevel;
                expected[i].lpfEnabled = t.lowPass != null && t.lowPass.enabled;
                expected[i].hpfEnabled = t.highPass != null && t.highPass.enabled;
                expected[i].revEnabled = t.reverb != null && t.reverb.enabled;
            }
        }
    }

    private void DetectFighting()
    {
        if (targets == null || expected == null) return;

        for (int i = 0; i < targets.Length && i < expected.Length; i++)
        {
            if (!expected[i].has) continue;

            Target t = targets[i];
            if (t == null || t.gameObject == null) continue;

            // If components are missing, that's the most common cause of "not working".
            if (t.lowPass == null && t.highPass == null && t.reverb == null)
            {
                if (debugLog)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] Target[{i}] {t.gameObject.name} has no filters assigned/found. If filters are on a child, enable searchInChildren.", this);
                }
                continue;
            }

            if (t.lowPass != null)
            {
                if (Mathf.Abs(t.lowPass.cutoffFrequency - expected[i].lpf) > debugMismatchToleranceHz)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} LPF cutoff overwritten. expected={expected[i].lpf:0} actual={t.lowPass.cutoffFrequency:0} enabled={(t.lowPass.enabled ? 1 : 0)}", t.lowPass);
                    expected[i].has = false;
                    continue;
                }

                if (expected[i].lpfEnabled && !t.lowPass.enabled)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} LPF got DISABLED after we enabled it.", t.lowPass);
                    expected[i].has = false;
                    continue;
                }
            }

            if (t.highPass != null)
            {
                if (Mathf.Abs(t.highPass.cutoffFrequency - expected[i].hpf) > debugMismatchToleranceHz)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} HPF cutoff overwritten. expected={expected[i].hpf:0} actual={t.highPass.cutoffFrequency:0} enabled={(t.highPass.enabled ? 1 : 0)}", t.highPass);
                    expected[i].has = false;
                    continue;
                }

                if (expected[i].hpfEnabled && !t.highPass.enabled)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} HPF got DISABLED after we enabled it.", t.highPass);
                    expected[i].has = false;
                    continue;
                }
            }

            if (t.reverb != null)
            {
                if (Mathf.Abs(t.reverb.reverbLevel - expected[i].rev) > debugMismatchToleranceReverb)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} Reverb level overwritten. expected={expected[i].rev:0} actual={t.reverb.reverbLevel:0} enabled={(t.reverb.enabled ? 1 : 0)}", t.reverb);
                    expected[i].has = false;
                    continue;
                }

                if (expected[i].revEnabled && !t.reverb.enabled)
                {
                    Debug.LogWarning($"[GridInnerOuterAngleFilterDriver] POSSIBLE FIGHT Target[{i}] {t.gameObject.name} Reverb got DISABLED after we enabled it.", t.reverb);
                    expected[i].has = false;
                    continue;
                }
            }
        }
    }

    private AngleBand GetAngleBand(Transform listenerTr, Transform emitterTr)
    {
        Vector3 toEmitter = emitterTr.position - listenerTr.position;
        toEmitter.y = 0f;
        if (toEmitter.sqrMagnitude < 0.0001f) return AngleBand.Side;

        Vector3 fwd = listenerTr.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return AngleBand.Side;

        float angle = Vector3.Angle(fwd, toEmitter);
        if (angle <= frontConeDegrees) return AngleBand.Front;
        if (angle >= 180f - backConeDegrees) return AngleBand.Back;
        return AngleBand.Side;
    }
}
