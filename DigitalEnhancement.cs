using System.Collections.Generic;
using UnityEngine;
using GHPC.Camera;
using GHPC.Player;
using ModUtil;

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
        private bool has_applied;
        private float last_fov;
        private bool last_fixed_reticle_size;

        public void Add(float zoom, float blur, float reticle_scale) {
            slot.OtherFovs = Util.AppendToArray(slot.OtherFovs, zoom);
            zoom_levels[zoom] = new ZoomSetting { Blur = blur, ReticleScale = reticle_scale };
        }

        void Update() {
            if (slot == null || slot.PairedOptic == null || PlayerInput.Instance == null ||
                PlayerInput.Instance.CurrentPlayerWeapon == null ||
                PlayerInput.Instance.CurrentPlayerWeapon.FCS == null || slot.PairedOptic.FCS == null)
            {
                return;
            }

            if (PlayerInput.Instance.CurrentPlayerWeapon.FCS.GetInstanceID() != slot.PairedOptic.FCS.GetInstanceID()) return;

            float current_fov = slot.CurrentFov;
            bool fixed_reticle_size = M1A1.de_fixed_reticle_size != null && M1A1.de_fixed_reticle_size.Value;
            if (has_applied && current_fov == last_fov && fixed_reticle_size == last_fixed_reticle_size)
            {
                return;
            }

            ZoomSetting setting;
            if (!zoom_levels.TryGetValue(current_fov, out setting))
            {
                setting.Blur = original_blur;
                setting.ReticleScale = 1f;
            }

            slot.BaseBlur = setting.Blur;
            float scale = fixed_reticle_size ? setting.ReticleScale : 1f;
            if (reticle_plane != null)
            {
                reticle_plane.localScale = new Vector3(scale, scale, scale);
            }

            last_fov = current_fov;
            last_fixed_reticle_size = fixed_reticle_size;
            has_applied = true;
        }
    }
}
