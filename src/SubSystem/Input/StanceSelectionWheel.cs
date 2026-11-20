using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using StanceOverhaul.Enums;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static RealismCommonLib.Plugin;
using static StanceOverhaul.Plugin;

namespace StanceOverhaul.SubSystem.StanceInput
{
    // Hold the key, move the mouse towards a stance, let go to toggle it. Letting go in the centre cancels the current stance.
    // The cursor stays locked, so the pointer is tracked from raw mouse movement. While it's open SelectionWheelAxesPatch
    // stops the mouse turning the player and InputHookPipeline blocks firing.
    // See docs/implementation-plans/Stance-Selection-Wheel-Plan.md.
    public class StanceSelectionWheel : ISubSystem
    {
        private class Slice
        {
            // Fixed slot
            public readonly string Name;
            public readonly float SlotStart;    // degrees CCW from the right
            public readonly float SlotWidth;    // all slots add up to 360
            public readonly bool PairsWithNext; // first half of a quarter, the next slot is the other half
            public readonly Func<EWheelStance> Stance;

            // Set from config on each open
            public bool InUse;       // false when set to None, its neighbours take the space
            public float StartAngle; // includes its share of any empty neighbours
            public float Width;
            public float GapAfter;   // empty space before the next slice, before it's shared out
            public string Label = string.Empty;
            public EStanceType StanceType;
            public Action Toggle = () => { };

            public float CentreAngle => StartAngle + Width * 0.5f;

            public Image? Panel; // dark base
            public Image? Glow;  // shown when highlighted or active
            public TextMeshProUGUI? Text;

            public Slice(string name, float slotStart, float slotWidth, bool pairsWithNext, Func<EWheelStance> stance)
            {
                Name = name;
                SlotStart = slotStart;
                SlotWidth = slotWidth;
                PairsWithNext = pairsWithNext;
                Stance = stance;
            }
        }

        // Static so the patches never hit a destroyed instance
        public static bool IsOpen { get; private set; }

        // Pointer is in wheel radii, 0 = centre, 1 = edge
        private const float BasePointerSensitivity = 0.04f;
        private const float DeadZone = 0.3f;

        // Layout, pixels at 1080p
        private const float WheelDiameter = 420f;
        private const float InnerRadiusRatio = 0.5f;
        private const float LabelRadiusRatio = 0.75f;
        private const float GapDegrees = 1.6f; // divider between slices, visual only
        private const float PointerDotSize = 10f;
        private const float FontSize = 15f;
        private const int SliceTextureSize = 512;
        private const int DotTextureSize = 32;

        // Styled after EFT's gesture menu. Widths are fractions of the ring thickness, measured in from the nearest edge.
        private const float BaseFadeWidth = 0.35f;
        private const float BaseEdgeAlpha = 0.45f;  // fill alpha left at the edge
        private const float GlowFadeWidth = 0.4f;   // 0.5 = brightest at the label
        private const float GlowEdgeAlpha = 0.45f;
        private const float RimWidth = 0.04f;
        private const float OuterGlowWidth = 0.16f; // how far the glow reaches past the edges
        private const float ActiveGlowStrength = 0.4f; // current stance when it isn't highlighted

        // The ring is drawn smaller than its texture to leave room for the outer glow
        private const float OuterGlowReach = OuterGlowWidth * (1f - InnerRadiusRatio); // fraction of the wheel radius
        private const float RingScale = 1f / (1f + OuterGlowReach);
        private const float SliceImageSize = WheelDiameter / RingScale;

        // The radial fill only cuts out the slice plus some padding for its glow. Dark panels stop mid-gap so
        // neighbours don't overlap, the gold glow gets enough padding to spread fully over them.
        private const float BasePad = GapDegrees * 0.5f;
        private static readonly float GlowPad = Mathf.Min(45f, 1f + Mathf.Rad2Deg * OuterGlowReach / Mathf.Max(InnerRadiusRatio - OuterGlowReach, 0.01f));

