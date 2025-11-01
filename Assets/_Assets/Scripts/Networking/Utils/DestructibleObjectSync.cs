using UnityEngine;
using Photon.Pun;

namespace Hanzo.Networking.Utils
{
    /// <summary>
    /// Optional component for networked destructible objects
    /// Ensures knockback forces are synchronized across all clients
    /// Attach to destructible objects that need network synchronization
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    [RequireComponent(typeof(Rigidbody))]
    public class DestructibleObjectSync : MonoBehaviourPun
    {
        [Header("Settings")]
        [Tooltip("If true, only the owner can apply forces (prevents desync)")]
        [SerializeField] private bool ownerAuthority = true;

        [Header("Effects")]
        [SerializeField] private GameObject destroyVFXPrefab;
        [SerializeField] private AudioClip destroySound;

        [Header("Health (Optional)")]
        [SerializeField] private bool hasHealth = false;
        [SerializeField] private int maxHealth = 100;
        private int currentHealth;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private Rigidbody rb;
        private AudioSource audioSource;
        private bool isDestroyed = false;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            currentHealth = maxHealth;

            // Setup audio source
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }

        /// <summary>
        /// Apply knockback to this destructible object
        /// Called locally by DashCollisionHandler
        /// </summary>
        public void ApplyKnockback(Vector3 forceDirection, float forceMagnitude, int damageAmount = 0)
        {
            if (isDestroyed) return;

            // Apply force locally
            if (rb != null && (!ownerAuthority || photonView.IsMine))
            {
                rb.velocity = Vector3.zero;
                rb.AddForce(forceDirection * forceMagnitude, ForceMode.Impulse);

                Debug.Log($"[Destructible] Applied force: {forceMagnitude}");
            }

            // Apply damage if health system is enabled
            if (hasHealth && damageAmount > 0)
            {
                TakeDamage(damageAmount);
            }

            // Sync to network
            if (photonView.IsMine)
            {
                photonView.RPC("RPC_ApplyKnockback", RpcTarget.OthersBuffered,
                    forceDirection, forceMagnitude, damageAmount);
            }
        }

        [PunRPC]
        private void RPC_ApplyKnockback(Vector3 forceDirection, float forceMagnitude, int damageAmount)
        {
            if (isDestroyed) return;

            // Apply force on remote clients
            if (rb != null && !ownerAuthority)
            {
                rb.velocity = Vector3.zero;
                rb.AddForce(forceDirection * forceMagnitude, ForceMode.Impulse);
            }

            // Apply damage
            if (hasHealth && damageAmount > 0)
            {
                TakeDamage(damageAmount);
            }

            Debug.Log($"[Destructible Remote] Received knockback: {forceMagnitude}");
        }

        private void TakeDamage(int damage)
        {
            if (isDestroyed) return;

            currentHealth -= damage;

            Debug.Log($"[Destructible] Took {damage} damage. Health: {currentHealth}/{maxHealth}");

            if (currentHealth <= 0)
            {
                DestroyObject();
            }
        }

        private void DestroyObject()
        {
            if (isDestroyed) return;

            isDestroyed = true;

            // Spawn VFX
            if (destroyVFXPrefab != null)
            {
                GameObject vfx = Instantiate(destroyVFXPrefab, transform.position, Quaternion.identity);
                Destroy(vfx, 3f);
            }

            // Play sound
            if (destroySound != null)
            {
                AudioSource.PlayClipAtPoint(destroySound, transform.position);
            }

            // Destroy object across network
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }

            Debug.Log($"[Destructible] Object destroyed!");
        }

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            // Show health bar above object
            if (hasHealth && Camera.main != null)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f);

                if (screenPos.z > 0)
                {
                    float barWidth = 100f;
                    float barHeight = 10f;

                    Rect barRect = new Rect(
                        screenPos.x - barWidth / 2,
                        Screen.height - screenPos.y - barHeight / 2,
                        barWidth,
                        barHeight
                    );

                    // Background
                    GUI.color = Color.black;
                    GUI.DrawTexture(barRect, Texture2D.whiteTexture);

                    // Health bar
                    float healthPercent = (float)currentHealth / maxHealth;
                    barRect.width *= healthPercent;
                    GUI.color = Color.Lerp(Color.red, Color.green, healthPercent);
                    GUI.DrawTexture(barRect, Texture2D.whiteTexture);

                    GUI.color = Color.white;
                }
            }
        }

    }
     
}