using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

namespace Hanzo.Networking
{
    /// <summary>
    /// Global networked scoring system using Photon Custom Properties
    /// Accessible to all players in the game
    /// </summary>
    public class NetworkedScoreManager : MonoBehaviourPunCallbacks
    {
        public static NetworkedScoreManager Instance { get; private set; }

        [Header("Score Settings")]
        [SerializeField]
        private int scoreForDashHit = 10;

        [SerializeField]
        private int scoreLostFromExplosion = 5;

        [SerializeField]
        private int survivalBonusPerDeath = 5; // NEW: Bonus for each player death

        [SerializeField]
        private bool showDebugInfo = true;

        // Custom property keys
        private const string SCORE_KEY = "PlayerScore";
        private const string HITS_TAKEN_KEY = "HitsTaken";
        private const string KILLS_KEY = "Kills";
        private const string DEATHS_KEY = "Deaths";

        // Add to local cache
        private Dictionary<int, int> playerDeaths = new Dictionary<int, int>();
        private Dictionary<int, int> playerScores = new Dictionary<int, int>();
        private Dictionary<int, int> playerHitsTaken = new Dictionary<int, int>();

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Initialize local player's score if they just joined
            if (PhotonNetwork.IsConnected && PhotonNetwork.LocalPlayer != null)
            {
                InitializePlayerScore(PhotonNetwork.LocalPlayer);
            }
        }

        /// <summary>
        /// Initialize a player's score when they join
        /// </summary>
        private void InitializePlayerScore(PhotonPlayer player)
        {
            if (!player.CustomProperties.ContainsKey(SCORE_KEY))
            {
                Hashtable props = new Hashtable
                {
                    { SCORE_KEY, 0 },
                    { HITS_TAKEN_KEY, 0 },
                    { KILLS_KEY, 0 },
                    { DEATHS_KEY, 0 },
                };
                player.SetCustomProperties(props);

                Debug.Log(
                    $"[ScoreManager] Initialized score for player {player.NickName} (ID: {player.ActorNumber})"
                );
            }
        }

        /// <summary>
        /// Add score to a player (called when they successfully hit someone with dash)
        /// </summary>
        public void AddDashHitScore(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
            {
                Debug.LogWarning(
                    $"[ScoreManager] Player with ActorNumber {actorNumber} not found!"
                );
                return;
            }

            int currentScore = GetPlayerScore(actorNumber);
            int newScore = currentScore + scoreForDashHit;

            // Update score
            Hashtable props = new Hashtable { { SCORE_KEY, newScore } };
            player.SetCustomProperties(props);

            // Increment kills
            int currentKills = GetPlayerKills(actorNumber);
            Hashtable killProps = new Hashtable { { KILLS_KEY, currentKills + 1 } };
            player.SetCustomProperties(killProps);

            Debug.Log(
                $"[ScoreManager] 💥 {player.NickName} scored {scoreForDashHit} points! (Dash Hit) | Total: {newScore}"
            );
        }

        /// <summary>
        /// Remove score from a player (called when hit by explosion)
        /// </summary>
        public void RemoveExplosionScore(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
            {
                Debug.LogWarning(
                    $"[ScoreManager] Player with ActorNumber {actorNumber} not found!"
                );
                return;
            }

            int currentScore = GetPlayerScore(actorNumber);
            int newScore = Mathf.Max(0, currentScore - scoreLostFromExplosion); // Don't go below 0

            Hashtable props = new Hashtable { { SCORE_KEY, newScore } };
            player.SetCustomProperties(props);

            Debug.Log(
                $"[ScoreManager] 💣 {player.NickName} lost {scoreLostFromExplosion} points! (Explosion Hit) | Total: {newScore}"
            );
        }

        /// <summary>
        /// NEW: Award survival bonus to all alive players when someone dies
        /// </summary>
        public void AwardSurvivalBonus(int deadPlayerActorNumber)
        {
            if (PhotonNetwork.CurrentRoom == null)
                return;

            int alivePlayerCount = 0;
            List<PhotonPlayer> alivePlayers = new List<PhotonPlayer>();

            // Find all alive players (excluding the one who just died)
            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (player.ActorNumber != deadPlayerActorNumber)
                {
                    int playerHits = GetPlayerHitsTaken(player.ActorNumber);
                    
                    // If they haven't taken 8 hits, they're still alive
                    if (playerHits < 8)
                    {
                        alivePlayerCount++;
                        alivePlayers.Add(player);
                    }
                }
            }

            // Award bonus to each alive player
            foreach (var alivePlayer in alivePlayers)
            {
                int currentScore = GetPlayerScore(alivePlayer.ActorNumber);
                int newScore = currentScore + survivalBonusPerDeath;

                Hashtable props = new Hashtable { { SCORE_KEY, newScore } };
                alivePlayer.SetCustomProperties(props);

                Debug.Log(
                    $"[ScoreManager] 🎖️ {alivePlayer.NickName} received survival bonus +{survivalBonusPerDeath}! (Total: {newScore})"
                );
            }

