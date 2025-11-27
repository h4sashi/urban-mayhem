using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Photon.Pun;
using Hanzo.Networking;

/// <summary>
/// Displays game results on the Game Over screen
/// Shows Position, Username, and Score from the match
/// </summary>
public class GameOverLeaderboardUI : MonoBehaviourPunCallbacks
{
    [Header("UI References")]
    [SerializeField] private GameObject leaderboardPanel;
    [SerializeField] private Transform leaderboardContent; // Parent for row prefabs
    
    [Header("Row Template")]
    [SerializeField] private GameObject leaderboardRowPrefab;
    
   
    private TextMeshProUGUI positionTextTemplate;
     private TextMeshProUGUI usernameTextTemplate;
     private TextMeshProUGUI scoreTextTemplate;
    
    [Header("Settings")]
    [SerializeField] private bool sortByScore = true;
    [SerializeField] private int maxLeaderboardEntries = 10;
    [SerializeField] private bool debugMode = true;
    
    private void Start()
    {
        // Hide initially
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Called from PhotonCountdownTimer when game ends
    /// Displays the final scores
    /// </summary>
    public void DisplayGameResults()
    {
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(true);
        }
        
        PopulateLeaderboard();
    }
    
    /// <summary>
    /// Populate the leaderboard with current game results from Photon
    /// </summary>
    private void PopulateLeaderboard()
    {
        // Clear existing rows
        foreach (Transform child in leaderboardContent)
        {
            Destroy(child.gameObject);
        }
        
        // Get all players and their scores
        List<PlayerLeaderboardEntry> entries = GetPlayerEntries();
        
        // Sort by score (descending)
        if (sortByScore)
        {
            entries.Sort((a, b) => b.Score.CompareTo(a.Score));
        }
        
        // Display top entries
        for (int i = 0; i < Mathf.Min(entries.Count, maxLeaderboardEntries); i++)
        {
            CreateLeaderboardRow(i + 1, entries[i]);
        }
        
        if (debugMode)
        {
            Debug.Log($"[GameOverUI] Displayed {entries.Count} players on leaderboard");
        }
    }
    
    /// <summary>
    /// Get all players from current room with their scores
    /// </summary>
    private List<PlayerLeaderboardEntry> GetPlayerEntries()
    {
        List<PlayerLeaderboardEntry> entries = new List<PlayerLeaderboardEntry>();
        
        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning("[GameOverUI] Not in a room!");
            return entries;
        }
        
        foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
        {
            int score = NetworkedScoreManager.Instance.GetPlayerScore(player.ActorNumber);
            int kills = NetworkedScoreManager.Instance.GetPlayerKills(player.ActorNumber);
            int deaths = NetworkedScoreManager.Instance.GetPlayerDeaths(player.ActorNumber);
            int hits = NetworkedScoreManager.Instance.GetPlayerHitsTaken(player.ActorNumber);
            
            entries.Add(new PlayerLeaderboardEntry
            {
                ActorNumber = player.ActorNumber,
                Username = player.NickName,
                Score = score,
                Kills = kills,
                Deaths = deaths,
                Hits = hits
            });
        }
        
        return entries;
    }
    
    /// <summary>
    /// Create a single row in the leaderboard UI
    /// </summary>
    private void CreateLeaderboardRow(int position, PlayerLeaderboardEntry entry)
    {
        // Instantiate row from prefab
        GameObject rowInstance = Instantiate(leaderboardRowPrefab, leaderboardContent);
        
        // Get text components from the row
        TextMeshProUGUI[] textComponents = rowInstance.GetComponentsInChildren<TextMeshProUGUI>();
        
        if (textComponents.Length >= 3)
        {
            // Assign values to the three columns
            textComponents[0].text = position.ToString(); // Position
            textComponents[1].text = entry.Username; // Username
            textComponents[2].text = entry.Score.ToString(); // Score
            
            if (debugMode)
            {
                Debug.Log($"[GameOverUI] Row {position}: {entry.Username} - Score: {entry.Score}");
            }
        }
        else
        {
            Debug.LogWarning($"[GameOverUI] Row prefab doesn't have 3 TextMeshProUGUI components!");
        }
    }
    
    /// <summary>
    /// Hide the leaderboard
    /// </summary>
    public void HideLeaderboard()
    {
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Data structure for leaderboard entry
    /// </summary>
    private struct PlayerLeaderboardEntry
    {
        public int ActorNumber;
        public string Username;
        public int Score;
        public int Kills;
        public int Deaths;
        public int Hits;
    }
}