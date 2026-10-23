using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 BODIES: proportions of the 15 photoreal bodies in the bind pose against human ranges (limb lengths, head, shoulder / waist / hip widths of the clothed mesh, thigh and arm diameters). QA only.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.BodyProportions.Run -pixBodyProbeOut dir   (no play mode).
    /// </summary>
    public static class BodyProportions
    {
        public static void Run()
        {
            string outDir = BodyProbe.Arg("-pixBodyProbeOut", "D:/PIXFootball/Renders/bodies_v0100");
            string tag = BodyProbe.Arg("-pixBodyProbeTag", "run");
            if (BodyProbe.Arg("-pixArmReimport", "0") == "1") PhotoArmProportions.ReimportAll();
            Directory.CreateDirectory(outDir);
            var lib = ArtLibrary.Load();
            var sb = new StringBuilder();
            var csv = new StringBuilder("body,heightM,hipsY,legLenOverH,thighOverH,shinOverH,upperArmOverH,foreArmOverH,torsoOverH,headHeightOverH,shoulderJointWidthOverH,hipJointWidthOverH,chestWidthM,chestDepthM,waistWidthM,waistDepthM,pelvisWidthM,pelvisDepthM,thighDiaM,upperArmDiaM,waistOverChest,waistOverPelvis,pelvisOverChest\n");
            sb.AppendLine("# Body proportions (bind pose, clothed mesh slices; human athlete reference in the last rows)\n");
            foreach (PhotoBody size in Enum.GetValues(typeof(PhotoBody)))
                foreach (BodyBase bb in Enum.GetValues(typeof(BodyBase)))
                {
                    string key = PlayerAppearance.PrefabKey(size, bb);
                    var prefab = lib != null ? lib.Prefab(key) : null;
                    if (prefab == null) { sb.AppendLine(key + ": prefab missing"); continue; }
                    var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
                    go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
                    try { csv.AppendLine(Measure(go, key, sb)); }
                    catch (Exception e) { sb.AppendLine(key + ": " + e.Message); }
                    UnityEngine.Object.DestroyImmediate(go);
                }
            File.WriteAllText(outDir + "/" + tag + "_proportions.csv", csv.ToString());
            File.WriteAllText(outDir + "/" + tag + "_proportions.txt", sb.ToString());
            Debug.Log("[BodyProportions] done");
            EditorApplication.Exit(0);
        }

        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        static string Measure(GameObject go, string key, StringBuilder log)
        {
            var an = go.GetComponent<Animator>(); if (an == null || !an.isHuman) throw new Exception("no humanoid animator");
            Func<HumanBodyBones, Transform> B = an.GetBoneTransform;
            var hips = B(HumanBodyBones.Hips); var lul = B(HumanBodyBones.LeftUpperLeg); var rul = B(HumanBodyBones.RightUpperLeg); var lll = B(HumanBodyBones.LeftLowerLeg); var lf = B(HumanBodyBones.LeftFoot);
            var lua = B(HumanBodyBones.LeftUpperArm); var rua = B(HumanBodyBones.RightUpperArm); var lla = B(HumanBodyBones.LeftLowerArm); var lh = B(HumanBodyBones.LeftHand);
            var neck = B(HumanBodyBones.Neck); var head = B(HumanBodyBones.Head); var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            Vector3 across = rul.position - lul.position; across.y = 0f;
            Vector3 fwd = Vector3.Cross(across.normalized, Vector3.up), right = Vector3.Cross(Vector3.up, fwd);
            // renderers (LOD0 of every part) in the bind pose
            var photo = go.GetComponent<PhotoPlayer>();
            var verts = new Dictionary<string, List<Vector3>>();
            float minY = 9f, maxY = -9f;
            foreach (var p in photo.parts)
            {
                if (p == null || p.lods == null || p.lods.Length == 0) continue;
                var smr = p.lods[0] as SkinnedMeshRenderer; if (smr == null) continue;
                var m = new Mesh(); smr.BakeMesh(m, true);
                var vs = m.vertices; var l = new List<Vector3>(vs.Length);
                for (int i = 0; i < vs.Length; i++) l.Add(smr.transform.TransformPoint(vs[i]));
                verts[p.key] = l; UnityEngine.Object.DestroyImmediate(m);
                foreach (var v in l) { if (v.y < minY) minY = v.y; if (v.y > maxY) maxY = v.y; }
            }
            float H = maxY - minY;
            Func<Vector3, float> lat = v => Vector3.Dot(v, right), dep = v => Vector3.Dot(v, fwd);
            Func<float, string[], Vector2> slice = (y, keys) =>
            {
                float lo = 9f, hi = -9f, dlo = 9f, dhi = -9f;
                foreach (var k in keys) { List<Vector3> l; if (!verts.TryGetValue(k, out l)) continue; foreach (var v in l) if (Mathf.Abs(v.y - y) < 0.02f) { float a = lat(v), d = dep(v); if (a < lo) lo = a; if (a > hi) hi = a; if (d < dlo) dlo = d; if (d > dhi) dhi = d; } }
                return new Vector2(hi - lo, dhi - dlo);
            };
            string[] torso = { "Jersey", "Pads", "Pants", "SleeveL", "SleeveR" };
            float hipsY = hips.position.y, chestY = chest.position.y;
            Vector2 sChest = slice(Mathf.Lerp(hipsY, chestY, 0.95f), new[] { "Jersey", "Pads" });
            Vector2 sWaist = slice(Mathf.Lerp(hipsY, chestY, 0.38f), new[] { "Jersey", "Pants" });
            Vector2 sPelvis = slice(hipsY - 0.02f, new[] { "Pants", "Jersey" });
            // thigh diameter: vertices of the pants halfway down the left thigh, left half only
            float ty = Mathf.Lerp(lul.position.y, lll.position.y, 0.5f), tdlo = 9f, tdhi = -9f, tlo = 9f, thi = -9f;
            List<Vector3> pl; if (verts.TryGetValue("Pants", out pl)) foreach (var v in pl) if (Mathf.Abs(v.y - ty) < 0.02f && lat(v) < lat(hips.position)) { float d = dep(v), a = lat(v); if (d < tdlo) tdlo = d; if (d > tdhi) tdhi = d; if (a < tlo) tlo = a; if (a > thi) thi = a; }
            float thighDia = Mathf.Max(0f, tdhi - tdlo);
            // upper-arm diameter: the sleeve / arm vertices half way along the left upper arm (the arm lies sideways in the bind pose)
            float armLat = lat(Vector3.Lerp(lua.position, lla.position, 0.5f)), alo = 9f, ahi = -9f;
            foreach (var k in new[] { "Arms", "SleeveL", "SleeveR", "Jersey" }) { List<Vector3> l; if (!verts.TryGetValue(k, out l)) continue; foreach (var v in l) if (Mathf.Abs(lat(v) - armLat) < 0.02f && Mathf.Abs(v.y - lua.position.y) < 0.2f) { if (v.y < alo) alo = v.y; if (v.y > ahi) ahi = v.y; } }
            float armDia = Mathf.Max(0f, ahi - alo);
            float thigh = Vector3.Distance(lul.position, lll.position), shin = Vector3.Distance(lll.position, lf.position);
            float uarm = Vector3.Distance(lua.position, lla.position), farm = Vector3.Distance(lla.position, lh.position);
            float torsoLen = Vector3.Distance(hips.position, neck != null ? neck.position : chest.position);
            float headH = 0f; List<Vector3> hv;
            if (verts.TryGetValue("Head", out hv) && hv.Count > 0) headH = hv.Max(v => v.y) - hv.Min(v => v.y);
            float shJoint = Vector3.Distance(lua.position, rua.position), hipJoint = Vector3.Distance(lul.position, rul.position);
            float legLen = hipsY - minY; float handLen = 0f; var mp = B(HumanBodyBones.LeftMiddleProximal); var mi = B(HumanBodyBones.LeftMiddleDistal); if (mp != null && lh != null) handLen = Vector3.Distance(lh.position, mp.position) * 2.2f;
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-28} H {1:0.00} hips {2:0.00} (leg {3:0.00}H) thigh {4:0.00}H shin {5:0.00}H uarm {6:0.00}H farm {7:0.00}H head {8:0.00}H | chest {9:0.00}x{10:0.00} waist {11:0.00}x{12:0.00} pelvis {13:0.00}x{14:0.00} m (width x depth) | thigh dia {15:0.00} arm dia {16:0.00}",
                key, H, hipsY - minY, legLen / H, thigh / H, shin / H, uarm / H, farm / H, headH / H, sChest.x, sChest.y, sWaist.x, sWaist.y, sPelvis.x, sPelvis.y, thighDia, armDia));
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "   metres: height {0:0.00}  hipsY {1:0.00} thigh {2:0.000} shin {3:0.000} upperArm {4:0.000} foreArm {5:0.000} ratio upperArm/foreArm {6:0.00} (human 1.25)  thigh/shin {7:0.00} (human 1.0)  head {8:0.00}  shoulderJoints {9:0.00} hipJoints {10:0.00} torso {11:0.00}", H, hipsY - minY, thigh, shin, uarm, farm, uarm / Mathf.Max(0.01f, farm), thigh / Mathf.Max(0.01f, shin), headH, shJoint, hipJoint, torsoLen));
            return string.Join(",", new[] { key, F(H), F(hipsY - minY), F(legLen / H), F(thigh / H), F(shin / H), F(uarm / H), F(farm / H), F(torsoLen / H), F(headH / H), F(shJoint / H), F(hipJoint / H), F(sChest.x), F(sChest.y), F(sWaist.x), F(sWaist.y), F(sPelvis.x), F(sPelvis.y), F(thighDia), F(armDia), F(sWaist.x / Mathf.Max(0.01f, sChest.x)), F(sWaist.x / Mathf.Max(0.01f, sPelvis.x)), F(sPelvis.x / Mathf.Max(0.01f, sChest.x)) });
        }
    }
}
