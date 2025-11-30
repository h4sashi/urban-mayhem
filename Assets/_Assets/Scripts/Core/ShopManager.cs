using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Hanzo.Networking.PlayFab;
using PlayFab;
using PlayFab.ClientModels;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

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

    [System.Serializable]
    public class PurchasedCharactersData
    {
        public List<string> characterIds = new List<string>();
    }

    public class ShopManager : MonoBehaviour
    {
        [Header("UI References")]
        public Button[] characterButtons;
        public TextMeshProUGUI[] characterCoinText;
        public Button[] abilityButtons;
        public TextMeshProUGUI[] abilityCoinText;

        [SerializeField]
        private TextMeshProUGUI umpBalanceText;

        [SerializeField]
        private TextMeshProUGUI statusText;

        [Header("Character Configuration")]
        [SerializeField]
        [HideInInspector]
        public string[] characterNames;

        [Header("API Configuration")]
        private const string API_URL =
            "https://kxltwbzkldztokoxakef.supabase.co/functions/v1/game-api";
        private const string API_KEY = "69H5sipdl0konk3DRgku8l6tTv02yr6EYz1OqGJlCE0=";
        private const string SUPABASE_ANON_KEY =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imt4bHR3YnprbGR6dG9rb3hha2VmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjM3MTU2MTQsImV4cCI6MjA3OTI5MTYxNH0.v8bsJpxtrtwp6pudPdCyWMAN0IIHMU-R_5vokFFOgEY";

        [Header("Player Configuration")]
        [SerializeField]
        private string playerId = "TestPlayer";

        // Private variables
        private float currentUmpBalance = 0;
        private PlayerDataResponse playerData;
        private HashSet<string> purchasedCharacters = new HashSet<string>();

        private const string PLAYFAB_PURCHASED_CHARACTERS_KEY = "PurchasedCharacters";
        private const string SELECTED_CHARACTER_PREF_KEY = "SelectedCharacterIndex";

        private void Start()
        {
            // Initialize character names if not set
            if (characterNames == null || characterNames.Length != characterButtons.Length)
            {
                characterNames = new string[characterButtons.Length];
                for (int i = 0; i < characterNames.Length; i++)
                {
                    characterNames[i] = $"Character_{i}";
                }
            }

            // Get PlayFab ID from PlayFabManager if available
            if (Hanzo.Networking.PlayFab.PlayFabManager.Instance != null)
            {
                playerId = Hanzo.Networking.PlayFab.PlayFabManager.Instance.PlayFabId;
            }

            // Setup button listeners
            SetupButtonListeners();

            // Load purchased characters from PlayFab FIRST
            // Default character will be granted in OnPlayFabDataLoaded if needed
            LoadPurchasedCharactersFromPlayFab();

            // Load saved character selection
            LoadSavedCharacterSelection();
        }

        /// <summary>
        /// Set player ID from PlayFab or authentication system
        /// </summary>
        ///
        void LoadPlayerData()
        {
            StartCoroutine(FetchPlayerData(playerId));
        }

        public void SetPlayerId(string newPlayerId)
        {
            playerId = newPlayerId;
            LoadPlayerData();
            LoadPurchasedCharactersFromPlayFab();
        }

        /// <summary>
        /// Load purchased characters from PlayFab
        /// </summary>
        private void LoadPurchasedCharactersFromPlayFab()
        {
            var request = new GetUserDataRequest
            {
                Keys = new List<string> { PLAYFAB_PURCHASED_CHARACTERS_KEY },
            };

            PlayFabClientAPI.GetUserData(request, OnPlayFabDataLoaded, OnPlayFabDataLoadError);
        }

        private void OnPlayFabDataLoaded(GetUserDataResult result)
        {
            if (result.Data != null && result.Data.ContainsKey(PLAYFAB_PURCHASED_CHARACTERS_KEY))
            {
                string json = result.Data[PLAYFAB_PURCHASED_CHARACTERS_KEY].Value;

                try
                {
                    PurchasedCharactersData data = JsonUtility.FromJson<PurchasedCharactersData>(
                        json
                    );
                    purchasedCharacters = new HashSet<string>(data.characterIds);

                    Debug.Log(
                        $"✅ Loaded {purchasedCharacters.Count} purchased characters from PlayFab"
                    );

                    // Ensure first character is owned
                    EnsureDefaultCharacterOwned();

                    // Update UI to reflect owned characters
                    UpdateShopUI();
                }
                catch (Exception e)
                {
                    Debug.LogError($"❌ Failed to parse PlayFab character data: {e.Message}");
                    // Even on error, ensure default character is owned
                    EnsureDefaultCharacterOwned();
                }
            }
            else
            {
                Debug.Log("No purchased characters found in PlayFab");
                purchasedCharacters = new HashSet<string>();

                // Grant default character since no data exists
                EnsureDefaultCharacterOwned();
            }
        }

        /// <summary>
        /// Grant the first character to the player by default
        /// </summary>
        private void GrantDefaultCharacter()
        {
            if (characterNames != null && characterNames.Length > 0)
            {
                string firstCharacterId = characterNames[0];

                if (!purchasedCharacters.Contains(firstCharacterId))
                {
                    Debug.Log($"🎁 Granting default character: {firstCharacterId}");
                    purchasedCharacters.Add(firstCharacterId);

                    // Save to PlayFab
                    SavePurchasedCharacterToPlayFab(firstCharacterId);
                }

                // Update UI
                UpdateShopUI();
            }
        }

        /// <summary>
        /// Ensure the first character is always owned (called after loading data)
        /// </summary>
        private void EnsureDefaultCharacterOwned()
        {
            if (characterNames != null && characterNames.Length > 0)
            {
                string firstCharacterId = characterNames[0];

                if (!purchasedCharacters.Contains(firstCharacterId))
                {
                    Debug.Log($"🎁 Ensuring default character is owned: {firstCharacterId}");
                    purchasedCharacters.Add(firstCharacterId);

                    // Save to PlayFab immediately
                    SavePurchasedCharacterToPlayFab(firstCharacterId);
                }
            }
        }

        private void OnPlayFabDataLoadError(PlayFabError error)
        {
            Debug.LogError($"❌ Failed to load PlayFab data: {error.GenerateErrorReport()}");
        }

        /// <summary>
        /// Save purchased character to PlayFab
        /// </summary>
        private void SavePurchasedCharacterToPlayFab(string characterId)
        {
            // Add to local set
            purchasedCharacters.Add(characterId);

            // Prepare data for PlayFab
            PurchasedCharactersData data = new PurchasedCharactersData
            {
                characterIds = new List<string>(purchasedCharacters),
            };

            string json = JsonUtility.ToJson(data);

            var request = new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string>
                {
                    { PLAYFAB_PURCHASED_CHARACTERS_KEY, json },
                },
            };

            PlayFabClientAPI.UpdateUserData(
                request,
                OnCharacterSavedToPlayFab,
                OnCharacterSaveError
            );
        }

        private void OnCharacterSavedToPlayFab(UpdateUserDataResult result)
        {
            Debug.Log($"✅ Character data saved to PlayFab successfully!");
        }

        private void OnCharacterSaveError(PlayFabError error)
        {
            Debug.LogError($"❌ Failed to save character to PlayFab: {error.GenerateErrorReport()}");
            ShowStatus("Failed to save purchase data", false);
        }

        /// <summary>
        /// Fetches player data from the API using player_id
        /// </summary>
        public IEnumerator FetchPlayerData(string playerIdToFetch)
        {
            string url = $"{API_URL}?action=get_player_data&player_id={playerIdToFetch}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("x-api-key", API_KEY);
                request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_ANON_KEY}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    Debug.Log("✅ Player data received: " + jsonResponse);

                    try
                    {
                        playerData = JsonUtility.FromJson<PlayerDataResponse>(jsonResponse);

                        if (playerData.success)
                        {
                            currentUmpBalance = playerData.ump_balance;
                            UpdateUmpBalanceDisplay();
                            ProcessPlayerSkins();
                            LoadPurchasedCharactersFromPlayFab();
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
                    ShowStatus("Network error", false);
                }
            }
        }

        /// <summary>
        /// Purchase an item using UMP
        /// </summary>
        public void BuyItem(string itemName, float price, System.Action<bool> onComplete = null)
        {
            Debug.Log(
                $"💰 BuyItem called: Item={itemName}, Price={price}, Balance={currentUmpBalance}"
            );

            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("❌ Player ID is not set!");
                ShowStatus("Player ID not set", false);
                onComplete?.Invoke(false);
                return;
            }

            if (currentUmpBalance < price)
            {
                Debug.LogWarning($"⚠️ Insufficient UMP! Need {price}, have {currentUmpBalance}");
                ShowStatus($"Not enough UMP! Need {price:F0}", false);
                onComplete?.Invoke(false);
                return;
            }
            playerId = GameObject.FindAnyObjectByType<UsernameValidator>().currentUsername;

            Debug.Log($"💰 Starting SpendUMP coroutine...");
            StartCoroutine(SpendUMP(playerId, price, itemName, onComplete));
        }

        /// <summary>
        /// Spend UMP by calling the API
        /// </summary>
        private IEnumerator SpendUMP(
            string playerIdToUse,
            float amount,
            string itemName,
            System.Action<bool> callback
        )
        {
            ShowStatus("Processing purchase...", true);

            string url = $"{API_URL}?action=spend_ump";

            // Create properly formatted JSON
            string json =
                $"{{\"player_id\":\"{playerIdToUse}\",\"amount\":{amount},\"item_name\":\"{itemName}\"}}";

            Debug.Log($"💰 Sending request to: {url}");
            Debug.Log($"💰 Request body: {json}");

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-api-key", API_KEY);
                request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_ANON_KEY}");

                yield return request.SendWebRequest();

                Debug.Log($"💰 Response Code: {request.responseCode}");
                Debug.Log($"💰 Response: {request.downloadHandler.text}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"💰 Raw response: {responseText}");

                    try
                    {
                        SpendResponse response = JsonUtility.FromJson<SpendResponse>(responseText);

                        if (response.success)
                        {
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
                    catch (System.Exception e)
                    {
                        Debug.LogError($"❌ Failed to parse response: {e.Message}");
                        Debug.LogError($"❌ Response was: {responseText}");
                        ShowStatus("❌ Failed to parse response", false);
                        callback?.Invoke(false);
                    }
                }
                else
                {
                    Debug.LogError($"❌ API Error: {request.error}");
                    Debug.LogError($"❌ Response Code: {request.responseCode}");
                    Debug.LogError($"❌ Response Body: {request.downloadHandler.text}");

                    // Try to show specific error if available
                    if (!string.IsNullOrEmpty(request.downloadHandler.text))
                    {
                        try
                        {
                            SpendResponse errorResponse = JsonUtility.FromJson<SpendResponse>(
                                request.downloadHandler.text
                            );
                            if (!string.IsNullOrEmpty(errorResponse.error))
                            {
                                ShowStatus($"❌ {errorResponse.error}", false);
                            }
                            else
                            {
                                ShowStatus("❌ Purchase failed", false);
                            }
                        }
                        catch
                        {
                            ShowStatus("❌ Purchase failed", false);
                        }
                    }
                    else
                    {
                        ShowStatus("❌ Purchase failed", false);
                    }

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
                Debug.Log(
                    $"🎨 Skin: {skin.item.name} - Rarity: {skin.item.rarity} - Equipped: {skin.is_equipped}"
                );

                if (skin.is_equipped)
                {
                    ApplyEquippedSkin(skin);
                }

                UnlockSkinInUI(skin);
            }
        }

        /// <summary>
        /// Apply equipped skin to player character
        /// </summary>
        private void ApplyEquippedSkin(PlayerSkin skin)
        {
            Debug.Log($"Applying equipped skin: {skin.item.name}");

            if (skin.item.stats != null)
            {
                // Apply stats to player controller
            }
        }

        /// <summary>
        /// Unlock/show skin in shop UI
        /// </summary>
        private void UnlockSkinInUI(PlayerSkin skin)
        {
            Debug.Log($"Unlocking UI element for: {skin.item.name}");
        }

        /// <summary>
        /// Update shop UI based on current balance and owned items
        /// </summary>
        private void UpdateShopUI()
        {
            int selectedIndex = GetSelectedCharacterIndex();

            for (int i = 0; i < characterButtons.Length; i++)
            {
                if (characterButtons[i] != null && characterCoinText[i] != null)
                {
                    string characterId = characterNames[i];
                    bool isOwned = purchasedCharacters.Contains(characterId);
                    bool isDefault = (i == 0);
                    bool isSelected = (i == selectedIndex);

                    if (isOwned || isDefault)
                    {
                        // Character is owned - show as selectable
                        characterButtons[i].interactable = true;

                        if (isSelected)
                        {
                            characterCoinText[i].text = "SELECTED";
                            characterCoinText[i].color = Color.yellow;
                        }
                        else if (isDefault)
                        {
                            characterCoinText[i].text = "DEFAULT";
                            characterCoinText[i].color = Color.green;
                        }
                        else
                        {
                            characterCoinText[i].text = "OWNED";
                            characterCoinText[i].color = Color.green;
                        }
                    }
                    else
                    {
                        // Character not owned - show price
                        string priceText = characterCoinText[i].text;

                        if (
                            priceText != "DEFAULT"
                            && priceText != "OWNED"
                            && priceText != "SELECTED"
                        )
                        {
                            if (float.TryParse(priceText.Split(' ')[0], out float price))
                            {
                                characterButtons[i].interactable = currentUmpBalance >= price;
                                characterCoinText[i].color = Color.white;
                            }
                        }
                    }
                }
            }

            // Update ability buttons
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
        /// Add selection buttons to shop UI
        /// </summary>
        private void SetupButtonListeners()
        {
            Debug.Log($"Setting up button listeners. Character buttons: {characterButtons.Length}");

            for (int i = 0; i < characterButtons.Length; i++)
            {
                int index = i;
                if (characterButtons[i] != null)
                {
                    characterButtons[i].onClick.RemoveAllListeners();

                    // Check if owned - if yes, make it a select button, else make it a buy button
                    characterButtons[i]
                        .onClick.AddListener(() =>
                        {
                            string charId = characterNames[index];
                            if (purchasedCharacters.Contains(charId) || index == 0)
                            {
                                // Character owned - select it
                                SelectCharacter(index);
                            }
                            else
                            {
                                // Character not owned - try to buy it
                                OnCharacterButtonClicked(index);
                            }
                        });

                    Debug.Log($"✅ Listener added for character button {index}");
                }
            }

            for (int i = 0; i < abilityButtons.Length; i++)
            {
                int index = i;
                if (abilityButtons[i] != null)
                {
                    abilityButtons[i].onClick.RemoveAllListeners();
                    abilityButtons[i].onClick.AddListener(() => OnAbilityButtonClicked(index));
                    Debug.Log($"✅ Listener added for ability button {index}");
                }
            }
        }

        /// <summary>
        /// Handle character button click
        /// </summary>
        private void OnCharacterButtonClicked(int index)
        {
            Debug.Log($"🔵 Character button {index} clicked!");

            if (index >= characterCoinText.Length)
            {
                Debug.LogError(
                    $"❌ Index {index} out of range for characterCoinText (length: {characterCoinText.Length})"
                );
                return;
            }

            if (index >= characterNames.Length)
            {
                Debug.LogError(
                    $"❌ Index {index} out of range for characterNames (length: {characterNames.Length})"
                );
                return;
            }

            string characterId = characterNames[index];
            Debug.Log($"🔵 Character ID: {characterId}");

            // Check if already owned
            if (purchasedCharacters.Contains(characterId))
            {
                Debug.LogWarning($"⚠️ Character {characterId} already owned!");
                ShowStatus("You already own this character!", false);
                return;
            }

            // Check if characterCoinText is null
            if (characterCoinText[index] == null)
            {
                Debug.LogError($"❌ characterCoinText[{index}] is null!");
                return;
            }

            // Parse price
            string priceText = characterCoinText[index].text;
            Debug.Log($"🔵 Price text: '{priceText}'");

            // Try to parse the price more robustly
            float price = 0;
            bool priceParseSuccess = false;

            // Try parsing with space split first
            string[] parts = priceText.Split(' ');
            if (parts.Length > 0)
            {
                priceParseSuccess = float.TryParse(parts[0], out price);
            }

            // If that fails, try parsing the whole string
            if (!priceParseSuccess)
            {
                priceParseSuccess = float.TryParse(priceText, out price);
            }

            if (priceParseSuccess)
            {
                Debug.Log($"🔵 Parsed price: {price} UMP");
                Debug.Log($"🔵 Current balance: {currentUmpBalance} UMP");

                if (currentUmpBalance < price)
                {
                    Debug.LogWarning(
                        $"⚠️ Insufficient funds! Need {price}, have {currentUmpBalance}"
                    );
                    ShowStatus($"Not enough UMP! Need {price:F0}", false);
                    return;
                }

                Debug.Log($"🔵 Initiating purchase for {characterId} at {price} UMP");
                BuyItem(
                    characterId,
                    price,
                    (success) =>
                    {
                        Debug.Log($"🔵 Purchase callback received. Success: {success}");
                        if (success)
                        {
                            OnCharacterPurchased(index, characterId);
                        }
                    }
                );
            }
            else
            {
                Debug.LogError($"❌ Failed to parse price from text: '{priceText}'");
                ShowStatus("Invalid price format", false);
            }
        }

        /// <summary>
        /// Handle ability button click
        /// </summary>
        private void OnAbilityButtonClicked(int index)
        {
            Debug.Log($"🟢 Ability button {index} clicked!");

            if (index >= abilityCoinText.Length)
            {
                Debug.LogError(
                    $"❌ Index {index} out of range for abilityCoinText (length: {abilityCoinText.Length})"
                );
                return;
            }

            // Check if abilityCoinText is null
            if (abilityCoinText[index] == null)
            {
                Debug.LogError($"❌ abilityCoinText[{index}] is null!");
                return;
            }

            string priceText = abilityCoinText[index].text;
            Debug.Log($"🟢 Price text: '{priceText}'");

            // Try to parse the price more robustly
            float price = 0;
            bool priceParseSuccess = false;

            // Try parsing with space split first
            string[] parts = priceText.Split(' ');
            if (parts.Length > 0)
            {
                priceParseSuccess = float.TryParse(parts[0], out price);
            }

            // If that fails, try parsing the whole string
            if (!priceParseSuccess)
            {
                priceParseSuccess = float.TryParse(priceText, out price);
            }

            if (priceParseSuccess)
            {
                Debug.Log($"🟢 Parsed price: {price} UMP");
                Debug.Log($"🟢 Current balance: {currentUmpBalance} UMP");

                string itemName = $"Ability_{index}";
                Debug.Log($"🟢 Initiating purchase for {itemName} at {price} UMP");

                BuyItem(
                    itemName,
                    price,
                    (success) =>
                    {
                        Debug.Log($"🟢 Purchase callback received. Success: {success}");
                        if (success)
                        {
                            OnAbilityPurchased(index);
                        }
                    }
                );
            }
            else
            {
                Debug.LogError($"❌ Failed to parse price from text: '{priceText}'");
                ShowStatus("Invalid price format", false);
            }
        }

        /// <summary>
        /// Called when character is successfully purchased
        /// </summary>
        private void OnCharacterPurchased(int index, string characterId)
        {
            Debug.Log($"Character {characterId} purchased!");

            // Save to PlayFab
            SavePurchasedCharacterToPlayFab(characterId);

            // Automatically select the newly purchased character
            SelectCharacter(index);

            // Update UI immediately
            UpdateShopUI();

            ShowStatus($"✅ {characterId} purchased and selected!", true);
        }

        /// <summary>
        /// Select a character and save to PlayerPrefs
        /// </summary>
        public void SelectCharacter(int characterIndex)
        {
            if (characterIndex < 0 || characterIndex >= characterNames.Length)
            {
                Debug.LogError($"❌ Invalid character index: {characterIndex}");
                return;
            }

            string characterId = characterNames[characterIndex];

            // Check if character is owned
            if (!purchasedCharacters.Contains(characterId) && characterIndex != 0)
            {
                Debug.LogWarning($"⚠️ Cannot select unowned character: {characterId}");
                ShowStatus("You don't own this character!", false);
                return;
            }

            // Save selection to PlayerPrefs
            PlayerPrefs.SetInt(SELECTED_CHARACTER_PREF_KEY, characterIndex);
            PlayerPrefs.Save();

            Debug.Log($"✅ Selected character: {characterId} (index: {characterIndex})");

            // Update CharacterSelector if available
            if (CharacterSelector.Instance != null)
            {
                CharacterSelector.Instance.SetSelectedCharacter(characterIndex);
                Debug.Log($"✅ Updated CharacterSelector with index: {characterIndex}");
            }
            else
            {
                Debug.LogWarning(
                    "⚠️ CharacterSelector.Instance not found. Selection saved but not applied."
                );
            }

            ShowStatus($"✅ {characterId} selected!", true);
        }

        /// <summary>
        /// Get the currently selected character index
        /// </summary>
        public int GetSelectedCharacterIndex()
        {
            return PlayerPrefs.GetInt(SELECTED_CHARACTER_PREF_KEY, 0);
        }

        /// <summary>
        /// Load saved character selection on start
        /// </summary>
        private void LoadSavedCharacterSelection()
        {
            int savedIndex = GetSelectedCharacterIndex();

            if (CharacterSelector.Instance != null)
            {
                CharacterSelector.Instance.SetSelectedCharacter(savedIndex);
                Debug.Log($"✅ Loaded saved character selection: index {savedIndex}");
            }
        }

        /// <summary>
        /// Called when ability is successfully purchased
        /// </summary>
        private void OnAbilityPurchased(int index)
        {
            Debug.Log($"Ability {index} purchased!");
            // Add your logic here
        }

        // ============ PUBLIC API ============

        public void RefreshPlayerData()
        {
            LoadPlayerData();
            LoadPurchasedCharactersFromPlayFab();
        }

        public float GetUmpBalance()
        {
            return currentUmpBalance;
        }

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

        public bool HasCharacter(string characterId)
        {
            return purchasedCharacters.Contains(characterId);
        }

        public PlayerSkin[] GetOwnedSkins()
        {
            return playerData?.skins ?? new PlayerSkin[0];
        }

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
            BuyItem(
                "Test Item",
                50f,
                (success) =>
                {
                    Debug.Log($"Test purchase result: {success}");
                }
            );
        }

        [ContextMenu("Test API Connection")]
        private void TestAPIConnection()
        {
            StartCoroutine(TestAPIConnectionCoroutine());
        }

        private IEnumerator TestAPIConnectionCoroutine()
        {
            Debug.Log("🧪 Testing API connection...");
            Debug.Log($"🧪 Player ID: {playerId}");
            Debug.Log($"🧪 API URL: {API_URL}");

            string url = $"{API_URL}?action=get_player_data&player_id={playerId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("x-api-key", API_KEY);
                request.SetRequestHeader("Authorization", $"Bearer {SUPABASE_ANON_KEY}");

                yield return request.SendWebRequest();

                Debug.Log($"🧪 Response Code: {request.responseCode}");
                Debug.Log($"🧪 Response: {request.downloadHandler.text}");

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("✅ API connection successful!");
                }
                else
                {
                    Debug.LogError($"❌ API connection failed: {request.error}");
                }
            }
        }

        [ContextMenu("Clear Purchased Characters")]
        private void ClearPurchasedCharacters()
        {
            purchasedCharacters.Clear();
            SavePurchasedCharacterToPlayFab("");
            UpdateShopUI();
            Debug.Log("Cleared all purchased characters");
        }

        [ContextMenu("Show Current State")]
        private void ShowCurrentState()
        {
            Debug.Log("=== SHOP MANAGER STATE ===");
            Debug.Log($"Player ID: {playerId}");
            Debug.Log($"UMP Balance: {currentUmpBalance}");
            Debug.Log($"Purchased Characters: {purchasedCharacters.Count}");
            foreach (var charId in purchasedCharacters)
            {
                Debug.Log($"  - {charId}");
            }
            Debug.Log($"Character Buttons: {characterButtons?.Length ?? 0}");
            Debug.Log($"Character Names: {characterNames?.Length ?? 0}");
            Debug.Log("========================");
        }
    }
}
