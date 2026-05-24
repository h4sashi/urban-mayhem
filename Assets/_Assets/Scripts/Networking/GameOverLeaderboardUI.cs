using System.Collections.Generic;
using Hanzo.Networking;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Displays game results on the Game Over screen
/// Shows Position, Username, Kills, and Deaths from the match
/// </summary>
public class GameOverLeaderboardUI : MonoBehaviourPunCallbacks
{
    [Header("UI References")]
    [SerializeField]
    private GameObject leaderboardPanel;

    [SerializeField]
    private GameObject lobbyButton;

    [SerializeField]
    private Transform leaderboardContent; // Parent for row prefabs

    [Header("Row Template")]
    [SerializeField]
    private GameObject leaderboardRowPrefab;

    [Header("Settings")]
    [FormerlySerializedAs("sortByScore")]
    [SerializeField]
    private bool sortByKills = true;

    [SerializeField]
    private int maxLeaderboardEntries = 10;

    [SerializeField]
    private bool debugMode = false;

    private static readonly Color DefaultRowColor = new Color32(18, 24, 38, 238);
    private static readonly Color FirstPlaceRowColor = new Color32(98, 72, 24, 255);
    private static readonly Color SecondPlaceRowColor = new Color32(46, 58, 78, 250);
    private static readonly Color ThirdPlaceRowColor = new Color32(80, 45, 32, 250);
    private static readonly Color LocalPlayerRowColor = new Color32(18, 96, 112, 255);
    private static readonly Color PrimaryTextColor = new Color32(236, 243, 255, 255);

