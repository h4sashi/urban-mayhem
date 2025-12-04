using System.Collections;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using ExitGames.Client.Photon;
using UnityEngine.SceneManagement;

namespace Hanzo.Networking
{
    /// <summary>
    /// Handles end-of-game score submission to PlayFab leaderboards
    /// </summary>
    public class PlayFabLeaderboardManager : MonoBehaviourPunCallbacks
    {
        public static PlayFabLeaderboardManager Instance { get; private set; }

        [Header("Leaderboard Settings")]
        [SerializeField] private string killsLeaderboardId = "Kills";
        [SerializeField] private string scoreLeaderboardId = "Score";
        [SerializeField] private string survivorLeaderboardId = "SurvivalTime";
        [SerializeField] private bool debugSubmissions = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if(scene.name == "Main")
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Submits all players' final stats to PlayFab leaderboards
        /// Call this when countdown timer reaches zero
        /// </summary>
        public void SubmitGameResults()
        {
            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[Leaderboard] Not in a room!");
                return;
            }

            Dictionary<int, PlayerGameStats> playerStats = CalculatePlayerStats();

            foreach (var kvp in playerStats)
            {
                int actorNumber = kvp.Key;
                PlayerGameStats stats = kvp.Value;
                Photon.Realtime.Player player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);

