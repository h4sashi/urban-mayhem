using UnityEngine;
using Photon.Pun;

namespace Hanzo.DebugTools
{
    /// <summary>
    /// Temporary debug script to diagnose Rigidbody physics issues
    /// Attach to player prefab temporarily, remove after fixing
    /// </summary>
    public class RigidbodyDebugChecker : MonoBehaviour
    {
        private Rigidbody rb;
        private PhotonView pv;
        
        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            pv = GetComponent<PhotonView>();
            
            if (pv.IsMine)
            {
                ValidateRigidbody();
            }
        }
        
        private void ValidateRigidbody()
        {
            Debug.Log("========== RIGIDBODY VALIDATION ==========");
            
            if (rb == null)
            {
                Debug.LogError("❌ CRITICAL: No Rigidbody component found!");
                return;
            }
            
            Debug.Log($"✅ Rigidbody found");
            Debug.Log($"   - IsKinematic: {rb.isKinematic} (should be FALSE)");
            Debug.Log($"   - Mass: {rb.mass} (recommended: 1-5)");
            Debug.Log($"   - Drag: {rb.drag} (recommended: 5-8)");
            Debug.Log($"   - Angular Drag: {rb.angularDrag}");
            Debug.Log($"   - Use Gravity: {rb.useGravity} (should be TRUE)");
            Debug.Log($"   - Interpolate: {rb.interpolation} (recommended: Interpolate)");
            Debug.Log($"   - Collision Detection: {rb.collisionDetectionMode}");
            Debug.Log($"   - Constraints: {rb.constraints}");
            
            // Check for issues
            if (rb.isKinematic)
            {
                Debug.LogError("❌ PROBLEM: Rigidbody is Kinematic! Physics won't work.");
                Debug.LogError("   FIX: Set IsKinematic to FALSE in Inspector");
            }
            
            if (rb.mass < 0.5f || rb.mass > 10f)
            {
                Debug.LogWarning("⚠️ WARNING: Mass is unusual. Recommended: 1-5");
            }
            
            if (!rb.useGravity)
            {
                Debug.LogWarning("⚠️ WARNING: Gravity disabled. Knockback may not look natural.");
            }
            
            Debug.Log("==========================================");
        }
        
        // Call this manually to test knockback without collision
        [ContextMenu("Test Knockback (Forward)")]
        private void TestKnockbackForward()
        {
            if (!pv.IsMine) return;
            
            Vector3 testDirection = transform.forward;
            float testForce = 15f;
            
            Debug.Log($"🧪 TEST: Applying knockback - Direction: {testDirection}, Force: {testForce}");
            
            rb.velocity = Vector3.zero;
            Vector3 knockbackVel = testDirection * testForce;
            knockbackVel.y = testForce * 0.4f;
            
            rb.velocity = knockbackVel;
            
            Debug.Log($"   Result velocity: {rb.velocity}");
        }
        
        [ContextMenu("Test Knockback (Backward)")]
        private void TestKnockbackBackward()
        {
            if (!pv.IsMine) return;
            
            Vector3 testDirection = -transform.forward;
            float testForce = 15f;
            
            Debug.Log($"🧪 TEST: Applying knockback - Direction: {testDirection}, Force: {testForce}");
            
            rb.velocity = Vector3.zero;
            Vector3 knockbackVel = testDirection * testForce;
            knockbackVel.y = testForce * 0.4f;
            
            rb.velocity = knockbackVel;
            
            Debug.Log($"   Result velocity: {rb.velocity}");
        }
        
        private void OnGUI()
        {
            if (!pv.IsMine) return;
            
            GUILayout.BeginArea(new Rect(Screen.width - 320, 10, 310, 200));
            GUILayout.Box("=== RIGIDBODY DEBUG ===");
            
            if (rb != null)
            {
                GUILayout.Label($"Velocity: {rb.velocity}");
                GUILayout.Label($"Speed: {rb.velocity.magnitude:F2} m/s");
                GUILayout.Label($"IsKinematic: {rb.isKinematic}");
                GUILayout.Label($"Mass: {rb.mass}");
                
                if (GUILayout.Button("Test Knockback Forward"))
                {
                    TestKnockbackForward();
                }
                
                if (GUILayout.Button("Test Knockback Backward"))
                {
                    TestKnockbackBackward();
                }
            }
            else
            {
                GUILayout.Label("❌ NO RIGIDBODY!");
            }
            
            GUILayout.EndArea();
        }
    }
}