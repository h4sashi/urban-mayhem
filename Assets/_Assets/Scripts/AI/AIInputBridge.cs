using UnityEngine;
using Hanzo.Player.Input;

namespace Hanzo.AI
{
    /// <summary>
    /// SIMPLIFIED: AIInputBridge just marks the input handler as AI-controlled
    /// The AIPlayerController handles all input sending directly
    /// </summary>
    [RequireComponent(typeof(AIPlayerController))]
    [RequireComponent(typeof(PlayerInputHandler))]
    public class AIInputBridge : MonoBehaviour
    {
        private AIPlayerController aiController;
        private PlayerInputHandler inputHandler;
        
        private void Awake()
        {
            aiController = GetComponent<AIPlayerController>();
            inputHandler = GetComponent<PlayerInputHandler>();
            
            if (aiController == null)
            {
                Debug.LogError("[AIInputBridge] AIPlayerController not found!");
                enabled = false;
                return;
            }
            
            if (inputHandler == null)
            {
                Debug.LogError("[AIInputBridge] PlayerInputHandler not found!");
                enabled = false;
                return;
            }
            
            // CRITICAL: Set AI-controlled flag so input handler doesn't fight for control
            inputHandler.SetAIControlled(true);
            Debug.Log("[AIInputBridge] ✓ Input handler marked as AI-controlled");
            
            // Disable this component - we only needed it for setup
            enabled = false;
        }
    }
}