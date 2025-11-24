using UnityEngine;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Photon-synchronized countdown timer that starts when room reaches max players.
/// All clients see the same countdown time thanks to Photon's time synchronization.
/// </summary>
public class PhotonCountdownTimer : MonoBehaviourPunCallbacks
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private UnityEngine.UI.Image radialFillImage; // Radial 360 fill image
    
    [Header("Radial Fill Settings")]
    [SerializeField] private bool invertFill = false; // If true, fills from 1 to 0 (empties). If false, fills from 0 to 1
    [SerializeField] private Color fillStartColor = Color.green;
    [SerializeField] private Color fillMiddleColor = Color.yellow;
    [SerializeField] private Color fillEndColor = Color.red;
    [SerializeField] private bool useColorGradient = true; // Enable color transition based on time
    
    [Header("Timer Settings")]
    [SerializeField] private float countdownDuration = 60f; // Duration in seconds (e.g., 60 = 1:00)
    
    [Header("Game Over Settings")]
    [SerializeField] private string gameOverSceneName = "GameOver"; // Optional: scene to load on game over
    [SerializeField] private bool justLogGameOver = true; // If true, just logs. If false, loads scene.
    
    // Timer state
    private double startTime; // Using Photon's server time
    private bool countdownStarted = false;
    private bool gameOverTriggered = false;

    void Start()
    {
        // Initialize timer display
        UpdateTimerDisplay(countdownDuration);
        
        // Initialize radial fill
        InitializeRadialFill();
        
        // Check if room is already full when joining
        if (PhotonNetwork.InRoom)
        {
            CheckIfShouldStartCountdown();
        }
    }

    void Update()
    {
        if (!countdownStarted || gameOverTriggered)
            return;

        // Calculate remaining time using Photon's synchronized server time
        double elapsed = PhotonNetwork.Time - startTime;
        float remaining = countdownDuration - (float)elapsed;

        if (remaining <= 0f)
        {
            remaining = 0f;
            TriggerGameOver();
        }

        UpdateTimerDisplay(remaining);
        UpdateRadialFill(remaining);
    }

    /// <summary>
    /// Updates the timer UI to display time in MM:SS format
    /// </summary>
    private void UpdateTimerDisplay(float timeInSeconds)
    {
        if (timerText == null)
            return;

        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        
        // Optional: Change color as time runs out
        if (timeInSeconds <= 10f)
        {
            timerText.color = Color.red;
        }
        else if (timeInSeconds <= 30f)
        {
            timerText.color = Color.yellow;
        }
        else
        {
            timerText.color = Color.white;
        }
    }

    /// <summary>
    /// Initializes the radial fill image settings
    /// </summary>
    private void InitializeRadialFill()
    {
        if (radialFillImage == null)
            return;

        // Set fill method to Radial 360
        radialFillImage.type = UnityEngine.UI.Image.Type.Filled;
        radialFillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        radialFillImage.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top; // Start from top
        radialFillImage.fillClockwise = true;
        
        // Set initial fill amount
        radialFillImage.fillAmount = invertFill ? 1f : 0f;
        
        // Set initial color
        if (useColorGradient)
        {
            radialFillImage.color = fillStartColor;
        }
        
        Debug.Log("[Timer] Radial fill initialized");
    }

    /// <summary>
    /// Updates the radial fill image based on remaining time
    /// </summary>
    private void UpdateRadialFill(float remainingTime)
    {
        if (radialFillImage == null)
            return;

        // Calculate progress (0 to 1)
        float progress = 1f - (remainingTime / countdownDuration); // 0 at start, 1 at end
        
        // Set fill amount based on invert setting
        if (invertFill)
        {
            // Empties over time (1 -> 0)
            radialFillImage.fillAmount = 1f - progress;
        }
        else
        {
            // Fills over time (0 -> 1)
            radialFillImage.fillAmount = progress;
        }

        // Update color gradient if enabled
        if (useColorGradient)
        {
            if (progress < 0.5f)
            {
                // First half: Green to Yellow
                radialFillImage.color = Color.Lerp(fillStartColor, fillMiddleColor, progress * 2f);
            }
            else
            {
                // Second half: Yellow to Red
                radialFillImage.color = Color.Lerp(fillMiddleColor, fillEndColor, (progress - 0.5f) * 2f);
            }
        }
    }

    /// <summary>
    /// Starts the countdown. Only Master Client can start it.
    /// </summary>
    private void StartCountdown()
    {
        if (countdownStarted)
            return;

        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[Timer] Only Master Client can start countdown!");
            return;
        }

        // Use Photon's server time for synchronization
        startTime = PhotonNetwork.Time;
        countdownStarted = true;

        // Store start time in room properties so late joiners can sync
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "CountdownStartTime", startTime },
            { "CountdownStarted", true }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        Debug.Log($"[Timer] Countdown started! Duration: {countdownDuration}s");
    }

    /// <summary>
    /// Checks if countdown should start (room is full)
    /// </summary>
    private void CheckIfShouldStartCountdown()
    {
        if (!PhotonNetwork.InRoom)
            return;

        Room room = PhotonNetwork.CurrentRoom;
        
        // Check if room is full
        if (room.PlayerCount >= room.MaxPlayers)
        {
            Debug.Log($"[Timer] Room full! ({room.PlayerCount}/{room.MaxPlayers}) Starting countdown...");
            
            // Only Master Client starts the countdown
            if (PhotonNetwork.IsMasterClient && !countdownStarted)
            {
                StartCountdown();
            }
            // Non-master clients sync from room properties
            else if (!PhotonNetwork.IsMasterClient)
            {
                SyncCountdownFromRoomProperties();
            }
        }
        else
        {
            Debug.Log($"[Timer] Waiting for players... ({room.PlayerCount}/{room.MaxPlayers})");
        }
    }

    /// <summary>
    /// Syncs countdown state from room properties (for late joiners or non-master clients)
    /// </summary>
    private void SyncCountdownFromRoomProperties()
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("CountdownStarted"))
        {
            bool started = (bool)PhotonNetwork.CurrentRoom.CustomProperties["CountdownStarted"];
            
            if (started && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("CountdownStartTime"))
            {
                startTime = (double)PhotonNetwork.CurrentRoom.CustomProperties["CountdownStartTime"];
                countdownStarted = true;
                Debug.Log("[Timer] Synced countdown from room properties");
            }
        }
    }

    /// <summary>
    /// Triggers when countdown reaches zero
    /// </summary>
    private void TriggerGameOver()
    {
        if (gameOverTriggered)
            return;

        gameOverTriggered = true;
        
        Debug.Log("========================================");
        Debug.Log("           🎮 GAME OVER! 🎮            ");
        Debug.Log("========================================");
        
        // Only Master Client handles scene transition or game over logic
        if (PhotonNetwork.IsMasterClient)
        {
            if (justLogGameOver)
            {
                Debug.Log("[Timer] Game Over! (Log only mode)");
                // You can add your custom game over logic here
                // For example: Calculate winner, show results, etc.
            }
            else
            {
                Debug.Log($"[Timer] Game Over! Loading scene: {gameOverSceneName}");
                PhotonNetwork.LoadLevel(gameOverSceneName);
            }
        }
    }

    #region Photon Callbacks

    /// <summary>
    /// Called when a player enters the room
    /// </summary>
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Timer] Player joined: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        CheckIfShouldStartCountdown();
    }

    /// <summary>
    /// Called when local player successfully joins a room
    /// </summary>
    public override void OnJoinedRoom()
    {
        Debug.Log($"[Timer] Joined room: {PhotonNetwork.CurrentRoom.Name}");
        
        // Sync countdown if it already started
        SyncCountdownFromRoomProperties();
        
        // Check if room is already full
        CheckIfShouldStartCountdown();
    }

    /// <summary>
    /// Called when room properties are updated
    /// </summary>
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        // Sync countdown state when properties change
        if (propertiesThatChanged.ContainsKey("CountdownStarted") && !countdownStarted)
        {
            SyncCountdownFromRoomProperties();
        }
    }

    /// <summary>
    /// Called when a player leaves the room
    /// </summary>
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Timer] Player left: {otherPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        
        // Optional: Pause or reset countdown if player leaves during game
        // Uncomment below if you want to pause when someone leaves
        /*
        if (countdownStarted && !gameOverTriggered)
        {
            Debug.LogWarning("[Timer] Player left during countdown! Pausing...");
            countdownStarted = false;
        }
        */
    }

    #endregion

    #region Public Methods for External Control

    /// <summary>
    /// Manually start countdown (can be called from other scripts)
    /// </summary>
    public void ManualStartCountdown()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            StartCountdown();
        }
    }

    /// <summary>
    /// Get remaining time in seconds
    /// </summary>
    public float GetRemainingTime()
    {
        if (!countdownStarted)
            return countdownDuration;

        double elapsed = PhotonNetwork.Time - startTime;
        return Mathf.Max(0f, countdownDuration - (float)elapsed);
    }

    /// <summary>
    /// Check if countdown is currently running
    /// </summary>
    public bool IsCountdownActive()
    {
        return countdownStarted && !gameOverTriggered;
    }

    #endregion
}