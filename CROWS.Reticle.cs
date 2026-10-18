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
        private static void BuildReticle()
        {
            if (reticle_so != null)
            {
                return;
            }

            reticle_so = ScriptableObject.CreateInstance<ReticleSO>();
            reticle_so.name = "CROWS-RETICLE";

            reticle_so.lights = new List<ReticleTree.Light>() { new ReticleTree.Light() };
            reticle_so.lights[0].type = ReticleTree.Light.Type.Powered;
            reticle_so.lights[0].color = SIGHT_COLOR;

            ReticleTree.FocalPlane plane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First);
            plane.elements = new List<ReticleTree.TransformElement>();
            reticle_so.planes = new List<ReticleTree.FocalPlane>() { plane };

            // The T-55A tree this sight grew out of was two nested groups: an outer boresight one and
            // the inner one everything is drawn under. Both align to the impact point, so the sight
            // picture follows the computer's solution and not the gun's own lay.
            ReticleTree.Angular outer = new ReticleTree.Angular(Vector2.zero, null);
            outer.name = "Boresight";
            outer.align = ReticleTree.GroupBase.Alignment.Impact;
            plane.elements.Add(outer);

            ReticleTree.Angular reticle_hq = new ReticleTree.Angular(Vector2.zero, outer);
            reticle_hq.name = "Horizontal";
            reticle_hq.align = ReticleTree.GroupBase.Alignment.Impact;
            outer.elements.Add(reticle_hq);

            // Measured off the T-55A night reticle: 0.1832596 mrad lines, thinned by 1.7 to suit a 15x
            // picture, both 13.0944 mrad long. Rotation is stored in mrad, so 90 degrees is 1570.7964.
            const float LINE_THICKNESS = 0.1832596f / 1.7f;
            const float LINE_LENGTH = 13.0944f;
            const float VERTICAL = 1570.7964f;

            ReticleTree.Line line1 = new ReticleTree.Line();
            line1.rotation.MRADS = 0f;
            line1.length.MRADS = LINE_LENGTH;
            line1.thickness.MRADS = LINE_THICKNESS;
            line1.length.unit = AngularLength.AngularUnit.MIL_USSR;
            line1.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
            line1.roundness = 1f;
            line1.illumination = ReticleTree.Light.Type.Powered;
            line1.visualType = ReticleTree.VisualElement.Type.Painted;

            ReticleTree.Line line2 = new ReticleTree.Line();
            line2.rotation.MRADS = VERTICAL;
            line2.length.MRADS = LINE_LENGTH;
            line2.thickness.MRADS = LINE_THICKNESS;
            line2.length.unit = AngularLength.AngularUnit.MIL_USSR;
            line2.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
            line2.roundness = 1f;
            line2.illumination = ReticleTree.Light.Type.Powered;
            line2.visualType = ReticleTree.VisualElement.Type.Painted;

            reticle_hq.elements.Add(line1);
            reticle_hq.elements.Add(line2);

            RecordReticlePart(line1);
            RecordReticlePart(line2);

            List<Vector3> box_pos = new List<Vector3>() {
                new Vector3(0f, -7.344f),
                new Vector3(0f, 7.344f),
                new Vector3(7.344f, 0f),
                new Vector3(-7.344f, 0f),
            };

            foreach (Vector3 pos in box_pos)
            {
                ReticleTree.Line box = new ReticleTree.Line();
                box.roundness = 0f;
                box.thickness.MRADS = line2.thickness.MRADS * 2.8f;
                box.length.MRADS = 6f;
                box.thickness.unit = AngularLength.AngularUnit.MIL_USSR;
                box.length.unit = AngularLength.AngularUnit.MIL_USSR;
                box.rotation.MRADS = pos.x == 0f ? line2.rotation.MRADS : line1.rotation.MRADS;

                box.position = new ReticleTree.Position(pos.x, pos.y, AngularLength.AngularUnit.MIL_NATO, LinearLength.LinearUnit.M);
                box.visualType = ReticleTree.VisualElement.Type.Painted;
                box.illumination = ReticleTree.Light.Type.Powered;

                reticle_hq.elements.Add(box);
                RecordReticlePart(box);
            }
        }

        private static void RecordReticlePart(ReticleTree.Line line)
        {
            ReticlePart part = new ReticlePart();
            part.line = line;
            part.position = new Vector2(line.position.x, line.position.y);
            part.length = line.length.MRADS;
            part.thickness = line.thickness.MRADS;
            reticle_parts.Add(part);
        }

        /// <summary>
        /// Rescales the reticle so it keeps the same apparent size and proportions at every
        /// magnification, using the 12X picture as the reference. It is drawn through PostMeshComp,
        /// which ignores the renderer's transform, so the geometry itself has to change.
        /// </summary>
        internal static void ApplyReticleScale(float factor)
        {
            if (reticle_parts.Count == 0)
            {
                return;
            }

            factor = Mathf.Clamp(factor, 0.05f, 40f);

            if (Mathf.Abs(factor - reticle_scale) < 0.001f)
            {
                return;
            }

            reticle_scale = factor;

            foreach (ReticlePart part in reticle_parts)
            {
                if (part.line == null)
                {
                    continue;
                }

                part.line.position.x = part.position.x * factor;
                part.line.position.y = part.position.y * factor;
                part.line.length.MRADS = part.length * factor;
                part.line.thickness.MRADS = part.thickness * factor;
            }

            PruneReticleMeshes();

            for (int i = 0; i < reticle_meshes.Count; i++)
            {
                RebuildReticle(reticle_meshes[i]);
            }


        }

        /// <summary>
        /// Drops the cached reticle scale so the next frame re-applies it.
        ///
        /// The scale is what keeps the reticle the same apparent size at every magnification, and it
        /// is remembered in a static field so the rebuild it triggers is not run sixty times a second.
        /// The reticle *meshes*, on the other hand, belong to the mission and are made again from the
        /// tree on every conversion. A cached factor that survives the mission therefore says "already
        /// applied" about a mesh that has never been scaled, the rebuild is skipped, and the sight
        /// falls back to a reticle that grows with the zoom - the way it looked before the rescaling
        /// existed. Conversion calls this so the first frame of the new mission re-applies it.
        /// </summary>
        internal static void InvalidateReticleScale()
        {
            reticle_scale = -1f;
        }

        /// <summary>
        /// Takes note of a sight's reticle mesh so the rescaling can reach it. Called for both optics of
        /// every CROWS the mission builds, not just the one the player ends up in.
        /// </summary>
        private static void RegisterReticleMesh(ReticleMesh mesh)
        {
            PruneReticleMeshes();

            if (mesh == null)
            {
                return;
            }

            for (int i = 0; i < reticle_meshes.Count; i++)
            {
                if (reticle_meshes[i] == mesh)
                {
                    return;
                }
            }

            reticle_meshes.Add(mesh);


        }

        /// <summary>
        /// Forgets the meshes of the mission that has ended. Unity reports a destroyed object as equal to
        /// null, which is what makes the test work on a plain list of components.
        /// </summary>
        private static void PruneReticleMeshes()
        {
            for (int i = reticle_meshes.Count - 1; i >= 0; i--)
            {
                if (reticle_meshes[i] == null)
                {
                    reticle_meshes.RemoveAt(i);
                }
            }
        }

        private static void RebuildReticle(ReticleMesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            // A sight that is still drawing the donor's reticle has nothing of ours in it to rescale,
            // and rebuilding it would only throw that sight's geometry away for nothing.
            if (mesh.reticleSO != reticle_so)
            {
                return;
            }

            SkinnedMeshRenderer smr = mesh.GetComponent<SkinnedMeshRenderer>();

            if (smr != null)
            {
                smr.sharedMesh = null;
            }

            mesh.Load();
        }

        // ------------------------------------------------------------------ mounting

    }
}




