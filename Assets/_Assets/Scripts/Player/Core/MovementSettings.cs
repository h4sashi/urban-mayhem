using UnityEngine;

namespace Hanzo.Player.Core
{
    [CreateAssetMenu(fileName = "MovementSettings", menuName = "Hanzo/Movement Settings")]
    public class MovementSettings : ScriptableObject
    {
        [Header("Basic Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 20f;
        [SerializeField] private float deceleration = 25f;
        [SerializeField] private float rotationSpeed = 720f;
        
        [Header("Physics")]
        [SerializeField] private float groundDrag = 6f;
        [SerializeField] private float airDrag = 0.5f;
        
        [Header("Movement Feel")]
        [SerializeField] private float inputSmoothing = 0.1f;
        [SerializeField] private float stopThreshold = 0.5f;
        
        [Header("Ground Detection")]
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private LayerMask groundLayer = ~0; // Everything by default
        
        [Header("Falling")]
        [SerializeField] private float fallThreshold = 0.5f; // Height above ground to trigger falling
        [SerializeField] private float fallCheckInterval = 0.1f; // How often to check if we should fall
        
        // Properties
        public float MoveSpeed => moveSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float RotationSpeed => rotationSpeed;
        public float GroundDrag => groundDrag;
        public float AirDrag => airDrag;
        public float InputSmoothing => inputSmoothing;
        public float StopThreshold => stopThreshold;
        public float GroundCheckDistance => groundCheckDistance;
        public LayerMask GroundLayer => groundLayer;
        public float FallThreshold => fallThreshold;
        public float FallCheckInterval => fallCheckInterval;
    }
}