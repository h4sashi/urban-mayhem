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
        private int survivalBonusPerDeath = 5; // NEW: Bonus for each player death

        [SerializeField]
        private bool showDebugInfo = true;

        // Custom property keys
        private const string SCORE_KEY = "PlayerScore";
        private const string HITS_TAKEN_KEY = "HitsTaken";
        private const string KILLS_KEY = "Kills";

        private const string DEATHS_KEY = "Deaths";
        private const string AI_SCORE_KEY_PREFIX = "AIScore_";
        private const string AI_KILLS_KEY_PREFIX = "AIKills_";
        private const string AI_DEATHS_KEY_PREFIX = "AIDeaths_";

        // In NetworkedScoreManager
        public Dictionary<int, string> GetAIPlayerNames() => new Dictionary<int, string>(aiNames);

        // Add to local cache
        private Dictionary<int, int> playerDeaths = new Dictionary<int, int>();
        private Dictionary<int, int> playerScores = new Dictionary<int, int>();
        private Dictionary<int, int> playerHitsTaken = new Dictionary<int, int>();

        // AI score tracking — keyed by negative IDs (-1, -2, -3...)
        private Dictionary<int, string> aiNames = new Dictionary<int, string>();
        private Dictionary<int, int> aiScores = new Dictionary<int, int>();
        private Dictionary<int, int> aiKills = new Dictionary<int, int>();
        private Dictionary<int, int> aiDeaths = new Dictionary<int, int>();
        private Dictionary<int, int> aiHitsTaken = new Dictionary<int, int>();

        // Add this alongside the existing playerScores/playerDeaths dictionaries
        private Dictionary<int, int> playerKills = new Dictionary<int, int>();

        /// <summary>
        /// Called by AIFillTimer after AIs spawn. Registers them so they
        /// appear on the scoreboard alongside real players.
        /// </summary>
        public void RegisterAIPlayers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int aiId = -(i + 1);
                EnsureAIRegistered(aiId);
                SyncAIStatsFromRoom(aiId);
                PublishInitialAIStats(aiId);
            }
        }

        // Returns real and AI hit scores for match result displays.
        public Dictionary<string, int> GetAllPlayerScores()
        {
            Dictionary<string, int> scores = new Dictionary<string, int>();

            if (PhotonNetwork.CurrentRoom != null)
                foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
                    scores[player.NickName] = GetPlayerScore(player.ActorNumber);

            foreach (var aiPlayer in aiNames)
                scores[aiPlayer.Value + " [AI]"] = GetAIScore(aiPlayer.Key);

            return scores;
        }

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

        public int GetPlayerKills(int actorNumber)
        {
            // Check local cache first (immediately consistent)
            if (playerKills.TryGetValue(actorNumber, out int cached))
                return cached;

            // Fallback to Photon custom properties
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(KILLS_KEY, out object killsObj))
                return (int)killsObj;

            return 0;
        }

        /// <summary>
        /// Add kill credit to a player.
        /// </summary>
        public void AddPlayerKill(int actorNumber)
        {
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
            {
                Debug.LogWarning(
                    $"[ScoreManager] AddPlayerKill: Player {actorNumber} not found!"
                );
                return;
            }

            int currentScore = GetPlayerScore(actorNumber);
            int newScore = currentScore + scoreForDashHit;

            int currentKills = GetPlayerKills(actorNumber);
            int newKills = currentKills + 1;

            // Write to local cache IMMEDIATELY — don't wait for Photon async
            playerScores[actorNumber] = newScore;
            playerKills[actorNumber] = newKills;

            // Sync to Photon custom properties (async — arrives late, but cache is already correct)
            Hashtable props = new Hashtable { { SCORE_KEY, newScore }, { KILLS_KEY, newKills } };
            player.SetCustomProperties(props);

            Debug.Log(
                $"[ScoreManager] {player.NickName} gained kill #{newKills} | Score: {newScore}"
            );
        }

        /// <summary>
        /// Awards kill credit to an AI attacker by its negative ID.
        /// Called from PlayerHealthComponent when the damage source is an AI.
        /// </summary>
        public void AddAIKill(int aiId)
        {
            EnsureAIRegistered(aiId);

            if (!aiNames.ContainsKey(aiId))
                return;

            int newScore = GetAIScore(aiId) + scoreForDashHit;
            int newKills = GetAIKills(aiId) + 1;

            aiScores[aiId] = newScore;
            aiKills[aiId] = newKills;

            if (PhotonNetwork.CurrentRoom != null)
            {
                Hashtable props = new Hashtable
                {
                    { GetAIScoreKey(aiId), newScore },
                    { GetAIKillsKey(aiId), newKills },
                };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }

            Debug.Log(
                $"[ScoreManager] {aiNames[aiId]} gained kill #{newKills} | Score: {newScore}"
            );
        }

        private static string GetAIScoreKey(int aiId)
        {
            return AI_SCORE_KEY_PREFIX + aiId;
        }

        private static string GetAIKillsKey(int aiId)
        {
            return AI_KILLS_KEY_PREFIX + aiId;
        }

        private static string GetAIDeathsKey(int aiId)
        {
            return AI_DEATHS_KEY_PREFIX + aiId;
        }

        private void SyncAIStatsFromRoom(int aiId)
        {
            EnsureAIRegistered(aiId);

            if (PhotonNetwork.CurrentRoom == null)
                return;

            if (
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    GetAIScoreKey(aiId),
                    out object scoreObj
                )
                && scoreObj is int score
            )
            {
                aiScores[aiId] = score;
            }

            if (
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    GetAIKillsKey(aiId),
                    out object killsObj
                )
                && killsObj is int kills
            )
            {
                aiKills[aiId] = kills;
            }

            if (
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    GetAIDeathsKey(aiId),
                    out object deathsObj
                )
                && deathsObj is int deaths
            )
            {
                aiDeaths[aiId] = deaths;
            }
        }

        private void PublishInitialAIStats(int aiId)
        {
            if (!PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
                return;

            Hashtable props = new Hashtable();

            if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(GetAIScoreKey(aiId)))
                props[GetAIScoreKey(aiId)] = GetAIScore(aiId);

            if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(GetAIKillsKey(aiId)))
                props[GetAIKillsKey(aiId)] = GetAIKills(aiId);

            if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(GetAIDeathsKey(aiId)))
                props[GetAIDeathsKey(aiId)] = GetAIDeaths(aiId);

            if (props.Count > 0)
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }

        private void EnsureAIRegistered(int aiId)
        {
            if (aiId == 0)
                return;

            int aiIndex = Mathf.Abs(aiId);

            if (!aiNames.ContainsKey(aiId))
                aiNames[aiId] = $"AI_Player_{aiIndex}";

            if (!aiScores.ContainsKey(aiId))
                aiScores[aiId] = 0;

            if (!aiKills.ContainsKey(aiId))
                aiKills[aiId] = 0;

            if (!aiDeaths.ContainsKey(aiId))
                aiDeaths[aiId] = 0;

            if (!aiHitsTaken.ContainsKey(aiId))
                aiHitsTaken[aiId] = 0;
        }

        /// <summary>
        /// Returns the negative AI ID for a given GameObject, or 0 if not an AI.
        /// </summary>
        public int GetAIId(GameObject obj)
        {
            Transform current = obj.transform;
            while (current != null)
            {
                string objName = current.gameObject.name;
                foreach (var kvp in aiNames)
                {
                    if (objName.Contains(kvp.Value))
                        return kvp.Key;
                }
                current = current.parent;
            }
            return 0;
        }

        public int GetAIScore(int aiId)
        {
            if (aiScores.TryGetValue(aiId, out int score))
                return score;

            if (
                PhotonNetwork.CurrentRoom != null
                && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    GetAIScoreKey(aiId),
                    out object scoreObj
                )
                && scoreObj is int roomScore
            )
            {
                return roomScore;
            }

            return 0;
        }

        public int GetAIHitsTaken(int aiId) => aiHitsTaken.TryGetValue(aiId, out int hits) ? hits : 0;

        public int GetAIKills(int aiId)
        {
            if (aiKills.TryGetValue(aiId, out int kills))
                return kills;

            if (
                PhotonNetwork.CurrentRoom != null
                && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    GetAIKillsKey(aiId),
                    out object killsObj
                )
                && killsObj is int roomKills
            )
            {
                return roomKills;
            }

            return 0;
        }

        public int GetAIDeaths(int aiId) => aiDeaths.TryGetValue(aiId, out int d) ? d : 0;

        /// <summary>
        /// Increments death count for an AI when it gets eliminated.
        /// </summary>
        public void IncrementAIDeaths(int aiId)
        {
            EnsureAIRegistered(aiId);

            if (!aiNames.ContainsKey(aiId))
                return;
            aiDeaths[aiId] += 1;

            if (PhotonNetwork.CurrentRoom != null)
            {
                Hashtable props = new Hashtable { { GetAIDeathsKey(aiId), aiDeaths[aiId] } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            }

            Debug.Log($"[ScoreManager] AI {aiNames[aiId]} death count: {aiDeaths[aiId]}");
        }

        /// <summary>
        /// Increments hit count for an AI when it takes damage.
        /// </summary>
        public void IncrementAIHits(int aiId)
        {
            if (!aiNames.ContainsKey(aiId))
                return;
            aiHitsTaken[aiId] += 1;
            Debug.Log($"[ScoreManager] AI {aiNames[aiId]} hits taken: {aiHitsTaken[aiId]}");
        }

        /// <summary>
        /// NEW: Award survival bonus to all alive players when someone dies
        /// </summary>
        public void AwardSurvivalBonus(int deadPlayerActorNumber)
        {
            // Survival bonus disabled: not used in current scoring rules.
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

            // Survival bonus disabled: no extra points awarded on death.
            // if (PhotonNetwork.IsMasterClient)
            // {
            //     AwardSurvivalBonus(actorNumber);
            // }
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
            // Check local cache first (immediately consistent)
            if (playerScores.TryGetValue(actorNumber, out int cachedScore))
                return cachedScore;

            // Fallback to Photon custom properties
            PhotonPlayer player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
            if (player == null)
                return 0;

            if (player.CustomProperties.TryGetValue(SCORE_KEY, out object scoreObj))
                return (int)scoreObj;

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

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            foreach (System.Collections.DictionaryEntry entry in propertiesThatChanged)
            {
                if (!(entry.Key is string key) || !(entry.Value is int value))
                    continue;

                if (key.StartsWith(AI_SCORE_KEY_PREFIX))
                {
                    if (int.TryParse(key.Substring(AI_SCORE_KEY_PREFIX.Length), out int aiId))
                    {
                        EnsureAIRegistered(aiId);
                        aiScores[aiId] = value;
                    }
                }
                else if (key.StartsWith(AI_KILLS_KEY_PREFIX))
                {
                    if (int.TryParse(key.Substring(AI_KILLS_KEY_PREFIX.Length), out int aiId))
                    {
                        EnsureAIRegistered(aiId);
                        aiKills[aiId] = value;
                    }
                }
                else if (key.StartsWith(AI_DEATHS_KEY_PREFIX))
                {
                    if (int.TryParse(key.Substring(AI_DEATHS_KEY_PREFIX.Length), out int aiId))
                    {
                        EnsureAIRegistered(aiId);
                        aiDeaths[aiId] = value;
                    }
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
            if (PhotonNetwork.CurrentRoom == null)
                return;

            Debug.Log("========== CURRENT SCORES ==========");
            foreach (var player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                int score = GetPlayerScore(player.ActorNumber);
                int hits = GetPlayerHitsTaken(player.ActorNumber);
                int kills = GetPlayerKills(player.ActorNumber);
                int deaths = GetPlayerDeaths(player.ActorNumber);

                Debug.Log(
                    $"{player.NickName}: Score={score} | Hits={hits}/8 | Kills={kills} | Deaths={deaths}"
                );
            }
            Debug.Log("====================================");
        }

        private void OnGUI()
        {
            if (!showDebugInfo)
                return;

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
