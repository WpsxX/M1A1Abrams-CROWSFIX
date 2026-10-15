using System;
using System.Collections;
using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.World;
using ModUtil;
using UnityEngine;
using UnityEngine.UI;

namespace M1A1Abrams
{
    /// <summary>
    /// Automatic target tracking ("ATT") for the M153 CROWS II, driven through the games own fire
    /// control system.
    ///
    /// GHPC already does the hard part: the sight and the weapon axis are decoupled, the ballistic
    /// computer holds the flight-time table, and the lead is derived from the angular rate the aim is
    /// rotated at. So this module only pins the aim onto a locked target and feeds that rotation in
    /// through <c>AdjustAimByDegrees</c> - the only entry point that writes <c>_lastAngleAdjust</c>,
    /// and therefore the only one that produces any lead at all (<c>SetAimWorldPosition</c> would
    /// leave the lead at zero). Flight time, superelevation, the lead split and the traverse-rate
    /// buffer are all the games.
    ///
    /// One of these per CROWS station, on the fire controls object. The sight overlays
    /// (<see cref="CrowsSightUi"/>) exist twice, once per optic, and only read this class.
    /// </summary>
    public partial class CrowsAutoTrack : MonoBehaviour
    {
        // ------------------------------------------------------------------ sight overlays

        private void BuildTextures()
        {

            bar_sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }




        private void BuildMarkers()
        {
            // The colour the rest of the CROWS overlay uses, so the gate reads as part of
            // the sight rather than as a bolted-on HUD.
            Color tint = new Color(255f / 255f, 122f / 255f, 158f / 255f, 0.85f);

            foreach (UsableOptic optic in optics)
            {
                if (optic == null)
                {
                    continue;
                }

                SightMarker marker = new SightMarker();
                marker.optic = optic;

                GameObject canvas_go = new GameObject("CROWS ATT UI", typeof(RectTransform));
                canvas_go.transform.SetParent(optic.transform, false);

                Canvas canvas = canvas_go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SORTING_ORDER;   // above the frame boxes (10) and the readouts

                // Deliberately no CanvasScaler: the markers are positioned in raw screen pixels, which
                // is exactly what WorldToScreenPoint hands back.

                marker.canvas = canvas_go.GetComponent<RectTransform>();
                marker.gate = AddRect(canvas_go.transform, "gate");
                marker.gate_corners = new RectTransform[4];

                for (int corner = 0; corner < 4; corner++)
                {
                    marker.gate_corners[corner] = AddGateCorner(marker.gate, corner, tint);
                }

                marker.gate.gameObject.SetActive(false);

                markers.Add(marker);
            }
        }

        /// <summary>
        /// One corner of the gate: an L-shaped bracket. Four of these read as a target gate far better
        /// than four unbroken edges - they frame the target without hiding it, and they stay legible
        /// when the box is only a few pixels across.
        ///
        /// corner: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right.
        /// </summary>
        private RectTransform AddGateCorner(RectTransform gate, int corner, Color tint)
        {
            bool right = corner == 1 || corner == 3;
            bool top = corner < 2;

            float ax = right ? 1f : 0f;
            float ay = top ? 1f : 0f;

            GameObject go = new GameObject("corner " + corner, typeof(RectTransform));
            go.transform.SetParent(gate, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(ax, ay);
            rect.anchorMax = new Vector2(ax, ay);
            rect.pivot = new Vector2(ax, ay);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(GATE_BRACKET_MIN, GATE_BRACKET_MIN);

            AddBar(rect, "arm h", new Vector2(0f, ay), new Vector2(1f, ay), new Vector2(0.5f, ay), true, tint);
            AddBar(rect, "arm v", new Vector2(ax, 0f), new Vector2(ax, 1f), new Vector2(ax, 0.5f), false, tint);

            return rect;
        }

        /// <summary>
        /// A gate bar stretched across its corner block. The anchors are what make the bar follow the
        /// corner block as the gate grows and shrinks, so nothing here is rewritten per frame.
        /// </summary>
        private void AddBar(RectTransform parent, string name, Vector2 anchor_min, Vector2 anchor_max, Vector2 pivot, bool horizontal, Color tint)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image image = go.AddComponent<Image>();
            image.sprite = bar_sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            image.color = tint;

            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchor_min;
            rect.anchorMax = anchor_max;
            rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = horizontal
                ? new Vector2(0f, GATE_LINE_PIXELS)
                : new Vector2(GATE_LINE_PIXELS, 0f);
        }

