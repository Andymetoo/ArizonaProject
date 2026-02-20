using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System;


public class PlayerProgressManager : MonoBehaviour
{
    public static PlayerProgressManager Instance;

    public event Action<int, int> RankChanged;

    [Header("XP Settings")]
    public int totalXP;           // Total cumulative XP
    public int currentRank;       // Current rank level
    public List<int> xpThresholds; // XP required per rank

    [Header("UI References")]
    public Image xpBar;
    public TextMeshProUGUI xpText;
    public TextMeshProUGUI rankText;
    public Image rankImage;

    [Header("Rank Visuals")]
    public List<string> rankNames;
    public List<Color> rankColors;
    public List<Sprite> rankIcons;

    [Header("Animation & Audio")]
    public float fillSpeed = 0.5f;
    public Image whiteFlash;
    public AudioSource xpGainLoop;
    public AudioSource sfxPlayer;
    public AudioClip levelUpSound;
    [SerializeField] private float flashFadeInTime = 0.15f;
    [SerializeField] private float flashFadeOutTime = 0.5f;

    private bool isFilling = false;
    public int CurrentRank => currentRank;
    [Header("Scene Behavior")]
    public bool showAnimationOnStart = false;
    [Header("Demo Settings")]
    public bool isDemo = false;
    public int demoMaxRank = 2; // Zero-indexed (rank 3)

    [Header("Debug")]
    [Tooltip("Inspector button-style toggle: when set true at runtime, will immediately grant Debug XP Amount and then reset to false.")]
    public bool debugAddXpNow = false;

    [Min(0)]
    public int debugXpAmount = 100;

    // Track XP thresholds for each rank for display
    private List<int> cumulativeXPThresholds = new List<int>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            // Removed DontDestroyOnLoad

            // Set up XP thresholds and load saved progress
            CalculateCumulativeXPThresholds();
            LoadProgress();

            // Update visuals
            UpdateRankUI();

