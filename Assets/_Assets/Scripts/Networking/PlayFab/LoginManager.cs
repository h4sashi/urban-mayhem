using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PlayFab;
using PlayFab.ClientModels;

namespace Hanzo.Networking.PlayFab
{
    public class LoginManager : MonoBehaviour
    {
        public GameObject loginPanel;
        public GameObject signUp;
        
        [Header("Login Fields")]
        public TMPro.TMP_InputField loginUsernameInput;
        public TMPro.TMP_InputField loginPasswordInput;
        
        [Header("Sign Up Fields")]
        public TMPro.TMP_InputField signUpEmailInput;
        public TMPro.TMP_InputField signUpPasswordInput;
        
        [Header("Scene Management")]
        public string nextSceneName = "MainGame"; // Scene to load after successful login
        
        [Header("UI Feedback (Optional)")]
        public TMPro.TMP_Text statusText; // Optional: For displaying status messages
        
        private void Start()
        {
            // Set PlayFab Title ID if not set in PlayFabSettings
            if (string.IsNullOrEmpty(PlayFabSettings.TitleId))
            {
                PlayFabSettings.TitleId = "154756"; // Replace with your actual Title ID
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
            
            string username = loginUsernameInput.text.ToLower().Trim();
            string password = loginPasswordInput.text;
            
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                UpdateStatus("Username and password cannot be empty!");
                return;
            }
            
            var request = new LoginWithPlayFabRequest
            {
                Username = username,
                Password = password
            };
            
            PlayFabClientAPI.LoginWithPlayFab(request, OnLoginSuccess, OnLoginFailure);
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
            
            // Register with email only - username will be set in next scene
            var request = new RegisterPlayFabUserRequest
            {
                Email = email,
                Password = password,
                RequireBothUsernameAndEmail = false
            };
            
            PlayFabClientAPI.RegisterPlayFabUser(request, OnRegisterSuccess, OnRegisterFailure);
        }
        
        public void ForgotPassword()
        {
            Debug.Log("Attempting Account Recovery...");
            UpdateStatus("Password recovery...");
            
            string email = loginUsernameInput.text.ToLower().Trim();
            
            if (string.IsNullOrEmpty(email))
            {
                UpdateStatus("Please enter your email address!");
                return;
            }
            
            var request = new SendAccountRecoveryEmailRequest
            {
                Email = email,
                TitleId = PlayFabSettings.TitleId
            };
            
            PlayFabClientAPI.SendAccountRecoveryEmail(request, OnPasswordRecoverySuccess, OnPasswordRecoveryFailure);
        }
        
        // Success Callbacks
        private void OnLoginSuccess(LoginResult result)
        {
            Debug.Log("Login successful! PlayFab ID: " + result.PlayFabId);
            UpdateStatus("Login successful!");
            LoadNextScene();
        }
        
        private void OnRegisterSuccess(RegisterPlayFabUserResult result)
        {
            Debug.Log("Registration successful! PlayFab ID: " + result.PlayFabId);
            // UpdateStatus("Account created! Please set your username.");
            LoadNextScene(); // Load username setup scene
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
            yield return new WaitForSeconds(1f); // Brief delay for user feedback
            
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