using System.Collections;
using UnityEngine;

namespace Hanzo.VFX
{
    [DisallowMultipleComponent]
    public class DashVFXController : MonoBehaviour
    {
        [Header("Prefab & Pooling")]
        [Tooltip("Prefab that contains the dash particle sub-systems (Dash_Streak, Dash_Burst, Dash_Sparks).")]
        [SerializeField] private GameObject dashVFXPrefab;
        [SerializeField] private int poolSize = 4;

        [Header("Playback")]
        [Tooltip("Local offset from the player root where the VFX should spawn.")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 1.0f, 0.2f);

        [Tooltip("If true, while Animator.DASH==true the controller will emit sparks repeatedly using Emit() so the trail feels continuous.")]
        [SerializeField] private bool emitWhileDashing = true;

        [Tooltip("How many sparks to emit per tick when emitting while dashing.")]
        [SerializeField] private int sparksEmitCount = 6;

        [Tooltip("Interval in seconds between continuous emits while dashing.")]
        [SerializeField] private float emitInterval = 0.06f;

        private GameObject[] pool;
        private int poolIndex = 0;
        private Animator animator;
        private int isDashingHash;
        private bool lastDashingState = false;
        private Coroutine emitCoroutine;

        void Awake()
        {
            if (dashVFXPrefab == null)
            {
                Debug.LogError("DashVFXController: dashVFXPrefab is not assigned!");
                return;
            }

            // Build pool
            pool = new GameObject[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(dashVFXPrefab, transform);
                go.SetActive(false);
                pool[i] = go;
            }

            // Try find animator on this root or children
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("DashVFXController: No Animator found on player or children. You can still Play() VFX manually.");
            }
            else
            {
                // Use same bool name as your DashAbility: "DASH"
                isDashingHash = Animator.StringToHash("DASH");
            }
        }

        void OnEnable()
        {
            // Reset last state so first frame detection is correct
            lastDashingState = false;
        }

        void Update()
        {
            if (animator == null) return;

            bool isDashing = animator.GetBool(isDashingHash);

            // Rising edge -> play one-shot VFX
            if (!lastDashingState && isDashing)
            {
                Play(localOffset);

                if (emitWhileDashing && emitCoroutine == null)
                {
                    // Start background coroutine to emit sparks repeatedly
                    emitCoroutine = StartCoroutine(EmitWhileDashing());
                }
            }

            // Falling edge -> stop continuous emission
            if (lastDashingState && !isDashing)
            {
                if (emitCoroutine != null)
                {
                    StopCoroutine(emitCoroutine);
                    emitCoroutine = null;
                }
            }

            lastDashingState = isDashing;
        }

        /// <summary>
        /// Play a dash VFX at the given local offset relative to this transform.
        /// </summary>
        public void Play(Vector3 localOffsetOverride)
        {
            if (pool == null || pool.Length == 0) return;

            var go = pool[poolIndex];
            poolIndex = (poolIndex + 1) % pool.Length;

            go.transform.localPosition = localOffsetOverride;
            // Align the effect forward to the player forward (so streak faces forward)
            go.transform.localRotation = Quaternion.identity;

            go.SetActive(true);

            // Restart any particle systems in the prefab
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            // Auto-disable when all systems finish
            StartCoroutine(DisableWhenDone(go, systems));
        }

        /// <summary>
        /// Manual Play with the configured localOffset.
        /// </summary>
        public void Play()
        {
            Play(localOffset);
        }

        private IEnumerator DisableWhenDone(GameObject go, ParticleSystem[] systems)
        {
            float maxLifetime = 0f;
            foreach (var ps in systems)
            {
                var m = ps.main;
                float startLifetime = 0f;
                if (m.startLifetime.constantMax > 0f)
                {
                    startLifetime = m.duration + m.startLifetime.constantMax;
                }
                else
                {
                    // fallback, use duration
                    startLifetime = m.duration;
                }

                if (startLifetime > maxLifetime) maxLifetime = startLifetime;
            }

            yield return new WaitForSeconds(maxLifetime + 0.05f);

            if (go != null) go.SetActive(false);
        }

        /// <summary>
        /// While dash bool is true, emit small sparks using Emit() from any child system named "Dash_Sparks".
        /// </summary>
        private IEnumerator EmitWhileDashing()
        {
            while (true)
            {
                // Find sparks system(s) on the currently used pool entry(s) and emit
                // We'll emit on all pool items so active one will get extra bursts
                for (int i = 0; i < pool.Length; i++)
                {
                    var go = pool[i];
                    if (!go.activeInHierarchy) continue;

                    var sparks = go.transform.Find("Dash_Sparks")?.GetComponent<ParticleSystem>();
                    if (sparks != null)
                    {
                        sparks.Emit(sparksEmitCount);
                    }
                    else
                    {
                        // fallback: emit on any small particle systems that are not the streak
                        var systems = go.GetComponentsInChildren<ParticleSystem>();
                        foreach (var ps in systems)
                        {
                            if (ps.main.startSize.constant <= 0.12f) // heuristic for small sparks
                            {
                                ps.Emit(sparksEmitCount / 2);
                            }
                        }
                    }
                }

                yield return new WaitForSeconds(emitInterval);
            }
        }

#if UNITY_EDITOR
        // Editor helper: show pool objects beneath controller for quick inspection
        private void OnValidate()
        {
            if (dashVFXPrefab == null) return;
            if (Application.isPlaying) return;
        }
#endif
    }
}
