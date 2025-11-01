using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

namespace Hanzo.DebugTools
{
    /// <summary>
    /// Diagnostic tool to help identify and fix layer mask issues
    /// Attach to any GameObject to see layer information in play mode
    /// UPDATED: Works with New Input System
    /// </summary>
    public class LayerMaskDiagnosticTool : MonoBehaviour
    {
        [Header("Layer Mask Testing")]
        [SerializeField] private LayerMask testLayerMask;
        
        [Header("Scan Settings")]
        [SerializeField] private float scanRadius = 5f;
        [SerializeField] private bool scanForPlayers = true;
        [SerializeField] private bool scanForDestructibles = true;
        [SerializeField] private bool autoScanOnStart = true;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private Color gizmoColor = Color.yellow;
        
        private void Start()
        {
            LogAllLayers();
            
            if (autoScanOnStart)
            {
                PerformLayerScan();
            }
            
            Debug.Log("=== LayerMaskDiagnosticTool ===");
            Debug.Log("Press 'L' key to perform layer scan");
            Debug.Log("Or use the context menu: Right-click component → Perform Layer Scan");
        }
        
        private void Update()
        {
            // New Input System - check for L key
            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            {
                PerformLayerScan();
            }
        }
        
        /// <summary>
        /// Logs all Unity layers and their indices
        /// </summary>
        private void LogAllLayers()
        {
            Debug.Log("========== UNITY LAYERS ==========");
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    int layerMask = 1 << i;
                    Debug.Log($"Layer {i}: '{layerName}' | Mask Value: {layerMask}");
                }
            }
            Debug.Log("==================================");
        }
        
        /// <summary>
        /// Scans area for objects and reports what layers they're on
        /// Can also be called from Inspector context menu
        /// </summary>
        [ContextMenu("Perform Layer Scan")]
        public void PerformLayerScan()
        {
            Debug.Log($"========== LAYER SCAN (Radius: {scanRadius}) ==========");
            Debug.Log($"Scanning from position: {transform.position}");
            
            Collider[] allColliders = Physics.OverlapSphere(transform.position, scanRadius);
            
            Debug.Log($"Found {allColliders.Length} total colliders");
            
            if (allColliders.Length == 0)
            {
                Debug.LogWarning("No colliders found! Try increasing scanRadius or moving closer to objects.");
                return;
            }
            
            System.Collections.Generic.Dictionary<int, int> layerCounts = 
                new System.Collections.Generic.Dictionary<int, int>();
            
            foreach (var col in allColliders)
            {
                int layer = col.gameObject.layer;
                
                if (!layerCounts.ContainsKey(layer))
                {
                    layerCounts[layer] = 0;
                }
                layerCounts[layer]++;
                
                // Log detailed info
                string layerName = LayerMask.LayerToName(layer);
                int layerMask = 1 << layer;
                
                // Check root object for PhotonView
                PhotonView pv = col.GetComponentInParent<PhotonView>();
                string pvInfo = pv != null ? $"PhotonView: {pv.ViewID}" : "No PhotonView";
                
                Debug.Log($"  → {col.name}: Layer={layer} ('{layerName}'), Mask={layerMask}, Tag='{col.tag}', {pvInfo}");
            }
            
            Debug.Log("--- Layer Summary ---");
            foreach (var kvp in layerCounts)
            {
                string layerName = LayerMask.LayerToName(kvp.Key);
                int layerMask = 1 << kvp.Key;
                Debug.Log($"Layer {kvp.Key} ('{layerName}'): {kvp.Value} objects | Mask Value: {layerMask}");
            }
            
            Debug.Log("=============================================");
        }
        
        /// <summary>
        /// Test specific layer mask against nearby objects
        /// </summary>
        [ContextMenu("Test Current Layer Mask")]
        public void TestCurrentLayerMask()
        {
            if (testLayerMask.value == 0)
            {
                Debug.LogWarning("Test Layer Mask is set to 'Nothing'. Configure it in Inspector first.");
                return;
            }
            
            Debug.Log($"========== TESTING LAYER MASK: {testLayerMask.value} ==========");
            
            // Show which layers this mask includes
            Debug.Log("This mask includes layers:");
            for (int i = 0; i < 32; i++)
            {
                if ((testLayerMask.value & (1 << i)) != 0)
                {
                    string layerName = LayerMask.LayerToName(i);
                    Debug.Log($"  • Layer {i}: '{layerName}'");
                }
            }
            
            // Test against nearby objects
            Collider[] allColliders = Physics.OverlapSphere(transform.position, scanRadius);
            Collider[] maskedColliders = Physics.OverlapSphere(transform.position, scanRadius, testLayerMask);
            
            Debug.Log($"Total objects in radius: {allColliders.Length}");
            Debug.Log($"Objects detected by mask: {maskedColliders.Length}");
            
            if (maskedColliders.Length == 0 && allColliders.Length > 0)
            {
                Debug.LogError("❌ Mask detected NOTHING! Check that target objects are on the correct layers.");
            }
            else if (maskedColliders.Length > 0)
            {
                Debug.Log("✅ Objects detected:");
                foreach (var col in maskedColliders)
                {
                    Debug.Log($"  → {col.name} (Layer: {col.gameObject.layer})");
                }
            }
            
            Debug.Log("=============================================");
        }
        
        private void OnDrawGizmos()
        {
            if (!showDebugInfo) return;
            
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, scanRadius);
            
            // Draw label
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2, 
                $"Layer Scan\nRadius: {scanRadius}\nPress L to scan");
            #endif
        }
        
        private void OnGUI()
        {
            if (!showDebugInfo) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 450, 350));
            GUILayout.Box("LAYER MASK DIAGNOSTIC TOOL");
            
            GUILayout.Space(10);
            GUILayout.Label("Press 'L' key to scan nearby objects");
            GUILayout.Label("Or right-click component → Perform Layer Scan");
            
            GUILayout.Space(10);
            GUILayout.Label("=== Test Layer Mask ===");
            GUILayout.Label($"Current Value: {testLayerMask.value}");
            
            // Show which layers are included
            GUILayout.Label("Includes layers:");
            bool hasLayers = false;
            for (int i = 0; i < 32; i++)
            {
                if ((testLayerMask.value & (1 << i)) != 0)
                {
                    string layerName = LayerMask.LayerToName(i);
                    GUILayout.Label($"  • Layer {i}: {layerName}");
                    hasLayers = true;
                }
            }
            if (!hasLayers)
            {
                GUILayout.Label("  (None - set to 'Nothing')");
            }
            
            GUILayout.Space(10);
            GUILayout.Label("=== GameObject Info ===");
            GUILayout.Label($"This object's layer: {gameObject.layer}");
            GUILayout.Label($"Layer name: {LayerMask.LayerToName(gameObject.layer)}");
            GUILayout.Label($"Layer mask: {1 << gameObject.layer}");
            
            GUILayout.Space(10);
            if (GUILayout.Button("Scan Now"))
            {
                PerformLayerScan();
            }
            
            if (testLayerMask.value != 0 && GUILayout.Button("Test Layer Mask"))
            {
                TestCurrentLayerMask();
            }
            
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Helper method to test if a layer is in a layer mask
        /// </summary>
        public static bool IsLayerInMask(int layer, LayerMask layerMask)
        {
            return (layerMask.value & (1 << layer)) != 0;
        }
        
        /// <summary>
        /// Convert layer index to layer mask
        /// </summary>
        public static int LayerToMask(int layer)
        {
            return 1 << layer;
        }
        
        /// <summary>
        /// Test if specific layer mask settings would detect an object
        /// </summary>
        public static void TestLayerMaskDetection(GameObject target, LayerMask mask)
        {
            int targetLayer = target.layer;
            bool wouldDetect = IsLayerInMask(targetLayer, mask);
            
            Debug.Log($"Testing: Would LayerMask {mask.value} detect {target.name}?");
            Debug.Log($"  Target Layer: {targetLayer} ({LayerMask.LayerToName(targetLayer)})");
            Debug.Log($"  Result: {(wouldDetect ? "✅ YES" : "❌ NO")}");
            
            if (!wouldDetect)
            {
                Debug.LogWarning($"  Fix: Set LayerMask to include layer {targetLayer}, " +
                    $"which has mask value {1 << targetLayer}");
            }
        }
    }
}