        private static readonly Color BaseFillColor = new Color(0.055f, 0.055f, 0.05f, 0.88f);
        private static readonly Color BaseRimColor = new Color(0.62f, 0.62f, 0.58f, 0.15f);
        private static readonly Color GlowFillColor = new Color(0.34f, 0.3f, 0.14f, 0.8f);
        private static readonly Color GlowRimColor = new Color(1f, 0.88f, 0.5f, 0.35f);
        private static readonly Color TextColor = new Color(0.83f, 0.83f, 0.79f, 1f);
        private static readonly Color HighlightTextColor = new Color(1f, 0.93f, 0.66f, 1f);

        private readonly StanceInputListener _inputListener;

        // CCW from the right. Up and down are quarters, left and right are split into halves.
        // A slot set to None has no slice and its neighbours grow into it. If all six are None the wheel doesn't open.
        private readonly Slice[] _slices =
        {
            new Slice("Upper Right", 0f, 45f, false, () => PluginConfig.WheelUpperRightStance.Value),
            new Slice("Up", 45f, 90f, false, () => PluginConfig.WheelUpStance.Value),
            new Slice("Upper Left", 135f, 45f, true, () => PluginConfig.WheelUpperLeftStance.Value),
            new Slice("Lower Left", 180f, 45f, false, () => PluginConfig.WheelLowerLeftStance.Value),
            new Slice("Down", 225f, 90f, false, () => PluginConfig.WheelDownStance.Value),
            new Slice("Lower Right", 315f, 45f, true, () => PluginConfig.WheelLowerRightStance.Value),
        };

        private Vector2 _pointer;
        private Slice? _selected;
        private bool _keyWasHeld;

        private GameObject? _canvasObject;
        private RectTransform? _pointerDot;
        private Sprite? _dotSprite;

        // Keyed by slice width, slices with the same width share textures
        private readonly Dictionary<float, Sprite> _baseSprites = new Dictionary<float, Sprite>();
        private readonly Dictionary<float, Sprite> _glowSprites = new Dictionary<float, Sprite>();
        private readonly List<float> _unusedWidths = new List<float>();

        public StanceSelectionWheel(StanceInputListener inputListener)
        {
            _inputListener = inputListener;
        }

        // ================= Lifecycle =================

        // Built while the raid loads so the first open doesn't hitch. Only runs for the local player.
        public void RunOnAwake()
        {
            BuildCanvas();
            ApplyLayout();
        }

        public void RunOnDestroy()
        {
            Close(false);
            DestroyCanvas();
        }

        public void RunOnUpdate(float deltaTime)
        {
            KeyboardShortcut key = PluginConfig.StanceSelectorKeybind.Value;
            bool keyIsHeld = key.MainKey != KeyCode.None && Input.GetKey(key.MainKey) && key.Modifiers.All(Input.GetKey);

            // Same gate as the stance keys, no wheel on pistols for now
            bool canBeOpen = !_inputListener.StanceInputBlocked && !WeaponStateInstance.TreatAsPistol;

            if (!IsOpen)
            {
                // Fresh press only, so a force-closed wheel doesn't reopen while the key is still held
                if (keyIsHeld && !_keyWasHeld && canBeOpen)
                    Open();
            }
            else if (!canBeOpen)
            {
                Close(false);
            }
            else if (!keyIsHeld)
            {
                Close(true);
            }
            else
            {
                UpdatePointer();
                UpdateVisuals();
            }

            _keyWasHeld = keyIsHeld;
        }

        // ================= Open, close, select =================

        private void Open()
        {
            if (_canvasObject == null)
                BuildCanvas();

            // Every slot is set to None
            if (!ApplyLayout())
                return;

            _pointer = Vector2.zero;
            _selected = null;
            IsOpen = true;

            UpdateVisuals();
            _canvasObject!.SetActive(true);
        }

