using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;

namespace Hanzo.Core
{
    [System.Serializable]
    public class PlayerDataResponse
    {
        public bool success;
        public string player_id;
        public string wallet_address;
        public float ump_balance;
        public PlayerSkin[] skins;
        public int total_skins;
        public string timestamp;
    }

    [System.Serializable]
    public class PlayerSkin
    {
        public string id;
        public int quantity;
        public bool is_equipped;
        public string acquired_at;
        public SkinItem item;
    }

    [System.Serializable]
    public class SkinItem
    {
        public string id;
        public string name;
        public string type;
        public string rarity;
        public string image_url;
        public SkinStats stats;
    }

    [System.Serializable]
    public class SkinStats
    {
        public float movementSpeed;
        public float armor;
    }

    [System.Serializable]
    public class SpendResponse
    {
        public bool success;
        public string error;
        public float new_balance;
        public float current_balance;
        public float previous_balance;
        public float spent;
        public string item;
    }

    public class ShopManager : MonoBehaviour
    {
        [Header("UI References")]
        public Button[] characterButtons;
        public TextMeshProUGUI[] characterCoinText;
        public Button[] abilityButtons;
        public TextMeshProUGUI[] abilityCoinText;
        [SerializeField] private TextMeshProUGUI umpBalanceText;
        [SerializeField] private TextMeshProUGUI statusText;
        
        [Header("API Configuration")]
        private const string API_URL = "https://kxltwbzkldztokoxakef.supabase.co/functions/v1/game-api";
        private const string API_KEY = "69H5sipdl0konk3DRgku8l6tTv02yr6EYz1OqGJlCE0=";
        private const string SUPABASE_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imt4bHR3YnprbGR6dG9rb3hha2VmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjM3MTU2MTQsImV4cCI6MjA3OTI5MTYxNH0.v8bsJpxtrtwp6pudPdCyWMAN0IIHMU-R_5vokFFOgEY";
        
        [Header("Player Configuration")]
        [SerializeField] private string playerId = "TestPlayer"; // PlayFab ID, NOT wallet address
        
        // Private variables
        private float currentUmpBalance = 0;
        private PlayerDataResponse playerData;
        
        private void Start()
        {
            // Fetch player data when the shop opens
            // LoadPlayerData();
            
            // Setup button listeners (example)
            SetupButtonListeners();
        }
        
        /// <summary>
        /// Set player ID from PlayFab or authentication system
        /// Call this after player logs in
        /// </summary>
        public void SetPlayerId(string newPlayerId)
        {
            playerId = newPlayerId;
            // LoadPlayerData();
        }
        
        /// <summary>
        /// Load player data including UMP balance and owned skins
        /// </summary>
        public void LoadPlayerData()
        {
            StartCoroutine(FetchPlayerData(playerId));
        }
        
        /// <summary>
        /// Fetches player data from the API using player_id
        /// </summary>
        public IEnumerator FetchPlayerData(string playerIdToFetch)
        {
            string url = $"{API_URL}?action=get_player_data&player_id={playerIdToFetch}";
            
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                // Add required headers
                request.SetRequestHeader("x-api-key", API_KEY);
                request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_ANON_KEY}");
                
                // Send the request
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    Debug.Log("✅ Player data received: " + jsonResponse);
                    
