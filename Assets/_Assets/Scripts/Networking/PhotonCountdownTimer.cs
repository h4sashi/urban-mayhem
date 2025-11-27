using UnityEngine;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Linq;

/// <summary>
/// Photon-synchronized countdown timer that starts when room reaches max players.
/// Submits results to PlayFab when countdown completes.
/// </summary>
public class PhotonCountdownTimer : MonoBehaviourPunCallbacks
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private UnityEngine.UI.Image radialFillImage;
    
    [Header("Radial Fill Settings")]
    [SerializeField] private bool invertFill = false;
    [SerializeField] private Color fillStartColor = Color.green;
    [SerializeField] private Color fillMiddleColor = Color.yellow;
    [SerializeField] private Color fillEndColor = Color.red;
    [SerializeField] private bool useColorGradient = true;
    
    [Header("Timer Settings")]
    [SerializeField] private float countdownDuration = 60f;
    
    [Header("Game Over Settings")]
    [SerializeField] private string gameOverSceneName = "GameOver";
    [SerializeField] private bool justLogGameOver = true;
    [SerializeField] private bool submitToPlayFab = true;
    [SerializeField] private GameOverLeaderboardUI gameOverUI;

    public GameObject countdownTimerObject;
    
    // Timer state
    private double startTime;
    private bool countdownStarted = false;
    private bool gameOverTriggered = false;
    private PhotonView photonView;

    void Start()
    {
        photonView = GetComponent<PhotonView>();
        if (photonView == null)
        {
            Debug.LogError("[Timer] PhotonView not found! Add PhotonView component to this GameObject.");
        }
        
        UpdateTimerDisplay(countdownDuration);
        InitializeRadialFill();
        
        if (PhotonNetwork.InRoom)
        {
            CheckIfShouldStartCountdown();
        }
    }

    void Update()
    {
        if (!countdownStarted || gameOverTriggered)
            return;

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

    private void UpdateTimerDisplay(float timeInSeconds)
    {
        if (timerText == null)
            return;

        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        
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

    private void InitializeRadialFill()
    {
        if (radialFillImage == null)
            return;

        radialFillImage.type = UnityEngine.UI.Image.Type.Filled;
        radialFillImage.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
        radialFillImage.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
        radialFillImage.fillClockwise = true;
        radialFillImage.fillAmount = invertFill ? 1f : 0f;
        
        if (useColorGradient)
        {
            radialFillImage.color = fillStartColor;
        }
        
        Debug.Log("[Timer] Radial fill initialized");
    }

    private void UpdateRadialFill(float remainingTime)
    {
        if (radialFillImage == null)
            return;

        float progress = 1f - (remainingTime / countdownDuration);
        
        if (invertFill)
        {
            radialFillImage.fillAmount = 1f - progress;
        }
        else
        {
            radialFillImage.fillAmount = progress;
        }

        if (useColorGradient)
        {
            if (progress < 0.5f)
            {
                radialFillImage.color = Color.Lerp(fillStartColor, fillMiddleColor, progress * 2f);
            }
            else
            {
                radialFillImage.color = Color.Lerp(fillMiddleColor, fillEndColor, (progress - 0.5f) * 2f);
            }
        }
    }

    private void StartCountdown()
    {
        if (countdownStarted)
            return;

        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[Timer] Only Master Client can start countdown!");
            return;
        }

        startTime = PhotonNetwork.Time;
        countdownStarted = true;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            { "CountdownStartTime", startTime },
            { "CountdownStarted", true }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        Debug.Log($"[Timer] Countdown started! Duration: {countdownDuration}s");
    }

    private void CheckIfShouldStartCountdown()
    {
        if (!PhotonNetwork.InRoom)
            return;

        Room room = PhotonNetwork.CurrentRoom;
        
        if (room.PlayerCount >= room.MaxPlayers)
        {
            Debug.Log($"[Timer] Room full! ({room.PlayerCount}/{room.MaxPlayers}) Starting countdown...");
            
            if (PhotonNetwork.IsMasterClient && !countdownStarted)
            {
                StartCountdown();
            }
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
    /// NEW: Triggers when countdown reaches zero - submits to PlayFab
    /// </summary>
    private void TriggerGameOver()
    {
        if (gameOverTriggered)
            return;

        gameOverTriggered = true;
        
        
        // RPC to all players to display leaderboard at the same time
        photonView.RPC("RPC_DisplayGameOver", RpcTarget.All);
        
        // NEW: Submit scores to PlayFab (only local player submits their own stats)
        if (submitToPlayFab && Hanzo.Networking.PlayFabLeaderboardManager.Instance != null)
        {
            Debug.Log("[Timer] Submitting game results to PlayFab...");
            Hanzo.Networking.PlayFabLeaderboardManager.Instance.SubmitGameResults();
        }
        
        if (PhotonNetwork.IsMasterClient)
        {
            if (justLogGameOver)
            {
                Debug.Log("[Timer] Game Over! (Log only mode)");
            }
            else
            {
                Debug.Log($"[Timer] Game Over! Loading scene: {gameOverSceneName}");
                
                // Wait a moment before loading scene to allow PlayFab submissions
                StartCoroutine(LoadSceneWithDelay());
            }
        }
    }
    
    /// <summary>
    /// RPC: Called on all clients to display game over leaderboard simultaneously
    /// </summary>
    [PunRPC]
    private void RPC_DisplayGameOver()
    {
        if (gameOverUI != null)
        {
            gameOverUI.DisplayGameResults();
            Debug.Log("[Timer] Game Over UI displayed on all clients");

            GameObject.FindGameObjectsWithTag("Player").ToList().ForEach(playerObj =>
            {
                playerObj.SetActive(false);
                countdownTimerObject.SetActive(false);
            });
        }
    }

    private System.Collections.IEnumerator LoadSceneWithDelay()
    {
        yield return new WaitForSeconds(2f); // Give time for PlayFab submissions
        PhotonNetwork.LoadLevel(gameOverSceneName);
    }

    #region Photon Callbacks

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[Timer] Player joined: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        CheckIfShouldStartCountdown();
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"[Timer] Joined room: {PhotonNetwork.CurrentRoom.Name}");
        SyncCountdownFromRoomProperties();
        CheckIfShouldStartCountdown();
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey("CountdownStarted") && !countdownStarted)
        {
            SyncCountdownFromRoomProperties();
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[Timer] Player left: {otherPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    #endregion

    #region Public Methods

    public void ManualStartCountdown()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            StartCountdown();
        }
    }

    public float GetRemainingTime()
    {
        if (!countdownStarted)
            return countdownDuration;

        double elapsed = PhotonNetwork.Time - startTime;
        return Mathf.Max(0f, countdownDuration - (float)elapsed);
    }

    public bool IsCountdownActive()
    {
        return countdownStarted && !gameOverTriggered;
    }

    #endregion
}