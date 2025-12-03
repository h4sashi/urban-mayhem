using UnityEngine;
using Photon.Pun;

namespace Hanzo.VFX
{
    /// <summary>
    /// Manages stun visual effects with network synchronization
    /// Handles particle systems, animations, and visual feedback for stunned players
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class StunVFXController : MonoBehaviour
    {
        [Header("VFX Prefabs")]
        [SerializeField] private GameObject stunVFXPrefab;
        [SerializeField] private GameObject recoveryVFXPrefab;
        
        [Header("Spawn Settings")]
        [SerializeField] private Transform stunVFXSpawnPoint;
        [SerializeField] private Vector3 stunVFXOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private Vector3 recoveryVFXOffset = new Vector3(0f, 0.5f, 0f);
        
        [Header("Visual Tinting")]
        [SerializeField] private Color stunTintColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        [SerializeField] private bool applyTinting = true;
        
        [Header("Settings")]
        [SerializeField] private bool autoDestroyRecoveryVFX = true;
        
        // Components
        private PhotonView photonView;
        private Renderer[] playerRenderers;
        private Color[] originalColors;
        
        // Active VFX tracking
        private GameObject activeStunVFX;
        private ParticleSystem[] stunParticleSystems;
        private bool isStunVFXActive = false;
        
        // Properties
        public bool IsStunVFXActive => isStunVFXActive;

        private AudioSource audioSource;
        public AudioClip stunFX;
        
        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            audioSource = GetComponent<AudioSource>();
            
            // Cache renderers and original colors for tinting
            CacheRendererColors();
        }
        
