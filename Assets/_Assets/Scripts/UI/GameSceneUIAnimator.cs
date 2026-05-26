using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameSceneUIAnimator : MonoBehaviour
{
    [Header("Intro")]
    [SerializeField] private bool playIntroOnStart = true;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private float introStagger = 0.055f;
    [SerializeField] private float introDuration = 0.42f;
    [SerializeField] private float slideDistance = 44f;

    [Header("Buttons")]
    [SerializeField] private float buttonHoverScale = 1.06f;
    [SerializeField] private float buttonPressScale = 0.94f;
    [SerializeField] private float buttonSpeed = 14f;

    [Header("Tabs")]
    [SerializeField] private float tabSelectedScale = 1.08f;
    [SerializeField] private float tabLift = 5f;

    [Header("Ambient Motion")]
    [SerializeField] private float idleBobDistance = 3.5f;
    [SerializeField] private float idleBobSpeed = 1.1f;
    [SerializeField] private float glowPulseStrength = 0.12f;

    [Header("Panel Reveal")]
    [SerializeField] private float revealDuration = 0.28f;

    private readonly string[] introElementNames =
    {
        "TopContainer",
        "Leaderboard",
        "LeaderboardContainer",
        "LobbyButton",
        "HealthUI"
    };

    private readonly string[] revealElementNames =
    {
        "Leaderboard",
        "LeaderboardContainer",
        "RespawnPanel",
        "HUDCanvas",
        "MobileInputCanvas"
    };

    private readonly string[] ambientElementNames =
    {
        "coutntdown_ico",
        "RankIcon",
        "KillsIcon",
        "DeathsIcon",
        "ico"
    };

    private readonly string[] pulseGraphicNames =
    {
        "coutntdown_ico",
        "RankIcon",
        "KillsIcon",
        "DeathsIcon"
    };

    private readonly List<RectTransform> sceneRects = new List<RectTransform>();
    private readonly List<SelectableMotion> selectableMotions = new List<SelectableMotion>();
    private readonly List<AmbientMotion> ambientMotions = new List<AmbientMotion>();
    private readonly List<GraphicPulse> graphicPulses = new List<GraphicPulse>();
    private readonly List<RevealTarget> revealTargets = new List<RevealTarget>();
    private readonly Dictionary<RectTransform, Coroutine> revealRoutines = new Dictionary<RectTransform, Coroutine>();

    private void Awake()
    {
        BuildTargets();
    }

    private void Start()
    {
        if (playIntroOnStart)
        {
            StartCoroutine(PlayIntroSequence());
        }
    }

    private void Update()
    {
        float deltaTime = GetDeltaTime();
        float time = GetTime();

        UpdateSelectableMotion(deltaTime);
        UpdateAmbientMotion(time);
        UpdateGraphicPulses(time, deltaTime);
        WatchRevealTargets();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        RestoreRuntimeState();
    }

    private void OnValidate()
    {
        introStagger = Mathf.Max(0f, introStagger);
        introDuration = Mathf.Max(0.05f, introDuration);
        slideDistance = Mathf.Max(0f, slideDistance);
        buttonHoverScale = Mathf.Max(1f, buttonHoverScale);
        buttonPressScale = Mathf.Clamp(buttonPressScale, 0.75f, 1f);
        buttonSpeed = Mathf.Max(1f, buttonSpeed);
        tabSelectedScale = Mathf.Max(1f, tabSelectedScale);
        tabLift = Mathf.Max(0f, tabLift);
        idleBobDistance = Mathf.Max(0f, idleBobDistance);
        idleBobSpeed = Mathf.Max(0f, idleBobSpeed);
        glowPulseStrength = Mathf.Clamp(glowPulseStrength, 0f, 0.4f);
        revealDuration = Mathf.Max(0.05f, revealDuration);
    }

    private void BuildTargets()
    {
        CollectSceneRectTransforms();
        WireSelectableMotion();
        WireAmbientMotion();
        WireGraphicPulses();
        WireRevealTargets();
    }

    private void CollectSceneRectTransforms()
    {
        sceneRects.Clear();

        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        HashSet<RectTransform> uniqueRects = new HashSet<RectTransform>();

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !IsSceneObject(canvas.gameObject))
            {
                continue;
            }

            RectTransform[] rects = canvas.GetComponentsInChildren<RectTransform>(true);
            for (int rectIndex = 0; rectIndex < rects.Length; rectIndex++)
            {
                RectTransform rect = rects[rectIndex];
                if (rect != null && uniqueRects.Add(rect))
                {
                    sceneRects.Add(rect);
                }
            }
        }
    }

    private void WireSelectableMotion()
    {
        selectableMotions.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            Selectable selectable = rect.GetComponent<Selectable>();
            if (!ShouldAnimateSelectable(selectable))
            {
                continue;
            }

            SelectableMotion motion = new SelectableMotion
            {
                Rect = rect,
                Selectable = selectable,
                BasePosition = rect.anchoredPosition,
                BaseScale = rect.localScale,
                IsTab = IsTabLike(rect)
            };

            EventTrigger trigger = rect.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = rect.gameObject.AddComponent<EventTrigger>();
            }

            if (trigger.triggers == null)
            {
                trigger.triggers = new List<EventTrigger.Entry>();
            }

            AddTrigger(trigger, EventTriggerType.PointerEnter, delegate { motion.IsHovering = true; });
            AddTrigger(trigger, EventTriggerType.PointerExit, delegate
            {
                motion.IsHovering = false;
                motion.IsPressed = false;
            });
            AddTrigger(trigger, EventTriggerType.PointerDown, delegate { motion.IsPressed = true; });
            AddTrigger(trigger, EventTriggerType.PointerUp, delegate { motion.IsPressed = false; });

            selectableMotions.Add(motion);
        }
    }

    private void WireAmbientMotion()
    {
        ambientMotions.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!NameMatches(rect.name, ambientElementNames))
            {
                continue;
            }

            ambientMotions.Add(new AmbientMotion
            {
                Rect = rect,
                BasePosition = rect.anchoredPosition,
                BaseScale = rect.localScale,
                Phase = i * 0.71f
            });
        }
    }

    private void WireGraphicPulses()
    {
        graphicPulses.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!NameMatches(rect.name, pulseGraphicNames))
            {
                continue;
            }

            Graphic graphic = rect.GetComponent<Graphic>();
            if (graphic == null)
            {
                continue;
            }

            graphicPulses.Add(new GraphicPulse
            {
                Graphic = graphic,
                BaseColor = graphic.color,
                Phase = i * 0.49f
            });
        }
    }

    private void WireRevealTargets()
    {
        revealTargets.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!ShouldReveal(rect))
            {
                continue;
            }

            CanvasGroup canvasGroup = EnsureCanvasGroup(rect);
            revealTargets.Add(new RevealTarget
            {
                Rect = rect,
                CanvasGroup = canvasGroup,
                BasePosition = rect.anchoredPosition,
                TargetAlpha = canvasGroup.alpha <= 0f ? 1f : canvasGroup.alpha,
                WasActive = rect.gameObject.activeInHierarchy
            });
        }
    }

    private IEnumerator PlayIntroSequence()
    {
        Canvas.ForceUpdateCanvases();

        List<RectTransform> introTargets = new List<RectTransform>();
        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (rect.gameObject.activeInHierarchy && NameMatches(rect.name, introElementNames))
            {
                introTargets.Add(rect);
            }
        }

        for (int i = 0; i < introTargets.Count; i++)
        {
            StartCoroutine(AnimateEntrance(introTargets[i], GetIntroOffset(introTargets[i].name), introDuration));
            yield return Wait(introStagger);
        }
    }

    private IEnumerator AnimateEntrance(RectTransform rect, Vector2 offset, float duration)
    {
        if (rect == null)
        {
            yield break;
        }

        CanvasGroup canvasGroup = EnsureCanvasGroup(rect);
        Vector2 targetPosition = rect.anchoredPosition;
        float targetAlpha = canvasGroup.alpha <= 0f ? 1f : canvasGroup.alpha;
        bool blocksRaycasts = canvasGroup.blocksRaycasts;
        bool interactable = canvasGroup.interactable;

        rect.anchoredPosition = targetPosition + offset;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += GetDeltaTime();
            float eased = EaseOutCubic(elapsed / duration);
            rect.anchoredPosition = Vector2.LerpUnclamped(targetPosition + offset, targetPosition, eased);
            canvasGroup.alpha = Mathf.Lerp(0f, targetAlpha, eased);
            yield return null;
        }

        rect.anchoredPosition = targetPosition;
        canvasGroup.alpha = targetAlpha;
        canvasGroup.blocksRaycasts = blocksRaycasts;
        canvasGroup.interactable = interactable;
    }

    private IEnumerator AnimateReveal(RevealTarget target)
    {
        RectTransform rect = target.Rect;
        CanvasGroup canvasGroup = target.CanvasGroup;

        if (rect == null || canvasGroup == null)
        {
            yield break;
        }

        Vector2 startPosition = target.BasePosition + GetRevealOffset(rect.name);
        bool blocksRaycasts = canvasGroup.blocksRaycasts;
        bool interactable = canvasGroup.interactable;

        rect.anchoredPosition = startPosition;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        float elapsed = 0f;
        while (elapsed < revealDuration)
        {
            elapsed += GetDeltaTime();
            float eased = EaseOutCubic(elapsed / revealDuration);
            rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, target.BasePosition, eased);
            canvasGroup.alpha = Mathf.Lerp(0f, target.TargetAlpha, eased);
            yield return null;
        }

        rect.anchoredPosition = target.BasePosition;
        canvasGroup.alpha = target.TargetAlpha;
        canvasGroup.blocksRaycasts = blocksRaycasts;
        canvasGroup.interactable = interactable;
        revealRoutines.Remove(rect);
    }

    private void UpdateSelectableMotion(float deltaTime)
    {
        for (int i = 0; i < selectableMotions.Count; i++)
        {
            SelectableMotion motion = selectableMotions[i];
            if (motion.Rect == null || !motion.Rect.gameObject.activeInHierarchy)
            {
                continue;
            }

            float targetScale = motion.IsPressed ? buttonPressScale : motion.IsHovering ? buttonHoverScale : 1f;
            if (motion.IsTab && IsSelected(motion))
            {
                targetScale = Mathf.Max(targetScale, tabSelectedScale);
            }

            Vector3 scaleTarget = motion.BaseScale * targetScale;
            motion.Rect.localScale = Vector3.Lerp(motion.Rect.localScale, scaleTarget, 1f - Mathf.Exp(-buttonSpeed * deltaTime));

            if (motion.IsTab)
            {
                bool lifted = motion.IsHovering || IsSelected(motion);
                Vector2 positionTarget = motion.BasePosition + (lifted ? new Vector2(0f, tabLift) : Vector2.zero);
                motion.Rect.anchoredPosition = Vector2.Lerp(motion.Rect.anchoredPosition, positionTarget, 1f - Mathf.Exp(-buttonSpeed * deltaTime));
            }
        }
    }

    private void UpdateAmbientMotion(float time)
    {
        for (int i = 0; i < ambientMotions.Count; i++)
        {
            AmbientMotion motion = ambientMotions[i];
            if (motion.Rect == null || !motion.Rect.gameObject.activeInHierarchy)
            {
                continue;
            }

            float wave = Mathf.Sin((time * idleBobSpeed) + motion.Phase);
            float scaleWave = Mathf.Sin((time * idleBobSpeed * 0.8f) + motion.Phase + 0.65f);
            motion.Rect.anchoredPosition = motion.BasePosition + new Vector2(0f, wave * idleBobDistance);
            motion.Rect.localScale = motion.BaseScale * (1f + (scaleWave * 0.015f));
        }
    }

    private void UpdateGraphicPulses(float time, float deltaTime)
    {
        for (int i = 0; i < graphicPulses.Count; i++)
        {
            GraphicPulse pulseTarget = graphicPulses[i];
            if (pulseTarget.Graphic == null || !pulseTarget.Graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            float pulse = (Mathf.Sin((time * idleBobSpeed * 1.8f) + pulseTarget.Phase) + 1f) * 0.5f;
            Color targetColor = Color.Lerp(pulseTarget.BaseColor, Brighten(pulseTarget.BaseColor, glowPulseStrength), pulse);
            pulseTarget.Graphic.color = Color.Lerp(pulseTarget.Graphic.color, targetColor, 1f - Mathf.Exp(-8f * deltaTime));
        }
    }

    private void WatchRevealTargets()
    {
        for (int i = 0; i < revealTargets.Count; i++)
        {
            RevealTarget target = revealTargets[i];
            if (target.Rect == null)
            {
                continue;
            }

            bool isActive = target.Rect.gameObject.activeInHierarchy;
            if (isActive && !target.WasActive)
            {
                target.BasePosition = target.Rect.anchoredPosition;
                target.TargetAlpha = target.CanvasGroup.alpha <= 0f ? 1f : target.CanvasGroup.alpha;

                Coroutine runningRoutine;
                if (revealRoutines.TryGetValue(target.Rect, out runningRoutine) && runningRoutine != null)
                {
                    StopCoroutine(runningRoutine);
                }

                revealRoutines[target.Rect] = StartCoroutine(AnimateReveal(target));
            }

            target.WasActive = isActive;
        }
    }

    private void RestoreRuntimeState()
    {
        for (int i = 0; i < selectableMotions.Count; i++)
        {
            SelectableMotion motion = selectableMotions[i];
            if (motion.Rect != null)
            {
                motion.Rect.anchoredPosition = motion.BasePosition;
                motion.Rect.localScale = motion.BaseScale;
            }
        }

        for (int i = 0; i < ambientMotions.Count; i++)
        {
            AmbientMotion motion = ambientMotions[i];
            if (motion.Rect != null)
            {
                motion.Rect.anchoredPosition = motion.BasePosition;
                motion.Rect.localScale = motion.BaseScale;
            }
        }

        for (int i = 0; i < graphicPulses.Count; i++)
        {
            GraphicPulse pulse = graphicPulses[i];
            if (pulse.Graphic != null)
            {
                pulse.Graphic.color = pulse.BaseColor;
            }
        }

        for (int i = 0; i < revealTargets.Count; i++)
        {
            RevealTarget target = revealTargets[i];
            if (target.Rect != null)
            {
                target.Rect.anchoredPosition = target.BasePosition;
            }

            if (target.CanvasGroup != null)
            {
                target.CanvasGroup.alpha = target.TargetAlpha;
            }
        }
    }

    private bool ShouldAnimateSelectable(Selectable selectable)
    {
        if (selectable == null || selectable.GetComponent<Hanzo.UI.MobileActionButton>() != null)
        {
            return false;
        }

        if (!(selectable is Button) && !(selectable is Toggle))
        {
            return false;
        }

        return IsSceneObject(selectable.gameObject);
    }

    private bool ShouldReveal(RectTransform rect)
    {
        if (NameMatches(rect.name, revealElementNames))
        {
            return true;
        }

        return ContainsIgnoreCase(rect.name, "Panel") ||
               ContainsIgnoreCase(rect.name, "Tabs") ||
               ContainsIgnoreCase(rect.name, "TabBar");
    }

    private bool IsTabLike(RectTransform rect)
    {
        if (rect == null)
        {
            return false;
        }

        string rectName = rect.name;
        if (ContainsIgnoreCase(rectName, "Tab") ||
            ContainsIgnoreCase(rectName, "Toggle") ||
            ContainsIgnoreCase(rectName, "Category"))
        {
            return true;
        }

        Transform parent = rect.parent;
        return parent != null &&
               (ContainsIgnoreCase(parent.name, "Tabs") ||
                ContainsIgnoreCase(parent.name, "TabBar") ||
                ContainsIgnoreCase(parent.name, "Navigation"));
    }

    private bool IsSelected(SelectableMotion motion)
    {
        if (motion.Selectable == null)
        {
            return false;
        }

        Toggle toggle = motion.Selectable as Toggle;
        if (toggle != null)
        {
            return toggle.isOn;
        }

        EventSystem eventSystem = EventSystem.current;
        return eventSystem != null && eventSystem.currentSelectedGameObject == motion.Selectable.gameObject;
    }

    private bool IsSceneObject(GameObject target)
    {
        return target != null &&
               target.scene.IsValid() &&
               target.scene == gameObject.scene &&
               target.hideFlags == HideFlags.None;
    }

    private CanvasGroup EnsureCanvasGroup(RectTransform rect)
    {
        CanvasGroup canvasGroup = rect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = rect.gameObject.AddComponent<CanvasGroup>();
        }

        return canvasGroup;
    }

    private void AddTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = type
        };

        entry.callback.AddListener(delegate { action(); });
        trigger.triggers.Add(entry);
    }

    private Vector2 GetIntroOffset(string elementName)
    {
        if (ContainsIgnoreCase(elementName, "Top"))
        {
            return new Vector2(0f, slideDistance);
        }

        if (ContainsIgnoreCase(elementName, "LobbyButton"))
        {
            return new Vector2(0f, -slideDistance);
        }

        if (ContainsIgnoreCase(elementName, "Health"))
        {
            return new Vector2(slideDistance, 0f);
        }

        return new Vector2(0f, -slideDistance * 0.5f);
    }

    private Vector2 GetRevealOffset(string elementName)
    {
        if (ContainsIgnoreCase(elementName, "Respawn"))
        {
            return new Vector2(0f, slideDistance * 0.45f);
        }

        if (ContainsIgnoreCase(elementName, "MobileInput"))
        {
            return new Vector2(0f, -slideDistance);
        }

        return new Vector2(0f, -slideDistance * 0.65f);
    }

    private IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += GetDeltaTime();
            yield return null;
        }
    }

    private float GetDeltaTime()
    {
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    private float GetTime()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    private static bool NameMatches(string candidate, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(candidate, names[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string value, string match)
    {
        return value != null && value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static float EaseOutCubic(float value)
    {
        value = Mathf.Clamp01(value);
        float inverse = 1f - value;
        return 1f - (inverse * inverse * inverse);
    }

    private static Color Brighten(Color color, float amount)
    {
        return new Color(
            Mathf.Clamp01(color.r + amount),
            Mathf.Clamp01(color.g + amount),
            Mathf.Clamp01(color.b + amount),
            color.a);
    }

    private sealed class SelectableMotion
    {
        public RectTransform Rect;
        public Selectable Selectable;
        public Vector2 BasePosition;
        public Vector3 BaseScale;
        public bool IsHovering;
        public bool IsPressed;
        public bool IsTab;
    }

    private sealed class AmbientMotion
    {
        public RectTransform Rect;
        public Vector2 BasePosition;
        public Vector3 BaseScale;
        public float Phase;
    }

    private sealed class GraphicPulse
    {
        public Graphic Graphic;
        public Color BaseColor;
        public float Phase;
    }

    private sealed class RevealTarget
    {
        public RectTransform Rect;
        public CanvasGroup CanvasGroup;
        public Vector2 BasePosition;
        public float TargetAlpha;
        public bool WasActive;
    }
}
