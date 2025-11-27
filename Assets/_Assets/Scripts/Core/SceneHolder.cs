using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Hanzo.Core
{
    public class SceneHolder : MonoBehaviour
    {
        [Header("Menu References")]
        public Button[] menuButtons;

        [Header("Shop References")]
        public Button backToMenuButton;
        public GameObject shopPanel;

        

        public void Play()
        {
            SceneManager.LoadScene("Loading");
        }

        public void OpenShop()
        {
            shopPanel.SetActive(true);
            backToMenuButton.gameObject.SetActive(true);
            foreach (var button in menuButtons)
            {
                button.gameObject.SetActive(false);
            }
        }

        public void BackToMenu()
        {
            shopPanel.SetActive(false);
            backToMenuButton.gameObject.SetActive(false);
            foreach (var button in menuButtons)
            {
                button.gameObject.SetActive(true);
            }
        }


    }
}
