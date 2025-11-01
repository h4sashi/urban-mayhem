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
    

    // Properties
    public float MoveSpeed => moveSpeed;
    public float Acceleration => acceleration;
    public float Deceleration => deceleration;
    public float RotationSpeed => rotationSpeed;
    public float GroundDrag => groundDrag;
    public float AirDrag => airDrag;
    public float InputSmoothing => inputSmoothing;
    public float StopThreshold => stopThreshold;
        
    }
}
