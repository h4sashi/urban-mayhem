using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;

public class ConnectToServer : MonoBehaviourPunCallbacks
{
    void Start()
    {
        // Set game version BEFORE connecting
        PhotonNetwork.GameVersion = "1.0"; // Must match CreateAndJoinRooms!
        
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us";
        PhotonNetwork.ConnectUsingSettings();
        Debug.Log("Connecting to Photon Server...");
        Debug.Log($"Game Version: {PhotonNetwork.GameVersion}");
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
        SceneManager.LoadScene("Lobby");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogError($"Disconnected from Photon: {cause}");
    }
}