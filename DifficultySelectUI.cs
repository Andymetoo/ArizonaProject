using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

[System.Serializable]
public class DifficultyData
{
    public string title;
    public Sprite image;
    public string description;

    [Header("Legacy (unused)")]
    [Tooltip("Legacy XP/rank gate. Kept for backward compatibility with existing inspector data.")]
    public int requiredRank;

    [Header("Unlock Gating (PlayerPrefs)")]
    [Tooltip("If empty, this difficulty is unlocked by default. If set, PlayerPrefs int must equal 1 to unlock (e.g., 'RestlessBeaten').")]
    public string requiredPlayerPrefKey;

    [Tooltip("Optional human-readable requirement shown in the locked overlay (e.g., 'Beat Restless to unlock').")]
    public string lockedRequirementText;

    [Header("Scene Override (Optional)")]
    [Tooltip("If set, Play should load this scene by name instead of the main game scene (e.g., Custom Night scene).")]
    public string sceneNameOverride;

    [Header("Endless (Optional)")]
    [Tooltip("If true, this difficulty launches the main game in Endless mode (IsEndlessMode=1).")]
    public bool isEndlessMode;
}

public class DifficultySelectUI : MonoBehaviour
{
    public DifficultyData[] difficultyOptions;

    [Header("UI References")]
    public TextMeshProUGUI titleText;
    public Image difficultyImage;
    public TextMeshProUGUI descriptionText;
    public Button leftArrow;
    public Button rightArrow;
    //public Button startButton;
    public GameObject lockedOverlay;
    public TextMeshProUGUI lockedText;
    [Tooltip("Optional: If set, this will fade the locked requirement text in/out.")]
    public TMPAlphaFader lockedRequirementFader;
    public int mainGameIndex = 1;

    private int currentIndex = 0;
    [SerializeField] private int dummyRank = 0;  // Editor-editable rank for testing
    [SerializeField] private bool useDummyRank = false; // Toggle in Inspector

    [Header("Debug")]
    [Tooltip("If true, all difficulties are treated as unlocked.")]
    public bool unlockAllLevelsDebug = false;

    [Header("Demo Lock (Optional)")]
    [Tooltip("If true, any difficulty option that loads a custom scene (sceneNameOverride) is locked unless the PlayerPref key below is set to 1.")]
    public bool demoLockCustomSceneOverrides = false;

    [Tooltip("PlayerPrefs key that unlocks custom-scene difficulties when demoLockCustomSceneOverrides is enabled.")]
    public string demoUnlockPlayerPrefKey = "Demo_CustomNightUnlocked";

    [Header("Audio")]
    public AudioSource sfxPlayer;
    public AudioClip arrowClickSound;
    public bool isViewingLockedDifficulty { get; private set; } = false;

    public bool IsCurrentOptionCustomScene => GetCurrentData() != null && !string.IsNullOrWhiteSpace(GetCurrentData().sceneNameOverride);
    public string CurrentOptionSceneNameOverride => GetCurrentData() != null ? GetCurrentData().sceneNameOverride : string.Empty;
    public bool IsCurrentOptionEndless => GetCurrentData() != null && GetCurrentData().isEndlessMode;

    void Start()
    {
        // Load the previously selected difficulty index
        currentIndex = PlayerPrefs.GetInt("SelectedDifficulty", 0);

        UpdatePage();
        ApplyModeFlagsForCurrentSelectionIfUnlocked();

        leftArrow.onClick.AddListener(() =>
        {
            PlayArrowSound();
            ChangePage(-1);
        });

        rightArrow.onClick.AddListener(() =>
        {
            PlayArrowSound();
            ChangePage(1);
        });
    }

