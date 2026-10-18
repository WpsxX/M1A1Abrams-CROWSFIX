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
        private static void BuildRangeBoxCanvas()
        {
            // A leftover template from an earlier mission is no longer any use - it is about to be
            // cloned again - and dropping it keeps the scene from collecting one copy per mission.
            if (box_canvas != null)
            {
                UnityEngine.Object.Destroy(box_canvas);
                box_canvas = null;
            }

            GameObject root = new GameObject("crows range box", typeof(RectTransform));
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 600f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;

            root.AddComponent<GraphicRaycaster>().blockingObjects = GraphicRaycaster.BlockingObjects.None;

            // The strip itself: one opaque black image. AddFrameBoxes() moves this child into place
            // for each of the four borders, so it keeps the anchors, pivot and size the sight picture
            // was laid out against.
            GameObject backdrop = new GameObject("backdrop", typeof(RectTransform));
            backdrop.transform.SetParent(root.transform, false);
            Image image = backdrop.AddComponent<Image>();

            RectTransform rect = (RectTransform)backdrop.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -318.7f);
            rect.sizeDelta = new Vector2(1000f, 300f);
            rect.localScale = Vector3.one;

            // No sprite: an Image with no sprite draws a plain rectangle, which is all a border strip
            // is. The sprite the donor used was a 512x512 black square anyway.
            image.color = Color.black;
            image.raycastTarget = false;

            box_canvas = root;
            box_canvas.SetActive(false);
            box_canvas.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }

        private static TMP_FontAsset PickSightUiFont()
        {
            TMP_FontAsset vcr = VcrFontAsset();

            if (vcr != null)
            {
                return vcr;
            }

            TMP_FontAsset arial = Resources.Load<TMP_FontAsset>("fonts & materials/Arial SDF");

            if (arial != null)
            {
                return arial;
            }

            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null && font.name.IndexOf("arial", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font;
                }
            }

            if (box_canvas != null)
            {
                foreach (TMP_Text text in box_canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text != null && text.font != null)
                    {
                        return text.font;
                    }
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static TMP_FontAsset vcr_font_asset;
        private static bool vcr_font_tried;

        /// <summary>VCR OSD Mono as a TMP font asset, built at most once.</summary>
        private static TMP_FontAsset VcrFontAsset()
        {
            if (vcr_font_tried)
            {
                return vcr_font_asset;
            }

            vcr_font_tried = true;

            // a TMP asset for it may already be in memory (the April Fools prefabs use one)
            TMP_FontAsset tinted = null;

            foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null || font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // "VCR_OSD_MONO_1 green" / "... red" carry a coloured face, which would fight the
                // sight's own colour, so they are only used if nothing plain exists.
                if (font.name.IndexOf("green", StringComparison.OrdinalIgnoreCase) >= 0
                    || font.name.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (tinted == null)
                    {
                        tinted = font;
                    }

                    continue;
                }

                vcr_font_asset = font;
                return vcr_font_asset;
            }

            if (tinted != null)
            {
                vcr_font_asset = tinted;
                return vcr_font_asset;
            }

            Font face = VcrTrueTypeFace();

            if (face == null)
            {
                return null;
            }

            try
            {
                TMP_FontAsset built = TMP_FontAsset.CreateFontAsset(face);

                if (built == null || built.material == null || built.material.shader == null)
                {
                    return null;
                }

                built.name = "VCR OSD Mono (CROWS)";
                vcr_font_asset = built;
            }
            catch (Exception)
            {
            // The call is best-effort: a failure here leaves the previous state alone.
            }

            return vcr_font_asset;
        }

        /// <summary>The VCR OSD Mono TrueType face, reached through GHPC's damage-number prefab.</summary>
        private static Font VcrTrueTypeFace()
        {
            foreach (Font font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font != null && font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return font;
                }
            }

            GameObject prefab = Resources.Load<GameObject>("prefabs/fools/FoolsDamageNumber");

            if (prefab == null)
            {
                return null;
            }

            Font first = null;

            foreach (UnityEngine.UI.Text text in prefab.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (text == null || text.font == null)
                {
                    continue;
                }

                if (text.font.name.IndexOf("VCR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return text.font;
                }

                if (first == null)
                {
                    first = text.font;
                }
            }

            return first;
        }

        /// <summary>
        /// Builds the CROWS sight frame: four screen-space canvases drawing the black border strips
        /// around the sight picture, parented to the optic so they come and go with it.
        /// </summary>
    }
}




