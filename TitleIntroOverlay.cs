using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TitleIntroOverlay : MonoBehaviour
{
    private static bool hasShownThisRun;

    public static bool IsOverlayActive { get; private set; }
    public static System.Action<bool> OverlayActiveChanged;

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text[] paragraphs;

    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionButtonLabel;
    [SerializeField] private string skipLabel = "Skip";
    [SerializeField] private string continueLabel = "Continue";

    [Header("Timing")]
    [Min(0f)] [SerializeField] private float paragraphFadeSeconds = 1.0f;
    [Min(0f)] [SerializeField] private float delayBetweenParagraphsSeconds = 0.75f;
    [SerializeField] private bool clickToAdvance = true;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Display")]
    [Tooltip("If enabled, the intro overlay will only show once per app launch.")]
    [SerializeField] private bool showOnlyOncePerAppLaunch = true;

    [Tooltip("If enabled, the intro overlay will only ever show once (saved in PlayerPrefs).")]
    [SerializeField] private bool persistSkipAcrossLaunches = false;

    [SerializeField] private string persistSkipPlayerPrefsKey = "TitleIntroOverlay_Shown";

    [Header("Editor Convenience")]
    [Tooltip("If true, this script will auto-enable its GameObject at runtime after scene load (helps if you disable it in the editor to work on the title screen).")]
    [SerializeField] private bool autoEnableOnSceneStart = true;

    [Tooltip("If true, will force the referenced CanvasGroup's GameObject active when the overlay is going to show.")]
    [SerializeField] private bool autoEnableCanvasGroupOnShow = true;

    [Header("Audio")]
    [Tooltip("The title screen BGM AudioSource that normally loops on the title screen.")]
    [SerializeField] private AudioSource titleBgmSource;

    [Tooltip("Optional: If enabled, forces the title BGM volume to this value when the overlay dismisses.")]
    [SerializeField] private bool forceTitleBgmVolumeOnDismiss = false;

    [Range(0f, 1f)]
    [SerializeField] private float titleBgmTargetVolume = 1f;

    [Tooltip("Optional intro music AudioSource (on this overlay). Will be faded in/out if assigned.")]
    [SerializeField] private AudioSource introMusicSource;

    [Range(0f, 1f)]
    [SerializeField] private float introMusicTargetVolume = 0.7f;

    [Min(0f)] [SerializeField] private float introMusicFadeInSeconds = 1.0f;
    [Min(0f)] [SerializeField] private float introMusicFadeOutSeconds = 1.0f;

    [Header("Overlay Gating (Optional)")]
    [Tooltip("Optional: Any GameObjects to disable while the overlay is active, then re-enable when dismissed/skipped.")]
    [SerializeField] private GameObject[] objectsToDisableWhileOverlayActive;

    [Tooltip("Optional: Any behaviours to disable while the overlay is active, then re-enable when dismissed/skipped.")]
    [SerializeField] private MonoBehaviour[] behavioursToDisableWhileOverlayActive;

    [Tooltip("Optional: AudioSources to stop while the overlay is active (prevents Play-On-Awake blips).")]
    [SerializeField] private AudioSource[] audioSourcesToStopWhileOverlayActive;

    [Tooltip("Optional: AudioSources to start playing when the overlay is dismissed/skipped (ex: a static loop).")]
    [SerializeField] private AudioSource[] audioSourcesToPlayOnDismiss;

    [Header("Orbiters (Optional)")]
    [Tooltip("Optional: Orbits to enable/disable with the title intro overlay (e.g., a praying-loop sound orbiter).")]
    [SerializeField] private OrbitAroundAudioListener[] orbiters;

    [Tooltip("Optional: AudioSources to start/stop with the title intro overlay. If empty, the script will also try to use AudioSources found on the orbiters.")]
    [SerializeField] private AudioSource[] orbiterAudioSources;

    [Tooltip("If true, orbiters are disabled and their AudioSources are stopped while the overlay is active.")]
    [SerializeField] private bool disableOrbitersWhileOverlayActive = true;

    [Tooltip("If true, orbiter AudioSources will Play() when the overlay is dismissed/skipped.")]
    [SerializeField] private bool playOrbiterAudioOnDismiss = true;

    private enum SequenceState
    {
        None,
        FadingParagraph,
        WaitingBetweenParagraphs,
        WaitingForButton
    }

    private SequenceState state = SequenceState.None;
    private bool advancePressed;
    private bool isClosing;
    private Coroutine sequenceCoroutine;
    private Coroutine introMusicFadeCoroutine;

    private GameObject OverlayRoot => canvasGroup != null ? canvasGroup.gameObject : null;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        // Stop the title BGM as early as we can (prevents a Play-On-Awake blip if this runs first).
        if (titleBgmSource != null)
        {
            titleBgmSource.Stop();
        }

        // Gate any other objects/behaviours/audio while the overlay is active.
        if (disableOrbitersWhileOverlayActive)
        {
            SetObjectsEnabled(objectsToDisableWhileOverlayActive, false);
            SetBehavioursEnabled(behavioursToDisableWhileOverlayActive, false);

            StopAudioSources(audioSourcesToStopWhileOverlayActive);
            StopAudioSources(audioSourcesToPlayOnDismiss);
        }

        if (disableOrbitersWhileOverlayActive)
        {
            SetOrbitersEnabled(false);
            StopOrbiterAudio();
        }

        if (actionButton != null)
        {
            actionButton.onClick.AddListener(OnActionButtonClicked);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        PrepareParagraphs();
        UpdateActionButtonLabel(isComplete: false);
    }

    private void OnEnable()
    {
        if (ShouldSkipBecauseAlreadyShown())
        {
            SkipOverlayImmediate();
            return;
        }

        // If the canvas was left disabled in the editor, ensure it is active at runtime.
        if (autoEnableCanvasGroupOnShow)
            ShowOverlayRootImmediate();

        SetOverlayActive(true);

        MarkAsShown();

        advancePressed = false;
        isClosing = false;

        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        sequenceCoroutine = StartCoroutine(RunSequence());
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        hasShownThisRun = false;
        IsOverlayActive = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoEnableOverlaysAfterSceneLoad()
    {
        // Best-effort: find overlays even if they are inactive, and ensure they can show.
        // This avoids the "I forgot to re-enable it" pitfall during authoring.
        var overlays = Resources.FindObjectsOfTypeAll<TitleIntroOverlay>();
        if (overlays == null || overlays.Length == 0)
            return;

        for (int i = 0; i < overlays.Length; i++)
        {
            var overlay = overlays[i];
            if (overlay == null) continue;
            if (!overlay.autoEnableOnSceneStart) continue;

            var scene = overlay.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            // Only auto-enable if it's actually supposed to show.
            if (overlay.ShouldSkipBecauseAlreadyShown())
                continue;

            if (!overlay.gameObject.activeSelf)
                overlay.gameObject.SetActive(true);

            if (overlay.autoEnableCanvasGroupOnShow)
                overlay.ShowOverlayRootImmediate();
        }
    }

    private void OnDisable()
    {
        // Safety: ensure we never get stuck "active".
        SetOverlayActive(false);
    }

    private bool ShouldSkipBecauseAlreadyShown()
    {
        if (persistSkipAcrossLaunches && PlayerPrefs.GetInt(persistSkipPlayerPrefsKey, 0) == 1)
        {
            return true;
        }

        if (showOnlyOncePerAppLaunch && hasShownThisRun)
        {
            return true;
        }

        return false;
    }

    private void MarkAsShown()
    {
        hasShownThisRun = true;

        if (persistSkipAcrossLaunches)
        {
            PlayerPrefs.SetInt(persistSkipPlayerPrefsKey, 1);
            PlayerPrefs.Save();
        }
    }

    private void SkipOverlayImmediate()
    {
        SetOverlayActive(false);

        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        if (introMusicFadeCoroutine != null)
        {
            StopCoroutine(introMusicFadeCoroutine);
            introMusicFadeCoroutine = null;
        }

        if (introMusicSource != null && introMusicSource.isPlaying)
        {
            introMusicSource.Stop();
        }

        if (titleBgmSource != null && !titleBgmSource.isPlaying)
        {
            if (forceTitleBgmVolumeOnDismiss)
            {
                titleBgmSource.volume = titleBgmTargetVolume;
            }

            titleBgmSource.Play();
        }

        if (disableOrbitersWhileOverlayActive)
        {
            SetObjectsEnabled(objectsToDisableWhileOverlayActive, true);
            SetBehavioursEnabled(behavioursToDisableWhileOverlayActive, true);

            PlayAudioSources(audioSourcesToPlayOnDismiss);
        }

        if (disableOrbitersWhileOverlayActive)
        {
            SetOrbitersEnabled(true);
            if (playOrbiterAudioOnDismiss)
            {
                PlayOrbiterAudio();
            }
        }

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        HideOverlayRootImmediate();
    }

    private void Update()
    {
        if (!clickToAdvance) return;
        if (isClosing) return;
        if (state == SequenceState.WaitingForButton) return;

        if (Input.GetMouseButtonDown(0))
        {
            advancePressed = true;
        }
    }

    private void PrepareParagraphs()
    {
        if (paragraphs == null) return;

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i];
            if (paragraph == null) continue;

            paragraph.alpha = 0f;
            paragraph.gameObject.SetActive(false);
        }
    }

    private IEnumerator RunSequence()
    {
        PrepareParagraphs();
        UpdateActionButtonLabel(isComplete: false);

        if (actionButton != null)
        {
            actionButton.interactable = true;
        }

        if (titleBgmSource != null)
        {
            titleBgmSource.Stop();
        }

        if (introMusicSource != null)
        {
            if (!introMusicSource.isPlaying)
            {
                introMusicSource.volume = 0f;
                introMusicSource.Play();
            }

            if (introMusicFadeInSeconds <= 0f)
            {
                introMusicSource.volume = introMusicTargetVolume;
            }
            else
            {
                if (introMusicFadeCoroutine != null)
                {
                    StopCoroutine(introMusicFadeCoroutine);
                }
                introMusicFadeCoroutine = StartCoroutine(FadeAudio(introMusicSource, 0f, introMusicTargetVolume, introMusicFadeInSeconds));
            }
        }

        if (paragraphs == null || paragraphs.Length == 0)
        {
            state = SequenceState.WaitingForButton;
            UpdateActionButtonLabel(isComplete: true);
            yield break;
        }

        int lastIndex = paragraphs.Length - 1;

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i];
            if (paragraph == null) continue;

            yield return StartCoroutine(FadeInParagraph(paragraph));

            bool allRevealed = i >= lastIndex;
            UpdateActionButtonLabel(isComplete: allRevealed);

            if (allRevealed)
            {
                state = SequenceState.WaitingForButton;
                yield break;
            }

            state = SequenceState.WaitingBetweenParagraphs;
            yield return StartCoroutine(WaitForSecondsOrAdvance(delayBetweenParagraphsSeconds));
        }

        state = SequenceState.WaitingForButton;
        UpdateActionButtonLabel(isComplete: true);
    }

    private IEnumerator FadeInParagraph(TMP_Text paragraph)
    {
        paragraph.gameObject.SetActive(true);

        state = SequenceState.FadingParagraph;
        advancePressed = false;

        if (paragraphFadeSeconds <= 0f)
        {
            paragraph.alpha = 1f;
            yield break;
        }

        float elapsed = 0f;
        paragraph.alpha = 0f;

        while (elapsed < paragraphFadeSeconds)
        {
            if (advancePressed)
            {
                // Click skips the remainder of the fade.
                paragraph.alpha = 1f;
                yield break;
            }

            elapsed += GetDeltaTime();
            float t = Mathf.Clamp01(elapsed / paragraphFadeSeconds);
            paragraph.alpha = t;
            yield return null;
        }

        paragraph.alpha = 1f;
    }

    private IEnumerator WaitForSecondsOrAdvance(float seconds)
    {
        if (seconds <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            if (advancePressed)
            {
                advancePressed = false;
                yield break;
            }

            elapsed += GetDeltaTime();
            yield return null;
        }
    }

    private void OnActionButtonClicked()
    {
        if (isClosing) return;
        isClosing = true;

        if (actionButton != null)
        {
            actionButton.interactable = false;
        }

        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        if (introMusicSource != null && introMusicSource.isPlaying)
        {
            if (introMusicFadeCoroutine != null)
            {
                StopCoroutine(introMusicFadeCoroutine);
                introMusicFadeCoroutine = null;
            }

            if (introMusicFadeOutSeconds <= 0f)
            {
                introMusicSource.volume = 0f;
            }
            else
            {
                yield return StartCoroutine(FadeAudio(introMusicSource, introMusicSource.volume, 0f, introMusicFadeOutSeconds));
            }

            introMusicSource.Stop();
        }

        if (titleBgmSource != null && !titleBgmSource.isPlaying)
        {
            if (forceTitleBgmVolumeOnDismiss)
            {
                titleBgmSource.volume = titleBgmTargetVolume;
            }

            titleBgmSource.Play();
        }

        if (disableOrbitersWhileOverlayActive)
        {
            SetObjectsEnabled(objectsToDisableWhileOverlayActive, true);
            SetBehavioursEnabled(behavioursToDisableWhileOverlayActive, true);

            PlayAudioSources(audioSourcesToPlayOnDismiss);
        }

        if (disableOrbitersWhileOverlayActive)
        {
            SetOrbitersEnabled(true);
            if (playOrbiterAudioOnDismiss)
            {
                PlayOrbiterAudio();
            }
        }

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        SetOverlayActive(false);

        HideOverlayRootImmediate();
    }

    private void ShowOverlayRootImmediate()
    {
        var root = OverlayRoot;
        if (root == null)
            return;

        if (!root.activeSelf)
            root.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void HideOverlayRootImmediate()
    {
        var root = OverlayRoot;
        if (root == null)
            return;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (root.activeSelf)
            root.SetActive(false);
    }

    private static void SetOverlayActive(bool active)
    {
        if (IsOverlayActive == active) return;
        IsOverlayActive = active;
        OverlayActiveChanged?.Invoke(active);
    }

    private void SetOrbitersEnabled(bool enabled)
    {
        if (orbiters == null) return;

        for (int i = 0; i < orbiters.Length; i++)
        {
            var orbiter = orbiters[i];
            if (orbiter == null) continue;
            orbiter.enabled = enabled;
        }
    }

    private static void SetObjectsEnabled(GameObject[] objects, bool enabled)
    {
        if (objects == null) return;

        for (int i = 0; i < objects.Length; i++)
        {
            var go = objects[i];
            if (go == null) continue;
            go.SetActive(enabled);
        }
    }

    private static void SetBehavioursEnabled(MonoBehaviour[] behaviours, bool enabled)
    {
        if (behaviours == null) return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;
            b.enabled = enabled;
        }
    }

    private static void StopAudioSources(AudioSource[] sources)
    {
        if (sources == null) return;

        for (int i = 0; i < sources.Length; i++)
        {
            var s = sources[i];
            if (s == null) continue;
            if (s.isPlaying) s.Stop();
        }
    }

    private static void PlayAudioSources(AudioSource[] sources)
    {
        if (sources == null) return;

        for (int i = 0; i < sources.Length; i++)
        {
            var s = sources[i];
            if (s == null) continue;
            if (!s.isPlaying && s.clip != null) s.Play();
        }
    }

    private void StopOrbiterAudio()
    {
        foreach (var source in EnumerateOrbiterAudioSources())
        {
            if (source == null) continue;
            if (source.isPlaying) source.Stop();
        }
    }

    private void PlayOrbiterAudio()
    {
        foreach (var source in EnumerateOrbiterAudioSources())
        {
            if (source == null) continue;
            if (source.clip == null) continue;
            if (!source.isPlaying) source.Play();
        }
    }

    private System.Collections.Generic.IEnumerable<AudioSource> EnumerateOrbiterAudioSources()
    {
        // Prefer explicit list.
        if (orbiterAudioSources != null)
        {
            for (int i = 0; i < orbiterAudioSources.Length; i++)
            {
                var s = orbiterAudioSources[i];
                if (s != null) yield return s;
            }
        }

        // Fallback: grab AudioSource on each orbiter.
        if (orbiters != null)
        {
            for (int i = 0; i < orbiters.Length; i++)
            {
                var orbiter = orbiters[i];
                if (orbiter == null) continue;
                var s = orbiter.GetComponent<AudioSource>();
                if (s != null) yield return s;
            }
        }
    }

    private IEnumerator FadeAudio(AudioSource source, float startVolume, float endVolume, float duration)
    {
        if (source == null) yield break;
        if (duration <= 0f)
        {
            source.volume = endVolume;
            yield break;
        }

        float elapsed = 0f;
        source.volume = startVolume;

        while (elapsed < duration)
        {
            elapsed += GetDeltaTime();
            float t = Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(startVolume, endVolume, t);
            yield return null;
        }

        source.volume = endVolume;
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private void UpdateActionButtonLabel(bool isComplete)
    {
        if (actionButtonLabel == null) return;
        actionButtonLabel.text = isComplete ? continueLabel : skipLabel;
    }
}
