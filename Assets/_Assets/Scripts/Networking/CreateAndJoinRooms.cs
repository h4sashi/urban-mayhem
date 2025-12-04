using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CreateAndJoinRooms : MonoBehaviourPunCallbacks
{
    [Header("UI")]
    public TMP_InputField CreateRoomInputField;
    public TMP_InputField JoinRoomInputField;
    public TextMeshProUGUI feedbackText;

    [Header("Options")]
    [SerializeField]
    private byte defaultMaxPlayers = 2; // Fallback value if PlayerPrefs not set
    
    private byte maxPlayers; // Will be loaded from PlayerPrefs

    [SerializeField]
    private string gameVersion = "1.0";

    [Header("Game Scene")]
    [SerializeField]
    private string gameSceneName = "Main 2"; // Scene to load when joining room

    void Start()
    {
        // ========== LOAD MAX PLAYERS FROM PLAYERPREFS ==========
        // This value was set by the scrollbar in ConnectToServer scene
        maxPlayers = (byte)PlayerPrefs.GetInt("RoomMaxPlayers", defaultMaxPlayers);
        maxPlayers = (byte)Mathf.Clamp(maxPlayers, 2, 9); // Safety clamp (2-9 players)
        
        Debug.Log($"[CreateAndJoinRooms] Max players loaded from PlayerPrefs: {maxPlayers}");
        
        // Basic safety: ensure Photon connected
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us"; // or "eu", "asia", etc.
        PhotonNetwork.GameVersion = gameVersion;

        // Generate a random nickname if not set
        if (string.IsNullOrEmpty(PhotonNetwork.NickName))
        {
            PhotonNetwork.NickName = PlayerPrefs.GetString("USERNAME");
            Debug.Log($"[CreateAndJoinRooms] Setting player nickname to: {PhotonNetwork.NickName}");
        }

        PhotonNetwork.AutomaticallySyncScene = true;

        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("[CreateAndJoinRooms] Not connected. Connecting to Photon...");
            feedbackText?.SetText("Connecting to server...");
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            Debug.Log("[CreateAndJoinRooms] Already connected to Photon.");
            feedbackText?.SetText("Connected.");
        }
    }

    public void CreateRoom()
    {
        if (!PhotonNetwork.IsConnected)
        {
            feedbackText?.SetText("Not connected to server yet. Please wait...");
            Debug.LogWarning("[CreateAndJoinRooms] CreateRoom called but Photon not connected.");
            return;
        }

        string roomName = CreateRoomInputField != null ? CreateRoomInputField.text.Trim() : "";
        if (string.IsNullOrEmpty(roomName))
        {
            feedbackText?.SetText("Room name cannot be empty.");
            return;
        }

        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayers, // Uses the value loaded from PlayerPrefs
            IsVisible = true,
            IsOpen = true,
        };

        Debug.Log($"[CreateAndJoinRooms] Creating room '{roomName}' (maxPlayers={maxPlayers})");
        feedbackText?.SetText($"Creating room '{roomName}' ({maxPlayers} players)...");
        PhotonNetwork.CreateRoom(roomName, roomOptions, TypedLobby.Default);
    }

    public void JoinRoom()
    {
        if (!PhotonNetwork.IsConnected)
        {
            feedbackText?.SetText("Not connected to server yet. Please wait...");
            Debug.LogWarning("[CreateAndJoinRooms] JoinRoom called but Photon not connected.");
            return;
        }

        string roomName = JoinRoomInputField != null ? JoinRoomInputField.text.Trim() : "";
        if (string.IsNullOrEmpty(roomName))
        {
            feedbackText?.SetText("Room name cannot be empty.");
            return;
        }

        Debug.Log($"[CreateAndJoinRooms] Joining room '{roomName}'");
        feedbackText?.SetText($"Joining room '{roomName}'...");
        PhotonNetwork.JoinRoom(roomName);
    }

    // Called when the local player has successfully joined a room
    public override void OnJoinedRoom()
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log(
            $"[CreateAndJoinRooms] OnJoinedRoom: {room.Name} ({room.PlayerCount}/{room.MaxPlayers})"
        );
        feedbackText?.SetText($"Joined Room: {room.Name} ({room.PlayerCount}/{room.MaxPlayers})");

        // Load level for all players in the room (requires AutomaticallySyncScene true)
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log($"[CreateAndJoinRooms] Master Client loading {gameSceneName}...");
            PhotonNetwork.LoadLevel(gameSceneName);
        }
    }

    // Called when CreateRoom fails
    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[CreateAndJoinRooms] CreateRoomFailed: ({returnCode}) {message}");
        feedbackText?.SetText($"Create room failed: {message}");

        // Common error: Room already exists
        if (returnCode == 32766) // Room already exists
        {
            feedbackText?.SetText("Room already exists. Try a different name.");
        }
    }

    // Called when JoinRoom fails
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[CreateAndJoinRooms] JoinRoomFailed: ({returnCode}) {message}");
        feedbackText?.SetText($"Join room failed: {message}");

        // Common error: Room doesn't exist
        if (returnCode == 32758) // Room not found
        {
            feedbackText?.SetText("Room not found. Check the name.");
        }
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError("[CreateAndJoinRooms] Disconnected: " + cause);
        feedbackText?.SetText("Disconnected: " + cause);
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[CreateAndJoinRooms] Connected to Master.");
        feedbackText?.SetText("Connected to server.");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log(
            $"[CreateAndJoinRooms] Player entered: {newPlayer.NickName} ({room.PlayerCount}/{room.MaxPlayers})"
        );
        feedbackText?.SetText($"Players: {room.PlayerCount}/{room.MaxPlayers}");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Room room = PhotonNetwork.CurrentRoom;
        Debug.Log(
            $"[CreateAndJoinRooms] Player left: {otherPlayer.NickName} ({room.PlayerCount}/{room.MaxPlayers})"
        );
        feedbackText?.SetText($"Players: {room.PlayerCount}/{room.MaxPlayers}");
    }
}