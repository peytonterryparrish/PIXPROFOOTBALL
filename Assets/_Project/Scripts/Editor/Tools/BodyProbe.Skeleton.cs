using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 STANCE-HEAD (owner: "many model, skeleton and mocap issues"): a per-body bone-against-mesh check of the 15 photoreal bodies. Edit mode, no play mode (batch):
    /// <c>Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.BodySkeletonCheck.Run -pixBodyProbeOut dir -pixBodyProbeTag tag</c>.
    /// Per body, from the baked skinned mesh of every LOD0 part and the humanoid bones, in the bind pose and in four extreme poses (arm overhead, hip flexed 100 degrees, elbow bent, trunk folded forward):
    ///  * rig: humanoid, mapped bones, left / right bone length and mirrored-position differences;
    ///  * weights: stray weights (a vertex whose dominant bone is further than a limb away), weight sum errors;
    ///  * limb centring: how far the bone axis is from the centre of the clothed limb at mid-segment (arm, forearm, thigh, shin);
    ///  * foot: ankle, toe-bone and toe-end heights over the lowest drawn sole vertex, left and right (what the audit's bone proxies assume: ankle 0.09, toe 0);
    ///  * skin stretch: triangle edges longer than 1.7 x or shorter than 0.4 x their bind length, per pose and per region (the bone the vertex is weighted to).
    /// Writes skeleton_check.csv / skeleton_check.txt. QA only; it never changes an asset.
    /// </summary>
    public static class BodySkeletonCheck
    {
        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        sealed class Part { public string Key; public SkinnedMeshRenderer Smr; public Vector3[] Rest; public int[] Tris; public int[] Main; public float[] Wmain; public Mesh Baked; }

        public static void Run()
        {
            string outDir = BodyProbe.Arg("-pixBodyProbeOut", "D:/PIXFootball/Renders/bodies_v0130");
            string tag = BodyProbe.Arg("-pixBodyProbeTag", "skeleton");
            Directory.CreateDirectory(outDir);
            var lib = ArtLibrary.Load();
            var csv = new StringBuilder("body,sym_maxLenDiffPct,sym_maxMirrorCm,stray_verts_over35cm,weight_sum_bad,arm_center_cm,forearm_center_cm,thigh_center_cm,shin_center_cm,ankle_over_sole_L,ankle_over_sole_R,toe_over_sole_L,toe_over_sole_R,toeend_over_sole_L,toeend_over_sole_R,sole_len_L,toe_frac_L,stretch_arm_up,stretch_hip_flex,stretch_elbow,stretch_trunk,top_region\n");
            var txt = new StringBuilder("# v0.13.0 body skeleton check (bind pose + 4 extreme poses; stretch = triangle edges over 1.7x or under 0.4x of their bind length)\n\n");
            var poseCsv = new StringBuilder("body,pose,part,edgesOver1.7,edgesUnder0.4,maxRatio,topRegions\n");
            foreach (PhotoBody size in Enum.GetValues(typeof(PhotoBody)))
                foreach (BodyBase bb in Enum.GetValues(typeof(BodyBase)))
                {
                    string key = PlayerAppearance.PrefabKey(size, bb);
                    var prefab = lib != null ? lib.Prefab(key) : null;
                    if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/PhotorealPlayer/Prefabs/" + BodyShots.StagePrefabName(key));   // v0.13.0 BODIES-2: roster keys can be missing from the shared ArtLibrary
                    if (BodyProbe.Arg("-pixBodyProbeStage", "0") == "1") prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_BodiesStage/Prefabs/" + BodyShots.StagePrefabName(key));   // v0.13.0 BODIES-2 hook: the private stage prefabs
                    if (prefab == null) { txt.AppendLine(key + ": prefab missing"); continue; }
                    var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
                    go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                    try { csv.AppendLine(Check(go, key, txt, poseCsv)); }
                    catch (Exception e) { txt.AppendLine(key + ": EXCEPTION " + e.Message + "\n" + e.StackTrace); }
                    UnityEngine.Object.DestroyImmediate(go);
                }
            File.WriteAllText(outDir + "/" + tag + ".csv", csv.ToString());
            File.WriteAllText(outDir + "/" + tag + "_poses.csv", poseCsv.ToString());
            File.WriteAllText(outDir + "/" + tag + ".txt", txt.ToString());
            Debug.Log("[BodySkeletonCheck] done");
            EditorApplication.Exit(0);
        }

        static string Check(GameObject go, string key, StringBuilder log, StringBuilder poseCsv)
        {
            var an = go.GetComponent<Animator>(); if (an == null || !an.isHuman) throw new Exception("no humanoid animator");
            Func<HumanBodyBones, Transform> B = an.GetBoneTransform;
            var photo = go.GetComponent<PhotoPlayer>(); if (photo == null) throw new Exception("no PhotoPlayer");
            var hips = B(HumanBodyBones.Hips); var lul = B(HumanBodyBones.LeftUpperLeg); var rul = B(HumanBodyBones.RightUpperLeg);
            Vector3 across = rul.position - lul.position; across.y = 0f; across.Normalize();
            Vector3 fwd = Vector3.Cross(across, Vector3.up), right = across;
            var parts = new List<Part>();
            foreach (var p in photo.parts)
            {
                if (p == null || p.lods == null || p.lods.Length == 0) continue;
                var smr = p.lods[0] as SkinnedMeshRenderer; if (smr == null || smr.sharedMesh == null) continue;
                var mesh = smr.sharedMesh; var bw = mesh.boneWeights;
                var pt = new Part { Key = p.key, Smr = smr, Tris = mesh.triangles, Baked = new Mesh() };
                pt.Main = new int[bw.Length]; pt.Wmain = new float[bw.Length];
                for (int i = 0; i < bw.Length; i++)
                {
                    int bi = bw[i].boneIndex0; float w = bw[i].weight0;
                    if (bw[i].weight1 > w) { bi = bw[i].boneIndex1; w = bw[i].weight1; }
                    if (bw[i].weight2 > w) { bi = bw[i].boneIndex2; w = bw[i].weight2; }
                    if (bw[i].weight3 > w) { bi = bw[i].boneIndex3; w = bw[i].weight3; }
                    pt.Main[i] = bi; pt.Wmain[i] = w;
                }
                parts.Add(pt);
            }
            // ---- rig symmetry (bone length and mirrored position over the sagittal plane through the hips)
            float maxLen = 0f, maxMir = 0f;
            var pairs = new[] { new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm }, new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand },
                new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg }, new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot },
                new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes } };
            foreach (var pr in pairs)
            {
                Transform a0 = B(pr[0]), b0 = B(pr[1]), a1 = B(pr[2]), b1 = B(pr[3]); if (a0 == null || b0 == null || a1 == null || b1 == null) continue;
                float la = Vector3.Distance(a0.position, a1.position), lb = Vector3.Distance(b0.position, b1.position);
                maxLen = Mathf.Max(maxLen, 100f * Mathf.Abs(la - lb) / Mathf.Max(0.01f, 0.5f * (la + lb)));
            }
            foreach (string hb in new[] { "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes", "Shoulder" })
            {
                Transform l = B((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), "Left" + hb)), r = B((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), "Right" + hb)); if (l == null || r == null) continue;
                Vector3 mid = 0.5f * (lul.position + rul.position); Vector3 lp = l.position - mid, rp = r.position - mid;      // the plane through the middle of the hip joints (the hips bone itself is off it by up to 1.6 cm in the base bodies)
                Vector3 rm = rp - 2f * Vector3.Dot(rp, right) * right;      // the right bone mirrored over the plane
                maxMir = Mathf.Max(maxMir, 100f * Vector3.Distance(lp, rm));
            }
            // ---- weights: stray weights and weight sums
            int stray = 0, sumBad = 0;
            var strayReg = new Dictionary<string, int>();
            var smrBones = new Dictionary<SkinnedMeshRenderer, Transform[]>();
            foreach (var pt in parts)
            {
                pt.Smr.BakeMesh(pt.Baked, true); pt.Rest = WorldVerts(pt);
                var bones = pt.Smr.bones; smrBones[pt.Smr] = bones;
                var bw = pt.Smr.sharedMesh.boneWeights;
                for (int i = 0; i < bw.Length; i++)
                {
                    float sum = bw[i].weight0 + bw[i].weight1 + bw[i].weight2 + bw[i].weight3; if (Mathf.Abs(sum - 1f) > 0.02f) sumBad++;
                    int bi = pt.Main[i]; if (bi < 0 || bi >= bones.Length || bones[bi] == null) { stray++; continue; }
                    if (Vector3.Distance(pt.Rest[i], bones[bi].position) > 0.35f) { stray++; string sk = pt.Key + ":" + bones[bi].name; int sc; strayReg.TryGetValue(sk, out sc); strayReg[sk] = sc + 1; }      // a vertex further than a limb segment from the bone that moves it
                }
            }
            log.AppendLine("   stray weights by part:bone " + string.Join(" ", strayReg.OrderByDescending(x => x.Value).Take(5).Select(x => x.Key + "=" + x.Value)));
            // ---- limb centring at mid-segment: clothed limb cross-section centre against the bone axis
            Func<HumanBodyBones, HumanBodyBones, float> centre = (b0, b1) =>
            {
                Transform a = B(b0), c = B(b1); if (a == null || c == null) return float.NaN;
                Vector3 ax = c.position - a.position; float len = ax.magnitude; if (len < 0.05f) return float.NaN; ax /= len;
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var pt in parts) { if (pt.Key == "Helmet" || pt.Key == "Head") continue; for (int i = 0; i < pt.Rest.Length; i++) { Vector3 d = pt.Rest[i] - a.position; float t = Vector3.Dot(d, ax) / len; if (t < 0.35f || t > 0.65f) continue; Vector3 perp = d - ax * Vector3.Dot(d, ax); if (perp.magnitude > 0.25f) continue; sum += perp; n++; } }
                return n > 20 ? 100f * (sum / n).magnitude : float.NaN;
            };
            float cArm = centre(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), cFore = centre(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand), cThigh = centre(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg), cShin = centre(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            // ---- foot: ankle, toe bone, toe end over the lowest drawn vertex around the foot
            float[] sole = new float[2], ank = new float[2], toe = new float[2], toeEnd = new float[2]; float soleLen = 0f, toeFrac = 0f;
            for (int s = 0; s < 2; s++)
            {
                Transform f = B(s == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot), t = B(s == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                float lo = 9f, zmin = 9f, zmax = -9f;
                foreach (var pt in parts) foreach (var v in pt.Rest) { float dx = v.x - f.position.x, dz = v.z - f.position.z; if (dx * dx + dz * dz <= 0.28f * 0.28f && v.y < f.position.y + 0.02f) { if (v.y < lo) lo = v.y; if (v.y < lo + 0.04f) { float z = Vector3.Dot(v - f.position, fwd); if (z < zmin) zmin = z; if (z > zmax) zmax = z; } } }
                sole[s] = lo; ank[s] = f.position.y - lo; toe[s] = t != null ? t.position.y - lo : float.NaN; var te = t != null && t.childCount > 0 ? t.GetChild(0) : null; toeEnd[s] = te != null ? te.position.y - lo : float.NaN;
                if (s == 0) { soleLen = zmax - zmin; toeFrac = t != null && soleLen > 0.01f ? (Vector3.Dot(t.position - f.position, fwd) - zmin) / soleLen : float.NaN; }
            }
            // ---- extreme poses: stretch of the skin
            var rest0 = parts.Select(p => p.Rest).ToList();
            var stretch = new float[7]; var regionTotals = new Dictionary<string, int>();
            string[] poseNames = { "arm_up", "hip_flex", "elbow", "trunk", "arm_up_rhythm15", "arm_up_rhythm25", "arm_up_rhythm35" };
            for (int ps = 0; ps < 7; ps++)
            {
                var saved = new Dictionary<Transform, Quaternion>(); foreach (var tr in go.GetComponentsInChildren<Transform>(true)) saved[tr] = tr.localRotation;
                ApplyPose(ps, B, fwd, right);
                int tot = 0;
                foreach (var pt in parts)
                {
                    pt.Smr.BakeMesh(pt.Baked, true); var posed = WorldVerts(pt); var bones = smrBones[pt.Smr];
                    int over = 0, under = 0; float mx = 0f; var reg = new Dictionary<string, int>();
                    for (int k = 0; k + 2 < pt.Tris.Length; k += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int a = pt.Tris[k + e], b = pt.Tris[k + (e + 1) % 3]; float r0 = Vector3.Distance(pt.Rest[a], pt.Rest[b]); if (r0 < 0.004f) continue;
                            float r = Vector3.Distance(posed[a], posed[b]) / r0; if (r > mx) mx = r;
                            if (r > 1.7f || r < 0.4f)
                            {
                                if (r > 1.7f) over++; else under++;
                                int bi = pt.Main[a]; string rn = bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "?"; int c; reg.TryGetValue(rn, out c); reg[rn] = c + 1;
                                int c2; regionTotals.TryGetValue(rn, out c2); regionTotals[rn] = c2 + 1;
                            }
                        }
                    tot += over + under;
                    if (over + under > 0) poseCsv.AppendLine(key + "," + poseNames[ps] + "," + pt.Key + "," + over + "," + under + "," + F(mx) + "," + string.Join(" ", reg.OrderByDescending(x => x.Value).Take(3).Select(x => x.Key + ":" + x.Value)));
                }
                stretch[ps] = tot;
                foreach (var tr in saved) tr.Key.localRotation = tr.Value;
            }
            foreach (var pt in parts) UnityEngine.Object.DestroyImmediate(pt.Baked);
            string top = string.Join(" ", regionTotals.OrderByDescending(x => x.Value).Take(3).Select(x => x.Key + ":" + x.Value));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-28} sym len {1:0.0}% mirror {2:0.0} cm | stray {3} sumBad {4} | centre arm {5:0.0} fore {6:0.0} thigh {7:0.0} shin {8:0.0} cm | ankle over sole L/R {9:0.000}/{10:0.000} toe {11:0.000}/{12:0.000} toeEnd {13:0.000}/{14:0.000} sole len {15:0.00} toe at {16:0.00} | stretch arm_up {17} hip_flex {18} elbow {19} trunk {20} | rhythm15/25/35 {22}/{23}/{24} | {21}",
                key, maxLen, maxMir, stray, sumBad, cArm, cFore, cThigh, cShin, ank[0], ank[1], toe[0], toe[1], toeEnd[0], toeEnd[1], soleLen, toeFrac, stretch[0], stretch[1], stretch[2], stretch[3], top, stretch[4], stretch[5], stretch[6]));
            return string.Join(",", new[] { key, F(maxLen), F(maxMir), stray.ToString(), sumBad.ToString(), F(cArm), F(cFore), F(cThigh), F(cShin), F(ank[0]), F(ank[1]), F(toe[0]), F(toe[1]), F(toeEnd[0]), F(toeEnd[1]), F(soleLen), F(toeFrac), F(stretch[0]), F(stretch[1]), F(stretch[2]), F(stretch[3]), F(stretch[4]), F(stretch[5]), F(stretch[6]), top.Replace(',', ' ') });
        }

        static Vector3[] WorldVerts(Part pt)
        {
            var vs = pt.Baked.vertices; var w = new Vector3[vs.Length]; var m = Matrix4x4.TRS(pt.Smr.transform.position, pt.Smr.transform.rotation, Vector3.one);
            for (int i = 0; i < vs.Length; i++) w[i] = m.MultiplyPoint3x4(vs[i]); return w;
        }

        /// <summary>Rotates bones about the body axes (the sign is chosen by the effect: the hand goes up, the knee comes forward, the elbow closes, the head comes forward).</summary>
        static void ApplyPose(int pose, Func<HumanBodyBones, Transform> B, Vector3 fwd, Vector3 right)
        {
            switch (pose)
            {
                case 0: // right arm overhead (150 degrees of abduction)
                    Turn(B(HumanBodyBones.RightUpperArm), fwd, 150f, t => B(HumanBodyBones.RightHand).position.y); break;
                case 1: // left hip flexed 100 degrees (sprint knee / three-point), the knee bent with it
                    Turn(B(HumanBodyBones.LeftUpperLeg), right, 100f, t => B(HumanBodyBones.LeftLowerLeg).position.y);
                    Turn(B(HumanBodyBones.LeftLowerLeg), right, 90f, t => -Vector3.Dot(B(HumanBodyBones.LeftFoot).position - t.position, fwd));
                    break;
                case 2: // right elbow bent 120 degrees
                    Turn(B(HumanBodyBones.RightLowerArm), Vector3.up, 120f, t => -Vector3.Distance(B(HumanBodyBones.RightHand).position, B(HumanBodyBones.RightUpperArm).position)); break;
                case 4: case 5: case 6: // right arm overhead with the shoulder girdle taking its share (scapulohumeral rhythm): the clavicle rises by R degrees, the upper arm turns 150 - R
                {
                    float R = pose == 4 ? 15f : pose == 5 ? 25f : 35f;
                    Turn(B(HumanBodyBones.RightShoulder), fwd, R, t => B(HumanBodyBones.RightUpperArm).position.y);
                    Turn(B(HumanBodyBones.RightUpperArm), fwd, 150f - R, t => B(HumanBodyBones.RightHand).position.y);
                    break;
                }
                case 3: // trunk folded 70 degrees forward over the hips (spine + chest)
                    Turn(B(HumanBodyBones.Spine), right, 35f, t => Vector3.Dot(B(HumanBodyBones.Head).position, fwd));
                    Turn(B(HumanBodyBones.Chest) ?? B(HumanBodyBones.UpperChest), right, 35f, t => Vector3.Dot(B(HumanBodyBones.Head).position, fwd)); break;
            }
        }

        static void Turn(Transform bone, Vector3 axis, float deg, Func<Transform, float> score)
        {
            if (bone == null) return;
            Quaternion q0 = bone.rotation;
            bone.rotation = Quaternion.AngleAxis(deg, axis) * q0; float sPlus = score(bone);
            bone.rotation = Quaternion.AngleAxis(-deg, axis) * q0; float sMinus = score(bone);
            bone.rotation = sPlus >= sMinus ? Quaternion.AngleAxis(deg, axis) * q0 : Quaternion.AngleAxis(-deg, axis) * q0;
        }
    }
}
