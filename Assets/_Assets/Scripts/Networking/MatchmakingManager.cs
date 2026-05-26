using System.Collections;
using Hanzo.Networking.PlayFab;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MatchmakingManager : MonoBehaviourPunCallbacks
{
    [Header("UI References")]
    public Button playButton;
    public TextMeshProUGUI countdownText;
    public TextMeshProUGUI statusText; // Optional: for showing connection status

    [Header("Matchmaking Settings")]
    [SerializeField]
    private byte requiredPlayers; // Number of players needed to start

    [SerializeField]
    private float aiSpawnTimeout = 30f; // Time to wait before spawning AIs (30 seconds)

    [SerializeField]
    private string gameSceneName = "Main 2"; // Game scene to load

    [Header("AI Settings")]
    [SerializeField]
    private int numberOfAIsToSpawn; // Number of AI players to spawn if no one joins

    [SerializeField]
    private string aiPrefabName = "AIPlayer"; // Name of AI prefab in Resources folder

    [Header("Connection Settings")]
    [SerializeField]
    private string gameVersion = "1.0";

    private bool isMatchmaking = false;
    private float currentCountdown;
    private Coroutine countdownCoroutine;
    private bool hasStartedGame = false;
    private bool startWithAIsPending = false;

    void Start()
    {
        // Setup Photon
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = gameVersion;
        PhotonNetwork.SendRate = 30;
        PhotonNetwork.SerializationRate = 20;

        // Set nickname from the profile that was loaded during login.
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            string nickname = PlayFabManager.Instance != null
                ? PlayFabManager.Instance.PlayerDisplayName
                : string.Empty;

            if (string.IsNullOrWhiteSpace(nickname))
            {
                nickname = PlayerPrefs.GetString("USERNAME", string.Empty);
            }

            PhotonNetwork.NickName = string.IsNullOrWhiteSpace(nickname)
                ? "Player" + Random.Range(1000, 9999)
                : nickname.Trim();
        }

        // Load max players setting
        // requiredPlayers = (byte)PlayerPrefs.GetInt("RoomMaxPlayers", 2);
        // requiredPlayers = (byte)Mathf.Clamp(requiredPlayers, 2, 9);

        // Setup UI
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonClicked);
            playButton.interactable = false; // Disabled until connected
        }

        // Initialize countdown at 0:00
        if (countdownText != null)
        {
            countdownText.text = "";
        }

        // Connect to Photon
        ConnectToPhoton();
    }

    void ConnectToPhoton()
    {
        if (!PhotonNetwork.IsConnected)
        {
            UpdateStatus("Connecting to server...");
            Debug.Log("[Matchmaking] Connecting to Photon...");
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            UpdateStatus("Connected!");
            if (playButton != null)
                playButton.interactable = true;
        }
    }

    public void OnPlayButtonClicked()
    {
        if (!PhotonNetwork.IsConnected)
        {
            UpdateStatus("Not connected to server!");
            return;
        }

        if (isMatchmaking)
        {
            // Cancel matchmaking
            CancelMatchmaking();
        }
        else
        {
            // Start matchmaking
            StartMatchmaking();
        }
    }

    void StartMatchmaking()
    {
        isMatchmaking = true;
        hasStartedGame = false;
        UpdateStatus("Searching for match...");
        Debug.Log("[Matchmaking] Starting matchmaking...");

        // Start countdown
        if (countdownCoroutine != null)
            StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(CountdownCoroutine());

        // Try to join random room, if none exists, create one
        PhotonNetwork.JoinRandomRoom();
    }

    void CancelMatchmaking()
    {
        isMatchmaking = false;
        UpdateStatus("Matchmaking cancelled");
        Debug.Log("[Matchmaking] Cancelled matchmaking");

        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }

        // Reset countdown to 0:00
        if (countdownText != null)
            countdownText.text = "0:00";

        // Leave room if in one
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }
    }

    IEnumerator CountdownCoroutine()
    {
        currentCountdown = 0f; // Start from 0

        while (currentCountdown < aiSpawnTimeout && isMatchmaking && !hasStartedGame)
        {
            if (countdownText != null)
                countdownText.text = FormatTime(currentCountdown);

            currentCountdown += Time.deltaTime;
            yield return null;
        }

        // Timeout reached - spawn AIs and start game
        if (isMatchmaking && !hasStartedGame && currentCountdown >= aiSpawnTimeout)
        {
            Debug.Log(
                "[Matchmaking] 30 seconds passed with no other players. Starting game with AIs..."
            );
            UpdateStatus("Starting game with AI players...");
            StartGameWithAIs();
        }
    }

    string FormatTime(float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60);
        return string.Format("{0}:{1:00}", minutes, seconds);
    }

    void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log($"[Matchmaking] {message}");
    }



