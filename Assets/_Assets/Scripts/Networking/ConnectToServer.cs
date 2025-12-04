using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class ConnectToServer : MonoBehaviourPunCallbacks
{
    public TextMeshProUGUI maxPlayerText;
    public Scrollbar scrollbar;
    public int minPlayers = 2;
    public int maxPlayers = 9;
    private int selectedMaxPlayers = 2;

    void Start()
    {
        // Load saved player count FIRST (before initializing scrollbar)
        LoadPlayerCount();

        // Initialize scrollbar
        if (scrollbar != null)
        {
            // Set scrollbar value based on loaded/default selectedMaxPlayers
            float initialScrollValue = (float)(selectedMaxPlayers - minPlayers) / (maxPlayers - minPlayers);
            scrollbar.value = initialScrollValue;
            scrollbar.onValueChanged.AddListener(OnScrollbarValueChanged);
        }

        // Initialize text display
        UpdateMaxPlayerText();

        // Set game version BEFORE connecting
        PhotonNetwork.GameVersion = "1.0"; // Must match CreateAndJoinRooms!
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us";
        PhotonNetwork.ConnectUsingSettings();
        
        Debug.Log("Connecting to Photon Server...");
        Debug.Log($"Game Version: {PhotonNetwork.GameVersion}");
        Debug.Log($"Selected Max Players: {selectedMaxPlayers}");
    }

    private void OnScrollbarValueChanged(float value)
    {
        // FIXED: Use FloorToInt for consistent rounding behavior
        // This prevents the value from jumping unexpectedly (e.g., 2 -> 3)
        float range = maxPlayers - minPlayers;
        float scaledValue = value * range;
        selectedMaxPlayers = minPlayers + Mathf.FloorToInt(scaledValue);
        
        // Ensure we stay within bounds
        selectedMaxPlayers = Mathf.Clamp(selectedMaxPlayers, minPlayers, maxPlayers);
        
        // Save immediately when changed
        SavePlayerCount();
        
        UpdateMaxPlayerText();
        
        Debug.Log($"[ConnectToServer] Scrollbar={value:F3}, Scaled={scaledValue:F3}, Max players={selectedMaxPlayers}");
    }

    private void UpdateMaxPlayerText()
    {
        if (maxPlayerText != null)
        {
            maxPlayerText.text = $"{selectedMaxPlayers}";
        }
    }

    // Call this method when creating a room (you'll need to pass this to your room creation logic)
    public int GetSelectedMaxPlayers()
    {
        return selectedMaxPlayers;
    }

    // Save to PlayerPrefs for persistence
    public void SavePlayerCount()
    {
        PlayerPrefs.SetInt("RoomMaxPlayers", selectedMaxPlayers);
        PlayerPrefs.Save();
        Debug.Log($"[ConnectToServer] Saved max players to PlayerPrefs: {selectedMaxPlayers}");
    }

    // Load from PlayerPrefs on start
    private void LoadPlayerCount()
    {
        if (PlayerPrefs.HasKey("RoomMaxPlayers"))
        {
            int savedCount = PlayerPrefs.GetInt("RoomMaxPlayers", minPlayers);
            savedCount = Mathf.Clamp(savedCount, minPlayers, maxPlayers);
            selectedMaxPlayers = savedCount;
            
            Debug.Log($"[ConnectToServer] Loaded max players from PlayerPrefs: {selectedMaxPlayers}");
        }
        else
        {
            // No saved value, use default (minPlayers)
            selectedMaxPlayers = minPlayers;
            Debug.Log($"[ConnectToServer] No saved max players, using default: {selectedMaxPlayers}");
        }
        
        UpdateMaxPlayerText();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        Debug.Log($"Current Region: {PhotonNetwork.CloudRegion}");
        Debug.Log($"Server Address: {PhotonNetwork.ServerAddress}");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("Joined Lobby");
        Debug.Log($"In Lobby - Region: {PhotonNetwork.CloudRegion}");
        Debug.Log($"Players in Lobby: {PhotonNetwork.CountOfPlayers}");
        
        // Ensure player count is saved before loading lobby scene
        SavePlayerCount();
        
        SceneManager.LoadScene("Lobby");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"Disconnected from Photon: {cause}");
    }
}