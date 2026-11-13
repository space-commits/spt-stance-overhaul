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
    // Hold the key to open the wheel, move the mouse towards a stance, release the key to toggle it.
    //
    // The cursor stays locked. A virtual pointer is tracked from raw mouse movement, and the selected slice is
    // the one the pointer's direction falls in. The slices fill the whole circle. The centre hub selects
    // no slice. Releasing there cancels the stance that is on.
    //
    // While it is open, SelectionWheelAxesPatch stops the mouse turning the player and InputHookPipeline vetoes
    // fire. Selecting raises the same StanceInputEvents as the stance keys, so StanceInputHandler decides the rest.
    // See docs/implementation-plans/Stance-Selection-Wheel-Plan.md.
    public class StanceSelectionWheel : ISubSystem
    {
        private class Slice
        {
            public readonly string Label;
            public readonly float StartAngle; // degrees, counter-clockwise from the right: 0 = right, 90 = up
            public readonly float Width;      // degrees. All widths add up to 360
            public readonly EStanceType StanceType;
            public readonly Action Toggle;

            public float CentreAngle => StartAngle + Width * 0.5f;
            public string Name => Label.Replace('\n', ' ');

            public Image? Glow; // on top of the dark base, shown for highlight or active
            public TextMeshProUGUI? Text;

            public Slice(string label, float startAngle, float width, EStanceType stanceType, Action toggle)
            {
                Label = label;
                StartAngle = startAngle;
                Width = width;
                StanceType = stanceType;
                Toggle = toggle;
            }
        }

        // Read by SelectionWheelAxesPatch and InputHookPipeline. Static so they never touch a destroyed controller.
        public static bool IsOpen { get; private set; }

        // ---- Pointer, as a fraction of the wheel: 0 = centre, 1 = as far as it goes ----
        private const float BasePointerSensitivity = 0.04f; // per unit of Unity's "Mouse X" / "Mouse Y" axes
        private const float DeadZone = 0.3f;

        // ---- Layout, in pixels at 1080p ----
        private const float WheelDiameter = 420f;
        private const float InnerRadiusRatio = 0.5f;
        private const float LabelRadiusRatio = 0.75f;
        private const float GapDegrees = 1.6f; // divider line between slices, drawing only
        private const float PointerDotSize = 10f;
        private const float FontSize = 15f;
        private const int SliceTextureSize = 512; // one base and one glow per distinct slice width
        private const int DotTextureSize = 32;

        // ---- Look, modelled on EFT's gesture menu: near-black panels with a faint light rim, gold when selected ----
        // Fade and rim widths are fractions of the ring's thickness, measured in from the nearest slice edge.
        private const float BaseFadeWidth = 0.35f;   // solid in the middle, only this close to an edge does it thin out
        private const float BaseEdgeAlpha = 0.45f;  // fraction of the fill's alpha left at the very edge
        private const float GlowFadeWidth = 0.5f;   // 0.5 reaches the middle of the ring: brightest at the label
        private const float GlowEdgeAlpha = 0.45f;
        private const float RimWidth = 0.04f;
        private const float OuterGlowWidth = 0.16f; // how far the rim's fuzzy glow reaches past the slice's edges
        private const float ActiveGlowStrength = 0.4f; // the stance that is on, when not under the pointer

        // The ring is drawn smaller than its texture, and the image larger than the wheel by the same factor,
        // which leaves a margin around the ring for the outer glow.
        private const float OuterGlowReach = OuterGlowWidth * (1f - InnerRadiusRatio); // as a fraction of the wheel's radius
        private const float RingScale = 1f / (1f + OuterGlowReach);
        private const float SliceImageSize = WheelDiameter / RingScale;

        private static readonly Color BaseFillColor = new Color(0.055f, 0.055f, 0.05f, 0.92f);
        private static readonly Color BaseRimColor = new Color(0.62f, 0.62f, 0.58f, 0.15f);
        private static readonly Color GlowFillColor = new Color(0.34f, 0.3f, 0.14f, 0.9f);
        private static readonly Color GlowRimColor = new Color(1f, 0.88f, 0.5f, 0.35f);
        private static readonly Color TextColor = new Color(0.83f, 0.83f, 0.79f, 1f);
        private static readonly Color HighlightTextColor = new Color(1f, 0.93f, 0.66f, 1f);

        private readonly StanceInputListener _inputListener;

        // Counter-clockwise from the right. Three full quarters, and the right quarter split in two.
        private readonly Slice[] _slices =
        {
            new Slice("SHORT\nSTOCK", 0f, 45f, EStanceType.ShortStock, StanceInputEvents.RaiseToggleShortStock),
            new Slice("HIGH READY", 45f, 90f, EStanceType.HighReady, StanceInputEvents.RaiseToggleHighReady),
            new Slice("ACTIVE AIM", 135f, 90f, EStanceType.ActiveAiming, StanceInputEvents.RaiseToggleActiveAim),
            new Slice("LOW READY", 225f, 90f, EStanceType.LowReady, StanceInputEvents.RaiseToggleLowReady),
            new Slice("PATROL", 315f, 45f, EStanceType.PatrolStance, StanceInputEvents.RaiseTogglePatrolStance),
        };

        private Vector2 _pointer;
        private Slice? _selected;
        private bool _keyWasHeld;

        private GameObject? _canvasObject;
        private RectTransform? _pointerDot;
        private Sprite? _dotSprite;

        // Slices of the same width share their textures
        private readonly Dictionary<float, Sprite> _baseSprites = new Dictionary<float, Sprite>();
        private readonly Dictionary<float, Sprite> _glowSprites = new Dictionary<float, Sprite>();

        public StanceSelectionWheel(StanceInputListener inputListener)
        {
            _inputListener = inputListener;
        }

        // ================= Lifecycle =================

        public void RunOnAwake()
        {
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

            // Same gate as the stance keys, plus: no wheel on pistols for now
            bool canBeOpen = !_inputListener.StanceInputBlocked && !WeaponStateInstance.TreatAsPistol;

            if (!IsOpen)
            {
                // Only a fresh press opens it, so a wheel that was forced shut doesn't pop back up while the key is still held
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
            _pointer = Vector2.zero;
            _selected = null;
            IsOpen = true;

            if (_canvasObject == null)
                BuildCanvas();

            UpdateVisuals();
            _canvasObject!.SetActive(true);
        }

        // keyReleased = the player let go of the key. False when the wheel was forced shut (aiming, sprinting, ...),
        // which changes nothing.
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

            // Released in the centre hub: drop whatever stance is on, so it doesn't have to be picked again to cancel it
            if (selected != null)
                selected.Toggle();
            else
                StanceInputEvents.RaiseToggleOffAllStances();
        }

        private void UpdatePointer()
        {
            // Raw axes, so EFT's inverted mouse settings don't apply: mouse up is always up on the wheel
            Vector2 mouseDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

            _pointer += mouseDelta * (BasePointerSensitivity * PluginConfig.StanceSelectorSensitivity.Value);
            _pointer = Vector2.ClampMagnitude(_pointer, 1f); // so reversing direction is quick

            if (_pointer.magnitude < DeadZone)
            {
                _selected = null;
                return;
            }

            float angle = Mathf.Atan2(_pointer.y, _pointer.x) * Mathf.Rad2Deg;

            // The slices cover the whole circle, so one of them always contains the angle
            foreach (Slice slice in _slices)
            {
                if (Mathf.Repeat(angle - slice.StartAngle, 360f) < slice.Width)
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
                bool highlighted = slice == _selected;
                bool active = slice.StanceType == currentStance;

                // The glow texture carries its own colours, so the tint only sets its strength
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

            // Scales with screen height, so the wheel stays round and the same relative size at any resolution
            CanvasScaler scaler = _canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            _dotSprite = CreateRingSprite(DotTextureSize, 0f);

            TMP_FontAsset? font = FindHudFont();
            float radius = WheelDiameter * 0.5f;

            // The radial fill only cuts out the part of the texture a slice may draw in: the slice plus some padding
            // past each divider for its outer glow. The slice's own edges are drawn by the texture.
            //
            // Dark panels stop at the middle of the gap, so neighbours meet there without overlapping. The gold glow
            // gets enough padding to spread its full width over the neighbours.
            float basePad = GapDegrees * 0.5f;
            float glowPad = Mathf.Min(45f, 1f + Mathf.Rad2Deg * OuterGlowReach / Mathf.Max(InnerRadiusRatio - OuterGlowReach, 0.01f));

            foreach (Slice slice in _slices)
            {
                float arc = slice.Width - GapDegrees;

                if (!_baseSprites.TryGetValue(arc, out Sprite baseSprite))
                {
                    baseSprite = CreateSliceSprite(SliceTextureSize, arc, basePad, BaseFillColor, BaseRimColor, BaseFadeWidth, BaseEdgeAlpha);
                    _baseSprites.Add(arc, baseSprite);
                }

                AddSliceImage(slice.Name, slice, baseSprite, arc, basePad);
            }

            // Glows after every dark panel, so a glow that spills over a neighbour isn't covered by it
            foreach (Slice slice in _slices)
            {
                float arc = slice.Width - GapDegrees;

                if (!_glowSprites.TryGetValue(arc, out Sprite glowSprite))
                {
                    glowSprite = CreateSliceSprite(SliceTextureSize, arc, glowPad, GlowFillColor, GlowRimColor, GlowFadeWidth, GlowEdgeAlpha);
                    _glowSprites.Add(arc, glowSprite);
                }

                slice.Glow = AddSliceImage(slice.Name + " Glow", slice, glowSprite, arc, glowPad);
                slice.Glow.enabled = false;
            }

            // Labels after the slices, so they draw on top of them
            foreach (Slice slice in _slices)
            {
                float radians = slice.CentreAngle * Mathf.Deg2Rad;
                RectTransform textRect = CreateChild(slice.Name + " Label", _canvasObject.transform, 160f, 40f);
                textRect.anchoredPosition = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (radius * LabelRadiusRatio);

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
                slice.Glow = null;
                slice.Text = null;
            }
        }

        // Fill runs counter-clockwise from the right, and a positive z rotation is counter-clockwise too. In texture
        // space the fill starts at 0°, and the slice itself at padDegrees (see CreateSliceSprite).
        private Image AddSliceImage(string name, Slice slice, Sprite sprite, float arcDegrees, float padDegrees)
        {
            RectTransform rect = CreateChild(name, _canvasObject!.transform, SliceImageSize, SliceImageSize);
            rect.localEulerAngles = new Vector3(0f, 0f, slice.StartAngle + GapDegrees * 0.5f - padDegrees);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Right;
            image.fillClockwise = false;
            image.fillAmount = Mathf.Clamp01((arcDegrees + padDegrees * 2f) / 360f);
            image.raycastTarget = false;
            return image;
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

        // White ring with soft edges, tinted through Image.color. An inner ratio of 0 gives a disc.
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

        // One slice of the ring, in its own colours (the Image tint stays white).
        //
        // Everything is driven by the signed distance to the nearest of the slice's four edges (inner arc, outer arc,
        // both dividers), positive inside the slice:
        //   inside   the fill is solid in the middle and thins to edgeAlpha over the last fadeWidth, with a light rim
        //            drawn over it along the edges
        //   outside  the rim colour carries on as a glow that fades out over OuterGlowWidth
        // fadeWidth, RimWidth and OuterGlowWidth are fractions of the ring's thickness.
        //
        // The slice spans padDegrees to padDegrees + arcDegrees, counter-clockwise from the right. The radial fill
        // draws from 0° to arcDegrees + 2 x padDegrees, so the padding is how far past a divider the glow can show.
        // The ring takes up RingScale of the texture, the rest is the margin for the glow past the outer edge.
        private static Sprite CreateSliceSprite(int size, float arcDegrees, float padDegrees, Color fill, Color rim, float fadeWidth, float edgeAlpha)
        {
            const float edgeSoftness = 1.5f; // pixels, antialiasing of the slice's edges

            float centre = (size - 1) * 0.5f;
            float outerRadius = (size * 0.5f - 1f) * RingScale;
            float innerRadius = outerRadius * InnerRadiusRatio;
            float thickness = outerRadius - innerRadius;
            float fadePixels = Mathf.Max(fadeWidth * thickness, 0.001f);
            float rimPixels = Mathf.Max(RimWidth * thickness, 0.001f);
            float glowPixels = Mathf.Max(OuterGlowWidth * thickness, 0.001f);
            float halfArc = arcDegrees * 0.5f;
            float sliceCentre = padDegrees + halfArc;
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Distance to the dividers is measured along the arc, so it is in pixels like the radial ones
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float toDividers = (halfArc - Mathf.Abs(Mathf.DeltaAngle(angle, sliceCentre))) * Mathf.Deg2Rad * distance;
                    float signedToEdge = Mathf.Min(toDividers, Mathf.Min(distance - innerRadius, outerRadius - distance));
                    float inside = Mathf.Max(signedToEdge, 0f);
                    float outside = Mathf.Max(-signedToEdge, 0f);

                    // Inside: rim over fill
                    float fillAlpha = fill.a * Mathf.Lerp(edgeAlpha, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / fadePixels)));
                    float rimStrength = 1f - Mathf.Clamp01(inside / rimPixels);
                    float rimAlpha = rim.a * rimStrength * rimStrength;
                    float insideAlpha = rimAlpha + fillAlpha * (1f - rimAlpha);
                    Color insideColor = insideAlpha > 0f ? (rim * rimAlpha + fill * (fillAlpha * (1f - rimAlpha))) / insideAlpha : fill;

                    // Outside: the rim colour, starting at the rim's own strength so the two join up
                    float glowStrength = 1f - Mathf.Clamp01(outside / glowPixels);
                    float outsideAlpha = rim.a * glowStrength * glowStrength;

                    // Blend the two across the edge, weighted by alpha so neither colour bleeds into the other
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

        // EFT's HUD font, if it is loaded. Null leaves TextMeshPro's own default font in place.
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