        // Runs on every open so config changes show up. Returns false if no slot is in use.
        // Textures are only made for new slice widths, and ones no longer used are dropped.
        private bool ApplyLayout()
        {
            int count = _slices.Length;
            bool anyInUse = false;

            foreach (Slice slice in _slices)
            {
                slice.InUse = slice.Stance() != EWheelStance.None;
                slice.StartAngle = slice.SlotStart;
                slice.Width = slice.InUse ? slice.SlotWidth : 0f;
                anyInUse |= slice.InUse;
            }

            // Empty slots go to their neighbours so the circle stays covered. Halves first: a lone half takes the whole quarter.
            for (int i = 0; i < count; i++)
            {
                Slice first = _slices[i];
                Slice second = _slices[(i + 1) % count];

                if (!first.PairsWithNext)
                    continue;

                if (first.InUse && !second.InUse)
                {
                    first.Width += second.SlotWidth;
                }
                else if (!first.InUse && second.InUse)
                {
                    second.StartAngle -= first.SlotWidth;
                    second.Width += first.SlotWidth;
                }
            }

            // Anything still empty is whole quarters, split between the slices either side. A single slice ends up with the
            // whole circle. Gaps are measured before any are shared out so one doesn't change the next.
            for (int i = 0; i < count; i++)
            {
                Slice slice = _slices[i];

                if (slice.InUse)
                    slice.GapAfter = Mathf.Repeat(NextInUse(i).StartAngle - (slice.StartAngle + slice.Width), 360f);
            }

            for (int i = 0; i < count; i++)
            {
                Slice slice = _slices[i];

                if (!slice.InUse)
                    continue;

                Slice next = NextInUse(i);
                float half = slice.GapAfter * 0.5f;

                slice.Width += half;
                next.StartAngle -= half;
                next.Width += half;
            }

            float radius = WheelDiameter * 0.5f;

            foreach (Slice slice in _slices)
            {
                if (slice.Panel != null) slice.Panel.gameObject.SetActive(slice.InUse);
                if (slice.Glow != null) slice.Glow.gameObject.SetActive(slice.InUse);
                if (slice.Text != null) slice.Text.gameObject.SetActive(slice.InUse);

                if (!slice.InUse)
                {
                    // So its textures can be dropped below
                    if (slice.Panel != null) slice.Panel.sprite = null;
                    if (slice.Glow != null) slice.Glow.sprite = null;
                    continue;
                }

                if (slice.Panel != null)
                    PlaceSliceImage(slice.Panel, slice, GetSliceSprite(slice.Width, false), BasePad);

                if (slice.Glow != null)
                    PlaceSliceImage(slice.Glow, slice, GetSliceSprite(slice.Width, true), GlowPad);

                if (slice.Text != null)
                {
                    float radians = slice.CentreAngle * Mathf.Deg2Rad;
                    slice.Text.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (radius * LabelRadiusRatio);
                }

                bool narrow = slice.Width < 90f; // one-line labels don't fit the 45° slices

                switch (slice.Stance())
                {
                    case EWheelStance.HighReady:
                        slice.Label = narrow ? "HIGH\nREADY" : "HIGH READY";
                        slice.StanceType = EStanceType.HighReady;
                        slice.Toggle = StanceInputEvents.RaiseToggleHighReady;
                        break;
                    case EWheelStance.LowReady:
                        slice.Label = narrow ? "LOW\nREADY" : "LOW READY";
                        slice.StanceType = EStanceType.LowReady;
                        slice.Toggle = StanceInputEvents.RaiseToggleLowReady;
                        break;
                    case EWheelStance.ActiveAim:
                        slice.Label = narrow ? "ACTIVE\nAIM" : "ACTIVE AIM";
                        slice.StanceType = EStanceType.ActiveAiming;
                        slice.Toggle = StanceInputEvents.RaiseToggleActiveAim;
                        break;
                    case EWheelStance.ShortStock:
                        slice.Label = narrow ? "SHORT\nSTOCK" : "SHORT STOCK";
                        slice.StanceType = EStanceType.ShortStock;
                        slice.Toggle = StanceInputEvents.RaiseToggleShortStock;
                        break;
                    case EWheelStance.LeftShoulder:
                        slice.Label = narrow ? "LEFT\nSHOULDER" : "LEFT SHOULDER";
                        slice.StanceType = EStanceType.LeftShoulder;
                        slice.Toggle = StanceInputEvents.RaiseToggleLeftShoulder;
                        break;
                    default:
                        slice.Label = "PATROL";
                        slice.StanceType = EStanceType.PatrolStance;
                        slice.Toggle = StanceInputEvents.RaiseTogglePatrolStance;
                        break;
                }

                if (slice.Text != null)
                    slice.Text.text = slice.Label;
            }

            DropUnusedSprites(_baseSprites);
            DropUnusedSprites(_glowSprites);

            return anyInUse;
        }