    private void ChangePage(int delta)
    {
        int newIndex = Mathf.Clamp(currentIndex + delta, 0, difficultyOptions.Length - 1);
        currentIndex = newIndex;
        UpdatePage();

        bool isUnlocked = IsUnlocked(difficultyOptions[currentIndex]);
        isViewingLockedDifficulty = !isUnlocked;
        
        if (isUnlocked)
        {
            PlayerPrefs.SetInt("SelectedDifficulty", currentIndex);
            PlayerPrefs.Save();
            Debug.Log($"Current Difficulty Updated and Set To: {currentIndex}");

            ApplyModeFlagsForCurrentSelectionIfUnlocked();
        }
        else
        {
            Debug.Log("Viewing locked difficulty (not saved).");
        }

        RefreshLockedRequirementUI();
    }

    void UpdatePage()
    {

        if (difficultyOptions == null || difficultyOptions.Length == 0)
        {
            Debug.LogError("Difficulty options list is empty or null!");
            return;
        }

        if (currentIndex < 0 || currentIndex >= difficultyOptions.Length)
        {
            Debug.LogError("Current index out of bounds!");
            return;
        }

        DifficultyData data = difficultyOptions[currentIndex];
        if (data == null)
        {
            Debug.LogError($"DifficultyData at index {currentIndex} is null!");
            return;
        }

        titleText.text = data.title;
        difficultyImage.sprite = data.image;
        descriptionText.text = data.description;

        leftArrow.interactable = currentIndex > 0;
        rightArrow.interactable = currentIndex < difficultyOptions.Length - 1;

        bool isUnlocked = IsUnlocked(data);
        //startButton.interactable = isUnlocked;

        if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
        if (lockedText != null)
            lockedText.text = isUnlocked ? "" : "Locked";

        RefreshLockedRequirementUI();

        ApplyModeFlagsForCurrentSelectionIfUnlocked();

        isViewingLockedDifficulty = !isUnlocked;
    }

    private void ApplyModeFlagsForCurrentSelectionIfUnlocked()
    {
        var data = GetCurrentData();
        if (data == null) return;

        // Don't flip mode flags when the player is looking at a locked option.
        if (!IsUnlocked(data)) return;

        // Endless option selected: set Endless now so the next scene can read it.
        if (data.isEndlessMode)
        {
            PlayerPrefs.SetInt("IsEndlessMode", 1);
            PlayerPrefs.SetInt("IsCustomNight", 0);
            PlayerPrefs.Save();
            return;
        }

        // Browsing standard difficulties: clear both flags.
        // (Custom Night screen / other custom scenes will set their own flags when starting.)
        if (string.IsNullOrWhiteSpace(data.sceneNameOverride))
        {
            PlayerPrefs.SetInt("IsCustomNight", 0);
            PlayerPrefs.SetInt("IsEndlessMode", 0);
            PlayerPrefs.Save();
        }
    }

    private void RefreshLockedRequirementUI()
    {
        var data = GetCurrentData();
        if (data == null) return;

        bool isUnlocked = IsUnlocked(data);
        string msg = isUnlocked ? string.Empty : BuildLockedText(data);

        if (lockedRequirementFader != null)
        {
            if (isUnlocked)
            {
                lockedRequirementFader.Hide();
            }
            else
            {
                lockedRequirementFader.SetTextAndShow(msg);
            }
        }
    }

    private DifficultyData GetCurrentData()
    {
        if (difficultyOptions == null || difficultyOptions.Length == 0) return null;
        if (currentIndex < 0 || currentIndex >= difficultyOptions.Length) return null;
        return difficultyOptions[currentIndex];
    }

    private bool IsUnlocked(DifficultyData data)
    {
        if (data == null) return false;

        if (unlockAllLevelsDebug)
        {
            return true;
        }

        // Optional demo lock: keep Custom Night (scene override) locked unless unlocked via PlayerPrefs.
        if (demoLockCustomSceneOverrides && !string.IsNullOrWhiteSpace(data.sceneNameOverride))
        {
            if (string.IsNullOrWhiteSpace(demoUnlockPlayerPrefKey))
            {
                // If misconfigured, fail closed (stay locked) rather than accidentally unlocking.
                return false;
            }

            if (PlayerPrefs.GetInt(demoUnlockPlayerPrefKey, 0) != 1)
            {
                return false;
            }
        }

        // New gating: if key is empty, unlocked by default.
        if (string.IsNullOrWhiteSpace(data.requiredPlayerPrefKey))
        {
            return true;
        }

        return PlayerPrefs.GetInt(data.requiredPlayerPrefKey, 0) == 1;
    }

