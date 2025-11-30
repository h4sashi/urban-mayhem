using UnityEngine;

namespace Hanzo.Core
{
    public class CharacterSelector : MonoBehaviour
    {
        public static CharacterSelector Instance { get; private set; }
        
        [Header("Character References")]
        public GameObject[] availableCharacters;
        public GameObject selectedCharacter;

        private const string SELECTED_CHARACTER_PREF_KEY = "SelectedCharacterIndex";
        private int currentSelectedIndex = 0;

        private void Awake()
        {
            // Singleton pattern
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

            // Load saved selection
            LoadSelectedCharacter();
        }

        private void Start()
        {
            // Apply the loaded selection
            ApplyCharacterSelection(currentSelectedIndex);
        }

        /// <summary>
        /// Load the selected character from PlayerPrefs
        /// </summary>
        private void LoadSelectedCharacter()
        {
            currentSelectedIndex = PlayerPrefs.GetInt(SELECTED_CHARACTER_PREF_KEY, 0);
            Debug.Log($"✅ Loaded character selection: index {currentSelectedIndex}");
        }

        /// <summary>
        /// Set the selected character by index
        /// </summary>
        public void SetSelectedCharacter(int characterIndex)
        {
            if (characterIndex < 0 || characterIndex >= availableCharacters.Length)
            {
                Debug.LogError($"❌ Invalid character index: {characterIndex}. Available characters: {availableCharacters.Length}");
                return;
            }

            currentSelectedIndex = characterIndex;
            
            // Save to PlayerPrefs
            PlayerPrefs.SetInt(SELECTED_CHARACTER_PREF_KEY, characterIndex);
            PlayerPrefs.Save();

            // Apply the selection
            ApplyCharacterSelection(characterIndex);

            Debug.Log($"✅ Character {characterIndex} selected and applied");
        }

        /// <summary>
        /// Apply the character selection to the scene
        /// </summary>
        private void ApplyCharacterSelection(int characterIndex)
        {
            if (availableCharacters == null || availableCharacters.Length == 0)
            {
                Debug.LogWarning("⚠️ No available characters configured");
                return;
            }

            if (characterIndex < 0 || characterIndex >= availableCharacters.Length)
            {
                Debug.LogWarning($"⚠️ Character index {characterIndex} out of range, defaulting to 0");
                characterIndex = 0;
            }

            // Deactivate all characters first
            foreach (GameObject character in availableCharacters)
            {
                if (character != null)
                {
                    character.SetActive(false);
                }
            }

            // Activate the selected character
            if (availableCharacters[characterIndex] != null)
            {
                availableCharacters[characterIndex].SetActive(true);
                selectedCharacter = availableCharacters[characterIndex];

                Debug.Log($"✅ Applied character selection: {selectedCharacter.name}");
            }
            else
            {
                Debug.LogError($"❌ Character at index {characterIndex} is null!");
            }

        }

      

        /// <summary>
        /// Get the currently selected character index
        /// </summary>
        public int GetSelectedCharacterIndex()
        {
            return currentSelectedIndex;
        }

        /// <summary>
        /// Get the currently selected character GameObject
        /// </summary>
        public GameObject GetSelectedCharacter()
        {
            return selectedCharacter;
        }

        /// <summary>
        /// For manual selection (e.g., from a character selection screen)
        /// </summary>
        public void SelectCharacterFromUI(int characterIndex)
        {
            SetSelectedCharacter(characterIndex);
        }

#if UNITY_EDITOR
        [ContextMenu("Reset Selection to Default")]
        private void ResetSelection()
        {
            PlayerPrefs.DeleteKey(SELECTED_CHARACTER_PREF_KEY);
            SetSelectedCharacter(0);
            Debug.Log("✅ Reset character selection to default (0)");
        }

        [ContextMenu("Show Current Selection")]
        private void ShowCurrentSelection()
        {
            Debug.Log($"=== CHARACTER SELECTOR STATE ===");
            Debug.Log($"Current Index: {currentSelectedIndex}");
            Debug.Log($"Selected Character: {(selectedCharacter ? selectedCharacter.name : "None")}");
            Debug.Log($"Available Characters: {availableCharacters?.Length ?? 0}");
            Debug.Log($"================================");
        }
#endif
    }
}