            RankChanged?.Invoke(currentRank, currentRank);
        }
        else if (Instance != this)
        {
            Destroy(gameObject); // ✅ Prevent duplicates from lingering
        }
    }


    void Start()
    {
        EnsureUIReferences();

        // Load data
        LoadProgress();
        CalculateCumulativeXPThresholds();

        if (showAnimationOnStart && PlayerPrefs.HasKey("LastXPGained"))
        {
            int gainedXP = PlayerPrefs.GetInt("LastXPGained");
            int oldXP = Mathf.Max(0, totalXP - gainedXP);
            PlayerPrefs.DeleteKey("LastXPGained"); // Clear it after use

            // Temporarily set the total XP and rank to the starting values
            int finalXP = totalXP;
            int finalRank = currentRank;

            // Set the display to show the starting values
            totalXP = oldXP;
            currentRank = CalculateRankFromTotalXP(oldXP);
            UpdateRankUI(); // This will show the starting rank/badge

            // Now start the animation to the final values
            totalXP = finalXP; // Restore the actual XP value
            StartCoroutine(FillXPBarRoutine(oldXP, finalXP, currentRank != finalRank));
        }
        else
        {
            UpdateRankUI();
        }
    }

    void Update()
    {
        if (debugAddXpNow)
        {
            debugAddXpNow = false;
            AddXP(debugXpAmount);
        }
    }

    // PlayMaker-friendly debug hook
    public void DebugAddXP(int amount)
    {
        AddXP(amount);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Safely reassign UI elements if present in the new scene
        EnsureUIReferences();

        // Only update UI if necessary references are valid
        if (xpText != null || xpBar != null)
            UpdateXPBar();

        if (rankText != null || rankImage != null)
            UpdateRankUI();
    }

    private void EnsureUIReferences()
    {
        if (whiteFlash == null)
            whiteFlash = GameObject.FindWithTag("WhiteFlash")?.GetComponent<Image>();

        if (xpBar == null)
            xpBar = GameObject.FindWithTag("XPBar")?.GetComponent<Image>();

        if (xpText == null)
            xpText = GameObject.FindWithTag("XPText")?.GetComponent<TextMeshProUGUI>();

        if (rankText == null)
            rankText = GameObject.FindWithTag("RankText")?.GetComponent<TextMeshProUGUI>();

        if (rankImage == null)
            rankImage = GameObject.FindWithTag("RankImage")?.GetComponent<Image>();
    }


    void CalculateCumulativeXPThresholds()
    {
        cumulativeXPThresholds.Clear();
        int cumulative = 0;

        // First threshold is always 0
        cumulativeXPThresholds.Add(0);

        // Calculate cumulative XP required for each rank
        for (int i = 0; i < xpThresholds.Count; i++)
        {
            cumulative += xpThresholds[i];
            cumulativeXPThresholds.Add(cumulative);
        }
    }


    public void AddXP(int amount)
    {
        int maxRank = isDemo ? demoMaxRank : xpThresholds.Count - 1;
        if (currentRank >= maxRank) return;
        if (isFilling) return;

        int oldXP = totalXP;
        totalXP += amount;

        // Calculate rank from total XP
        int oldRank = CalculateRankFromTotalXP(oldXP);
        int newRank = CalculateRankFromTotalXP(totalXP);

        StartCoroutine(FillXPBarRoutine(oldXP, totalXP, oldRank != newRank));
    }

    int CalculateRankFromTotalXP(int totalXP)
    {
        int rank = 0;

        for (int i = 0; i < cumulativeXPThresholds.Count - 1; i++)
        {
            if (totalXP >= cumulativeXPThresholds[i] && totalXP < cumulativeXPThresholds[i + 1])
            {
                rank = i;
                break;
            }
        }

        // If we've surpassed all thresholds, use the max rank
        if (totalXP >= cumulativeXPThresholds[cumulativeXPThresholds.Count - 1])
        {
            rank = cumulativeXPThresholds.Count - 2; // -2 because we have one more threshold than ranks
        }
        if (isDemo && rank > demoMaxRank)
            rank = demoMaxRank;
        return rank;
    }

    IEnumerator FillXPBarRoutine(int fromXP, int toXP, bool rankChanged)
    {
        isFilling = true;
        xpGainLoop?.Play();

        currentRank = CalculateRankFromTotalXP(fromXP);
        int startThreshold = GetThresholdForRank(currentRank);
        int endThreshold = GetThresholdForRank(currentRank + 1);
        int currentDisplayXP = fromXP;

        while (currentDisplayXP < toXP)
        {
            int maxRank = isDemo ? demoMaxRank : xpThresholds.Count - 1;
            if (toXP >= endThreshold && currentRank < maxRank)
            {
                // Animate to end of current threshold
                yield return StartCoroutine(AnimateXPBar(
                    currentDisplayXP,
                    endThreshold,
                    startThreshold,
                    endThreshold));

                // Level up
                int oldRank = currentRank;
                currentRank++;
                LevelUp();

                RankChanged?.Invoke(oldRank, currentRank);

                startThreshold = GetThresholdForRank(currentRank);
                endThreshold = GetThresholdForRank(currentRank + 1);
                currentDisplayXP = startThreshold;
            }
            else
            {
                // Animate to final XP
                yield return StartCoroutine(AnimateXPBar(
                    currentDisplayXP,
                    toXP,
                    startThreshold,
                    endThreshold));

                currentDisplayXP = toXP;
            }
        }

        int previousRank = currentRank;
        currentRank = CalculateRankFromTotalXP(totalXP);
        UpdateRankUI();

        if (currentRank != previousRank)
            RankChanged?.Invoke(previousRank, currentRank);

        xpGainLoop?.Stop();
        isFilling = false;
        SaveProgress();
    }


    int GetThresholdForRank(int rank)
    {
        if (rank < 0) return 0;
        if (rank >= cumulativeXPThresholds.Count) return cumulativeXPThresholds[cumulativeXPThresholds.Count - 1];
        return cumulativeXPThresholds[rank];
    }

    IEnumerator AnimateXPBar(int fromXP, int toXP, int minThreshold, int maxThreshold)
    {
        int range = maxThreshold - minThreshold;
        if (range <= 0) range = 1; // Prevent division by zero

        float startFill = (float)(fromXP - minThreshold) / range;
        float endFill = (float)(toXP - minThreshold) / range;

        // Clamp fill values to valid range
        startFill = Mathf.Clamp01(startFill);
        endFill = Mathf.Clamp01(endFill);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * fillSpeed;
            float currentFill = Mathf.Lerp(startFill, endFill, t);
            int currentValue = Mathf.FloorToInt(Mathf.Lerp(fromXP, toXP, t));

            // Update the XP bar fill amount
            if (xpBar != null)
                xpBar.fillAmount = currentFill;

            // Update XP text during animation - showing cumulative XP
            if (xpText != null)
                xpText.text = (currentValue - minThreshold) + " / " + (maxThreshold - minThreshold);

            yield return null;
        }

        if (xpBar != null)
            xpBar.fillAmount = endFill;

        if (xpText != null)
        {
            if (isDemo && currentRank >= demoMaxRank)
                xpText.text = "Max Demo Rank";
            else if (currentRank >= xpThresholds.Count - 1)
                xpText.text = "Max";
            else
                xpText.text = (toXP - minThreshold) + " / " + (maxThreshold - minThreshold);
        }
    }

    public void ResetProgress()
    {
        int oldRank = currentRank;
        PlayerPrefs.DeleteKey("TotalXP");
        PlayerPrefs.DeleteKey("Rank");
        totalXP = 0;
        currentRank = 0;
        UpdateRankUI();

        RankChanged?.Invoke(oldRank, currentRank);
    }

    void LevelUp()
    {
        StartCoroutine(FlashEffect());
        sfxPlayer?.PlayOneShot(levelUpSound);
        UpdateRankUI();
        Debug.Log("Leveled up.");
    }

    IEnumerator FlashEffect()
    {
        if (whiteFlash == null)
        {
            EnsureUIReferences();
            if (whiteFlash == null)
            {
                Debug.LogWarning("[PlayerProgressManager] White flash UI Image not found. Assign 'whiteFlash' in inspector or tag the overlay Image as 'WhiteFlash'.");
                yield break;
            }
        }

        bool wasActive = whiteFlash.gameObject.activeSelf;
        bool wasEnabled = whiteFlash.enabled;
        if (!wasActive) whiteFlash.gameObject.SetActive(true);
        if (!wasEnabled) whiteFlash.enabled = true;

        // Start from transparent
        whiteFlash.color = new Color(1, 1, 1, 0);

        // Fade in
        float t = 0f;
        while (t < flashFadeInTime)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / flashFadeInTime);
            whiteFlash.color = new Color(1, 1, 1, alpha);
            yield return null;
        }

        // Fade out
        t = 0f;
        while (t < flashFadeOutTime)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(1 - (t / flashFadeOutTime));
            whiteFlash.color = new Color(1, 1, 1, alpha);
            yield return null;
        }

        // Ensure fully transparent at end
        whiteFlash.color = new Color(1, 1, 1, 0);

        if (!wasEnabled) whiteFlash.enabled = false;
        if (!wasActive) whiteFlash.gameObject.SetActive(false);
    }

    void UpdateXPBar()
    {
        int minThreshold = GetThresholdForRank(currentRank);
        int maxThreshold = GetThresholdForRank(currentRank + 1);

        int maxRank = isDemo ? demoMaxRank : xpThresholds.Count - 1;

        if (currentRank >= maxRank)
        {
            if (xpBar != null)
                xpBar.fillAmount = 1f;

            if (xpText != null)
            {
                if (isDemo)
                    xpText.text = "Max Demo Rank";
                else
                    xpText.text = "Max";
            }
            return;
        }

        int range = maxThreshold - minThreshold;
        if (range <= 0) range = 1;

        float progress = (float)(totalXP - minThreshold) / range;
        progress = Mathf.Clamp01(progress);

        if (xpBar != null)
            xpBar.fillAmount = progress;

        if (xpText != null)
            xpText.text = (totalXP - minThreshold) + " / " + (maxThreshold - minThreshold);
    }


    void UpdateRankUI()
    {
        if (rankText != null && currentRank < rankNames.Count)
        {
            rankText.text = rankNames[currentRank];

            // Properly set color with visible alpha
            if (currentRank < rankColors.Count)
            {
                Color c = rankColors[currentRank];
                c.a = 1f;
                rankText.color = c;
            }
        }

        if (rankImage != null && currentRank < rankIcons.Count)
        {
            rankImage.sprite = rankIcons[currentRank];

            // Ensure the rankImage is visible
            Color c = rankImage.color;
            c.a = 1f;
            rankImage.color = c;
        }

        UpdateXPBar();
    }

    void SaveProgress()
    {
        PlayerPrefs.SetInt("TotalXP", totalXP ^ 193); // Light obfuscation
        PlayerPrefs.SetInt("Rank", currentRank ^ 193);
        PlayerPrefs.Save(); // Ensure data is saved
    }

    void LoadProgress()
    {
        int oldRank = currentRank;
        totalXP = PlayerPrefs.HasKey("TotalXP") ? PlayerPrefs.GetInt("TotalXP") ^ 193 : 0;
        currentRank = PlayerPrefs.HasKey("Rank") ? PlayerPrefs.GetInt("Rank") ^ 193 : 0;

        // Verify rank based on XP
        int calculatedRank = CalculateRankFromTotalXP(totalXP);
        if (calculatedRank != currentRank)
        {
            Debug.LogWarning("Saved rank didn't match calculated rank from XP. Using calculated rank.");
            currentRank = calculatedRank;
        }

        if (oldRank != currentRank)
            RankChanged?.Invoke(oldRank, currentRank);
    }
}