        private static RectTransform AddRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }



        private void HideMarkers()
        {
            foreach (SightMarker marker in markers)
            {
                SetActive(marker.gate, false);

                // The canvas itself goes too: with no lock there must be nothing on screen at all,
                // whatever state an individual marker was left in.
                SetActive(marker.canvas, false);
            }
        }

        private static void SetActive(RectTransform rect, bool active)
        {
            if (rect != null && rect.gameObject.activeSelf != active)
            {
                rect.gameObject.SetActive(active);
            }
        }

        private void UpdateMarkers()
        {
            // Nothing is drawn while the station is not the one being used: the sight is not on screen,
            // and a station the player has switched away from must not be holding anything.
            if (!IsActiveWeapon())
            {
                HideMarkers();
                return;
            }

            // The gate is on screen whenever the sight is pointed at something - that is what it is for,
            // and it does not need a lock. A lock only adds the impact marker and the leader line,
            // because only a lock gives the fire control a target to work a solution for.
            Unit framed;
            Vector3 framed_centre;
            Vector3 framed_half;
            Vector3 anchor;

            if (target != null)
            {
                // A lost lock shows nothing at all: the gate means "this is being tracked", and a broken
                // line of sight means it is not.
                if (state != TrackState.Locked)
                {
                    HideMarkers();
                    return;
                }

                framed = target;
                framed_centre = box_centre_local;
                framed_half = box_half_local;

                // The gate is drawn on the target itself, and only its size comes from the measured box.
                // See GatePoint: the box marks the target, and locking moves the sight onto the target
                // rather than the box onto the crosshair.
                if (!GatePoint(out anchor))
                {
                    HideMarkers();
                    return;
                }
            }
            else if (designated != null && !designated.Destroyed)
            {
                framed = designated;
                framed_centre = designated_centre_local;
                framed_half = designated_half_local;
                anchor = framed.transform.TransformPoint(framed_centre);
            }
            else
            {
                HideMarkers();
                return;
            }

            Camera cam = CameraManager.MainCam;

            if (cam == null)
            {
                HideMarkers();
                return;
            }

            foreach (SightMarker marker in markers)
            {
                if (marker != null)
                {
                    SetActive(marker.canvas, true);
                }
            }

            BoxCorners(framed, framed_centre, framed_half);

            bool box_ok = true;

            for (int i = 0; i < 8; i++)
            {
                // Projected in the pictures own frame, not the main cameras: see PicturePoint. The two
                // are the same in the day sight and differ by the field of view in the thermal one.
                if (!PicturePoint(corners[i], out projected[i]))
                {
                    box_ok = false;
                    projected[i] = new Vector3(-Screen.width * 2f, -Screen.height * 2f, 0f);
                }

                // A corner close to the camera plane projects to a huge screen coordinate. Clamping
                // keeps one such corner from stretching the gate across the screen for a frame.
                projected[i].x = Mathf.Clamp(projected[i].x, -Screen.width * 2f, Screen.width * 3f);
                projected[i].y = Mathf.Clamp(projected[i].y, -Screen.height * 2f, Screen.height * 3f);
            }

            // The box belongs to the magnified sight. At 1x the picture is the whole field and a
            // bracket drawn over it marks nothing the player cannot already see, so it is dropped
            // there. Only the box: the lock and its readouts carry on at every magnification.
            if (box_ok && AtWidestZoom())
            {
                box_ok = false;
            }

            float min_x = float.MaxValue;
            float max_x = float.MinValue;
            float min_y = float.MaxValue;
            float max_y = float.MinValue;

            if (box_ok)
            {
                for (int i = 0; i < 8; i++)
                {
                    min_x = Mathf.Min(min_x, projected[i].x);
                    max_x = Mathf.Max(max_x, projected[i].x);
                    min_y = Mathf.Min(min_y, projected[i].y);
                    max_y = Mathf.Max(max_y, projected[i].y);
                }
            }

            // Where the gate goes: the target, projected with the same camera the corners were.
            Vector3 anchor_point;

            if (!PicturePoint(anchor, out anchor_point))
            {
                box_ok = false;
                anchor_point = Vector3.zero;
            }

            // The filter runs once per frame, not once per marker and not once per UpdateMarkers call:
            // this method is reached both from LateUpdate and from Camera.onPreCull, and the gate exists
            // twice (one per optic).
            if (Time.frameCount != smoothed_frame)
            {
                smoothed_frame = Time.frameCount;

                if (box_ok)
                {
                    // The gate is not allowed to swallow the sight picture: past that point the brackets
                    // would sit on the black frame and stop meaning anything.
                    // The position is filtered as lightly as the targets own stepping allows - a box drawn
                    // on a target has to sit *on* it, and every millisecond of filter is a lag the player
                    // watches the box trail by; the size keeps the ordinary rate, being a readout rather
                    // than a mark on the world.
                    Smooth(ref smoothed_gate_position,
                        new Vector2(anchor_point.x, anchor_point.y),
                        gate_state_valid,
                        Interpolated(framed) ? GATE_POSITION_SMOOTHING_INTERPOLATED : MARKER_SMOOTHING);

                    Smooth(ref smoothed_gate_size,
                        new Vector2(
                            Mathf.Clamp(max_x - min_x, GATE_MIN_PIXELS, Screen.width * GATE_MAX_SCREEN_FRACTION),
                            Mathf.Clamp(max_y - min_y, GATE_MIN_PIXELS, Screen.height * GATE_MAX_SCREEN_FRACTION)),
                        gate_state_valid);

                    gate_state_valid = true;
                }
            }

            foreach (SightMarker marker in markers)
            {
                if (marker == null)
                {
                    continue;
                }

                UpdateGate(marker, box_ok);
            }
        }