                    // Parse the JSON response
                    try
                    {
                        playerData = JsonUtility.FromJson<PlayerDataResponse>(jsonResponse);
                        
                        if (playerData.success)
                        {
                            // Update UMP balance
                            currentUmpBalance = playerData.ump_balance;
                            UpdateUmpBalanceDisplay();
                            
                            // Process skins data
                            ProcessPlayerSkins();
                            
                            // Update shop UI based on balance
                            UpdateShopUI();
                            
                            Debug.Log($"✅ Player: {playerData.player_id}");
                            Debug.Log($"✅ UMP Balance: {currentUmpBalance}");
                            Debug.Log($"✅ Total Skins: {playerData.total_skins}");
                        }
                        else
                        {
                            Debug.LogError("❌ API returned success: false");
                            ShowStatus("Failed to load player data", false);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"❌ Failed to parse player data: {e.Message}");
                        ShowStatus("Error loading data", false);
                    }
                }
                else
                {
                    Debug.LogError($"❌ Failed to fetch player data: {request.error}");
                    Debug.LogError($"Response Code: {request.responseCode}");
                    Debug.LogError($"Response: {request.downloadHandler.text}");
                    ShowStatus("Network error", false);
                }
            }
        }
        
        /// <summary>
        /// Purchase an item using UMP
        /// </summary>
        public void BuyItem(string itemName, float price, System.Action<bool> onComplete = null)
        {
            // Client-side validation
            if (currentUmpBalance < price)
            {
                Debug.LogWarning("⚠️ Insufficient UMP!");
                ShowStatus($"Not enough UMP! Need {price:F0}", false);
                onComplete?.Invoke(false);
                return;
            }
            
            StartCoroutine(SpendUMP(playerId, price, itemName, onComplete));
        }
        
        /// <summary>
        /// Spend UMP by calling the API
        /// </summary>
        private IEnumerator SpendUMP(string playerIdToUse, float amount, string itemName, System.Action<bool> callback)
        {
            ShowStatus("Processing purchase...", true);
            
            string url = $"{API_URL}?action=spend_ump";
            string json = $"{{\"player_id\":\"{playerIdToUse}\",\"amount\":{amount},\"item_name\":\"{itemName}\"}}";
            
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", API_KEY);
                request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_ANON_KEY}");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    SpendResponse response = JsonUtility.FromJson<SpendResponse>(request.downloadHandler.text);
                    
                    if (response.success)
                    {
                        // Update local balance
                        currentUmpBalance = response.new_balance;
                        UpdateUmpBalanceDisplay();
                        UpdateShopUI();
                        
                        Debug.Log($"✅ Purchase successful! New balance: {currentUmpBalance}");
                        ShowStatus($"✅ Bought {itemName}!", true);
                        
                        callback?.Invoke(true);
                    }
                    else
                    {
                        Debug.LogError($"❌ Purchase failed: {response.error}");
                        ShowStatus($"❌ {response.error}", false);
                        callback?.Invoke(false);
                    }
                }
                else
                {
                    Debug.LogError($"❌ API Error: {request.error}");
                    ShowStatus("❌ Purchase failed", false);
                    callback?.Invoke(false);
                }
            }
        }
        
        /// <summary>
        /// Updates the UMP balance text display
        /// </summary>
        private void UpdateUmpBalanceDisplay()
        {
            if (umpBalanceText != null)
            {
                umpBalanceText.text = $"{currentUmpBalance:F2} UMP";
            }
        }
        
        /// <summary>
        /// Show status message to user
        /// </summary>
        private void ShowStatus(string message, bool isSuccess)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = isSuccess ? Color.green : Color.red;
                
                // Clear after 3 seconds
                CancelInvoke(nameof(ClearStatus));
                Invoke(nameof(ClearStatus), 3f);
            }
        }
        
        private void ClearStatus()
        {
            if (statusText != null)
            {
                statusText.text = "";
            }
        }
        
        /// <summary>
        /// Process and display player's owned skins
        /// </summary>
        private void ProcessPlayerSkins()
        {
            if (playerData == null || playerData.skins == null)
                return;
            
            foreach (PlayerSkin skin in playerData.skins)
            {
                Debug.Log($"🎨 Skin: {skin.item.name} - Rarity: {skin.item.rarity} - Equipped: {skin.is_equipped}");
                
                // Apply skin to character if equipped
                if (skin.is_equipped)
                {
                    ApplyEquippedSkin(skin);
                }
                
                // Unlock skin in UI
                UnlockSkinInUI(skin);
            }
        }
        
        /// <summary>
        /// Apply equipped skin to player character
        /// </summary>
        private void ApplyEquippedSkin(PlayerSkin skin)
        {
            Debug.Log($"Applying equipped skin: {skin.item.name}");
            
            // Apply stats if available
            if (skin.item.stats != null)
            {
                // Example: Apply to your player controller
                // playerController.movementSpeed += skin.item.stats.movementSpeed;
                // playerController.armor += skin.item.stats.armor;
            }
            
            // Load and apply visual skin
            // Example: LoadSkinModel(skin.item.image_url);
        }
        
        /// <summary>
        /// Unlock/show skin in shop UI
        /// </summary>
        private void UnlockSkinInUI(PlayerSkin skin)
        {
            // Example: Find the character button for this skin and mark it as owned
            // This is where you'd update your UI to show the skin is owned
            Debug.Log($"Unlocking UI element for: {skin.item.name}");
        }
        
        /// <summary>
        /// Update shop UI based on current balance
        /// Enable/disable purchase buttons
        /// </summary>
        private void UpdateShopUI()
        {
            // Example: Update character buttons
            for (int i = 0; i < characterButtons.Length; i++)
            {
                if (characterButtons[i] != null && characterCoinText[i] != null)
                {
                    // Parse the price from the text (assuming format like "50 UMP")
                    string priceText = characterCoinText[i].text;
                    if (float.TryParse(priceText.Split(' ')[0], out float price))
                    {
                        // Enable button only if player has enough UMP
                        characterButtons[i].interactable = currentUmpBalance >= price;
                    }
                }
            }
            
            // Example: Update ability buttons
            for (int i = 0; i < abilityButtons.Length; i++)
            {
                if (abilityButtons[i] != null && abilityCoinText[i] != null)
                {
                    string priceText = abilityCoinText[i].text;
                    if (float.TryParse(priceText.Split(' ')[0], out float price))
                    {
                        abilityButtons[i].interactable = currentUmpBalance >= price;
                    }
                }
            }
        }
        
        /// <summary>
        /// Setup button click listeners
        /// </summary>
        private void SetupButtonListeners()
        {
            // Example: Setup character purchase buttons
            for (int i = 0; i < characterButtons.Length; i++)
            {
                int index = i; // Capture for closure
                if (characterButtons[i] != null)
                {
                    characterButtons[i].onClick.AddListener(() => OnCharacterButtonClicked(index));
                }
            }
            
            // Example: Setup ability purchase buttons
            for (int i = 0; i < abilityButtons.Length; i++)
            {
                int index = i; // Capture for closure
                if (abilityButtons[i] != null)
                {
                    abilityButtons[i].onClick.AddListener(() => OnAbilityButtonClicked(index));
                }
            }
        }
        
        /// <summary>
        /// Handle character button click
        /// </summary>
        private void OnCharacterButtonClicked(int index)
        {
            if (index >= characterCoinText.Length) return;
            
            // Parse price
            string priceText = characterCoinText[index].text;
            if (float.TryParse(priceText.Split(' ')[0], out float price))
            {
                string itemName = $"Character_{index}"; // Replace with actual name
                BuyItem(itemName, price, (success) =>
                {
                    if (success)
                    {
                        OnCharacterPurchased(index);
                    }
                });
            }
        }
        
        /// <summary>
        /// Handle ability button click
        /// </summary>
        private void OnAbilityButtonClicked(int index)
        {
            if (index >= abilityCoinText.Length) return;
            
            // Parse price
            string priceText = abilityCoinText[index].text;
            if (float.TryParse(priceText.Split(' ')[0], out float price))
            {
                string itemName = $"Ability_{index}"; // Replace with actual name
                BuyItem(itemName, price, (success) =>
                {
                    if (success)
                    {
                        OnAbilityPurchased(index);
                    }
                });
            }
        }
        
        /// <summary>
        /// Called when character is successfully purchased
        /// </summary>
        private void OnCharacterPurchased(int index)
        {
            Debug.Log($"Character {index} purchased!");
            // Add your logic here: unlock character, update UI, etc.
        }
        
        /// <summary>
        /// Called when ability is successfully purchased
        /// </summary>
        private void OnAbilityPurchased(int index)
        {
            Debug.Log($"Ability {index} purchased!");
            // Add your logic here: unlock ability, update UI, etc.
        }
        
        // ============ PUBLIC API ============
        
        /// <summary>
        /// Refresh player data (call after web store purchases)
        /// </summary>
        public void RefreshPlayerData()
        {
            LoadPlayerData();
        }
        
        /// <summary>
        /// Get current UMP balance
        /// </summary>
        public float GetUmpBalance()
        {
            return currentUmpBalance;
        }
        
        /// <summary>
        /// Check if player owns a specific skin by name
        /// </summary>
        public bool HasSkin(string skinName)
        {
            if (playerData == null || playerData.skins == null)
                return false;
            
            foreach (PlayerSkin skin in playerData.skins)
            {
                if (skin.item.name == skinName)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Get all owned skins
        /// </summary>
        public PlayerSkin[] GetOwnedSkins()
        {
            return playerData?.skins ?? new PlayerSkin[0];
        }
        
        /// <summary>
        /// Check if player has enough UMP for a purchase
        /// </summary>
        public bool CanAfford(float price)
        {
            return currentUmpBalance >= price;
        }
        
        // ============ TESTING ============
        
        [ContextMenu("Test Load Player Data")]
        private void TestLoadPlayerData()
        {
            LoadPlayerData();
        }
        
        [ContextMenu("Test Purchase (50 UMP)")]
        private void TestPurchase()
        {
            BuyItem("Test Item", 50f, (success) =>
            {
                Debug.Log($"Test purchase result: {success}");
            });
        }
    }
}