    private void Start()
    {
        // Hide initially
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
        if (lobbyButton != null)
        {
            lobbyButton.SetActive(false);
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
        if (lobbyButton != null)
        {
            lobbyButton.SetActive(true);
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

        // Get all players and their match stats
        List<PlayerLeaderboardEntry> entries = GetPlayerEntries();

        // Sort by kills descending, then deaths ascending
        if (sortByKills)
        {
            entries.Sort(
                (a, b) =>
                {
                    int killCompare = b.Kills.CompareTo(a.Kills);
                    return killCompare != 0 ? killCompare : a.Deaths.CompareTo(b.Deaths);
                }
            );
        }

        for (int i = 0; i < entries.Count; i++)
        {
            PlayerLeaderboardEntry rankedEntry = entries[i];
            rankedEntry.Rank = i + 1;
            entries[i] = rankedEntry;
        }

        List<PlayerLeaderboardEntry> displayedEntries = GetDisplayedEntries(entries);

        for (int i = 0; i < displayedEntries.Count; i++)
        {
            CreateLeaderboardRow(displayedEntries[i]);
        }

        if (debugMode)
        {
            Debug.Log(
                $"[GameOverUI] Displayed {displayedEntries.Count} of {entries.Count} players on leaderboard"
            );
        }
    }

    private List<PlayerLeaderboardEntry> GetDisplayedEntries(List<PlayerLeaderboardEntry> rankedEntries)
    {
        List<PlayerLeaderboardEntry> displayedEntries = new List<PlayerLeaderboardEntry>();

        int topCount = Mathf.Min(3, rankedEntries.Count);
        int displayLimit = Mathf.Min(maxLeaderboardEntries, rankedEntries.Count);
        int localPlayerIndex = rankedEntries.FindIndex(entry => entry.IsLocalPlayer);
        bool pinLocalPlayer = localPlayerIndex >= topCount;

        if (pinLocalPlayer)
        {
            displayLimit = Mathf.Min(rankedEntries.Count, Mathf.Max(displayLimit, topCount + 1));
        }

        for (int i = 0; i < topCount && displayedEntries.Count < displayLimit; i++)
        {
            displayedEntries.Add(rankedEntries[i]);
        }

        if (pinLocalPlayer && displayedEntries.Count < displayLimit)
        {
            displayedEntries.Add(rankedEntries[localPlayerIndex]);
        }

        for (int i = topCount; i < rankedEntries.Count && displayedEntries.Count < displayLimit; i++)
        {
            if (pinLocalPlayer && i == localPlayerIndex)
                continue;

            displayedEntries.Add(rankedEntries[i]);
        }

        return displayedEntries;
    }

    /// <summary>
    /// Get real players and AI players with their match kills/deaths.
    /// </summary>
    private List<PlayerLeaderboardEntry> GetPlayerEntries()
    {
        List<PlayerLeaderboardEntry> entries = new List<PlayerLeaderboardEntry>();

        if (!PhotonNetwork.InRoom)
            return entries;

        foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
        {
            entries.Add(
                new PlayerLeaderboardEntry
                {
                    ActorNumber = player.ActorNumber,
                    Username = player.NickName,
                    Kills = NetworkedScoreManager.Instance.GetPlayerKills(player.ActorNumber),
                    Deaths = NetworkedScoreManager.Instance.GetPlayerDeaths(player.ActorNumber),
                    IsLocalPlayer = player.ActorNumber == PhotonNetwork.LocalPlayer.ActorNumber,
                }
            );
        }

        foreach (var aiPlayer in NetworkedScoreManager.Instance.GetAIPlayerNames())
        {
            entries.Add(
                new PlayerLeaderboardEntry
                {
                    ActorNumber = aiPlayer.Key,
                    Username = aiPlayer.Value + " [AI]",
                    Kills = NetworkedScoreManager.Instance.GetAIKills(aiPlayer.Key),
                    Deaths = NetworkedScoreManager.Instance.GetAIDeaths(aiPlayer.Key),
                }
            );
        }

        return entries;
    }

    /// <summary>
    /// Create a single row in the leaderboard UI
    /// </summary>
    private void CreateLeaderboardRow(PlayerLeaderboardEntry entry)
    {
        // Instantiate row from prefab
        GameObject rowInstance = Instantiate(leaderboardRowPrefab, leaderboardContent);

        TextMeshProUGUI positionText = FindRowText(
            rowInstance.transform,
            "_positionText",
            "PositionText",
            "Position"
        );
        TextMeshProUGUI usernameText = FindRowText(
            rowInstance.transform,
            "_usernameText",
            "UsernameText",
            "NameText",
            "Username",
            "PlayerName"
        );
        TextMeshProUGUI killsText = FindRowText(
            rowInstance.transform,
            "_killsText",
            "KillsText",
            "Kills"
        );
        TextMeshProUGUI deathsText = FindRowText(
            rowInstance.transform,
            "DeathText",
            "_deathText",
            "_deathsText",
            "DeathsText",
            "Deaths"
        );

        // Fallback for older row prefabs that still rely on hierarchy order.
        TextMeshProUGUI[] textComponents = rowInstance.GetComponentsInChildren<TextMeshProUGUI>(
            true
        );
        if (positionText == null && textComponents.Length > 0)
            positionText = textComponents[0];
        if (usernameText == null && textComponents.Length > 1)
            usernameText = textComponents[1];
        if (killsText == null && textComponents.Length > 2)
            killsText = textComponents[2];
        if (deathsText == null && textComponents.Length > 3)
            deathsText = textComponents[3];

        if (positionText != null && usernameText != null && killsText != null && deathsText != null)
        {
            positionText.text = entry.Rank.ToString();
            usernameText.text = entry.Username;
            killsText.text = entry.Kills.ToString();
            deathsText.text = entry.Deaths.ToString();
            ApplyRowVisualStyle(
                entry,
                rowInstance,
                positionText,
                usernameText,
                killsText,
                deathsText
            );

            if (debugMode)
            {
                Debug.Log(
                    $"[GameOverUI] Rank {entry.Rank}: {entry.Username} - Kills: {entry.Kills}, Deaths: {entry.Deaths}"
                );
            }
        }
        else
        {
            Debug.LogWarning(
                "[GameOverUI] LeaderboardStats prefab is missing one or more text fields: Position, Username, Kills, Deaths."
            );
        }
    }

    private void ApplyRowVisualStyle(
        PlayerLeaderboardEntry entry,
        GameObject rowInstance,
        TextMeshProUGUI positionText,
        TextMeshProUGUI usernameText,
        TextMeshProUGUI killsText,
        TextMeshProUGUI deathsText
    )
    {
        Image rowBackground = rowInstance.GetComponent<Image>();
        if (rowBackground != null)
        {
            if (entry.IsLocalPlayer)
                rowBackground.color = LocalPlayerRowColor;
            else if (entry.Rank == 1)
                rowBackground.color = FirstPlaceRowColor;
            else if (entry.Rank == 2)
                rowBackground.color = SecondPlaceRowColor;
            else if (entry.Rank == 3)
                rowBackground.color = ThirdPlaceRowColor;
            else
                rowBackground.color = DefaultRowColor;
        }

        positionText.color = PrimaryTextColor;
        usernameText.color = PrimaryTextColor;
        killsText.color = PrimaryTextColor;
        deathsText.color = PrimaryTextColor;

        if (entry.IsLocalPlayer || entry.Rank == 1)
        {
            positionText.fontStyle |= FontStyles.Bold;
            usernameText.fontStyle |= FontStyles.Bold;
            killsText.fontStyle |= FontStyles.Bold;
            deathsText.fontStyle |= FontStyles.Bold;
        }
    }

    private TextMeshProUGUI FindRowText(Transform root, params string[] names)
    {
        foreach (string targetName in names)
        {
            Transform found = FindChildRecursive(root, targetName);
            if (found == null)
                continue;

            TextMeshProUGUI text = found.GetComponent<TextMeshProUGUI>();
            if (text != null)
                return text;
        }

        return null;
    }

    private Transform FindChildRecursive(Transform root, string targetName)
    {
        if (root.name == targetName)
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindChildRecursive(child, targetName);
            if (found != null)
                return found;
        }

        return null;
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
        if (lobbyButton != null)
        {
            lobbyButton.SetActive(true);
        }
    }

    public void BackToLobby()
    {
        Debug.Log("[Leaderboard] Returning to Lobby...");

        // Only leave room for this player
        // OnLeftRoom callback will handle scene transition
        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        base.OnLeftRoom();
        Debug.Log("[Leaderboard] Left room successfully, loading Main scene...");

        // Load scene locally (only for this player)
        SceneManager.LoadScene("Main");
    }

    /// <summary>
    /// Data structure for leaderboard entry
    /// </summary>
    private struct PlayerLeaderboardEntry
    {
        public int Rank;
        public int ActorNumber;
        public string Username;
        public int Kills;
        public int Deaths;
        public bool IsLocalPlayer;
    }
}