        /// <summary>
        /// Whether the sight in use is at its widest magnification, where the target box is not drawn.
        /// False when no sight can be identified, which leaves the box on rather than hiding it
        /// everywhere.
        /// </summary>
        private bool AtWidestZoom()
        {
            for (int i = 0; i < optics.Count; i++)
            {
                UsableOptic optic = optics[i];

                if (optic != null && optic.slot != null && optic.slot.IsActive)
                {
                    return optic.slot.FovIndex <= 0;
                }
            }

            return false;
        }



        private void UpdateGate(SightMarker marker, bool box_ok)
        {
            if (marker.gate == null)
            {
                return;
            }

            bool visible = box_ok;

            if (visible)
            {
                Vector2 local;

                if (ToCanvas(marker, smoothed_gate_position, out local))
                {
                    marker.gate.anchoredPosition = local;
                    marker.gate.sizeDelta = smoothed_gate_size;

                    float arm = Mathf.Clamp(
                        Mathf.Min(smoothed_gate_size.x, smoothed_gate_size.y) * GATE_BRACKET_FRACTION,
                        GATE_BRACKET_MIN,
                        GATE_BRACKET_MAX);

                    Vector2 arm_size = new Vector2(arm, arm);

                    if (marker.gate_corners != null)
                    {
                        for (int i = 0; i < marker.gate_corners.Length; i++)
                        {
                            if (marker.gate_corners[i] != null && marker.gate_corners[i].sizeDelta != arm_size)
                            {
                                marker.gate_corners[i].sizeDelta = arm_size;
                            }
                        }
                    }
                }
                else
                {
                    visible = false;
                }
            }

            SetActive(marker.gate, visible);
        }




        private static void Smooth(ref Vector2 state, Vector2 value, bool valid)
        {
            Smooth(ref state, value, valid, MARKER_SMOOTHING);
        }

        private static void Smooth(ref Vector2 state, Vector2 value, bool valid, float rate)
        {
            if (!valid)
            {
                state = value;
                return;
            }

            float k = 1f - Mathf.Exp(-rate * Time.deltaTime);
            state = Vector2.Lerp(state, value, k);
        }


        /// <summary>
        /// Screen point to canvas-local point. The canvas is a plain overlay canvas with no scaler, so
        /// this is a straight pixel offset from the screen centre - but the conversion is done through
        /// RectTransformUtility so a scaler added later cannot silently break it.
        /// </summary>
        private static bool ToCanvas(SightMarker marker, Vector2 screen_point, out Vector2 local)
        {
            local = new Vector2(screen_point.x - Screen.width * 0.5f, screen_point.y - Screen.height * 0.5f);

            if (marker.canvas == null)
            {
                return true;
            }

            Vector2 converted;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(marker.canvas, screen_point, null, out converted))
            {
                local = converted;
            }

