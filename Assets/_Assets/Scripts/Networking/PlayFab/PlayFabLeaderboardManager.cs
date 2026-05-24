using System.Collections.Generic;
using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hanzo.Networking
{
    /// <summary>
    /// Handles end-of-game stat submission to PlayFab leaderboards.
    /// </summary>
    public class PlayFabLeaderboardManager : MonoBehaviourPunCallbacks
    {
        public static PlayFabLeaderboardManager Instance { get; private set; }

        [Header("Leaderboard Settings")]
        [SerializeField]
        private string killsLeaderboardId = "Kills";

        [SerializeField]
        private string survivalTimeLeaderboardId = "SurvivalTime";

        [SerializeField]
        private string totalDeathsLeaderboardId = "TotalDealths";

        [SerializeField]
        private string totalGamesPlayedLeaderboardId = "TotalGamesPlayed";

        [SerializeField]
        private string totalHitsTakenLeaderboardId = "TotalHitsTaken";

        [SerializeField]
        private string totalKillsLeaderboardId = "TotalKills";

        [SerializeField]
        private string winsLeaderboardId = "Wins";

        [SerializeField]
        private string averageKDRatioLeaderboardId = "AverageKDRatio";

        [SerializeField]
        private string deathsLeaderboardId = "Deaths";

        [SerializeField]
        private bool debugSubmissions = true;

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

        public override void OnEnable()
        {
            base.OnEnable();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public override void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            base.OnDisable();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Main")
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Submits the local real player's final match stats to PlayFab.
        /// </summary>
        public void SubmitGameResults(float matchSurvivalTimeSeconds = 0f)
        {
            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[Leaderboard] Not in a room!");
                return;
            }

            Dictionary<int, PlayerGameStats> playerStats = CalculatePlayerStats(
                matchSurvivalTimeSeconds
            );

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

        private Dictionary<int, PlayerGameStats> CalculatePlayerStats(float matchSurvivalTimeSeconds)
        {
            Dictionary<int, PlayerGameStats> stats = new Dictionary<int, PlayerGameStats>();

            if (PhotonNetwork.CurrentRoom == null || NetworkedScoreManager.Instance == null)
                return stats;

            GetWinningResult(out int winningKillCount, out int winningDeathCount);
            int survivalTime = Mathf.Max(0, Mathf.RoundToInt(matchSurvivalTimeSeconds));

            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                PlayerGameStats playerStats = new PlayerGameStats
                {
                    PlayFabId = player.UserId,
                    DisplayName = player.NickName,
                    ActorNumber = player.ActorNumber,
                    Score = NetworkedScoreManager.Instance.GetPlayerScore(player.ActorNumber),
                    Kills = NetworkedScoreManager.Instance.GetPlayerKills(player.ActorNumber),
                    Deaths = NetworkedScoreManager.Instance.GetPlayerDeaths(player.ActorNumber),
                    HitsTaken = NetworkedScoreManager.Instance.GetPlayerHitsTaken(
                        player.ActorNumber
                    ),
                    SurvivalTime = survivalTime,
                };

                playerStats.KillDeathRatio = CalculateKDRatio(playerStats);
                playerStats.Wins =
                    winningKillCount > 0
                    && playerStats.Kills == winningKillCount
                    && playerStats.Deaths == winningDeathCount
                        ? 1
                        : 0;

                stats[player.ActorNumber] = playerStats;

                if (debugSubmissions)
                {
                    Debug.Log(
                        $"[Leaderboard] Calculated stats for {playerStats.DisplayName}: "
                            + $"Kills={playerStats.Kills}, Deaths={playerStats.Deaths}, "
                            + $"HitsTaken={playerStats.HitsTaken}, Wins={playerStats.Wins}"
                    );
                }
            }

            return stats;
        }

        private void SubmitPlayerStats(PlayerGameStats stats)
        {
            Debug.Log(
                $"[Leaderboard] Submitting stats for {stats.DisplayName}: "
                    + $"Kills={stats.Kills}, Deaths={stats.Deaths}, Wins={stats.Wins}"
            );

            var request = new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate { StatisticName = killsLeaderboardId, Value = stats.Kills },
                    new StatisticUpdate
                    {
                        StatisticName = survivalTimeLeaderboardId,
                        Value = stats.SurvivalTime,
                    },
                    new StatisticUpdate
                    {
                        StatisticName = totalDeathsLeaderboardId,
                        Value = stats.Deaths,
                    },
                    new StatisticUpdate
                    {
                        StatisticName = totalGamesPlayedLeaderboardId,
                        Value = 1,
                    },
                    new StatisticUpdate
                    {
                        StatisticName = totalHitsTakenLeaderboardId,
                        Value = stats.HitsTaken,
                    },
                    new StatisticUpdate
                    {
                        StatisticName = totalKillsLeaderboardId,
                        Value = stats.Kills,
                    },
                    new StatisticUpdate { StatisticName = winsLeaderboardId, Value = stats.Wins },
                    new StatisticUpdate
                    {
                        StatisticName = averageKDRatioLeaderboardId,
                        Value = Mathf.RoundToInt(stats.KillDeathRatio * 100f),
                    },
                    new StatisticUpdate { StatisticName = deathsLeaderboardId, Value = stats.Deaths },
                },
            };

            PlayFabClientAPI.UpdatePlayerStatistics(
                request,
                result =>
                {
                    if (debugSubmissions)
                        Debug.Log($"[Leaderboard] Submitted match stats for {stats.DisplayName}");
                },
                error =>
                    Debug.LogError(
                        $"[Leaderboard] Failed to submit match stats: {error.GenerateErrorReport()}"
                    )
            );
        }

        private void GetWinningResult(out int winningKillCount, out int winningDeathCount)
        {
            winningKillCount = 0;
            winningDeathCount = int.MaxValue;

            if (NetworkedScoreManager.Instance == null)
            {
                winningDeathCount = 0;
                return;
            }

            if (PhotonNetwork.CurrentRoom != null)
            {
                foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
                {
                    int kills = NetworkedScoreManager.Instance.GetPlayerKills(player.ActorNumber);
                    int deaths = NetworkedScoreManager.Instance.GetPlayerDeaths(player.ActorNumber);
                    UpdateWinningResult(kills, deaths, ref winningKillCount, ref winningDeathCount);
                }
            }

            foreach (var aiPlayer in NetworkedScoreManager.Instance.GetAIPlayerNames())
            {
                int kills = NetworkedScoreManager.Instance.GetAIKills(aiPlayer.Key);
                int deaths = NetworkedScoreManager.Instance.GetAIDeaths(aiPlayer.Key);
                UpdateWinningResult(kills, deaths, ref winningKillCount, ref winningDeathCount);
            }

            if (winningDeathCount == int.MaxValue)
                winningDeathCount = 0;
        }

        private void UpdateWinningResult(
            int kills,
            int deaths,
            ref int winningKillCount,
            ref int winningDeathCount
        )
        {
            if (kills > winningKillCount || (kills == winningKillCount && deaths < winningDeathCount))
            {
                winningKillCount = kills;
                winningDeathCount = deaths;
            }
        }

        private float CalculateKDRatio(PlayerGameStats stats)
        {
            if (stats.Deaths == 0)
                return stats.Kills > 0 ? stats.Kills : 0f;

            return (float)stats.Kills / stats.Deaths;
        }

        public void GetLeaderboard(string leaderboardId, int maxResults = 10)
        {
            var request = new GetLeaderboardRequest
            {
                StatisticName = leaderboardId,
                MaxResultsCount = maxResults,
            };

            PlayFabClientAPI.GetLeaderboard(
                request,
                result =>
                {
                    Debug.Log($"[Leaderboard] Retrieved {leaderboardId}:");
                    foreach (var entry in result.Leaderboard)
                    {
                        Debug.Log(
                            $"  {entry.Position + 1}. {entry.DisplayName}: {entry.StatValue}"
                        );
                    }
                },
                error =>
                    Debug.LogError(
                        $"[Leaderboard] Failed to get leaderboard: {error.GenerateErrorReport()}"
                    )
            );
        }
    }

    public struct PlayerGameStats
    {
        public string PlayFabId;
        public string DisplayName;
        public int ActorNumber;

        public int Score;
        public int Kills;
        public int Deaths;
        public int HitsTaken;
        public int SurvivalTime;
        public int Wins;

        public float KillDeathRatio;
    }
}