    private string BuildLockedText(DifficultyData data)
    {
        if (data == null) return "Locked.";
        if (!string.IsNullOrWhiteSpace(data.lockedRequirementText)) return data.lockedRequirementText;

        if (demoLockCustomSceneOverrides && !string.IsNullOrWhiteSpace(data.sceneNameOverride))
        {
            return "Locked in demo.";
        }

        if (!string.IsNullOrWhiteSpace(data.requiredPlayerPrefKey)) return "Beat the previous difficulty to unlock.";
        return "Locked.";
    }

    void SelectCurrentDifficulty()
    {
        PlayerPrefs.SetInt("SelectedDifficulty", currentIndex);
        PlayerPrefs.Save();
        //startButton.onClick.AddListener(() => LoadScene(mainGameIndex));
    }

    void LoadScene(int index)
    {
        Time.timeScale = 1.0f; // Reset paused game
        SceneManager.LoadScene(index, LoadSceneMode.Single);
        //SceneManager.LoadScene(index);
    }

    // Use this for your main Play button.
    // - If the selected option has sceneNameOverride, loads that scene (e.g., Custom Night screen)
    // - If isEndlessMode is true, sets IsEndlessMode=1 and loads mainGameIndex
    // - Otherwise loads mainGameIndex in standard mode
    public void PlayCurrentSelection()
    {
        var data = GetCurrentData();
        if (data == null) return;

        if (!IsUnlocked(data))
        {
            Debug.Log("Cannot play: selected difficulty is locked.");
            return;
        }

        // Endless: set the flag, then load either the override scene (if provided) or the main game.
        if (data.isEndlessMode)
        {
            PlayerPrefs.SetInt("IsEndlessMode", 1);
            PlayerPrefs.SetInt("IsCustomNight", 0);
            PlayerPrefs.Save();

            if (!string.IsNullOrWhiteSpace(data.sceneNameOverride))
            {
                Time.timeScale = 1.0f;
                SceneManager.LoadScene(data.sceneNameOverride, LoadSceneMode.Single);
            }
            else
            {
                LoadScene(mainGameIndex);
            }

            return;
        }

        // Non-endless: if this option loads a custom scene, don't force other mode flags here.
        // That scene's own UI should decide when to start the run (e.g., Custom Night screen).
        if (!string.IsNullOrWhiteSpace(data.sceneNameOverride))
        {
            PlayerPrefs.SetInt("IsEndlessMode", 0);
            PlayerPrefs.Save();
            Time.timeScale = 1.0f;
            SceneManager.LoadScene(data.sceneNameOverride, LoadSceneMode.Single);
            return;
        }

        // Standard main game.
        PlayerPrefs.SetInt("IsEndlessMode", 0);
        PlayerPrefs.Save();
        LoadScene(mainGameIndex);
    }
    void PlayArrowSound()
    {
        if (sfxPlayer != null && arrowClickSound != null)
            sfxPlayer.PlayOneShot(arrowClickSound);
    }

    // --- Endless launch helpers (optional) ---
    // Wire these to a UI Button or PlayMaker Call Method.
    public void LaunchMainGame_Endless()
    {
        PlayerPrefs.SetInt("IsEndlessMode", 1);
        PlayerPrefs.SetInt("IsCustomNight", 0);
        PlayerPrefs.Save();
        LoadScene(mainGameIndex);
    }

    public void LaunchMainGame_Standard()
    {
        PlayerPrefs.SetInt("IsEndlessMode", 0);
        PlayerPrefs.SetInt("IsCustomNight", 0);
        PlayerPrefs.Save();
        LoadScene(mainGameIndex);
    }

}