            return true;
        }

        // ------------------------------------------------------------------ sight alignment (C2)

        /// <summary>
        /// The stations sights are left on their own alignment; CrowsAimSlave points them at the target.
        /// Aiming them at the fire controls impact point instead used to put the crosshair on the gun
        /// rather than the target, and tie the camera to the gun servo, so every twitch of the barrel -
        /// and every rewrite of the range - went into the picture.
        /// </summary>
        private void RestoreAlignment()
        {
            for (int i = 0; i < optics.Count; i++)
            {
                UsableOptic optic = optics[i];

                if (optic == null || i >= original_alignment.Count)
                {
                    continue;
                }

                if (optic.Alignment != original_alignment[i])
                {
                    optic.Alignment = original_alignment[i];
                }
            }
        }

        // ------------------------------------------------------------------ readouts

        /// <summary>The line the sight overlay draws in its status area. Empty when idle.</summary>
        public string StatusText
        {
            get
            {
                // Nothing to say unless something is being tracked by the sight in use. A broken line of
                // sight, a station that is not the one being used, and no target at all all empty the
                // line rather than labelling themselves - the box has already gone, and the readout
                // follows it. The lost state itself stays: it is what holds the aim through a brief
                // occlusion and what keeps the box hidden, it just has nothing to say on screen.
                if (target == null || !IsActiveWeapon() || state == TrackState.Lost)
                {
                    return "";
                }

                return HasLead() ? "TRACK  LEAD" : "TRACK";
            }
        }

        /// <summary>
        /// Whether the readout says a lead is dialled in, with hysteresis so it cannot flicker; see
        /// LEAD_SHOW_DEGREES.
        ///
        /// Measured as an angle at the target, not as a traverse rate, and this is the rewrite that
        /// matters: the rate route asked the fire control to report a number about its own internals -
        /// <c>GetLeadTraverseRate()</c> hands back <c>_averageTraverseRate</c>, which is zeroed while
        /// this module holds the lead suspended, is whatever the games own buffer last averaged if the
        /// Harmony patch is not installed, and knows nothing at all about the platforms half of the
        /// lead. Any one of those and the line reads TRACK with a lead dialled in. A lead *is* an angle
        /// - the bore sitting off the sight line to meet a moving target - so this asks the two
        /// questions that cannot go wrong:
        ///
        /// * what the solution needs (see MeasureLead, from the relative velocity and the flight time),
        ///   which is arithmetic this module does itself and always answers; and
        /// * what the computer has (the fire controls own lead offset, read off its solution).
        ///
        /// Either one above the threshold is a lead, so a moving target, a moving station, or both, all
        /// put LEAD on the line.
        /// </summary>
        private bool HasLead()
        {
            float lead = Mathf.Max(intended_lead_deg, applied_lead_deg);

            if (lead_shown)
            {
                if (lead < LEAD_HIDE_DEGREES)
                {
                    lead_shown = false;
                }
            }
            else if (lead > LEAD_SHOW_DEGREES)
            {
                lead_shown = true;
            }

            return lead_shown;
        }

        /// <summary>
        /// Whether the tracker is the one driving the rangefinder. While it is, the sight overlay must
        /// not run its own lase timer on top (see CrowsSightUi.UpdateRange).
        /// </summary>
        public bool OwnsRanging
        {
            get { return armed && target != null; }
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnDestroy()
        {
            if (weapon != null) weapon.Fired -= OnRoundFired;
            // A static camera callback is exactly the kind of thing that outlives a mission and then
            // calls into a destroyed tracker, so it is unhooked before anything else. The held aim input
            // goes with it, or the next mission would start with a fire control that ignores the player.
            ReleaseAimLock();
            // Defensive even if aim_held was cleared by an interrupted disable/reload path.
            CrowsAimLock.Unregister(fcs);
            CrowsLeadLock.Drop(this);
            CrowsShotFeedback.Drop(this);
            Camera.onPreCull -= HandlePreCull;

            DestroySprite(ref bar_sprite);

            for (int i = 0; i < markers.Count; i++)
            {
                SightMarker marker = markers[i];
                if (marker != null && marker.canvas != null)
                {
                    UnityEngine.Object.Destroy(marker.canvas.gameObject);
                }
            }
            markers.Clear();

            optics.Clear();
            original_alignment.Clear();
            part_bounds.Clear();
            part_weights.Clear();
            parts_from = null;
            parts_renderers = null;
            colliders_from = null;
            parts_colliders = null;

            // No static state anywhere in this class, on purpose: the tracker is a scene object and goes
            // away with the mission, and a stale static target (or a cached Instantiate()d sprite) is
            // exactly the class of bug that survives a scene change.
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
                sprite = null;
            }
        }
    }
}