void StartGameWithAIs()
{
        if (hasStartedGame)
            return;

        if (!PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsConnected)
            {
                Debug.LogError("[Matchmaking] Cannot start game with AIs: not connected to Photon.");
                UpdateStatus("Disconnected: cannot start AI game.");
                isMatchmaking = false;
                return;
            }

            if (!startWithAIsPending)
            {
                startWithAIsPending = true;
                UpdateStatus("Creating room for AI match...");
                Debug.Log("[Matchmaking] Not in room yet. Creating room to start AI match.");

                RoomOptions roomOptions = new RoomOptions
                {
                    MaxPlayers = requiredPlayers,
                    IsVisible = false,
                    IsOpen = true,
                    PlayerTtl = PhotonReconnectRecovery.RoomPlayerTtlMilliseconds,
                    EmptyRoomTtl = PhotonReconnectRecovery.RoomEmptyTtlMilliseconds,
                };

                string roomName = "Room_AI_" + Random.Range(1000, 9999);
                PhotonNetwork.CreateRoom(roomName, roomOptions, TypedLobby.Default);
            }

            return;
        }

        hasStartedGame = true;
        isMatchmaking = false;
        startWithAIsPending = false;

        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }

        int currentPlayers = PhotonNetwork.CurrentRoom.PlayerCount;
        int aisNeeded = Mathf.Max(0, requiredPlayers - currentPlayers);

        Debug.Log($"[Matchmaking] Required={requiredPlayers}, " +
                  $"RealPlayers={currentPlayers}, AIsNeeded={aisNeeded}");

        PlayerPrefs.SetInt("SpawnAIPlayers",   aisNeeded > 0 ? 1 : 0);
        PlayerPrefs.SetInt("NumberOfAIs",      aisNeeded);
        PlayerPrefs.SetInt("RealPlayerCount",  currentPlayers);
        PlayerPrefs.SetInt("RequiredPlayers",  requiredPlayers);
        PlayerPrefs.SetString("AIPrefabName",  aiPrefabName);
        PlayerPrefs.Save();

        PhotonNetwork.CurrentRoom.IsOpen    = false;
        PhotonNetwork.CurrentRoom.IsVisible = false;

        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"[Matchmaking] Loading {gameSceneName} — spawning {aisNeeded} AIs.");
            PhotonNetwork.LoadLevel(gameSceneName);
        }
        else
        {
            Debug.Log("[Matchmaking] Waiting for MasterClient to load the scene.");
        }
    }



    // ==================== PHOTON CALLBACKS ====================

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Matchmaking] Connected to Master Server");
        UpdateStatus("Connected!");

        if (playButton != null)
            playButton.interactable = true;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"[Matchmaking] Disconnected: {cause}");
        UpdateStatus($"Disconnected: {cause}");

        if (playButton != null)
            playButton.interactable = true;

        isMatchmaking = false;
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        // No room available, create a new one
        Debug.Log($"[Matchmaking] No room found, creating new room... ({message})");
        UpdateStatus("Creating new match...");

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = requiredPlayers,
            IsVisible = true,
            IsOpen = true,
            PlayerTtl = PhotonReconnectRecovery.RoomPlayerTtlMilliseconds,
            EmptyRoomTtl = PhotonReconnectRecovery.RoomEmptyTtlMilliseconds,
        };

        // Create room with random name
        string roomName = "Room_" + Random.Range(1000, 9999);
        PhotonNetwork.CreateRoom(roomName, roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log($"[Matchmaking] Joined room: {room.Name} ({room.PlayerCount}/{room.MaxPlayers})");
        UpdateStatus($"In lobby: {room.PlayerCount}/{room.MaxPlayers} players");

        // Check if we have enough players
        CheckIfCanStartGame();

        if (startWithAIsPending && !hasStartedGame)
        {
            StartGameWithAIs();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log(
            $"[Matchmaking] Player joined: {newPlayer.NickName} ({room.PlayerCount}/{room.MaxPlayers})"
        );
        UpdateStatus($"In lobby: {room.PlayerCount}/{room.MaxPlayers} players");

        // Check if we have enough players
        CheckIfCanStartGame();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log(
            $"[Matchmaking] Player left: {otherPlayer.NickName} ({room.PlayerCount}/{room.MaxPlayers})"
        );
        UpdateStatus($"In lobby: {room.PlayerCount}/{room.MaxPlayers} players");
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[Matchmaking] Create room failed: ({returnCode}) {message}");
        UpdateStatus("Failed to create room. Retrying...");

        // Try joining random again
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnLeftRoom()
    {
        Debug.Log("[Matchmaking] Left room");
    }

    void CheckIfCanStartGame()
    {
        if (!isMatchmaking || hasStartedGame)
            return;

        Room room = PhotonNetwork.CurrentRoom;
        if (room == null || room.PlayerCount < requiredPlayers)
            return;

        Debug.Log($"[Matchmaking] Room full ({room.PlayerCount}/{requiredPlayers}). Starting...");
        UpdateStatus("Starting game...");

        hasStartedGame = true;
        isMatchmaking = false;

        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }

        // No AIs needed — real players filled the room
        PlayerPrefs.SetInt("SpawnAIPlayers", 0);
        PlayerPrefs.SetInt("NumberOfAIs", 0);
        PlayerPrefs.SetInt("RealPlayerCount", room.PlayerCount);
        PlayerPrefs.Save();

        if (PhotonNetwork.IsMasterClient)
        {
            room.IsOpen = false;
            room.IsVisible = false;
            PhotonNetwork.LoadLevel(gameSceneName);
        }
    }

    void OnDestroy()
    {
        if (playButton != null)
            playButton.onClick.RemoveListener(OnPlayButtonClicked);
    }
}
