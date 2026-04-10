using System.Collections;
using Hanzo.Networking.Utils;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

namespace Hanzo.Networking
{
    /// <summary>
    /// Waits 20 seconds after the game scene loads. If the room still isn't
    /// full, fills the remaining slots with AIs and signals AISpawnerManager.
    /// Only runs on the Master Client — all other clients ignore it.
    /// </summary>
    public class AIFillTimer : MonoBehaviourPunCallbacks
    {
        [Header("Timing")]
        [SerializeField]
        private float waitForPlayersTimeout = 20f;

        [Header("UI (optional)")]
        [SerializeField]
        private TextMeshProUGUI countdownText;

        [SerializeField]
        private GameObject countdownPanel;

        [Header("References")]
        [SerializeField]
        private AISpawnerManager aiSpawnerManager;

        [SerializeField]
        private PhotonCountdownTimer countdownTimer; // NEW

        private Coroutine countdownCoroutine;
        private bool aisFilled = false;

        private void Start()
        {
            int shouldSpawn = PlayerPrefs.GetInt("SpawnAIPlayers", 0);
            int numberOfAIs = PlayerPrefs.GetInt("NumberOfAIs", 0);
            int realPlayers = PlayerPrefs.GetInt("RealPlayerCount", 0);
            int requiredCount = PlayerPrefs.GetInt("RequiredPlayers", 0);

            Debug.Log(
                $"[AIFillTimer] SpawnAIPlayers={shouldSpawn}, NumberOfAIs={numberOfAIs}, "
                    + $"RealPlayers={realPlayers}, Required={requiredCount}, "
                    + $"IsMaster={PhotonNetwork.IsMasterClient}"
            );

            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)
            {
                HideCountdownUI();
                return;
            }

            // Use NumberOfAIs as the sole gate — SpawnAIPlayers flag is unreliable
            // because AISpawnerManager.Start may clear it before we read it
            if (numberOfAIs > 0)
            {
                Debug.Log($"[AIFillTimer] Spawning exactly {numberOfAIs} AIs.");
                aisFilled = true;
                StartCoroutine(SpawnImmediately(numberOfAIs));
            }
            else
            {
                Debug.Log($"[AIFillTimer] NumberOfAIs=0, no spawning needed. Starting countdown.");
                HideCountdownUI();
                if (countdownTimer != null)
                    countdownTimer.ManualStartCountdown();
            }
        }

        private IEnumerator SpawnImmediately(int numberOfAIs)
        {
            // Let the scene finish setting up
            yield return new WaitForSeconds(1.5f);

            Debug.Log($"[AIFillTimer] Executing spawn of {numberOfAIs} AIs.");

            if (aiSpawnerManager != null)
            {
                aiSpawnerManager.ResetSpawnFlag();
                aiSpawnerManager.ManualSpawnAIs();
            }
            else
            {
                Debug.LogWarning(
                    "[AIFillTimer] aiSpawnerManager is null — assign it in Inspector."
                );
            }

            // Clear the flag so a scene reload doesn't spawn again
            PlayerPrefs.SetInt("SpawnAIPlayers", 0);
            PlayerPrefs.Save();

            yield return StartCoroutine(NotifyAfterSpawn(numberOfAIs));
        }

        // Remove WaitThenFillWithAI and FillRemainingWithAI entirely —
        // MatchmakingManager owns that decision now.
        // Keep NotifyAfterSpawn and the rest unchanged.

        public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
        {
            // Someone joined — check if we're now full
            if (IsRoomFull() && countdownCoroutine != null)
            {
                StopCoroutine(countdownCoroutine);
                HideCountdownUI();
                Debug.Log("[AIFillTimer] Room filled by real players — AI fill cancelled.");
            }
        }

        private IEnumerator WaitThenFillWithAI()
        {
            if (countdownPanel != null)
                countdownPanel.SetActive(true);

            float remaining = waitForPlayersTimeout;

            while (remaining > 0f)
            {
                if (countdownText != null)
                    countdownText.text = $"Waiting for players: {Mathf.CeilToInt(remaining)}s";

                yield return new WaitForSeconds(1f);
                remaining -= 1f;

                // Re-check every second — someone may have joined
                if (IsRoomFull())
                {
                    HideCountdownUI();
                    Debug.Log("[AIFillTimer] Room filled during countdown — AI fill cancelled.");
                    yield break;
                }
            }

            HideCountdownUI();
            FillRemainingWithAI();
            aiSpawnerManager.ResetSpawnFlag();
            aiSpawnerManager.ManualSpawnAIs();
        }

        private void FillRemainingWithAI()
        {
            if (aisFilled)
                return;
            aisFilled = true;

            int currentPlayers = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
            int maxPlayers = PhotonNetwork.InRoom
                ? (int)PhotonNetwork.CurrentRoom.MaxPlayers
                : PlayerPrefs.GetInt("RoomMaxPlayers", 2);

            int numberOfAIs = Mathf.Max(0, maxPlayers - currentPlayers);

            if (numberOfAIs <= 0)
            {
                Debug.Log("[AIFillTimer] No AI slots needed.");

                // Room is full — start countdown directly
                if (countdownTimer != null)
                    countdownTimer.ManualStartCountdown();
                return;
            }

            Debug.Log(
                $"[AIFillTimer] Filling {numberOfAIs} slot(s) with AI "
                    + $"({currentPlayers}/{maxPlayers} real players)."
            );

            PlayerPrefs.SetInt("SpawnAIPlayers", 1);
            PlayerPrefs.SetInt("NumberOfAIs", numberOfAIs);
            PlayerPrefs.Save();

            if (aiSpawnerManager != null)
            {
                aiSpawnerManager.ResetSpawnFlag();
                aiSpawnerManager.ManualSpawnAIs();
            }

            StartCoroutine(NotifyAfterSpawn(numberOfAIs));
        }

        private IEnumerator NotifyAfterSpawn(int numberOfAIs)
        {
            // Wait for all AI spawns to complete
            float spawnStagger = 0.3f;
            yield return new WaitForSeconds(numberOfAIs * spawnStagger + 1.0f);

            Debug.Log(
                $"[AIFillTimer] Spawn wait complete. "
                    + $"IsMaster={PhotonNetwork.IsMasterClient}, "
                    + $"InRoom={PhotonNetwork.InRoom}"
            );

            // Register AI scores
            if (NetworkedScoreManager.Instance != null)
                NetworkedScoreManager.Instance.RegisterAIPlayers(numberOfAIs);

            // Only Master Client drives the countdown start
            if (!PhotonNetwork.IsMasterClient)
            {
                Debug.Log("[AIFillTimer] Not master — skipping ManualStartCountdown.");
                yield break;
            }

            if (countdownTimer != null)
            {
                Debug.Log("[AIFillTimer] Calling ManualStartCountdown...");
                countdownTimer.ManualStartCountdown();
            }
            else
            {
                Debug.LogWarning(
                    "[AIFillTimer] countdownTimer reference is null! "
                        + "Assign it in the Inspector."
                );
            }
        }

        private bool IsRoomFull()
        {
            if (!PhotonNetwork.InRoom)
                return false;
            Room room = PhotonNetwork.CurrentRoom;
            return room.PlayerCount >= room.MaxPlayers;
        }

        private void HideCountdownUI()
        {
            if (countdownPanel != null)
                countdownPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (countdownCoroutine != null)
                StopCoroutine(countdownCoroutine);
        }
    }
}