                if (player != null && player.IsLocal)
                {
                    SubmitPlayerStats(stats);
                }
            }
        }

        /// <summary>
        /// Calculate final stats for all players from Photon custom properties
        /// </summary>
        private Dictionary<int, PlayerGameStats> CalculatePlayerStats()
        {
            Dictionary<int, PlayerGameStats> stats = new Dictionary<int, PlayerGameStats>();

            if (PhotonNetwork.CurrentRoom == null)
                return stats;

            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                PlayerGameStats playerStats = new PlayerGameStats
                {
                    PlayFabId = player.UserId,
                    DisplayName = player.NickName,
                    ActorNumber = player.ActorNumber,
                    
                    // Get stats from NetworkedScoreManager
                    Score = NetworkedScoreManager.Instance.GetPlayerScore(player.ActorNumber),
                    Kills = NetworkedScoreManager.Instance.GetPlayerKills(player.ActorNumber),
                    Deaths = NetworkedScoreManager.Instance.GetPlayerDeaths(player.ActorNumber),
                    HitsTaken = NetworkedScoreManager.Instance.GetPlayerHitsTaken(player.ActorNumber),
                };

                // Calculate derived stats
                playerStats.Accuracy = CalculateAccuracy(playerStats);
                playerStats.KillDeathRatio = CalculateKDRatio(playerStats);
                playerStats.SurvivalScore = CalculateSurvivalScore(playerStats);

                stats[player.ActorNumber] = playerStats;

                if (debugSubmissions)
                {
                    Debug.Log($"[Leaderboard] Calculated stats for {playerStats.DisplayName}: " +
                        $"Score={playerStats.Score}, Kills={playerStats.Kills}, Deaths={playerStats.Deaths}");
                }
            }

            return stats;
        }

        /// <summary>
        /// Submit a single player's stats to PlayFab
        /// </summary>
        private void SubmitPlayerStats(PlayerGameStats stats)
        {
            // Submit to multiple leaderboards based on different metrics
            SubmitToLeaderboard(stats.Score, scoreLeaderboardId, stats.DisplayName);
            SubmitToLeaderboard(stats.Kills, killsLeaderboardId, stats.DisplayName);
            SubmitToLeaderboard((int)stats.SurvivalScore, survivorLeaderboardId, stats.DisplayName);

            // Also update player statistics for tracking
            UpdatePlayerStatistics(stats);
        }

        /// <summary>
        /// Submit a score to a specific PlayFab leaderboard
        /// </summary>
        private void SubmitToLeaderboard(int score, string leaderboardId, string playerName)
        {
            var request = new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate
                    {
                        StatisticName = leaderboardId,
                        Value = score
                    }
                }
            };

            PlayFabClientAPI.UpdatePlayerStatistics(request,
                result =>
                {
                    if (debugSubmissions)
                    {
                        Debug.Log($"[Leaderboard] ✅ Submitted {leaderboardId}: {score} for {playerName}");
                    }
                },
                error =>
                {
                    Debug.LogError($"[Leaderboard] ❌ Failed to submit {leaderboardId}: {error.GenerateErrorReport()}");
                }
            );
        }

        /// <summary>
        /// Update comprehensive player statistics
        /// </summary>
        private void UpdatePlayerStatistics(PlayerGameStats stats)
        {
            var statistics = new List<StatisticUpdate>
            {
                new StatisticUpdate { StatisticName = "TotalGamesPlayed", Value = 1 }, // Increment
                new StatisticUpdate { StatisticName = "TotalKills", Value = stats.Kills },
                new StatisticUpdate { StatisticName = "TotalDeaths", Value = stats.Deaths },
                new StatisticUpdate { StatisticName = "TotalScore", Value = stats.Score },
                new StatisticUpdate { StatisticName = "AverageKDRatio", Value = (int)(stats.KillDeathRatio * 100) }, // Store as int (multiply by 100)
                new StatisticUpdate { StatisticName = "TotalHitsTaken", Value = stats.HitsTaken },
            };

            var request = new UpdatePlayerStatisticsRequest { Statistics = statistics };

            PlayFabClientAPI.UpdatePlayerStatistics(request,
                result => Debug.Log($"[Leaderboard] Updated player statistics for {stats.DisplayName}"),
                error => Debug.LogError($"[Leaderboard] Failed to update statistics: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// Calculate accuracy (kills / hits taken ratio)
        /// </summary>
        private float CalculateAccuracy(PlayerGameStats stats)
        {
            if (stats.HitsTaken == 0)
                return stats.Kills > 0 ? 100f : 0f;

            return (float)stats.Kills / stats.HitsTaken * 100f;
        }

        /// <summary>
        /// Calculate K/D ratio
        /// </summary>
        private float CalculateKDRatio(PlayerGameStats stats)
        {
            if (stats.Deaths == 0)
                return stats.Kills > 0 ? stats.Kills : 0f;

            return (float)stats.Kills / stats.Deaths;
        }

        /// <summary>
        /// Calculate survival score (how long they lasted / hits taken)
        /// Lower hits taken = higher score
        /// </summary>
        private float CalculateSurvivalScore(PlayerGameStats stats)
        {
            // Formula: (Kills * 10) - (Deaths * 5) - (HitsTaken * 2)
            // Incentivizes kills and survival, penalizes deaths
            return (stats.Kills * 10) - (stats.Deaths * 5) - (stats.HitsTaken * 2);
        }

        /// <summary>
        /// Get leaderboard data from PlayFab
        /// </summary>
        public void GetLeaderboard(string leaderboardId, int maxResults = 10)
        {
            var request = new GetLeaderboardRequest
            {
                StatisticName = leaderboardId,
                MaxResultsCount = maxResults
            };

            PlayFabClientAPI.GetLeaderboard(request,
                result =>
                {
                    Debug.Log($"[Leaderboard] Retrieved {leaderboardId}:");
                    foreach (var entry in result.Leaderboard)
                    {
                        Debug.Log($"  {entry.Position + 1}. {entry.DisplayName}: {entry.StatValue}");
                    }
                },
                error => Debug.LogError($"[Leaderboard] Failed to get leaderboard: {error.GenerateErrorReport()}")
            );
        }

        public void BackToLobby()
        {
            PhotonNetwork.LeaveRoom();
            PhotonNetwork.LoadLevel("Main");
        }





    }

    /// <summary>
    /// Data structure for player game stats
    /// </summary>
    public struct PlayerGameStats
    {
        public string PlayFabId;
        public string DisplayName;
        public int ActorNumber;
        
        // Raw stats from Photon
        public int Score;
        public int Kills;
        public int Deaths;
        public int HitsTaken;
        
        // Calculated stats
        public float Accuracy;
        public float KillDeathRatio;
        public float SurvivalScore;
    }
}