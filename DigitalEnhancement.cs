using System.Collections.Generic;
using UnityEngine;
using GHPC.Camera;
using GHPC.Player;
using ModUtil;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    public class DigitalEnhancement : MonoBehaviour
    {
        private struct ZoomSetting
        {
            public float Blur;
            public float ReticleScale;
        }

        private readonly Dictionary<float, ZoomSetting> zoom_levels = new Dictionary<float, ZoomSetting>();
        public CameraSlot slot;
        public Transform reticle_plane;
        public float original_blur;

        // Zoom levels are float keys, so an exact dictionary hit is fragile: CurrentFov is the
        // live camera value and can come back as 2.4000001.
        private const float FovMatchTolerance = 0.05f;

        private bool TryGetZoomSetting(float fov, out ZoomSetting setting)
        {
            foreach (KeyValuePair<float, ZoomSetting> entry in zoom_levels)
            {
                if (Mathf.Abs(entry.Key - fov) <= FovMatchTolerance)
                {
                    setting = entry.Value;
                    return true;
                }
            }

            setting = default(ZoomSetting);
            return false;
        }

        // The registered zoom levels are samples of one linear law: the reticle plane has to
        // scale in proportion to the magnification so the reticle keeps a constant apparent
        // size. Reading them as discrete steps made the reticle grow for a moment whenever a
        // zoom step swept past an intermediate FOV (the previous level's size was held while
        // the new one was still out of range), so the law itself is evaluated instead. Above
        // the widest registered level the line is clamped to the uncompensated size, which is
        // how the base magnification is drawn.
        private bool TryReticleScale(float fov, out float scale)
        {
            scale = 1f;
            if (zoom_levels.Count == 0) return false;

            float low_fov = float.MaxValue;
            float low_scale = 1f;
            float high_fov = float.MinValue;
            float high_scale = 1f;

            foreach (KeyValuePair<float, ZoomSetting> entry in zoom_levels)
            {
                if (entry.Key < low_fov)
                {
                    low_fov = entry.Key;
                    low_scale = entry.Value.ReticleScale;
                }

                if (entry.Key > high_fov)
                {
                    high_fov = entry.Key;
                    high_scale = entry.Value.ReticleScale;
                }
            }

            if (zoom_levels.Count == 1 || Mathf.Abs(high_fov - low_fov) < 0.0001f)
            {
                scale = Mathf.Clamp(low_scale * fov / Mathf.Max(low_fov, 0.0001f), 0.05f, 1f);
                return true;
            }

            float slope = (high_scale - low_scale) / (high_fov - low_fov);
            scale = Mathf.Clamp(low_scale + slope * (fov - low_fov), 0.05f, 1f);
            return true;
        }

        public void Add(float zoom, float blur, float reticle_scale) {
            slot.OtherFovs = VUtil.AppendToArray(slot.OtherFovs, zoom);
            zoom_levels[zoom] = new ZoomSetting { Blur = blur, ReticleScale = reticle_scale };
        }

        private void OnEnable()
        {
            Camera.onPreCull += ApplyBeforeRender;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= ApplyBeforeRender;
        }

        // Applied from the camera's pre-cull rather than from Update: the slot writes FovIndex
        // while the camera is being set up for the frame, so a per-frame Update can read the
        // previous magnification and draw the reticle at the old size for exactly one frame.
        private void ApplyBeforeRender(Camera camera)
        {
            Apply();
        }

        private void Apply()
        {
            if (slot == null || slot.PairedOptic == null || PlayerInput.Instance == null ||
                PlayerInput.Instance.CurrentPlayerWeapon == null ||
                PlayerInput.Instance.CurrentPlayerWeapon.FCS == null || slot.PairedOptic.FCS == null)
            {
                return;
            }

            if (PlayerInput.Instance.CurrentPlayerWeapon.FCS.GetInstanceID() != slot.PairedOptic.FCS.GetInstanceID()) return;

            float current_fov = slot.CurrentFov;

            ZoomSetting setting;
            slot.BaseBlur = TryGetZoomSetting(current_fov, out setting) ? setting.Blur : original_blur;

            bool fixed_reticle_size = M1A1.de_fixed_reticle_size != null && M1A1.de_fixed_reticle_size.Value;
            float scale = 1f;
            if (fixed_reticle_size)
            {
                TryReticleScale(current_fov, out scale);
            }

            if (reticle_plane != null)
            {
                // Re-assert the scale whenever the plane does not already carry it: the optic
                // resets the plane to 1 when it is deactivated, so comparing against the
                // transform (instead of remembering the last FOV) is what recovers the reticle
                // after leaving and re-entering the sight.
                Vector3 current = reticle_plane.localScale;
                if (Mathf.Abs(current.x - scale) > 0.0005f)
                {
                    reticle_plane.localScale = new Vector3(scale, scale, scale);
                }
            }
        }
    }
}
