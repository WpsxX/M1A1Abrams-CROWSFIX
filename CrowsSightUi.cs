using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using ModUtil;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace M1A1Abrams
{
    /// <summary>
    /// The CROWS sight overlay: laser range, belt count, arm / fire state, tracking status,
    /// magnification and the azimuth ring.
    ///
    /// Everything sits inside the mod's black frame (sight picture x 0.0875 .. 0.9120, y 0.0676 ..
    /// 0.9333) in the sight's own colour, rgba(255, 122, 158) at 75% opacity, with a red glow. One
    /// instance is built per optic; the two share a zoom index so day and thermal match.
    /// </summary>
    public class CrowsSightUi : MonoBehaviour
    {
        // Sight picture edges (screen fractions) left by the mod's black frame.
        private const float FRAME_LEFT = 0.098f;
        private const float RANGE_Y = 0.53f;
        private const float AMMO_Y = 0.5049f;   // clear of the range glyphs at this size

        private const float RANGE_SIZE = 28f;
        private const float AMMO_SIZE = 28f;
        private const float STATE_SIZE = 26f;
        private const float ZOOM_SIZE = 15f;

        private const float UI_ALPHA = 0.75f;
        private const float GLOW = 0.55f;

        /// <summary>The sight UI colour: rgba(255, 122, 158) at 75% opacity, with a red glow.</summary>
        private static readonly Color UI_COLOUR = new Color(255f / 255f, 122f / 255f, 158f / 255f, UI_ALPHA);
        private static readonly Color GLOW_COLOUR = new Color(1f, 0f, 0f, GLOW);

        private static readonly string[] ZOOM_LABELS = new string[] { "1X", "5X", "10X", "15X" };

        /// <summary>
        /// The field of view the reticle's size was set against, and deliberately not the widest one the
        /// sight has. The reticle is scaled by its magnification, so what it looks like is decided by
        /// this number alone - 30/2.5 here is the twelve-times proportion the reticle was drawn to.
        /// Following the sight's own top step (15X, 2 degrees) instead made every step a quarter larger
        /// than the sight had been showing.
        /// </summary>
        private const float RETICLE_REFERENCE_FOV = 2.5f;

        // Azimuth ring, to the user's reference artwork: twelve graduations reaching 15% inwards, ring
        // and graduations as thick as the needle (3.8% of the radius), needle stopping 7 reference
        // pixels short of them.
        private const float RING_RADIUS = 0.90f;    // of half the texture
        private const float RING_LINE = 0.038f;     // of the radius, same as the needle
        private const float TICK_LENGTH = 0.15f;    // of the radius
        private const float TICK_STEP_DEG = 30f;    // twelve graduations
        private const float NEEDLE_CLEARANCE = 7f;  // reference pixels between needle and graduations
        private const float NEEDLE_WIDTH = 0.038f;  // of the radius
        private const float RING_DIAMETER = 190f;   // on screen, in reference pixels
        private const float RING_TOP_GAP = 10f;     // reference pixels below the ammo line

        // Needle motion: a light low-pass (18/s, about 55 ms) plus a per-frame rate cap, so the
        // platform's LateUpdate steps cannot show as jumps.
        private const float NEEDLE_SMOOTH = 18f;
        private const float NEEDLE_MAX_RATE = 360f; // deg/s

        private FireControlSystem fcs;
        private WeaponSystem weapon;
        private CameraSlot slot;
        private CameraSlot other_slot;
        private UsableOptic optic;
        private Transform azimuth_source;

        /// <summary>
        /// The station's tracker, read-only from here. This overlay is built twice - once per optic -
        /// so anything decided here would be decided twice.
        /// </summary>
        private CrowsAutoTrack tracker;

        private readonly List<Graphic> tinted = new List<Graphic>();
        private readonly List<Material> owned_materials = new List<Material>();
        private Texture2D ring_texture;
        private Sprite needle_sprite;

        private TextMeshProUGUI range_text;
        private TextMeshProUGUI ammo_text;
        private GameObject armed_root;
        private TextMeshProUGUI fire_text;
        private TextMeshProUGUI track_text;
        private TextMeshProUGUI zoom_text;
        private RectTransform needle;
        private Canvas canvas;

        private float lase_timer;
        private int shown_range = int.MinValue;
        private int shown_belt = int.MinValue;
        private int shown_fire_state = -1;
        private int shown_zoom = -1;

        public static void Attach(UsableOptic optic, CameraSlot slot, CameraSlot other_slot,
            FireControlSystem fcs, WeaponSystem weapon, Transform azimuth_source, TMP_FontAsset font,
            CrowsAutoTrack tracker)
        {
            if (optic == null)
            {
                return;
            }

            GameObject root = new GameObject("CROWS sight UI");
            root.transform.SetParent(optic.transform, false);

            CrowsSightUi ui = root.AddComponent<CrowsSightUi>();
            ui.optic = optic;
            ui.slot = slot;
            ui.other_slot = other_slot;
            ui.fcs = fcs;
            ui.weapon = weapon;
            ui.azimuth_source = azimuth_source;
            ui.tracker = tracker;
            ui.Build(font);
        }

        private void Build(TMP_FontAsset font)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // above the mod's frame boxes

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            range_text = AddText(font, "range", "LRF ----", new Vector2(FRAME_LEFT, RANGE_Y), RANGE_SIZE, TextAlignmentOptions.Left);
            ammo_text = AddText(font, "ammo", "AMMO ---", new Vector2(FRAME_LEFT, AMMO_Y), AMMO_SIZE, TextAlignmentOptions.Left);

            // ARMED and the fire state share this left edge, with a left pivot to keep the text on screen.
            const float STATE_LEFT = 0.70f;

            TextMeshProUGUI armed = AddText(font, "armed", "ARMED", new Vector2(STATE_LEFT, 0.53f), STATE_SIZE, TextAlignmentOptions.Left);
            armed_root = armed.gameObject;

            fire_text = AddText(font, "fire", "Firing Enabled", new Vector2(STATE_LEFT, 0.32f), STATE_SIZE, TextAlignmentOptions.Left);

            // Tracking status: TRACK / TRACK LEAD, above ARMED and clear of the ring. Anything else -
            // no target, a broken line of sight, a station that is not in use - leaves this empty rather
            // than labelling itself.
            track_text = AddText(font, "track", "", new Vector2(STATE_LEFT, 0.60f), STATE_SIZE, TextAlignmentOptions.Left);
            track_text.gameObject.SetActive(false);

            // magnification: a fixed spot, so the readout does not travel with the zoom
            zoom_text = AddText(font, "zoom", ZOOM_LABELS[1], new Vector2(0.5f, 0.5f), ZOOM_SIZE, TextAlignmentOptions.Left);
            zoom_text.rectTransform.anchoredPosition = new Vector2(204f, -250f);

            BuildAzimuthRing();

            ApplyColour(UI_COLOUR);
        }

        private TextMeshProUGUI AddText(TMP_FontAsset font, string name, string value, Vector2 anchor, float size, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();

            if (font != null)
            {
                text.font = font;
            }

            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(460f, size * 1.6f);

            tinted.Add(text);
            return text;
        }

        /// <summary>
        /// The azimuth ring, under the ammo readout on the frame's left edge. Only the needle turns,
        /// with the CROWS's own azimuth, so the graduations stay put.
        /// </summary>
        private void BuildAzimuthRing()
        {
            const int size = 512;

            float radius = size * 0.5f * RING_RADIUS;
            float half_line = radius * RING_LINE * 0.5f;
            float step_rad = Mathf.PI / (180f / TICK_STEP_DEG);

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            ring_texture = texture;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.name = "CROWS azimuth ring";

            Color[] pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float from_ring = Mathf.Abs(distance - radius);

                    bool painted = from_ring <= half_line;

                    if (!painted)
                    {
                        float step = Mathf.Repeat(angle + Mathf.PI * 0.5f, step_rad);
                        float angular_offset = Mathf.Min(step, step_rad - step) * radius;

                        if (angular_offset <= half_line
                            && distance <= radius
                            && distance >= radius * (1f - TICK_LENGTH))
                        {
                            painted = true;
                        }
                    }

                    if (painted)
                    {
                        pixels[y * size + x] = Color.white;
                    }
                    else if (from_ring <= half_line * 2.1f)
                    {
                        // red halo, i.e. the glow: the graphic's own colour multiplies it, so a red
                        // tint here stays red
                        pixels[y * size + x] = new Color(1f, 0.12f, 0.22f, GLOW);
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);

            GameObject ring = new GameObject("azimuth ring");
            ring.transform.SetParent(transform, false);

            RawImage image = ring.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            tinted.Add(image);

            // the ring's top edge sits RING_TOP_GAP below the bottom of the ammo line
            float ammo_bottom = AMMO_Y - (AMMO_SIZE * 1.6f * 0.5f) / 1080f;
            float ring_top = ammo_bottom - RING_TOP_GAP / 1080f;
            float ring_centre_y = ring_top - (RING_DIAMETER * 0.5f) / 1080f;

            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(FRAME_LEFT + 0.05f, ring_centre_y);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(RING_DIAMETER, RING_DIAMETER);

            // needle: a bar pivoted at the ring centre, as thick as the ring
            GameObject needle_object = new GameObject("azimuth needle");
            needle_object.transform.SetParent(rect, false);

            Image needle_image = needle_object.AddComponent<Image>();
            needle_sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0f));
            needle_image.sprite = needle_sprite;
            needle_image.raycastTarget = false;
            tinted.Add(needle_image);

            float screen_radius = RING_DIAMETER * 0.5f * RING_RADIUS;
            float needle_length = Mathf.Max(4f, screen_radius * (1f - TICK_LENGTH) - NEEDLE_CLEARANCE);

            needle = needle_image.rectTransform;
            needle.anchorMin = new Vector2(0.5f, 0.5f);
            needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0f);
            needle.anchoredPosition = Vector2.zero;
            needle.sizeDelta = new Vector2(Mathf.Max(2f, screen_radius * NEEDLE_WIDTH), needle_length);
        }

        private void ApplyColour(Color colour)
        {
            foreach (Graphic graphic in tinted)
            {
                if (graphic == null)
                {
                    continue;
                }

                graphic.color = colour;

                TextMeshProUGUI text = graphic as TextMeshProUGUI;

                if (text != null)
                {
                    ApplyGlow(text);
                }
            }
        }

        /// <summary>Gives a text object the red glow (a per-text material instance).</summary>
        private void ApplyGlow(TMP_Text text)
        {
            if (text.fontSharedMaterial == null)
            {
                return;
            }

            Material material = null;
            try
            {
                Material shared = text.fontSharedMaterial;

                // Not every TextMeshPro shader carries the glow keywords (the game also ships bitmap
                // and mobile variants), so check before allocating a material instance.
                if (!shared.HasProperty("_GlowColor"))
                {
                    if (!glow_logged)
                    {
                        glow_logged = true;
                    }

                    return;
                }

                material = new Material(shared);
                material.EnableKeyword("GLOW_ON");
                material.SetColor("_GlowColor", GLOW_COLOUR);
                material.SetFloat("_GlowOffset", 0f);
                material.SetFloat("_GlowInner", 0.05f);
                material.SetFloat("_GlowOuter", GLOW);
                material.SetFloat("_GlowPower", GLOW);
                text.fontMaterial = material;
                owned_materials.Add(material);
                material = null; // ownership transferred to this overlay
            }
            catch (System.Exception)
            {
                // a shader without glow support simply keeps its normal look
            }
            finally
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }
        }

        private void OnDestroy()
        {
            // Optics are toggled during play; release owned assets only when this UI is destroyed.
            if (needle_sprite != null)
            {
                UnityEngine.Object.Destroy(needle_sprite);
                needle_sprite = null;
            }

            if (ring_texture != null)
            {
                UnityEngine.Object.Destroy(ring_texture);
                ring_texture = null;
            }

            foreach (Material material in owned_materials)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            owned_materials.Clear();
        }

        private static bool glow_logged;

        private void Update()
        {
            SyncZoom();
            UpdateReticleScale();
            UpdateRange();
            UpdateReadouts();
            UpdateZoomLabel();
            UpdateTrackStatus();
        }

        /// <summary>The tracker's state word. A readout only: neither instance can change the state.</summary>
        private void UpdateTrackStatus()
        {
            if (track_text == null)
            {
                return;
            }

            string status = tracker == null ? "" : tracker.StatusText;
            bool visible = !string.IsNullOrEmpty(status);

            if (track_text.gameObject.activeSelf != visible)
            {
                track_text.gameObject.SetActive(visible);
            }

            if (visible && track_text.text != status)
            {
                track_text.text = status;
            }
        }

        /// <summary>
        /// Driven from LateUpdate because that is where AimablePlatform moves the aim transform;
        /// sampling it from Update() read the previous frame and showed the platform's steps raw.
        /// </summary>
        private void LateUpdate()
        {
            UpdateNeedle();
        }

        /// <summary>Keeps both sights on one magnification; each camera slot has its own zoom index.</summary>
        private void SyncZoom()
        {
            if (slot == null || other_slot == null)
            {
                return;
            }

            CameraSlot active = CameraSlot.ActiveInstance;
            CameraSlot active_slot = active == slot ? slot : (active == other_slot ? other_slot : null);

            if (active_slot == null)
            {
                return;
            }

            CameraSlot follower = active_slot == slot ? other_slot : slot;

            if (follower.FovIndex != active_slot.FovIndex)
            {
                Reflect.SetProperty(follower, "FovIndex", active_slot.FovIndex);
            }
        }

        /// <summary>Rescales the reticle geometry so it looks the same size at every magnification.</summary>
        private void UpdateReticleScale()
        {
            if (slot == null)
            {
                return;
            }

            CROWS.ApplyReticleScale(slot.CurrentFov / RETICLE_REFERENCE_FOV);
        }

        /// <summary>
        /// Live range, and the solution that goes with it: the laser is fired on a timer so the sight
        /// keeps ranging without the player pressing E. The readout only shows a distance while the beam
        /// is returning, since DoLase() leaves the old range when it hits nothing.
        ///
        /// While the tracker holds a target that timer stands down: two lase timers fighting over the
        /// range swing the superelevation, and the gun and marker with it.
        /// </summary>
        private void UpdateRange()
        {
            if (range_text == null || fcs == null)
            {
                return;
            }

            float range = fcs.ReportedRange;

            if (tracker != null && tracker.OwnsRanging)
            {
                // the tracker ranges the lock itself
                lase_timer = 0f;
                ShowRange(range > 1f, range);
                return;
            }

            lase_timer -= Time.deltaTime;

            if (lase_timer <= 0f)
            {
                lase_timer = 0.5f;
                fcs.Lase();
                has_return = LaserHasReturn();
            }

            ShowRange(has_return && range > 1f, range);
        }

        private void ShowRange(bool valid, float range)
        {
            int metres = valid ? Mathf.RoundToInt(range) : -1;

            if (shown_range == metres)
            {
                return;
            }

            shown_range = metres;
            range_text.text = metres >= 0 ? "LRF " + metres + "m" : "LRF ----";
        }

        /// <summary>
        /// Whether the laser is returning, tested exactly as FireControlSystem.DoLase() does it: one
        /// ray for smoke on the visibility layer, one through the rangefinder layer mask.
        /// </summary>
        private bool LaserHasReturn()
        {
            Transform origin = fcs.LaserOrigin;

            if (origin == null || fcs.MaxLaserRange <= 0f)
            {
                return false;
            }

            Ray ray = new Ray(origin.position, origin.forward);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, fcs.MaxLaserRange, 1 << GHPC.Utility.CodeUtils.LAYER_INDEX_VISIBILITYONLY)
                && hit.collider.tag == "Smoke")
            {
                return true;
            }

            return GHPC.PhysicsHelpers.RaycastColliderUtils.Raycast(ray, out hit, fcs.MaxLaserRange,
                ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask);
        }

        private bool has_return;

        private void UpdateReadouts()
        {
            if (weapon != null && weapon.Feed != null)
            {
                int belt = weapon.Feed.CurrentClipRemainingCount;

                if (ammo_text != null && shown_belt != belt)
                {
                    shown_belt = belt;
                    ammo_text.text = "AMMO " + belt;
                }

                // Driven by the belt, not by AmmoTypeInBreech: the breech is empty for a moment on
                // every shot, which made the label blink through a burst.
                if (armed_root != null && armed_root.activeSelf != (belt > 0))
                {
                    armed_root.SetActive(belt > 0);
                }
            }

            if (fcs != null && fire_text != null)
            {
                int state = fcs.FiringBlocked ? 1 : 0;

                if (shown_fire_state != state)
                {
                    shown_fire_state = state;
                    fire_text.text = state != 0 ? "Firing Blocked" : "Firing Enabled";
                }
            }
        }

        private void UpdateZoomLabel()
        {
            if (zoom_text == null)
            {
                return;
            }

            int index = slot == null ? 0 : Mathf.Clamp(slot.FovIndex, 0, ZOOM_LABELS.Length - 1);
            bool visible = index > 0;

            if (zoom_text.gameObject.activeSelf != visible)
            {
                zoom_text.gameObject.SetActive(visible);
            }

            if (visible && shown_zoom != index)
            {
                shown_zoom = index;
                zoom_text.text = ZOOM_LABELS[index];
            }
        }

        /// <summary>
        /// Turns the needle with the CROWS's own azimuth: the angle between the vehicle's nose and the
        /// station's aim, in the hull's horizontal plane.
        ///
        /// SignedAngle rather than localEulerAngles, whose Y warps with elevation and flips at gimbal
        /// lock, and smoothed because the platform applies its rotation in LateUpdate.
        /// </summary>
        private void UpdateNeedle()
        {
            if (needle == null)
            {
                return;
            }

            Transform source = azimuth_source != null
                ? azimuth_source
                : (optic == null ? null : optic.transform.parent);

            if (source == null)
            {
                return;
            }

            Transform reference = HullTransform();
            Vector3 up = reference != null ? reference.up : Vector3.up;
            Vector3 from = Vector3.ProjectOnPlane(reference != null ? reference.forward : Vector3.forward, up);
            Vector3 to = Vector3.ProjectOnPlane(source.forward, up);

            // A steeply pitched aim projects to almost nothing, and its direction is then noise.
            if (from.sqrMagnitude < 0.0625f || to.sqrMagnitude < 0.0625f)
            {
                // nothing sensible to measure this frame, hold the last angle
                return;
            }

            // Positive is an aim to the right of the nose, which turns the needle clockwise on screen.
            float raw = Vector3.SignedAngle(from, to, up);

            if (float.IsNaN(shown_azimuth))
            {
                shown_azimuth = raw;
            }
            else
            {
                float step = Mathf.DeltaAngle(shown_azimuth, raw) * (1f - Mathf.Exp(-NEEDLE_SMOOTH * Time.deltaTime));
                float limit = NEEDLE_MAX_RATE * Time.deltaTime;
                shown_azimuth = Mathf.Repeat(shown_azimuth + Mathf.Clamp(step, -limit, limit) + 180f, 360f) - 180f;
            }

            needle.localEulerAngles = new Vector3(0f, 0f, -shown_azimuth);
        }

        private float shown_azimuth = float.NaN;

        private Transform hull;

        private Transform HullTransform()
        {
            if (hull == null && optic != null)
            {
                Vehicle vehicle = optic.GetComponentInParent<Vehicle>();

                if (vehicle != null)
                {
                    hull = vehicle.transform;
                }
            }

            return hull;
        }
    }
}
