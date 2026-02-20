using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TapePlayerController : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource tape1AudioSource; // Assign Tape 1 Audio
    public AudioSource tape2AudioSource; // Assign Tape 2 Audio
    public AudioSource tape3AudioSource;

    private AudioSource currentAudioSource; // Active Tape

    [Header("Tape Settings")]
    public float rewindSpeed = 5f; // Rewind speed (seconds per second)
    public float fastForwardSpeed = 5f; // Fast-forward speed (seconds per second)
    private float rewindHoldTime = 0f;
    private float fastForwardHoldTime = 0f;
    private float safeEndMargin = 0.2f; // Safe margin from the end of tape
    private float rewindThreshold = 0.5f; // How much rewind is needed to clear end state

    [Header("Sound Effects")]
    public AudioClip buttonPressSFX;
    public AudioClip buttonClickSFX;
    public AudioClip fastForwardLoopSFX;
    public AudioClip rewindLoopSFX;
    private AudioSource sfxSource; // For sound effects
    public AudioClip tapeSwapSFX; // Sound when switching tapes

    private bool isPlaying = false;
    private bool isFastForwarding = false;
    private bool isRewinding = false;
    private int selectedTape = 0; // 0 means no tape selected
    private ZoomInteraction zoomSystem; // Reference to ZoomInteraction

    // Track if tape has reached the end and requires rewind
    private bool tape1ReachedEnd = false;
    private bool tape2ReachedEnd = false;

    [Header("UI Elements")]
    public Image tape1Button; // Assign in Inspector
    public Image tape2Button;
    public Image tape3Button;

    public Image playPauseButton;
    public Image rewindButton;
    public Image fastForwardButton;

    [Header("UI Sprites")]
    public Sprite idleTape;
    public Sprite selectedTapeSprite;
    public Sprite idlePlay;
    public Sprite selectedPlay;
    public Sprite idleRewind;
    public Sprite selectedRewind;
    public Sprite idleFastForward;
    public Sprite selectedFastForward;

    [Header("Prayer")]
    public GameObject prayerTrigger;
    public GameObject prayerText;
    private bool skipFirstSelection = true; // Prevents sound on first selection

    [Header("UI Progress Bar")]
    public Slider tapeProgressSlider; // Assign in Inspector
    [Header("Subtitles")]
    public SubtitleEntry[] tape1Subtitles;
    public SubtitleEntry[] tape2Subtitles;
    public SubtitleEntry[] tape3Subtitles;

    public TMPro.TMP_Text subtitleText; // Assign your subtitle UI here

    private SubtitleEntry[] currentSubtitles;
    private int currentSubtitleIndex = -1;
    private Coroutine subtitleFadeCoroutine;


    void Awake()
    {
        zoomSystem = FindObjectOfType<ZoomInteraction>(); // Find Zoom System
    }

    void Start()
    {
        sfxSource = gameObject.AddComponent<AudioSource>();

        // Safety check to ensure audio sources have clips
        ValidateAudioSources();

        UpdateUI(); // Ensure UI initializes before selecting tape
        SelectTape(1); // Now, properly selects Tape 1 and updates UI
        skipFirstSelection = false; // Now, future selections will play sound
        subtitleText.alpha = 0f;
    }

    // Verify audio sources have clips assigned
    private void ValidateAudioSources()
    {
        if (tape1AudioSource != null && tape1AudioSource.clip == null)
        {
            Debug.LogWarning("Tape 1 has no audio clip assigned!");
        }

        if (tape2AudioSource != null && tape2AudioSource.clip == null)
        {
            Debug.LogWarning("Tape 2 has no audio clip assigned!");
        }
        if (tape3AudioSource != null && tape3AudioSource.clip == null)
        {
            Debug.LogWarning("Tape 3 has no audio clip assigned!");
        }

        // Initialize tape positions
        InitializeTapePositions();
    }

    // Initialize tape positions and end flags
    private void InitializeTapePositions()
    {
        // Check if tapes are already at the end
        if (tape1AudioSource != null && tape1AudioSource.clip != null)
        {
            float tape1Position = tape1AudioSource.time;
            float tape1Length = tape1AudioSource.clip.length;

            tape1ReachedEnd = (tape1Position >= tape1Length - safeEndMargin);

            // Ensure position is valid
            if (tape1Position > tape1Length)
            {
                tape1AudioSource.time = tape1Length - safeEndMargin;
                tape1ReachedEnd = true;
            }
        }

        if (tape2AudioSource != null && tape2AudioSource.clip != null)
        {
            float tape2Position = tape2AudioSource.time;
            float tape2Length = tape2AudioSource.clip.length;

            tape2ReachedEnd = (tape2Position >= tape2Length - safeEndMargin);

            // Ensure position is valid
            if (tape2Position > tape2Length)
            {
                tape2AudioSource.time = tape2Length - safeEndMargin;
                tape2ReachedEnd = true;
            }
        }
    }

    void Update()
    {
        bool isActive = IsTapePlayerActive();

        // When player zooms out, reset playback states
        if (!isActive)
        {
            if (isFastForwarding || isRewinding)
            {
                StopFastForward();
                StopRewind();
            }

            HideTapeUI();
            return;
        }

        if (tape1Button != null)
        {
            if (isActive)
            {
                tape1Button.transform.parent.gameObject.SetActive(true); // Enable UI when zoomed in
                UpdateUI(); // Ensure UI updates correctly

            }
            else
            {
                HideTapeUI(); // Fully disable UI when zoomed out
                return; // Prevent further execution
            }
        }

        // Check if tape has reached the end while playing
        if (currentAudioSource != null && isPlaying)
        {
            // Use a safe margin to detect end of tape
            float clipLength = GetSafeClipLength(currentAudioSource);

            if (currentAudioSource.time >= clipLength - safeEndMargin)
            {
                StopTapeAtEnd();
            }

            // Handle case where Unity might automatically reset position
            if (!HasCurrentTapeReachedEnd() && currentAudioSource.time < 0.1f &&
                currentAudioSource.timeSamples == 0)
            {
                // Unity has automatically reset - move back to end position
                if (currentAudioSource == tape1AudioSource && tape1ReachedEnd)
                {
                    currentAudioSource.time = clipLength - safeEndMargin;
                    StopTapeAtEnd();
                }
                else if (currentAudioSource == tape2AudioSource && tape2ReachedEnd)
                {
                    currentAudioSource.time = clipLength - safeEndMargin;
                    StopTapeAtEnd();
                }
            }
        }

        HandlePlayback();
        HandleRewind();
        HandleFastForward();
        HandleTapeSelection();
        UpdateTapeProgress();
        UpdateSubtitles();
    }


    private void HandleTapeSelection()
    {
        if (Input.GetKeyDown(KeyCode.Q))
        {
            SelectTape(1);
        }
        else if (Input.GetKeyDown(KeyCode.W))
        {
            SelectTape(2);
        }
        else if (Input.GetKeyDown(KeyCode.E))
        {
            SelectTape(3);
        }
    }


    private bool IsTapePlayerActive()
    {
        bool isActive = zoomSystem != null && zoomSystem.IsZoomedOnTapePlayer();
        return isActive;
    }

    private void SelectTape(int tapeNumber)
    {
        if (tapeNumber == selectedTape) return; // Prevent reselecting the same tape

        if (!skipFirstSelection) // Prevents sound on first frame
        {
            PlaySFX(tapeSwapSFX);
        }

        selectedTape = tapeNumber;

        if (currentAudioSource == tape1AudioSource) tape1AudioSource.Pause();
        if (currentAudioSource == tape2AudioSource) tape2AudioSource.Pause();
        if (currentAudioSource == tape3AudioSource) tape3AudioSource.Pause();

        if (subtitleFadeCoroutine != null)
            StopCoroutine(subtitleFadeCoroutine);

        subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());
        subtitleText.text = "";
        currentSubtitleIndex = -1;

        if (tapeNumber == 1)
        {
            currentAudioSource = tape1AudioSource;
            currentSubtitles = tape1Subtitles;
        }
        else if (tapeNumber == 2)
        {
            currentAudioSource = tape2AudioSource;
            currentSubtitles = tape2Subtitles;
        }
        else if (tapeNumber == 3)
        {
            currentAudioSource = tape3AudioSource;
            currentSubtitles = tape3Subtitles;
        }

        if (currentAudioSource != null)
        {
            ValidateAudioPosition();
            currentAudioSource.Pause();
        }

        isPlaying = false;
        UpdateUI();
    }

    // Check if current tape has reached its end
    private bool HasCurrentTapeReachedEnd()
    {
        if (currentAudioSource == tape1AudioSource)
            return tape1ReachedEnd;
        else if (currentAudioSource == tape2AudioSource)
            return tape2ReachedEnd;
        return false;
    }

    // Safely get the clip length with null checks
    private float GetSafeClipLength(AudioSource source)
    {
        if (source == null || source.clip == null)
            return 0f;
        return source.clip.length;
    }

    // Ensure audio position is valid
    private void ValidateAudioPosition()
    {
        if (currentAudioSource == null || currentAudioSource.clip == null)
            return;

        float clipLength = currentAudioSource.clip.length;

        // If position is beyond clip length, set to a valid position near the end
        if (currentAudioSource.time > clipLength)
        {
            currentAudioSource.time = Mathf.Max(0, clipLength - safeEndMargin);

            // Mark tape as at the end
            if (currentAudioSource == tape1AudioSource)
                tape1ReachedEnd = true;
            else if (currentAudioSource == tape2AudioSource)
                tape2ReachedEnd = true;
        }

        // If negative position, set to beginning
        if (currentAudioSource.time < 0)
        {
            currentAudioSource.time = 0;

            // Tape is definitely not at the end if at position 0
            if (currentAudioSource == tape1AudioSource)
                tape1ReachedEnd = false;
            else if (currentAudioSource == tape2AudioSource)
                tape2ReachedEnd = false;
        }
    }

    private void UpdateUI()
    {
        // Update tape selection
        if (tape1Button != null)
            tape1Button.sprite = (selectedTape == 1) ? selectedTapeSprite : idleTape;

        if (tape2Button != null)
            tape2Button.sprite = (selectedTape == 2) ? selectedTapeSprite : idleTape;

        if (tape3Button != null)
            tape3Button.sprite = (selectedTape == 3) ? selectedTapeSprite : idleTape;

        // Play/Pause button updates correctly
        if (playPauseButton != null)
            playPauseButton.sprite = isPlaying ? selectedPlay : idlePlay;

        // Update FF/Rewind buttons dynamically (fixes stuck issue)
        if (rewindButton != null)
            rewindButton.sprite = isRewinding ? selectedRewind : idleRewind;

        if (fastForwardButton != null)
            fastForwardButton.sprite = isFastForwarding ? selectedFastForward : idleFastForward;

        // Update tape progress bar
        UpdateTapeProgress();
    }

    private void HandlePlayback()
    {
        if (selectedTape == 0 || currentAudioSource == null) // No tape selected
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                PlaySFX(buttonClickSFX);
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            // Safety check in case clip is null
            if (currentAudioSource.clip == null) return;

            // Make sure position is valid
            ValidateAudioPosition();

            // Check if tape has reached end state (critical fix)
            bool tapeAtEnd = HasCurrentTapeReachedEnd();

            if (tapeAtEnd)
            {
                // Don't allow playing when at the end, require rewind first
                PlaySFX(buttonClickSFX);
                Debug.Log("Tape has reached end. Please rewind to play again.");
                return;
            }

            if (isPlaying)
            {
                currentAudioSource.Pause();
                PlaySFX(buttonClickSFX);

                // Clear subtitles when pausing
                if (subtitleFadeCoroutine != null)
                    StopCoroutine(subtitleFadeCoroutine);
                subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());
            }
            else
            {
                try
                {
                    currentAudioSource.Play();
                    PlaySFX(buttonPressSFX);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error playing audio: " + e.Message);

                    // Try to recover - reset position and try again
                    float clipLength = GetSafeClipLength(currentAudioSource);
                    currentAudioSource.time = Mathf.Min(currentAudioSource.time, clipLength - safeEndMargin);

                    try
                    {
                        currentAudioSource.Play();
                    }
                    catch
                    {
                        Debug.LogError("Critical error playing audio, unable to recover.");
                        return;
                    }
                }
            }

            isPlaying = !isPlaying;
            UpdateUI();
        }
    }

    private void HandleRewind()
    {
        if (currentAudioSource == null || currentAudioSource.clip == null) return;

        if (Input.GetKey(KeyCode.S))
        {
            isRewinding = true;
            rewindHoldTime += Time.deltaTime;

            if (!sfxSource.isPlaying) PlayLoopingSFX(rewindLoopSFX);

            // Store previous time to calculate how much we've rewound
            float previousTime = currentAudioSource.time;

            // Calculate the new time, but ensure it stays within valid range
            float newTime = Mathf.Max(0, currentAudioSource.time - rewindSpeed * Time.deltaTime);
            currentAudioSource.time = newTime;

            // Only clear "reached end" flag if we've rewound significantly (not just a quick tap)
            float rewindAmount = previousTime - newTime;

            if (rewindAmount > rewindThreshold)
            {
                // Clear "reached end" flag after significant rewind
                if (currentAudioSource == tape1AudioSource)
                    tape1ReachedEnd = false;
                else if (currentAudioSource == tape2AudioSource)
                    tape2ReachedEnd = false;
            }

            if (newTime <= 0.01f && isRewinding)
            {
                StopRewind();

                // Definitely clear the end flag when fully rewound
                if (currentAudioSource == tape1AudioSource)
                    tape1ReachedEnd = false;
                else if (currentAudioSource == tape2AudioSource)
                    tape2ReachedEnd = false;
            }

            UpdateUI(); // Update UI in real-time
        }

        if (Input.GetKeyUp(KeyCode.S))
        {
            StopRewind();
        }
    }

    private void StopRewind()
    {
        if (!isRewinding) return; // Prevents spam
        isRewinding = false;
        rewindHoldTime = 0;
        sfxSource.Stop();
        PlaySFX(buttonClickSFX);
        UpdateUI(); // Ensure UI resets when stopping rewind
    }

    private void HandleFastForward()
    {
        if (currentAudioSource == null || currentAudioSource.clip == null) return;

        if (Input.GetKey(KeyCode.D))
        {
            float clipLength = currentAudioSource.clip.length;

            // If tape has never been played, manually initialize its position
            if (!isPlaying && currentAudioSource.time == 0)
            {
                currentAudioSource.Play(); // Briefly play to initialize `time`
                currentAudioSource.Pause(); // Then pause immediately
            }

            // If we're already at/near the end, stop fast forwarding immediately
            if (currentAudioSource.time >= clipLength - safeEndMargin)
            {
                StopFastForward();

                // Set the "reached end" flag
                if (currentAudioSource == tape1AudioSource)
                    tape1ReachedEnd = true;
                else if (currentAudioSource == tape2AudioSource)
                    tape2ReachedEnd = true;

                return;
            }

            isFastForwarding = true;
            fastForwardHoldTime += Time.deltaTime;

            if (!sfxSource.isPlaying) PlayLoopingSFX(fastForwardLoopSFX);

            // Calculate new position without exceeding clip length
            float newTime = currentAudioSource.time + fastForwardSpeed * Time.deltaTime;

            // Ensure FF updates time even if tape is not playing
            currentAudioSource.time = Mathf.Min(newTime, clipLength - safeEndMargin);

            // If the tape reaches the end, stop fast-forwarding
            if (currentAudioSource.time >= clipLength - safeEndMargin)
            {
                currentAudioSource.time = clipLength - safeEndMargin;

                // Mark tape as at the end
                if (currentAudioSource == tape1AudioSource)
                    tape1ReachedEnd = true;
                else if (currentAudioSource == tape2AudioSource)
                    tape2ReachedEnd = true;

                StopFastForward();
            }

            UpdateUI();
        }

        if (Input.GetKeyUp(KeyCode.D))
        {
            StopFastForward();
        }
    }

    public void ForceStop()
    {
        if (currentAudioSource != null && isPlaying)
        {
            currentAudioSource.Pause();
            isPlaying = false;
            PlaySFX(buttonClickSFX);
            UpdateUI();
        }

        StopFastForward();
        StopRewind();

        if (subtitleFadeCoroutine != null)
            StopCoroutine(subtitleFadeCoroutine);

        subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());

        currentSubtitleIndex = -1;
        subtitleText.text = "";
    }


    private void StopFastForward()
    {
        if (!isFastForwarding) return; // Prevents spam
        isFastForwarding = false;
        fastForwardHoldTime = 0;
        sfxSource.Stop();
        PlaySFX(buttonClickSFX);
        UpdateUI(); // Ensure UI resets when stopping fast forward
    }

    private void PlaySFX(AudioClip clip)
    {
        if (sfxSource != null && clip != null)
        {
            sfxSource.PlayOneShot(clip, 0.5f);
        }
    }

    private void PlayLoopingSFX(AudioClip clip)
    {
        if (sfxSource != null && clip != null)
        {
            sfxSource.clip = clip;
            sfxSource.loop = true;
            sfxSource.Play();
        }
    }

    public void HideTapeUI()
    {
        if (tape1Button != null)
        {
            tape1Button.transform.parent.gameObject.SetActive(false); // Completely disable UI
        }

        // Important: Make sure any playing states are cleared
        StopFastForward();
        StopRewind();
    }

    private void StopTapeAtEnd()
    {
        if (currentAudioSource == null || currentAudioSource.clip == null) return;

        // Safety - make sure we don't exceed clip length
        float clipLength = currentAudioSource.clip.length;
        currentAudioSource.time = clipLength - safeEndMargin / 2; // Keep tape very close to end but within safe bounds

        currentAudioSource.Pause(); // Use Pause instead of Stop to maintain position
        isPlaying = false; // Prevent auto-restarting

        // Set the "reached end" flag - THIS IS THE KEY FIX
        if (currentAudioSource == tape1AudioSource)
            tape1ReachedEnd = true;
        else if (currentAudioSource == tape2AudioSource)
            tape2ReachedEnd = true;

        if (subtitleFadeCoroutine != null)
            StopCoroutine(subtitleFadeCoroutine);
        subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());

        PlaySFX(buttonClickSFX); // Play stop sound effect
        UpdateUI(); // Ensure UI updates correctly
    }

    private void UpdateTapeProgress()
    {
        if (tapeProgressSlider == null || currentAudioSource == null || currentAudioSource.clip == null)
            return;

        // Normalize progress (0 to 1)
        tapeProgressSlider.value = currentAudioSource.time / currentAudioSource.clip.length;
    }

    // Called when application is closed or scene changes
    private void OnDisable()
    {
        // Ensure we stop any looping sounds when disabled
        if (sfxSource != null)
        {
            sfxSource.Stop();
        }
    }

    private void UpdateSubtitles()
    {
        if (currentAudioSource == null || currentSubtitles == null || subtitleText == null)
        {
            subtitleText.text = "";
            return;
        }

        // If not playing, immediately fade out subtitles
        if (!isPlaying)
        {
            if (subtitleText.alpha > 0f && subtitleFadeCoroutine == null)
            {
                // Stop any existing subtitle fade coroutine
                if (subtitleFadeCoroutine != null)
                    StopCoroutine(subtitleFadeCoroutine);

                // Start fading out
                subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());
            }
            return;
        }

        float currentTime = currentAudioSource.time;

        if (currentSubtitleIndex == -1 || currentTime < currentSubtitles[currentSubtitleIndex].startTime || currentTime > currentSubtitles[currentSubtitleIndex].endTime)
        {
            currentSubtitleIndex = FindSubtitleIndex(currentTime);
        }

        if (currentSubtitleIndex != -1)
        {
            string newText = currentSubtitles[currentSubtitleIndex].subtitleText;

            if (subtitleText.text != newText)
            {
                if (subtitleFadeCoroutine != null)
                    StopCoroutine(subtitleFadeCoroutine);

                subtitleFadeCoroutine = StartCoroutine(FadeSubtitle(newText));
            }
        }
        else
        {
            if (subtitleText.alpha > 0f && subtitleFadeCoroutine == null)
            {
                subtitleFadeCoroutine = StartCoroutine(FadeOutSubtitle());
            }
        }
    }


    private IEnumerator FadeSubtitle(string newText)
    {
        subtitleText.alpha = 0f;
        subtitleText.text = newText;

        float duration = 0.5f;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            subtitleText.alpha = Mathf.Lerp(0f, 1f, timer / duration);
            yield return null;
        }

        subtitleText.alpha = 1f;
    }

    private IEnumerator FadeOutSubtitle()
    {
        float duration = 0.5f;
        float startAlpha = subtitleText.alpha;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            subtitleText.alpha = Mathf.Lerp(startAlpha, 0f, timer / duration);
            yield return null;
        }

        subtitleText.alpha = 0f;
        subtitleText.text = "";
        currentSubtitleIndex = -1; // 🛠 Reset so nothing lingers
    }



    private int FindSubtitleIndex(float currentTime)
    {
        for (int i = 0; i < currentSubtitles.Length; i++)
        {
            if (currentTime >= currentSubtitles[i].startTime && currentTime <= currentSubtitles[i].endTime)
            {
                return i;
            }
        }
        return -1; // No subtitle matches current time
    }
}