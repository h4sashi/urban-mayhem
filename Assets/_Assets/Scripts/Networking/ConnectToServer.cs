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
    
    private int minPlayers = 3;
    private int maxPlayers = 9;
    private int selectedMaxPlayers = 3;
    
    void Start()
    {
        // Initialize scrollbar
        if (scrollbar != null)
        {
            // Set scrollbar value based on min players (value = 0 corresponds to min players)
            scrollbar.value = 0f; // Start at minimum
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
    }
    
    private void OnScrollbarValueChanged(float value)
    {
        // Convert scrollbar value (0-1) to player count (3-9)
        // Formula: minPlayers + (value * (maxPlayers - minPlayers))
        selectedMaxPlayers = minPlayers + Mathf.RoundToInt(value * (maxPlayers - minPlayers));
        
        // Ensure we stay within bounds
        selectedMaxPlayers = Mathf.Clamp(selectedMaxPlayers, minPlayers, maxPlayers);
        
        UpdateMaxPlayerText();
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
    
    // Optional: Save to PlayerPrefs for persistence
    public void SavePlayerCount()
    {
        PlayerPrefs.SetInt("RoomMaxPlayers", selectedMaxPlayers);
        PlayerPrefs.Save();
    }
    
    // Optional: Load from PlayerPrefs on start
    private void LoadPlayerCount()
    {
        if (PlayerPrefs.HasKey("RoomMaxPlayers"))
        {
            int savedCount = PlayerPrefs.GetInt("RoomMaxPlayers", minPlayers);
            savedCount = Mathf.Clamp(savedCount, minPlayers, maxPlayers);
            
            // Convert saved count back to scrollbar value
            float scrollValue = (float)(savedCount - minPlayers) / (maxPlayers - minPlayers);
            if (scrollbar != null)
            {
                scrollbar.value = scrollValue;
            }
            selectedMaxPlayers = savedCount;
            UpdateMaxPlayerText();
        }
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
        
        // Save player count before loading lobby scene
        SavePlayerCount();
        SceneManager.LoadScene("Lobby");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"Disconnected from Photon: {cause}");
    }
}