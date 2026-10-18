using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Thermals;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using MelonLoader;
using MelonLoader.Utils;
using ModUtil;
using Reticle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VUtil = VanillaAssetUtil.ModUtil.Util;

namespace M1A1Abrams
{
    /// <summary>
    /// M153 CROWS II remote weapon station.
    ///
    /// Builds the station on the player's own Abrams: the .50 cal, its gunsight and its fire control
    /// computer are cloned from the cupola that is already on the vehicle, and the reticle tree and
    /// the range-box canvas are constructed in code. No other vehicle (M60A3, T-55A, M2 Bradley) has
    /// to be in the mission. Every non-public member is reached through ModUtil.Reflect: the running
    /// game loads the official (non-publicized) Assembly-CSharp.
    /// </summary>
    internal static partial class CROWS
    {
        internal static bool ResolveOwnCupola(Transform vehicle_root)
        {
            m2_browning = null;
            m2_gun_sight = null;
            m2_fcs = null;

            if (vehicle_root == null)
            {
                Fail("no vehicle to take the cupola station from");
                return false;
            }

            // The three pieces are not all under one node on the M1: the gun and the sight hang off
            // CUPOLA/CUPOLA_GUN while the computer lives in CUPOLA/Cupola Scripts, so resolving
            // 'Cupola Scripts' and searching inside it finds only the computer. Resolve the cupola
            // itself and search the whole of it; on the M60A3-style layout everything really is
            // inside 'Cupola Scripts', which is then used as the root instead.
            Transform scripts = FindDescendant(vehicle_root, "Cupola Scripts");
            Transform cupola = FindDescendant(vehicle_root, "CUPOLA");

            if (cupola == null)
            {
                cupola = scripts;
            }

            if (cupola == null)
            {
                Fail("this vehicle has no cupola to clone the .50 cal from");
                return false;
            }

            // Found by component and by the name the station is built from, not by a full path: the
            // path differs between the M1 variants and a hierarchy change should not take the station
            // with it. The gun is the only WeaponSystem under the cupola whose name carries its
            // calibre; the sight is the optic named a gunsight; the computer is the cupola's own FCS.
            foreach (WeaponSystem weapon in cupola.GetComponentsInChildren<WeaponSystem>(true))
            {
                if (weapon != null && weapon.Feed != null && weapon.gameObject.name.Contains("12.7"))
                {
                    m2_browning = weapon.gameObject;
                    break;
                }
            }

            foreach (UsableOptic optic in cupola.GetComponentsInChildren<UsableOptic>(true))
            {
                if (optic != null && optic.slot != null && optic.gameObject.name.Contains("gunsight"))
                {
                    m2_gun_sight = optic.gameObject;
                    break;
                }
            }

            // The computer is the cupola's own FCS, which lives in the Cupola Scripts node; searching
            // there first keeps the main gun's computer out of the picture when the cupola root is
            // the whole CUPOLA node.
            Transform fcs_root = scripts != null ? scripts : cupola;

            foreach (FireControlSystem computer in fcs_root.GetComponentsInChildren<FireControlSystem>(true))
            {
                if (computer != null)
                {
                    m2_fcs = computer.gameObject;
                    break;
                }
            }

            if (m2_browning == null || m2_gun_sight == null || m2_fcs == null)
            {
                Fail("this vehicle's cupola is missing one of its .50 cal, its sight and its computer");
                return false;
            }

            return true;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendant(root.GetChild(i), name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the sight frame template in code: a screen-space canvas holding one black image, the
        /// low blackout that hides the world above the sight picture. It replaces the Bradley GPS
        /// canvas this used to be cloned from, so the station no longer needs a third vehicle.
        /// </summary>
        private static void DetachFromDonor(Component component, Transform model_root)
        {
            if (component == null)
            {
                return;
            }

            // Type.GetFields() does not return private fields declared on base classes, so
            // the hierarchy is walked by hand (same rule as ModUtil.Reflect).
            for (Type type = component.GetType(); type != null && type != typeof(Behaviour); type = type.BaseType)
            {
                foreach (System.Reflection.FieldInfo field in type.GetFields(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    Type field_type = field.FieldType;

                    if (typeof(Component).IsAssignableFrom(field_type) || typeof(GameObject).IsAssignableFrom(field_type))
                    {
                        if (IsExternal(field.GetValue(component) as UnityEngine.Object, model_root))
                        {
                            field.SetValue(component, null);
                        }

                        continue;
                    }

                    if (!field_type.IsArray)
                    {
                        continue;
                    }

                    Type element_type = field_type.GetElementType();

                    if (!typeof(Component).IsAssignableFrom(element_type) && !typeof(GameObject).IsAssignableFrom(element_type))
                    {
                        continue;
                    }

                    Array array = field.GetValue(component) as Array;

                    if (array == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < array.Length; i++)
                    {
                        if (IsExternal(array.GetValue(i) as UnityEngine.Object, model_root))
                        {
                            array.SetValue(null, i);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Switches the vanilla scope overlay off while a CROWS sight is the active camera slot: the
        /// CROWS ships its own black frame, and CameraManager would otherwise turn the shared "Scope
        /// Sprite" back on from the slot's SpriteType. The cached type is invalidated too, since
        /// SetActiveSprite() early-returns when the requested type matches it.
        ///
        /// The borrowed sight's own PostMeshComp must NOT be removed: that is what draws the reticle.
        /// </summary>
        public class CrowsScopeOverlay : MonoBehaviour
        {
            public CameraSlot day_slot;
            public CameraSlot night_slot;

            private CameraSpriteManager manager;
            private GameObject[] sprites;

            void Start()
            {
                manager = UnityEngine.Object.FindObjectOfType<CameraSpriteManager>();

                if (manager == null || manager.Sprites == null)
                {
                    enabled = false;
                    return;
                }

                List<GameObject> found = new List<GameObject>();

                foreach (CameraSpriteManager.CameraSpriteData data in manager.Sprites)
                {
                    if (data != null && data.SpriteObject != null)
                    {
                        found.Add(data.SpriteObject);
                    }
                }

                sprites = found.ToArray();
            }

            void Update()
            {
                CameraSlot active = CameraSlot.ActiveInstance;

                if (active != day_slot && active != night_slot)
                {
                    return;
                }

                foreach (GameObject sprite in sprites)
                {
                    if (sprite != null && sprite.activeSelf)
                    {
                        sprite.SetActive(false);
                    }
                }

                Reflect.Set(manager, "_activeType", (CameraSpriteManager.SpriteType)(-1));
            }
        }

        /// <summary>
        /// Picks the font for the sight overlay: VCR OSD Mono, built from the TrueType face because no
        /// Resources asset references a TMP asset for it. Arial is the fallback, then the mod's own
        /// markers' font, then the TMP default.
        /// </summary>
    }
}




