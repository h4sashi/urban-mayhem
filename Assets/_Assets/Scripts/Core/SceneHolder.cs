using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Hanzo.Core
{
    public class SceneHolder : MonoBehaviour
    {
        [Header("Menu References")]
        public Button[] menuButtons;
        public Volume cameraPostProcessVolume;

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
            cameraPostProcessVolume.enabled = true;
            foreach (var button in menuButtons)
            {
                button.enabled = false;
            }
        }

        public void BackToMenu()
        {
            shopPanel.SetActive(false);
            backToMenuButton.gameObject.SetActive(false);
              cameraPostProcessVolume.enabled = false;
            foreach (var button in menuButtons)
            {
               button.enabled = true;
            }
        }


    }
}
