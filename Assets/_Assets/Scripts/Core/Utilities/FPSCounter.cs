using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private TextMeshProUGUI fpsText;
    
    [Header("Settings")]
    [SerializeField] private float updateInterval = 0.5f;
    
    private float deltaTime = 0.0f;
    private float timer = 0.0f;
    private int frameCount = 0;
    private float fps = 0.0f;
    
    void Update()
    {
        // Calculate delta time
        deltaTime += Time.unscaledDeltaTime;
        timer += Time.unscaledDeltaTime;
        frameCount++;
        
        // Update FPS display at specified interval
        if (timer >= updateInterval)
        {
            fps = frameCount / timer;
            
            // Update the text
            if (fpsText != null)
            {
                fpsText.text = $"FPS: {Mathf.Ceil(fps)}";
                
                // Optional: Color code based on performance
                if (fps >= 60)
                    fpsText.color = Color.green;
                else if (fps >= 30)
                    fpsText.color = Color.yellow;
                else
                    fpsText.color = Color.red;
            }
            
            // Reset counters
            timer = 0.0f;
            frameCount = 0;
        }
    }
}