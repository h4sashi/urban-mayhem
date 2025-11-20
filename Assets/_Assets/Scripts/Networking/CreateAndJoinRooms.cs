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
    private byte maxPlayers = 2;

    [SerializeField]
    private string gameVersion = "1.0";

    void Start()
    {
        // Basic safety: ensure Photon connected
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us"; // or "eu", "asia", etc.
        PhotonNetwork.GameVersion = gameVersion;

        PhotonNetwork.NickName = "Player" + Random.Range(1000, 9999);
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
            MaxPlayers = maxPlayers,
            IsVisible = true,
            IsOpen = true,
        };
        Debug.Log($"[CreateAndJoinRooms] Creating room '{roomName}' (maxPlayers={maxPlayers})");
        feedbackText?.SetText($"Creating room '{roomName}'...");
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
        Debug.Log("[CreateAndJoinRooms] OnJoinedRoom: " + PhotonNetwork.CurrentRoom.Name);
        feedbackText?.SetText("Joined Room: " + PhotonNetwork.CurrentRoom.Name);

        // Load level for all players in the room (requires AutomaticallySyncScene true)
        Debug.Log("[CreateAndJoinRooms] Loading Main scene...");
        PhotonNetwork.LoadLevel("Main 2"); // ensure "Main" exists in Build Settings
    }

    // Called when CreateRoom fails
    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[CreateAndJoinRooms] CreateRoomFailed: ({returnCode}) {message}");
        feedbackText?.SetText($"Create room failed: {message} ({returnCode})");
    }

    // Called when JoinRoom fails
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[CreateAndJoinRooms] JoinRoomFailed: ({returnCode}) {message}");
        feedbackText?.SetText($"Join room failed: {message} ({returnCode})");
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
}