        // Next slice in use CCW, or itself if it's the only one
        private Slice NextInUse(int index)
        {
            for (int step = 1; step < _slices.Length; step++)
            {
                Slice slice = _slices[(index + step) % _slices.Length];

                if (slice.InUse)
                    return slice;
            }

            return _slices[index];
        }

        // keyReleased is false when force-closed (aiming, sprinting...), which leaves the stance alone
        private void Close(bool keyReleased)
        {
            if (!IsOpen)
                return;

            IsOpen = false;

            if (_canvasObject != null)
                _canvasObject.SetActive(false);

            Slice? selected = _selected;
            _selected = null;

            if (!keyReleased)
                return;

            // Released in the centre: cancel the current stance
            if (selected != null)
                selected.Toggle();
            else
                StanceInputEvents.RaiseToggleOffAllStances();
        }

        private void UpdatePointer()
        {
            // Raw axes so EFT's mouse invert doesn't flip the wheel
            Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

            _pointer += mouseDelta * (BasePointerSensitivity * PluginConfig.StanceSelectorSensitivity.Value);
            _pointer = Vector2.ClampMagnitude(_pointer, 1f); // so changing direction is quick

            if (_pointer.magnitude < DeadZone)
            {
                _selected = null;
                return;
            }

            float angle = Mathf.Atan2(_pointer.y, _pointer.x) * Mathf.Rad2Deg;

            // Slices cover the whole circle, so one always matches
            foreach (Slice slice in _slices)
            {
                if (slice.InUse && Mathf.Repeat(angle - slice.StartAngle, 360f) < slice.Width)
                {
                    _selected = slice;
                    return;
                }
            }
        }

        // ================= Drawing =================

        private void UpdateVisuals()
        {
            EStanceType currentStance = StanceControllerInstance.CurrentStanceType;

            foreach (Slice slice in _slices)
            {
                if (!slice.InUse)
                    continue;

                bool highlighted = slice == _selected;
                bool active = slice.StanceType == currentStance;

                // The glow texture has its own colours, the tint only sets how strong it is
                if (slice.Glow != null)
                {
                    slice.Glow.enabled = highlighted || active;
                    slice.Glow.color = new Color(1f, 1f, 1f, highlighted ? 1f : ActiveGlowStrength);
                }

                if (slice.Text != null)
                    slice.Text.color = highlighted ? HighlightTextColor : TextColor;
            }

            if (_pointerDot != null)
                _pointerDot.anchoredPosition = _pointer * (WheelDiameter * 0.5f * InnerRadiusRatio);
        }

        private void BuildCanvas()
        {
            _canvasObject = new GameObject("StanceSelectionWheel");
            _canvasObject.SetActive(false);

            Canvas canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            // Scale with screen height so the wheel keeps its size and shape at any resolution
            CanvasScaler scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            _dotSprite = CreateRingSprite(DotTextureSize, 0f);

            TMP_FontAsset? font = FindHudFont();

            // ApplyLayout sets their texture, angle and size
            foreach (Slice slice in _slices)
                slice.Panel = AddSliceImage(slice.Name);

            // Glows go after all the panels so a glow spilling onto a neighbour isn't covered by it
            foreach (Slice slice in _slices)
            {
                slice.Glow = AddSliceImage(slice.Name + " Glow");
                slice.Glow.enabled = false;
            }

            // Labels last so they draw on top
            foreach (Slice slice in _slices)
            {
                RectTransform textRect = CreateChild(slice.Name + " Label", _canvasObject.transform, 160f, 40f);

                TextMeshProUGUI text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
                if (font != null) text.font = font;
                text.text = slice.Label;
                text.fontSize = FontSize;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.raycastTarget = false;
                slice.Text = text;
            }

            _pointerDot = CreateChild("Pointer", _canvasObject.transform, PointerDotSize, PointerDotSize);
            Image dot = _pointerDot.gameObject.AddComponent<Image>();
            dot.sprite = _dotSprite;
            dot.color = TextColor;
            dot.raycastTarget = false;
        }

