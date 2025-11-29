using System.Collections;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hanzo.Networking.PlayFab
{
    public class PlayFabManager : MonoBehaviour
    {
        public static PlayFabManager Instance { get; private set; }

        public GameObject loginPanel;
        public GameObject signUp;

        [Header("Login Fields")]
        public TMPro.TMP_InputField loginEmailInput;
        public TMPro.TMP_InputField loginPasswordInput;

        [Header("Sign Up Fields")]
        public TMPro.TMP_InputField signUpEmailInput;
        public TMPro.TMP_InputField signUpPasswordInput;

        [Header("Scene Management")]
        public string nextSceneName = "MainGame";

        [Header("UI Feedback (Optional)")]
        public TMPro.TMP_Text statusText;

        // Public properties to store player's display name and ID
        public string PlayerDisplayName { get; set; }
        public string PlayFabId { get; set; }
        public string PlayerEmail { get; set; }

        private void Awake()
        {
            // Singleton pattern with DontDestroyOnLoad
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        void OnEnable()
        {
            // On Scene Load, Check if we are in the Main Scene
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            // PlayerPrefs.DeleteAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Main")
            {
               //Use cloudscripting to get player profile infoif he is new player or a player that
               //has bought items outside of playfab then get the profile info from outside source
            }
        }

        private void Start()
        {
            // Set PlayFab Title ID if not set in PlayFabSettings
            if (string.IsNullOrEmpty(PlayFabSettings.TitleId))
            {
                PlayFabSettings.TitleId = "154756";
            }
        }

        public void SignUpOption()
        {
            loginPanel.SetActive(false);
            signUp.SetActive(true);
            UpdateStatus("");
        }

        public void CancelLogin()
        {
            loginPanel.SetActive(true);
            signUp.SetActive(false);
            UpdateStatus("");
        }

        public void Login()
        {
            Debug.Log("Attempting Login...");
            UpdateStatus("Logging in...");

            string email = loginEmailInput.text.ToLower().Trim();
            string password = loginPasswordInput.text;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                UpdateStatus("Email and password cannot be empty!");
                return;
            }

            var request = new LoginWithEmailAddressRequest { Email = email, Password = password };

            PlayFabClientAPI.LoginWithEmailAddress(request, OnLoginSuccess, OnLoginFailure);
        }

        public void SignUp()
        {
            Debug.Log("Attempting Sign Up...");
            UpdateStatus("Creating account...");

            string email = signUpEmailInput.text.ToLower().Trim();
            string password = signUpPasswordInput.text;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                UpdateStatus("Email and password cannot be empty!");
                return;
            }

            if (!IsValidEmail(email))
            {
                UpdateStatus("Please enter a valid email address!");
                return;
            }

            if (password.Length < 6)
            {
                UpdateStatus("Password must be at least 6 characters!");
                return;
            }

            var request = new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                RequireBothUsernameAndEmail = false,
            };

            PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess, OnRegisterFailure);
        }

        public void ForgotPassword()
        {
            Debug.Log("Attempting Account Recovery...");
            UpdateStatus("Password recovery...");

            string email = loginEmailInput.text.ToLower().Trim();

            if (string.IsNullOrEmpty(email))
            {
                UpdateStatus("Please enter your email address!");
                return;
            }

            var request = new SendAccountRecoveryEmailRequest
            {
                Email = email,
                TitleId = PlayFabSettings.TitleId,
            };

            PlayFabClientAPI.SendAccountRecoveryEmail(
                request,
                OnPasswordRecoverySuccess,
                OnPasswordRecoveryFailure
            );
        }

        // Public method to get player profile info
        public void GetPlayerProfile(
            System.Action<GetPlayerProfileResult> onSuccess,
            System.Action<PlayFabError> onFailure
        )
        {
            var request = new GetPlayerProfileRequest
            {
                ProfileConstraints = new PlayerProfileViewConstraints { ShowDisplayName = true },
            };

            PlayFabClientAPI.GetPlayerProfile(request, onSuccess, onFailure);
        }

        // Success Callbacks
        private void OnLoginSuccess(LoginResult result)
        {
            Debug.Log("Login successful! PlayFab ID: " + result.PlayFabId);
            PlayFabId = result.PlayFabId;
            UpdateStatus("Login successful!");
            LoadNextScene();
        }

        private void OnRegisterSuccess(RegisterPlayFabUserResult result)
        {
            Debug.Log("Registration successful! PlayFab ID: " + result.PlayFabId);
            PlayFabId = result.PlayFabId;
            PlayerEmail = signUpEmailInput.text.ToLower().Trim();

            UpdateStatus("Registration successful!");
            LoadNextScene();
        }

        private void OnPasswordRecoverySuccess(SendAccountRecoveryEmailResult result)
        {
            Debug.Log("Password recovery email sent!");
            UpdateStatus("Recovery email sent! Check your inbox.");
        }

        // Failure Callbacks
        private void OnLoginFailure(PlayFabError error)
        {
            Debug.LogError("Login failed: " + error.GenerateErrorReport());
            UpdateStatus("Login failed: " + error.ErrorMessage);
        }

        private void OnRegisterFailure(PlayFabError error)
        {
            Debug.LogError("Registration failed: " + error.GenerateErrorReport());
            UpdateStatus("Registration failed: " + error.ErrorMessage);
        }

        private void OnPasswordRecoveryFailure(PlayFabError error)
        {
            Debug.LogError("Password recovery failed: " + error.GenerateErrorReport());
            UpdateStatus("Recovery failed: " + error.ErrorMessage);
        }

        // Helper Methods
        private void LoadNextScene()
        {
            StartCoroutine(LoadSceneAsync());
        }

        private IEnumerator LoadSceneAsync()
        {
            yield return new WaitForSeconds(1f);

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);

            while (!asyncLoad.isDone)
            {
                yield return null;
            }
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
}