            if (alivePlayerCount > 0)
            {
                Debug.Log(
                    $"[ScoreManager] 💀 Player eliminated! {alivePlayerCount} survivors received +{survivalBonusPerDeath} bonus points"
                );
            }
        }

        public void IncrementPlayerDeaths(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
            {
                Debug.LogWarning(
                    $"[ScoreManager] Player with ActorNumber {actorNumber} not found!"
                );
                return;
            }

            int currentDeaths = GetPlayerDeaths(actorNumber);
            int newDeaths = currentDeaths + 1;

            Hashtable props = new Hashtable { { DEATHS_KEY, newDeaths } };
            player.SetCustomProperties(props);

            Debug.Log($"[ScoreManager] 💀 {player.NickName} has died {newDeaths} times");
            
            // NEW: Award survival bonus to other players
            if (PhotonNetwork.IsMasterClient)
            {
                AwardSurvivalBonus(actorNumber);
            }
        }

        /// <summary>
        /// Get a player's death count
        /// </summary>
        public int GetPlayerDeaths(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(DEATHS_KEY, out object deathsObj))
            {
                return (int)deathsObj;
            }

            return 0;
        }

        /// <summary>
        /// Increment the hit counter for a player
        /// Returns the new hit count
        /// </summary>
        public int IncrementPlayerHits(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
            {
                Debug.LogWarning(
                    $"[ScoreManager] Player with ActorNumber {actorNumber} not found!"
                );
                return 0;
            }

            int currentHits = GetPlayerHitsTaken(actorNumber);
            int newHits = currentHits + 1;

            Hashtable props = new Hashtable { { HITS_TAKEN_KEY, newHits } };
            player.SetCustomProperties(props);

            Debug.Log($"[ScoreManager] {player.NickName} has taken {newHits}/8 hits");

            return newHits;
        }

        /// <summary>
        /// Reset a player's hit counter (e.g., after respawn)
        /// </summary>
        public void ResetPlayerHits(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return;

            Hashtable props = new Hashtable { { HITS_TAKEN_KEY, 0 } };
            player.SetCustomProperties(props);

            Debug.Log($"[ScoreManager] Reset hit counter for {player.NickName}");
        }

        /// <summary>
        /// Get a player's current score
        /// </summary>
        public int GetPlayerScore(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(SCORE_KEY, out object scoreObj))
            {
                return (int)scoreObj;
            }

            return 0;
        }

        /// <summary>
        /// Get a player's current hit count
        /// </summary>
        public int GetPlayerHitsTaken(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(HITS_TAKEN_KEY, out object hitsObj))
            {
                return (int)hitsObj;
            }

            return 0;
        }

        /// <summary>
        /// Get a player's kill count
        /// </summary>
        public int GetPlayerKills(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(KILLS_KEY, out object killsObj))
            {
                return (int)killsObj;
            }

            return 0;
        }

        /// <summary>
        /// Get all player scores for leaderboard display
        /// </summary>
        public Dictionary<string, int> GetAllPlayerScores()
        {
            Dictionary<string, int> scores = new Dictionary<string, int>();

            if (PhotonNetwork.CurrentRoom == null)
                return scores;

            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                scores[player.NickName] = GetPlayerScore(player.ActorNumber);
            }

            return scores;
        }

        /// <summary>
        /// Called when player properties are updated
        /// </summary>
        public override void OnPlayerPropertiesUpdate(
            PhotonPlayer targetPlayer,
            Hashtable changedProps
        )
        {
            if (changedProps.ContainsKey(SCORE_KEY))
            {
                int newScore = (int)changedProps[SCORE_KEY];
                playerScores[targetPlayer.ActorNumber] = newScore;

                if (showDebugInfo)
                {
                    Debug.Log(
                        $"[ScoreManager] 🎯 {targetPlayer.NickName}'s score updated: {newScore}"
                    );
                }
            }

            if (changedProps.ContainsKey(HITS_TAKEN_KEY))
            {
                int newHits = (int)changedProps[HITS_TAKEN_KEY];
                playerHitsTaken[targetPlayer.ActorNumber] = newHits;
            }

            if (changedProps.ContainsKey(DEATHS_KEY))
            {
                int newDeaths = (int)changedProps[DEATHS_KEY];
                playerDeaths[targetPlayer.ActorNumber] = newDeaths;

                if (showDebugInfo)
                {
                    Debug.Log(
                        $"[ScoreManager] 💀 {targetPlayer.NickName}'s deaths updated: {newDeaths}"
                    );
                }
            }
        }

        /// <summary>
        /// Called when a new player joins the room
        /// </summary>
        public override void OnPlayerEnteredRoom(PhotonPlayer newPlayer)
        {
            InitializePlayerScore(newPlayer);
        }

        /// <summary>
        /// Print all scores to console (for debugging)
        /// </summary>
        public void PrintAllScores()
        {
            if (PhotonNetwork.CurrentRoom == null) return;
            
            Debug.Log("========== CURRENT SCORES ==========");
            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                int score = GetPlayerScore(player.ActorNumber);
                int hits = GetPlayerHitsTaken(player.ActorNumber);
                int kills = GetPlayerKills(player.ActorNumber);
                int deaths = GetPlayerDeaths(player.ActorNumber);
                
                Debug.Log($"{player.NickName}: Score={score} | Hits={hits}/8 | Kills={kills} | Deaths={deaths}");
            }
            Debug.Log("====================================");
        }

        private void OnGUI()
        {
            if (!showDebugInfo) return;
            
            GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 300, 500));
            GUILayout.Box("=== GLOBAL SCOREBOARD ===");
            
            if (PhotonNetwork.CurrentRoom != null)
            {
                foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
                {
                    int score = GetPlayerScore(player.ActorNumber);
                    int hits = GetPlayerHitsTaken(player.ActorNumber);
                    int kills = GetPlayerKills(player.ActorNumber);
                    int deaths = GetPlayerDeaths(player.ActorNumber);
                    
                    GUILayout.Label($"{player.NickName}:");
                    GUILayout.Label($"  Score: {score}");
                    GUILayout.Label($"  Hits: {hits}/8");
                    GUILayout.Label($"  Kills: {kills}");
                    GUILayout.Label($"  Deaths: {deaths}");
                    GUILayout.Space(5);
                }
            }
            else
            {
                GUILayout.Label("Not connected to room");
            }
            
            GUILayout.EndArea();
        }
    }
}