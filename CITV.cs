using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using GHPC.Weapons;
using System.Reflection;
using GHPC.Utility;
using GHPC;
using GHPC.Crew;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace M1A1Abrams
{
    /// <summary>
    /// Answers "does this unit carry a CITV" for the three patches below. GetComponentInChildren walks
    /// the whole vehicle subtree and those patches run on every crew-health call, so the answer is
    /// cached per unit and dropped whenever the live scene changes (see E13-perf).
    /// </summary>
    public static class CITVLookup
    {
        private static readonly Dictionary<int, bool> installed = new Dictionary<int, bool>();
        private static int epoch = -1;

        public static bool HasCITV(IUnit unit)
        {
            // Cache entries are only valid for the scene that produced them: instance IDs are reused
            // after a load, and the component a stale entry describes is gone. Scene load count is the
            // one counter that steps whenever any of that changes.
            int scene = SceneManager.GetActiveScene().buildIndex;
            if (SceneManager.sceneCount > 1) scene = SceneManager.sceneCount * 1000 + scene;

            if (scene != epoch)
            {
                installed.Clear();
                epoch = scene;
            }

            int id = unit.transform.GetInstanceID();
            bool found;
            if (installed.TryGetValue(id, out found))
            {
                return found;
            }

            found = unit.transform.GetComponentInChildren<CITV>(true) != null;
            installed[id] = found;
            return found;
        }
    }

    [HarmonyPatch(typeof(CrewManager), "DisableGunner")]
    public class CITVOverrideGunnerDeath
    {
        public static bool Prefix(CrewManager __instance)
        {
            if (!CITVLookup.HasCITV(__instance.Unit)) return true;
          
            ModUtil.Reflect.Call(__instance, "DisableCrewMemberBase", CrewPosition.Gunner);

            if (__instance.GetCrewBrain(CrewPosition.Commander).Incapacitated)
            {
                return true;
            }

            __instance.GetCrewBrain(CrewPosition.Gunner).Suspended = false;

            return false;
        }
    }

    [HarmonyPatch(typeof(CrewManager), "DisableCommander")]
    public class CITVOverrideCommanderDeath
    {
        public static bool Prefix(CrewManager __instance)
        {
            if (!CITVLookup.HasCITV(__instance.Unit)) return true;

            if (__instance.GetCrewBrain(CrewPosition.Gunner).Incapacitated)
            {
                // The commander is moving to the CITV, so the gunner's loss must cut the main gun's
                // trigger but must not neutralize the vehicle - the whole point of this takeover.
                // NotifyCannotShoot also reaches Unit.NotifyIncapacitated through
                // `_neutralizedWhenCannotShoot`, so the flag is cleared for exactly the length of the
                // call and restored immediately afterwards.
                FieldInfo neutralized_when_cannot_shoot = typeof(GHPC.Unit).GetField(
                    "_neutralizedWhenCannotShoot", BindingFlags.Instance | BindingFlags.NonPublic);

                if (neutralized_when_cannot_shoot != null)
                {
                    bool saved = (bool)neutralized_when_cannot_shoot.GetValue(__instance.Unit);
                    neutralized_when_cannot_shoot.SetValue(__instance.Unit, false);

                    try
                    {
                        __instance.Unit.NotifyCannotShoot();
                    }
                    finally
                    {
                        neutralized_when_cannot_shoot.SetValue(__instance.Unit, saved);
                    }
                }
                else
                {
                    __instance.Unit.NotifyCannotShoot();
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(CrewManager), "IsCrewMemberConscious")]
    public class CITVOverrideConscious
    {
        public static bool Prefix(CrewManager __instance, CrewPosition position, ref bool __result)
        {
            if (!CITVLookup.HasCITV(__instance.Unit)) return true;
            if (position != CrewPosition.Gunner) return true;

            if (__instance.GetCrewBrain(CrewPosition.Gunner).Incapacitated)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    public class CITVManager : MonoBehaviour {
        public GameObject monitor;
        public GameObject wfov;
        public GameObject nfov;
        private Transform canvas;
        private Transform _3d_reticle;
        private FieldInfo snapp;
        private FieldInfo max_distance; 

        private Vector3 CITVCurrentAimPoint
        {
            get
            {
                var cam_follow = CameraManager.Instance.CameraFollow;
                float _max_distance = (float)max_distance.GetValue(cam_follow.BufferedCamera);

                Ray ray = new Ray(cam_follow.BufferedCamera.transform.position, cam_follow.CurrentAimVector);
                RaycastHit raycastHit;

                if (!Physics.Raycast(ray, out raycastHit, _max_distance, ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask.value))
                {
                    return cam_follow.BufferedCamera.transform.position + ray.direction * _max_distance;
                }

                return raycastHit.point;
            }
        }

        public static void Init() {
            if (GameObject.Find("CITV manager") != null) return;

            GameObject manager_obj = new GameObject("CITV manager");
            manager_obj.transform.SetParent(GameObject.Find("_APP_GHPC_").transform);
            CITVManager manager = manager_obj.AddComponent<CITVManager>();           
        }

        void Awake() {
            canvas = GameObject.Find("_APP_GHPC_").transform.Find("UIHUDCanvas");
            _3d_reticle = canvas.Find("3P aim reticle");

            monitor = GameObject.Instantiate(Assets.citv_monitor, GameObject.Find("_APP_GHPC_").transform);
            monitor.transform.localPosition = new Vector3(0f, 0f, 0f);
            monitor.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
            monitor.GetComponent<Canvas>().sortingOrder = -1;
            monitor.SetActive(false);

            nfov = monitor.transform.Find("nfov").gameObject;
            wfov = monitor.transform.Find("wfov").gameObject;

            snapp = typeof(GHPC.Camera.BufferedCameraFollow).GetField("SNAPPINESS", BindingFlags.Static | BindingFlags.NonPublic);
            max_distance = typeof(GHPC.Camera.BufferedCameraFollow).GetField("MAX_AIM_DISTANCE", BindingFlags.Static | BindingFlags.NonPublic);
        }

        void Update() {
            CameraSlot cam_slot = ModUtil.Reflect.Get<CameraSlot>(CameraManager.Instance, "_currentCamSlot");
            if (cam_slot != null && cam_slot.GetComponent<CITV>() != null)
            {
                monitor.SetActive(true);
                _3d_reticle.gameObject.SetActive(false);

                nfov.gameObject.SetActive(cam_slot.CurrentFov < 10f);
                wfov.gameObject.SetActive(cam_slot.CurrentFov >= 10f);

                if (M1A1.citv_smooth.Value && snapp != null)
                {
                    snapp.SetValue(null, 0.20f);
                }

                if (InputUtil.MainPlayer.GetButton("Smooth Aim")) {
                    FireControlSystem fcs = PlayerInput.Instance.CurrentPlayerWeapon.Weapon.FCS;
                    fcs.SetAimWorldPosition(this.CITVCurrentAimPoint);
                }
            }
            else {
                monitor.SetActive(false);
                _3d_reticle.gameObject.SetActive(true);

                if (M1A1.citv_smooth.Value && snapp != null)
                {
                    snapp.SetValue(null, 10f);
                }
            }
        }
    } 

    public class CITV : MonoBehaviour
    {
        private static GHPC.Camera.BufferedCameraFollow camera;
        public GameObject model; 
        private CameraSlot nods;
        private int id;

        void Awake() {
            if (camera == null)
                camera = CameraManager.Instance.CameraFollow;

            nods = gameObject.GetComponent<CameraSlot>();
            id = gameObject.GetInstanceID();

            nods.transform.localPosition = new Vector3(-1.1638f, -0.3435f, 1.2273f);
            nods.VisionType = NightVisionType.Thermal;
            nods.IsExterior = false;
            nods.BaseBlur = 0f;
            nods.OverrideFLIRResolution = true;
            nods.CanToggleFlirPolarity = true;
            nods.FLIRWidth = 1024;
            nods.FLIRHeight = 576;
            nods.DefaultFov = 15f;
            nods.OtherFovs = new float[] {10f, 4f};
            nods.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
            nods.FLIRBlitMaterialOverride = Assets.flir_blit_mat_green_no_scan;
            SimpleNightVision nods_snv = nods.GetComponent<SimpleNightVision>();
            Component.Destroy(nods.GetComponent<PostProcessVolume>());
            GameObject post = GameObject.Instantiate(Assets.flir_post, nods.transform);
            post.transform.Find("MainCam Volume").gameObject.SetActive(false);

            PostProcessVolume post_vol = post.transform.Find("FLIR Only Volume").GetComponent<PostProcessVolume>();
            post_vol.enabled = true;
            ModUtil.Reflect.Set(nods_snv, "_postVolume", post_vol);
            // SimpleNightVision owns this volume after initialization.  Keep it disabled
            // until the thermal state machine explicitly enables it.
            post_vol.enabled = false;
        }

        void Update() {
            Transform commander_head = ModUtil.Reflect.Get<Transform>(camera, "commanderHead");

            if (commander_head == null || commander_head.GetComponent<CITV>() == null)
            {
                model.SetActive(true);
                ModUtil.Reflect.Set(camera, "aimSensitivity3P", 5f);
                return;
            }

            if (commander_head.gameObject.GetInstanceID() != id)
            {
                model.SetActive(true);
                return;
            }

            float sensitivity = 1.25f * (nods.CurrentFov / 60f);
            ModUtil.Reflect.Set(camera, "aimSensitivity3P", sensitivity);
            model.SetActive(false);
        }
    }
}
