using System.Collections;
using System.Collections.Generic;
using Hanzo.Core;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Hanzo.Networking.PlayFab
{
    public class UsernameValidator : MonoBehaviour
    {
        [Header("PostFX References")]
        public Volume postProcessVolume;

        [Header("Username UI References")]
        public GameObject usernameCanvas;
        public TMPro.TMP_InputField usernameInput;
        public TMPro.TMP_Text statusText;
        public TextMeshProUGUI playerProfileNameText;

        [HideInInspector]public string currentUsername;

        [Header("Settings")]
        public int minUsernameLength = 3;
        public int maxUsernameLength = 20;

        private void Start()
        {
            // Ensure PlayFabManager exists
            if (PlayFabManager.Instance == null)
            {
                Debug.LogError(
                    "PlayFabManager instance not found! Make sure it persists from login scene."
                );
                return;
            }

            // Check if player has a username
            CheckPlayerUsername();
        }

        private void CheckPlayerUsername()
        {
            PlayFabManager.Instance.GetPlayerProfile(OnProfileSuccess, OnProfileFailure);
        }

        private void OnProfileSuccess(GetPlayerProfileResult result)
        {
            string displayName = result.PlayerProfile.DisplayName;

            if (string.IsNullOrEmpty(displayName))
            {
                // No username set - show username setup UI
                Debug.Log("No username found. Prompting user to create one.");
                EnableUsernameSetup(true);
            }
            else
            {
                // Username exists - disable setup UI and show username
                Debug.Log("Username found: " + displayName);
                PlayFabManager.Instance.PlayerDisplayName = displayName;

                if (playerProfileNameText != null)
                {
                    playerProfileNameText.text = displayName;
                    currentUsername = displayName;
                    StartCoroutine(
                        GameObject
                            .FindAnyObjectByType<ShopManager>()
                            .FetchPlayerData(displayName)
                    );
                    PlayerPrefs.SetString("USERNAME", displayName);
                }

                EnableUsernameSetup(false);
            }
        }

        private void OnProfileFailure(PlayFabError error)
        {
            Debug.LogError("Failed to get player profile: " + error.GenerateErrorReport());
            UpdateStatus("Error loading profile. Please try again.");
        }

        private void EnableUsernameSetup(bool enable)
        {
            if (usernameCanvas != null)
            {
                usernameCanvas.SetActive(enable);
            }

            if (postProcessVolume != null)
            {
                postProcessVolume.enabled = enable;
            }
        }

        public void SubmitUsername()
        {
            string username = usernameInput.text.Trim();

            // Validate username
            if (string.IsNullOrEmpty(username))
            {
                UpdateStatus("Username cannot be empty!");
                return;
            }

            if (username.Length < minUsernameLength)
            {
                UpdateStatus($"Username must be at least {minUsernameLength} characters!");
                return;
            }

            if (username.Length > maxUsernameLength)
            {
                UpdateStatus($"Username must be less than {maxUsernameLength} characters!");
                return;
            }

            // Check for invalid characters (alphanumeric and underscore only)
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            {
                UpdateStatus("Username can only contain letters, numbers, and underscores!");
                return;
            }

            UpdateStatus("Setting username...");
            SetPlayerUsername(username);
        }

        private void SetPlayerUsername(string username)
        {
            var request = new UpdateUserTitleDisplayNameRequest { DisplayName = username };

            PlayFabClientAPI.UpdateUserTitleDisplayName(
                request,
                OnUsernameSetSuccess,
                OnUsernameSetFailure
            );
        }

        private void OnUsernameSetSuccess(UpdateUserTitleDisplayNameResult result)
        {
            Debug.Log("Username set successfully: " + result.DisplayName);
            PlayFabManager.Instance.PlayerDisplayName = result.DisplayName;

            if (playerProfileNameText != null)
            {
                playerProfileNameText.text = result.DisplayName;
                currentUsername = result.DisplayName;
                
                PlayerPrefs.SetString("USERNAME", result.DisplayName);
                StartCoroutine(
                    GameObject
                        .FindAnyObjectByType<ShopManager>()
                        .FetchPlayerData(result.DisplayName)
                );
            }

            UpdateStatus("Username set successfully!");

            // Disable username setup UI
            StartCoroutine(DisableUsernameSetupDelayed());
        }

        private void OnUsernameSetFailure(PlayFabError error)
        {
            Debug.LogError("Failed to set username: " + error.GenerateErrorReport());

            // Check for specific errors
            if (error.Error == PlayFabErrorCode.NameNotAvailable)
            {
                UpdateStatus("Username already taken. Please choose another.");
            }
            else
            {
                UpdateStatus("Failed to set username: " + error.ErrorMessage);
            }
        }

        private IEnumerator DisableUsernameSetupDelayed()
        {
            yield return new WaitForSeconds(1.5f);
            EnableUsernameSetup(false);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log(message);
        }
    }
}