        private void CacheRendererColors()
        {
            playerRenderers = GetComponentsInChildren<Renderer>();
            originalColors = new Color[playerRenderers.Length];
            
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i].material.HasProperty("_BaseColor"))
                {
                    originalColors[i] = playerRenderers[i].material.GetColor("_BaseColor");
                }
                else if (playerRenderers[i].material.HasProperty("_Color"))
                {
                    originalColors[i] = playerRenderers[i].material.color;
                }
            }
        }
        
        /// <summary>
        /// Spawns stun VFX - called by PlayerStateController
        /// Automatically syncs across network if this is the owner
        /// </summary>
        public void ShowStunVFX()
        {
            if (isStunVFXActive)
            {
                Debug.LogWarning("Stun VFX already active!");
                return;
            }
            
            // Spawn locally
            SpawnStunVFXLocal();
            
            // Sync to network if we own this object
            if (photonView.IsMine)
            {
                photonView.RPC("RPC_ShowStunVFX", RpcTarget.OthersBuffered);
            }
        }
        
        /// <summary>
        /// Stops stun VFX - called by PlayerStateController
        /// </summary>
        public void HideStunVFX()
        {
            if (!isStunVFXActive)
            {
                return;
            }
            
            // Stop locally
            StopStunVFXLocal();
            
            // Sync to network if we own this object
            if (photonView.IsMine)
            {
                photonView.RPC("RPC_HideStunVFX", RpcTarget.OthersBuffered);
            }
        }
        
        /// <summary>
        /// Spawns recovery VFX - called by PlayerStateController
        /// </summary>
        public void ShowRecoveryVFX()
        {
            // Spawn locally
            SpawnRecoveryVFXLocal();
            
            // Sync to network if we own this object
            if (photonView.IsMine)
            {
                photonView.RPC("RPC_ShowRecoveryVFX", RpcTarget.OthersBuffered);
            }
        }
        
        /// <summary>
        /// Applies visual tinting to player
        /// </summary>
        public void ApplyStunTint()
        {
            if (!applyTinting) return;
            
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] == null) continue;
                
                if (playerRenderers[i].material.HasProperty("_BaseColor"))
                {
                    playerRenderers[i].material.SetColor("_BaseColor", stunTintColor);
                }
                else if (playerRenderers[i].material.HasProperty("_Color"))
                {
                    playerRenderers[i].material.color = stunTintColor;
                }
            }
        }
        
        /// <summary>
        /// Removes visual tinting from player
        /// </summary>
        public void RemoveStunTint()
        {
            if (!applyTinting) return;
            
            for (int i = 0; i < playerRenderers.Length; i++)
            {
                if (playerRenderers[i] == null) continue;
                
                if (playerRenderers[i].material.HasProperty("_BaseColor"))
                {
                    playerRenderers[i].material.SetColor("_BaseColor", originalColors[i]);
                }
                else if (playerRenderers[i].material.HasProperty("_Color"))
                {
                    playerRenderers[i].material.color = originalColors[i];
                }
            }
        }
        
        // ============================================
        // LOCAL VFX SPAWNING (called locally and via RPC)
        // ============================================
        
        private void SpawnStunVFXLocal()
        {
            if (stunVFXPrefab == null)
            {
                Debug.LogWarning("StunVFXController: No stun VFX prefab assigned!");
                return;
            }
            
            // Calculate spawn position
            Vector3 spawnPos = stunVFXSpawnPoint != null 
                ? stunVFXSpawnPoint.position 
                : transform.position + stunVFXOffset;
            
            // Instantiate VFX
            activeStunVFX = Instantiate(stunVFXPrefab, spawnPos, Quaternion.identity);
            activeStunVFX.transform.SetParent(transform, true);
            
            // Get all particle systems
            stunParticleSystems = activeStunVFX.GetComponentsInChildren<ParticleSystem>();
            
            // Play all particle systems
            foreach (var ps in stunParticleSystems)
            {
                if (ps != null)
                {
                    ps.Play();
                }
            }
            
            isStunVFXActive = true;
            
            Debug.Log($"[StunVFX] Stun VFX spawned at {spawnPos}");
        }
        
        private void StopStunVFXLocal()
        {
            if (activeStunVFX == null) return;
            
            // Stop emitting but let existing particles finish
            if (stunParticleSystems != null)
            {
                foreach (var ps in stunParticleSystems)
                {
                    if (ps != null)
                    {
                        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
            
            // Destroy after particles finish
            Destroy(activeStunVFX, 2f);
            activeStunVFX = null;
            stunParticleSystems = null;
            isStunVFXActive = false;
            
            Debug.Log("[StunVFX] Stun VFX stopped");
        }
        
        private void SpawnRecoveryVFXLocal()
        {
            if (recoveryVFXPrefab == null)
            {
                Debug.LogWarning("StunVFXController: No recovery VFX prefab assigned!");
                return;
            }
            
            Vector3 spawnPos = transform.position + recoveryVFXOffset;
            
            GameObject recoveryVFX = Instantiate(recoveryVFXPrefab, spawnPos, Quaternion.identity);
            recoveryVFX.transform.SetParent(transform, true);
            
            // Play all particle systems
            ParticleSystem[] particleSystems = recoveryVFX.GetComponentsInChildren<ParticleSystem>();
            float maxDuration = 0f;
            
            foreach (var ps in particleSystems)
            {
                if (ps != null)
                {
                    ps.Play();
                    float duration = ps.main.duration + ps.main.startLifetime.constantMax;
                    if (duration > maxDuration) maxDuration = duration;
                }
            }
            
            // Auto-destroy if enabled
            if (autoDestroyRecoveryVFX)
            {
                Destroy(recoveryVFX, maxDuration + 0.5f);
            }
            
            Debug.Log($"[StunVFX] Recovery VFX spawned at {spawnPos}");
        }
        
        // ============================================
        // PHOTON RPCs - Network Synchronization
        // ============================================
        
        [PunRPC]
        private void RPC_ShowStunVFX()
        {
            SpawnStunVFXLocal();
        }
        
        [PunRPC]
        private void RPC_HideStunVFX()
        {
            StopStunVFXLocal();
        }
        
        [PunRPC]
        private void RPC_ShowRecoveryVFX()
        {
            SpawnRecoveryVFXLocal();
        }
        
        // ============================================
        // CLEANUP
        // ============================================
        
        private void OnDestroy()
        {
            // Clean up any active VFX
            if (activeStunVFX != null)
            {
                Destroy(activeStunVFX);
            }
        }
    }
}