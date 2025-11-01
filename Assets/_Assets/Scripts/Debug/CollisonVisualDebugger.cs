using UnityEngine;
using System.Collections.Generic;

namespace Hanzo.DebugTools
{
    /// <summary>
    /// Visual debugging tool that draws lines and markers for collision detection
    /// Attach this to the same GameObject as DashCollisionHandler
    /// Shows detection sphere and all detected objects in real-time
    /// </summary>
    public class CollisionVisualDebugger : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool enableDebug = true;
        [SerializeField] private float markerDuration = 0.5f;
        
        [Header("Colors")]
        [SerializeField] private Color detectionSphereColor = new Color(0, 1, 1, 0.3f);
        [SerializeField] private Color playerDetectedColor = Color.yellow;
        [SerializeField] private Color destructibleDetectedColor = Color.green;
        [SerializeField] private Color hitMarkerColor = Color.red;
        
        [Header("Detection Settings (Copy from DashCollisionHandler)")]
        [SerializeField] private float detectionRadius = 1.5f;
        [SerializeField] private Vector3 detectionOffset = new Vector3(0, 0.5f, 0.5f);
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private LayerMask destructibleLayer;
        
        private struct HitMarker
        {
            public Vector3 position;
            public float timestamp;
            public string objectName;
            public bool isPlayer;
        }
        
        private List<HitMarker> recentHits = new List<HitMarker>();
        
        private void Update()
        {
            if (!enableDebug) return;
            
            // Clean up old markers
            recentHits.RemoveAll(m => Time.time - m.timestamp > markerDuration);
        }
        
        /// <summary>
        /// Call this from DashCollisionHandler when a hit occurs
        /// </summary>
        public void MarkHit(Vector3 position, string objectName, bool isPlayer)
        {
            recentHits.Add(new HitMarker
            {
                position = position,
                timestamp = Time.time,
                objectName = objectName,
                isPlayer = isPlayer
            });
            
            Debug.Log($"[CollisionVisualDebugger] ⭐ HIT MARKED: {objectName} at {position}");
        }
        
        private void OnDrawGizmos()
        {
            if (!enableDebug) return;
            
            Vector3 detectionPos = transform.position + transform.TransformDirection(detectionOffset);
            
            // Draw detection sphere
            Gizmos.color = detectionSphereColor;
            Gizmos.DrawWireSphere(detectionPos, detectionRadius);
            
            // Draw line from player to detection center
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, detectionPos);
            
            // Draw forward direction
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, transform.forward * 2f);
            
            // Draw detected objects (if in play mode)
            if (Application.isPlaying)
            {
                DrawDetectedObjects(detectionPos);
                DrawHitMarkers();
            }
        }
        
        private void DrawDetectedObjects(Vector3 detectionPos)
        {
            // Check for players
            Collider[] playerColliders = Physics.OverlapSphere(detectionPos, detectionRadius, playerLayer);
            foreach (var col in playerColliders)
            {
                if (col.transform.root != transform.root) // Don't draw self
                {
                    Gizmos.color = playerDetectedColor;
                    Gizmos.DrawLine(detectionPos, col.transform.position);
                    Gizmos.DrawWireSphere(col.transform.position, 0.5f);
                    
                    #if UNITY_EDITOR
                    UnityEditor.Handles.Label(col.transform.position + Vector3.up, 
                        $"PLAYER: {col.name}");
                    #endif
                }
            }
            
            // Check for destructibles
            Collider[] destructibleColliders = Physics.OverlapSphere(detectionPos, detectionRadius, destructibleLayer);
            foreach (var col in destructibleColliders)
            {
                Gizmos.color = destructibleDetectedColor;
                Gizmos.DrawLine(detectionPos, col.transform.position);
                Gizmos.DrawWireCube(col.transform.position, Vector3.one * 0.5f);
                
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(col.transform.position + Vector3.up, 
                    $"DESTRUCTIBLE: {col.name}\nTag: {col.tag}");
                #endif
            }
        }
        
        private void DrawHitMarkers()
        {
            foreach (var marker in recentHits)
            {
                float age = Time.time - marker.timestamp;
                float alpha = 1f - (age / markerDuration);
                
                Gizmos.color = new Color(hitMarkerColor.r, hitMarkerColor.g, hitMarkerColor.b, alpha);
                
                // Draw X marker
                float size = 0.5f;
                Gizmos.DrawLine(marker.position + new Vector3(-size, 0, -size), 
                               marker.position + new Vector3(size, 0, size));
                Gizmos.DrawLine(marker.position + new Vector3(-size, 0, size), 
                               marker.position + new Vector3(size, 0, -size));
                
                // Draw vertical line
                Gizmos.DrawLine(marker.position, marker.position + Vector3.up * 2f);
                
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(marker.position + Vector3.up * 2.5f, 
                    $"💥 HIT!\n{marker.objectName}\n{age:F1}s ago");
                #endif
            }
        }
    }
}