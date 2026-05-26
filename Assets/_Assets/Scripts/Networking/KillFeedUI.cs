using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

namespace Hanzo.Networking
{
    public class KillFeedUI : MonoBehaviour, IOnEventCallback
    {
        private const byte KillFeedEventCode = 73;
        private const string KillEventType = "Kill";
        private const string DamageEventType = "Damage";

        private static KillFeedUI instance;

        [Header("UI References")]
        [SerializeField]
        private TextMeshProUGUI killerText;

        [SerializeField]
        private TextMeshProUGUI victimText;

        [SerializeField]
        private CanvasGroup canvasGroup;

        [Header("Display")]
        [SerializeField]
        private float displayDuration = 2.6f;

        [SerializeField]
        private float fadeDuration = 0.18f;

        [SerializeField]
        private bool useUnscaledTime = true;

        private readonly Queue<KillFeedEntry> pendingEntries = new Queue<KillFeedEntry>();
        private Coroutine displayRoutine;

        private void Awake()
        {
            instance = this;
            BindReferences();
            HideImmediate();
        }

        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);

            if (displayRoutine != null)
            {
                StopCoroutine(displayRoutine);
                displayRoutine = null;
            }

            pendingEntries.Clear();

            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnValidate()
        {
            displayDuration = Mathf.Max(0.1f, displayDuration);
            fadeDuration = Mathf.Max(0f, fadeDuration);
        }

        public static void BroadcastKill(string killerName, string victimName)
        {
            BroadcastCombatEvent(KillEventType, killerName, victimName, string.Empty);
        }

        public static void BroadcastDamage(
            string sourceName,
            string victimName,
            float damageAmount,
            string damageType
        )
        {
            string amount = Mathf.CeilToInt(Mathf.Max(0f, damageAmount)).ToString();
            BroadcastCombatEvent(DamageEventType, sourceName, victimName, amount);
        }

        private static void BroadcastCombatEvent(
            string eventType,
            string sourceName,
            string victimName,
            string detail
        )
        {
            eventType = CleanName(eventType, DamageEventType);
            sourceName = CleanName(sourceName, "Unknown");
            victimName = CleanName(victimName, "Unknown");
            detail = CleanName(detail, string.Empty);

            if (PhotonNetwork.InRoom)
            {
                object[] payload = { eventType, sourceName, victimName, detail };
                bool sent = PhotonNetwork.RaiseEvent(
                    KillFeedEventCode,
                    payload,
                    new RaiseEventOptions { Receivers = ReceiverGroup.All },
                    SendOptions.SendReliable
                );

                if (sent)
                {
                    return;
                }
            }

            if (instance != null)
            {
                instance.EnqueueEntry(eventType, sourceName, victimName, detail);
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != KillFeedEventCode)
            {
                return;
            }

            if (
                !TryReadPayload(
                    photonEvent.CustomData,
                    out string eventType,
                    out string sourceName,
                    out string victimName,
                    out string detail
                )
            )
            {
                return;
            }

            EnqueueEntry(eventType, sourceName, victimName, detail);
        }

        private void BindReferences()
        {
            if (killerText == null)
            {
                killerText = FindChildText("KillerText");
            }

            if (victimText == null)
            {
                victimText = FindChildText("VictimText");
            }

            if (canvasGroup == null && !TryGetComponent(out canvasGroup))
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        private TextMeshProUGUI FindChildText(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        private void EnqueueEntry(
            string eventType,
            string sourceName,
            string victimName,
            string detail
        )
        {
            pendingEntries.Enqueue(
                new KillFeedEntry
                {
                    EventType = CleanName(eventType, DamageEventType),
                    SourceName = CleanName(sourceName, "Unknown"),
                    VictimName = CleanName(victimName, "Unknown"),
                    Detail = CleanName(detail, string.Empty),
                }
            );

            if (displayRoutine == null && isActiveAndEnabled)
            {
                displayRoutine = StartCoroutine(ProcessQueue());
            }
        }

        private IEnumerator ProcessQueue()
        {
            while (pendingEntries.Count > 0)
            {
                KillFeedEntry entry = pendingEntries.Dequeue();
                SetEntry(entry);

                yield return FadeTo(1f);
                yield return Wait(displayDuration);
                yield return FadeTo(0f);
            }

            displayRoutine = null;
        }

        private void SetEntry(KillFeedEntry entry)
        {
            if (killerText != null)
            {
                killerText.text = entry.SourceName;
            }

            if (victimText != null)
            {
                victimText.text =
                    entry.EventType == DamageEventType && !string.IsNullOrEmpty(entry.Detail)
                        ? $"{entry.VictimName} -{entry.Detail}"
                        : entry.VictimName;
            }
        }

        private IEnumerator FadeTo(float targetAlpha)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            if (fadeDuration <= 0f)
            {
                canvasGroup.alpha = targetAlpha;
                yield break;
            }

            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += GetDeltaTime();
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
        }

        private IEnumerator Wait(float seconds)
        {
            if (!useUnscaledTime)
            {
                yield return new WaitForSeconds(seconds);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void HideImmediate()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private float GetDeltaTime()
        {
            return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        private static bool TryReadPayload(
            object payload,
            out string eventType,
            out string sourceName,
            out string victimName,
            out string detail
        )
        {
            eventType = null;
            sourceName = null;
            victimName = null;
            detail = null;

            if (payload is object[] values && values.Length >= 2)
            {
                if (values.Length >= 4)
                {
                    eventType = values[0] as string;
                    sourceName = values[1] as string;
                    victimName = values[2] as string;
                    detail = values[3] as string;
                }
                else
                {
                    eventType = KillEventType;
                    sourceName = values[0] as string;
                    victimName = values[1] as string;
                    detail = string.Empty;
                }

                return !string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(victimName);
            }

            if (payload is string[] names && names.Length >= 2)
            {
                if (names.Length >= 4)
                {
                    eventType = names[0];
                    sourceName = names[1];
                    victimName = names[2];
                    detail = names[3];
                }
                else
                {
                    eventType = KillEventType;
                    sourceName = names[0];
                    victimName = names[1];
                    detail = string.Empty;
                }

                return !string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(victimName);
            }

            return false;
        }

        private static string CleanName(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            string cleaned = value.Replace("(Clone)", string.Empty).Trim();
            return string.IsNullOrEmpty(cleaned) ? fallback : cleaned;
        }

        private struct KillFeedEntry
        {
            public string EventType;
            public string SourceName;
            public string VictimName;
            public string Detail;
        }
    }
}