        private void DestroyCanvas()
        {
            if (_canvasObject != null)
                UnityEngine.Object.Destroy(_canvasObject);

            DestroySprite(_dotSprite);

            _canvasObject = null;
            _pointerDot = null;
            _dotSprite = null;

            foreach (Sprite sprite in _baseSprites.Values)
                DestroySprite(sprite);

            foreach (Sprite sprite in _glowSprites.Values)
                DestroySprite(sprite);

            _baseSprites.Clear();
            _glowSprites.Clear();

            foreach (Slice slice in _slices)
            {
                slice.Panel = null;
                slice.Glow = null;
                slice.Text = null;
            }
        }

        private Image AddSliceImage(string name)
        {
            RectTransform rect = CreateChild(name, _canvasObject!.transform, SliceImageSize, SliceImageSize);

            Image image = rect.gameObject.AddComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Right;
            image.fillClockwise = false;
            image.raycastTarget = false;
            return image;
        }

        // Fill and z rotation both run CCW from the right. In texture space the slice starts at the padding.
        private static void PlaceSliceImage(Image image, Slice slice, Sprite sprite, float padDegrees)
        {
            float arc = ArcOf(slice.Width);
            float pad = PadOf(slice.Width, padDegrees);

            image.sprite = sprite;
            image.fillAmount = Mathf.Clamp01((arc + pad * 2f) / 360f);
            image.rectTransform.localEulerAngles = new Vector3(0f, 0f, slice.StartAngle + (slice.Width - arc) * 0.5f - pad);
        }

        // The drawn part of a slice, the rest is the divider. A full-circle slice has no dividers.
        private static float ArcOf(float width)
        {
            return width >= 360f ? 360f : width - GapDegrees;
        }

        // Can't pad more than the circle that's left
        private static float PadOf(float width, float padDegrees)
        {
            return Mathf.Min(padDegrees, (360f - ArcOf(width)) * 0.5f);
        }

        private Sprite GetSliceSprite(float width, bool glow)
        {
            Dictionary<float, Sprite> cache = glow ? _glowSprites : _baseSprites;

            if (cache.TryGetValue(width, out Sprite sprite))
                return sprite;

            sprite = glow
                ? CreateSliceSprite(SliceTextureSize, ArcOf(width), PadOf(width, GlowPad), GlowFillColor, GlowRimColor, GlowFadeWidth, GlowEdgeAlpha)
                : CreateSliceSprite(SliceTextureSize, ArcOf(width), PadOf(width, BasePad), BaseFillColor, BaseRimColor, BaseFadeWidth, BaseEdgeAlpha);

            cache.Add(width, sprite);
            return sprite;
        }

        private void DropUnusedSprites(Dictionary<float, Sprite> cache)
        {
            _unusedWidths.Clear();

            foreach (float width in cache.Keys)
            {
                bool used = false;

                foreach (Slice slice in _slices)
                    used |= slice.InUse && slice.Width == width;

                if (!used)
                    _unusedWidths.Add(width);
            }

            foreach (float width in _unusedWidths)
            {
                DestroySprite(cache[width]);
                cache.Remove(width);
            }
        }

        private static void DestroySprite(Sprite? sprite)
        {
            if (sprite == null)
                return;

            UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }

        // Centred on the parent
        private static RectTransform CreateChild(string name, Transform parent, float width, float height)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)child.transform;

            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);

            return rect;
        }

        // White soft-edged ring, tinted through Image.color. An inner ratio of 0 gives a disc.
        private static Sprite CreateRingSprite(int size, float innerRadiusRatio)
        {
            const float edgeSoftness = 1.5f; // pixels

            float centre = (size - 1) * 0.5f;
            float outerRadius = size * 0.5f - 1f;
            float innerRadius = outerRadius * innerRadiusRatio;
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float alpha = Mathf.Clamp01((outerRadius - distance) / edgeSoftness);

                    if (innerRadius > 0f)
                        alpha *= Mathf.Clamp01((distance - innerRadius) / edgeSoftness);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // One slice with its colours baked in, the Image tint stays white. Everything comes from the signed distance to
        // the nearest edge: inside, the fill thins out towards the edges with a light rim on top, and outside the rim
        // fades out as a glow. The slice sits at padDegrees so the padding leaves room for the glow past the dividers.
        private static Sprite CreateSliceSprite(int size, float arcDegrees, float padDegrees, Color fill, Color rim, float fadeWidth, float edgeAlpha)
        {
            const float edgeSoftness = 1.5f; // pixels

            float centre = (size - 1) * 0.5f;
            float outerRadius = (size * 0.5f - 1f) * RingScale;
            float innerRadius = outerRadius * InnerRadiusRatio;
            float thickness = outerRadius - innerRadius;
            float fadePixels = Mathf.Max(fadeWidth * thickness, 0.001f);
            float rimPixels = Mathf.Max(RimWidth * thickness, 0.001f);
            float glowPixels = Mathf.Max(OuterGlowWidth * thickness, 0.001f);
            float halfArc = arcDegrees * 0.5f;
            float sliceCentre = padDegrees + halfArc;
            bool hasDividers = arcDegrees < 360f; // a full-circle slice is just a ring
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Measured along the arc so it's in pixels like the radial distances
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float toDividers = hasDividers
                        ? (halfArc - Mathf.Abs(Mathf.DeltaAngle(angle, sliceCentre))) * Mathf.Deg2Rad * distance
                        : float.MaxValue;
                    float signedToEdge = Mathf.Min(toDividers, Mathf.Min(distance - innerRadius, outerRadius - distance));
                    float inside = Mathf.Max(signedToEdge, 0f);
                    float outside = Mathf.Max(-signedToEdge, 0f);

                    // Inside: rim over fill
                    float fillAlpha = fill.a * Mathf.Lerp(edgeAlpha, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / fadePixels)));
                    float rimStrength = 1f - Mathf.Clamp01(inside / rimPixels);
                    float rimAlpha = rim.a * rimStrength * rimStrength;
                    float insideAlpha = rimAlpha + fillAlpha * (1f - rimAlpha);
                    Color insideColor = insideAlpha > 0f ? (rim * rimAlpha + fill * (fillAlpha * (1f - rimAlpha))) / insideAlpha : fill;

                    // Outside: starts at the rim's strength so the two join up
                    float glowStrength = 1f - Mathf.Clamp01(outside / glowPixels);
                    float outsideAlpha = rim.a * glowStrength * glowStrength;

                    // Alpha-weighted so neither colour bleeds into the other across the edge
                    float coverage = Mathf.Clamp01(signedToEdge / edgeSoftness + 0.5f);
                    float alpha = Mathf.Lerp(outsideAlpha, insideAlpha, coverage);
                    Color color = alpha > 0f
                        ? (rim * (outsideAlpha * (1f - coverage)) + insideColor * (insideAlpha * coverage)) / alpha
                        : rim;
                    color.a = alpha;

                    pixels[y * size + x] = color;
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // EFT's HUD font if it's loaded, otherwise TextMeshPro's default
        private static TMP_FontAsset? FindHudFont()
        {
            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font.name.IndexOf("bender", StringComparison.OrdinalIgnoreCase) >= 0)
                    return font;
            }

            return null;
        }
    }
}
