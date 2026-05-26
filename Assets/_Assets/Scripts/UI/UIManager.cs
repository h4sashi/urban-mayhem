using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIManager : MonoBehaviour
{
    [Header("Intro")]
    [SerializeField] private bool playIntroOnStart = true;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private float introStagger = 0.055f;
    [SerializeField] private float introDuration = 0.42f;
    [SerializeField] private float slideDistance = 44f;

    [Header("Button Feel")]
    [SerializeField] private bool animateButtonHover = true;
    [SerializeField] private float buttonHoverScale = 1.06f;
    [SerializeField] private float buttonPressScale = 0.94f;
    [SerializeField] private float buttonSpeed = 14f;

    [Header("Ambient Motion")]
    [SerializeField] private bool animateAmbientMotion = true;
    [SerializeField] private bool animateColorPulses = true;
    [SerializeField] private float idleBobDistance = 3.5f;
    [SerializeField] private float idleBobSpeed = 1.1f;
    [SerializeField] private float glowPulseStrength = 0.12f;

    [Header("Panel Reveal")]
    [SerializeField] private float revealDuration = 0.28f;
    [SerializeField] private float revealPollInterval = 0.1f;

    private const float TransformEpsilon = 0.000001f;

    private readonly string[] introElementNames =
    {
        "MainBackground",
        "TopContainer",
        "UsernameProfile",
        "_leftContainer",
        "Left_SideContainer",
        "Btm_Container",
        "ChatContainer"
    };

    private readonly string[] revealPanelNames =
    {
        "ShopPanel",
        "UsernamePanel"
    };

    private readonly string[] ambientElementNames =
    {
        "profileImage",
        "currencyICO"
    };

    private readonly string[] progressPulseNames =
    {
        "_progress"
    };

    private readonly List<RectTransform> sceneRects = new List<RectTransform>();
    private readonly List<AnimatedButton> animatedButtons = new List<AnimatedButton>();
    private readonly List<AmbientElement> ambientElements = new List<AmbientElement>();
    private readonly List<ColorPulseElement> colorPulseElements = new List<ColorPulseElement>();
    private readonly List<PanelRevealTarget> panelRevealTargets = new List<PanelRevealTarget>();
    private readonly Dictionary<RectTransform, Coroutine> panelRevealRoutines = new Dictionary<RectTransform, Coroutine>();
    private float nextRevealPollTime;

    private void Awake()
    {
        BuildRuntimeAnimationTargets();
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

        if (animateButtonHover)
        {
            UpdateButtonMotion(deltaTime);
        }

        if (animateAmbientMotion)
        {
            UpdateAmbientMotion(time);
        }

        if (animateColorPulses)
        {
            UpdateColorPulses(time, deltaTime);
        }

        if (panelRevealTargets.Count > 0 && time >= nextRevealPollTime)
        {
            nextRevealPollTime = time + Mathf.Max(0.02f, revealPollInterval);
            WatchPanelRevealTargets();
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        RestoreRuntimeAnimationState();
    }

    private void OnValidate()
    {
        introStagger = Mathf.Max(0f, introStagger);
        introDuration = Mathf.Max(0.05f, introDuration);
        slideDistance = Mathf.Max(0f, slideDistance);
        buttonHoverScale = Mathf.Max(1f, buttonHoverScale);
        buttonPressScale = Mathf.Clamp(buttonPressScale, 0.75f, 1f);
        buttonSpeed = Mathf.Max(1f, buttonSpeed);
        idleBobDistance = Mathf.Max(0f, idleBobDistance);
        idleBobSpeed = Mathf.Max(0f, idleBobSpeed);
        glowPulseStrength = Mathf.Clamp(glowPulseStrength, 0f, 0.4f);
        revealDuration = Mathf.Max(0.05f, revealDuration);
        revealPollInterval = Mathf.Max(0.02f, revealPollInterval);
    }

    private void BuildRuntimeAnimationTargets()
    {
        CollectSceneRectTransforms();
        WireButtonMotion();
        WireAmbientMotion();
        WireProgressPulses();
        WirePanelRevealTargets();
    }

    private void CollectSceneRectTransforms()
    {
        sceneRects.Clear();

        HashSet<RectTransform> uniqueRects = new HashSet<RectTransform>();

        RectTransform rootRect = GetComponent<RectTransform>();
        if (rootRect != null && IsSceneObject(rootRect.gameObject))
        {
            AddRectTransforms(rootRect.GetComponentsInChildren<RectTransform>(true), uniqueRects);
            return;
        }

        Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !IsSceneObject(canvas.gameObject))
            {
                continue;
            }

            AddRectTransforms(canvas.GetComponentsInChildren<RectTransform>(true), uniqueRects);
        }

        if (sceneRects.Count > 0)
        {
            return;
        }

        Canvas parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null && IsSceneObject(parentCanvas.gameObject))
        {
            AddRectTransforms(parentCanvas.GetComponentsInChildren<RectTransform>(true), uniqueRects);
        }
    }

    private void AddRectTransforms(RectTransform[] rects, HashSet<RectTransform> uniqueRects)
    {
        for (int rectIndex = 0; rectIndex < rects.Length; rectIndex++)
        {
            RectTransform rect = rects[rectIndex];
            if (rect != null && uniqueRects.Add(rect))
            {
                sceneRects.Add(rect);
            }
        }
    }

    private void WireButtonMotion()
    {
        animatedButtons.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            Button button = rect.GetComponent<Button>();
            if (button == null || !ShouldAnimateButton(button))
            {
                continue;
            }

            AnimatedButton animatedButton = new AnimatedButton
            {
                Rect = rect,
                BaseScale = rect.localScale
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

            AddTrigger(trigger, EventTriggerType.PointerEnter, delegate { animatedButton.IsHovering = true; });
            AddTrigger(trigger, EventTriggerType.PointerExit, delegate
            {
                animatedButton.IsHovering = false;
                animatedButton.IsPressed = false;
            });
            AddTrigger(trigger, EventTriggerType.PointerDown, delegate { animatedButton.IsPressed = true; });
            AddTrigger(trigger, EventTriggerType.PointerUp, delegate { animatedButton.IsPressed = false; });

            animatedButtons.Add(animatedButton);
        }
    }

    private void WireAmbientMotion()
    {
        ambientElements.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!NameMatches(rect.name, ambientElementNames))
            {
                continue;
            }

            ambientElements.Add(new AmbientElement
            {
                Rect = rect,
                BasePosition = rect.anchoredPosition,
                BaseScale = rect.localScale,
                Phase = i * 0.73f
            });
        }
    }

    private void WireProgressPulses()
    {
        colorPulseElements.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!NameMatches(rect.name, progressPulseNames))
            {
                continue;
            }

            Graphic graphic = rect.GetComponent<Graphic>();
            if (graphic == null)
            {
                continue;
            }

            colorPulseElements.Add(new ColorPulseElement
            {
                Graphic = graphic,
                BaseColor = graphic.color,
                Phase = i * 0.51f
            });
        }
    }

    private void WirePanelRevealTargets()
    {
        panelRevealTargets.Clear();

        for (int i = 0; i < sceneRects.Count; i++)
        {
            RectTransform rect = sceneRects[i];
            if (!NameMatches(rect.name, revealPanelNames))
            {
                continue;
            }

            CanvasGroup canvasGroup = EnsureCanvasGroup(rect);
            panelRevealTargets.Add(new PanelRevealTarget
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

    private IEnumerator AnimatePanelReveal(PanelRevealTarget target)
    {
        RectTransform rect = target.Rect;
        CanvasGroup canvasGroup = target.CanvasGroup;

        if (rect == null || canvasGroup == null)
        {
            yield break;
        }

        Vector2 startPosition = target.BasePosition + GetPanelRevealOffset(rect.name);
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
        panelRevealRoutines.Remove(rect);
    }

    private void UpdateButtonMotion(float deltaTime)
    {
        for (int i = 0; i < animatedButtons.Count; i++)
        {
            AnimatedButton button = animatedButtons[i];
            if (button.Rect == null || !button.Rect.gameObject.activeInHierarchy)
            {
                continue;
            }

            float targetScale = button.IsPressed ? buttonPressScale : button.IsHovering ? buttonHoverScale : 1f;
            Vector3 target = button.BaseScale * targetScale;
            Vector3 current = button.Rect.localScale;
            if (Approximately(current, target))
            {
                continue;
            }

            Vector3 next = Vector3.Lerp(current, target, 1f - Mathf.Exp(-buttonSpeed * deltaTime));
            button.Rect.localScale = Approximately(next, target) ? target : next;
        }
    }

    private void UpdateAmbientMotion(float time)
    {
        for (int i = 0; i < ambientElements.Count; i++)
        {
            AmbientElement element = ambientElements[i];
            if (element.Rect == null || !element.Rect.gameObject.activeInHierarchy)
            {
                continue;
            }

            float wave = Mathf.Sin((time * idleBobSpeed) + element.Phase);
            float scaleWave = Mathf.Sin((time * idleBobSpeed * 0.8f) + element.Phase + 0.65f);
            element.Rect.anchoredPosition = element.BasePosition + new Vector2(0f, wave * idleBobDistance);
            element.Rect.localScale = element.BaseScale * (1f + (scaleWave * 0.015f));
        }
    }

    private void UpdateColorPulses(float time, float deltaTime)
    {
        for (int i = 0; i < colorPulseElements.Count; i++)
        {
            ColorPulseElement element = colorPulseElements[i];
            if (element.Graphic == null || !element.Graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            float pulse = (Mathf.Sin((time * idleBobSpeed * 1.8f) + element.Phase) + 1f) * 0.5f;
            Color targetColor = Color.Lerp(element.BaseColor, Brighten(element.BaseColor, glowPulseStrength), pulse);
            element.Graphic.color = Color.Lerp(element.Graphic.color, targetColor, 1f - Mathf.Exp(-8f * deltaTime));
        }
    }

    private void WatchPanelRevealTargets()
    {
        for (int i = 0; i < panelRevealTargets.Count; i++)
        {
            PanelRevealTarget target = panelRevealTargets[i];
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
                if (panelRevealRoutines.TryGetValue(target.Rect, out runningRoutine) && runningRoutine != null)
                {
                    StopCoroutine(runningRoutine);
                }

                panelRevealRoutines[target.Rect] = StartCoroutine(AnimatePanelReveal(target));
            }

            target.WasActive = isActive;
        }
    }

    private void RestoreRuntimeAnimationState()
    {
        for (int i = 0; i < animatedButtons.Count; i++)
        {
            AnimatedButton button = animatedButtons[i];
            if (button.Rect != null)
            {
                button.Rect.localScale = button.BaseScale;
            }
        }

        for (int i = 0; i < ambientElements.Count; i++)
        {
            AmbientElement element = ambientElements[i];
            if (element.Rect != null)
            {
                element.Rect.anchoredPosition = element.BasePosition;
                element.Rect.localScale = element.BaseScale;
            }
        }

        for (int i = 0; i < colorPulseElements.Count; i++)
        {
            ColorPulseElement element = colorPulseElements[i];
            if (element.Graphic != null)
            {
                element.Graphic.color = element.BaseColor;
            }
        }

        for (int i = 0; i < panelRevealTargets.Count; i++)
        {
            PanelRevealTarget target = panelRevealTargets[i];
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

    private bool ShouldAnimateButton(Button button)
    {
        if (button == null || button.GetComponent<Hanzo.UI.MobileActionButton>() != null)
        {
            return false;
        }

        string buttonName = button.name;
        if (ContainsIgnoreCase(buttonName, "DashButton") || ContainsIgnoreCase(buttonName, "SpeedButton"))
        {
            return false;
        }

        return IsSceneObject(button.gameObject);
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
        if (ContainsIgnoreCase(elementName, "Background"))
        {
            return Vector2.zero;
        }

        if (ContainsIgnoreCase(elementName, "Top") || ContainsIgnoreCase(elementName, "Username"))
        {
            return new Vector2(0f, slideDistance);
        }

        if (ContainsIgnoreCase(elementName, "Btm") || ContainsIgnoreCase(elementName, "Bottom"))
        {
            return new Vector2(0f, -slideDistance);
        }

        if (ContainsIgnoreCase(elementName, "Left") || ContainsIgnoreCase(elementName, "Chat"))
        {
            return new Vector2(-slideDistance, 0f);
        }

        return new Vector2(0f, -slideDistance * 0.5f);
    }

    private Vector2 GetPanelRevealOffset(string panelName)
    {
        if (ContainsIgnoreCase(panelName, "Shop"))
        {
            return new Vector2(0f, -slideDistance * 0.65f);
        }

        return new Vector2(0f, slideDistance * 0.45f);
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

    private static bool Approximately(Vector3 first, Vector3 second)
    {
        return (first - second).sqrMagnitude <= TransformEpsilon;
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

    private sealed class AnimatedButton
    {
        public RectTransform Rect;
        public Vector3 BaseScale;
        public bool IsHovering;
        public bool IsPressed;
    }

    private sealed class AmbientElement
    {
        public RectTransform Rect;
        public Vector2 BasePosition;
        public Vector3 BaseScale;
        public float Phase;
    }

    private sealed class ColorPulseElement
    {
        public Graphic Graphic;
        public Color BaseColor;
        public float Phase;
    }

    private sealed class PanelRevealTarget
    {
        public RectTransform Rect;
        public CanvasGroup CanvasGroup;
        public Vector2 BasePosition;
        public float TargetAlpha;
        public bool WasActive;
    }
}
