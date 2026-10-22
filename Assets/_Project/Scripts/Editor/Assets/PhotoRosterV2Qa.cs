using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// QA of the photoreal roster v2 (staging): pads before / after, the 15-body roster in PIX uniforms, helmet-off faces, position roster (ChooseBody), clip sheets and a numeric
    /// pose check on every body (AnimationMode.SampleAnimationClip, Foot IK is not involved), pad shoulder stress poses, the new Biped clips on S / M / L.
    /// Renders go to D:/PIXFootball/Renders/roster_v2.  Run WITHOUT -nographics.  -qaWhat pads,roster,faces,positions,clips,check,biped,motion
    /// </summary>
    public static class PhotoRosterV2Qa
    {
        static readonly string OutDir = Directory.Exists("D:/PIXFootball") ? "D:/PIXFootball/Renders/roster_v2" : "Docs/QA/photoreal_v2";
        static Camera _cam; static Light _sun;
        static double _t0;

        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        static void Watchdog(string what) { if (EditorApplication.timeSinceStartup - _t0 > 3300) throw new TimeoutException("PhotoRosterV2Qa watchdog at " + what); }

        public static void RunAll()
        {
            _t0 = EditorApplication.timeSinceStartup;
            Directory.CreateDirectory(OutDir);
            ArtLibrary.ResetCache(); PhotoPlayerLibrary.ResetCache();
            string what = Arg("-qaWhat", "pads,roster,faces,positions,check,clips,motion,biped");
            if (what.Contains("pads")) PadsCompare();
            if (what.Contains("roster")) Roster15(true, "roster15_sheet");
            if (what.Contains("faces")) Roster15(false, "roster15_faces");
            if (what.Contains("positions")) PositionRoster();
            if (what.Contains("check")) NumericCheck();
            if (what.Contains("clips")) BodyClips();
            if (what.Contains("motion")) PadMotion();
            if (what.Contains("biped")) BipedSheets();
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            Debug.Log("[RosterQa] done");
        }

        // ------------------------------------------------------------------ studio / render helpers (same as PhotoPlayerQa)
        static void Studio(float exposure = 12f)
        {
            AssetScreenshots.NewStudio(exposure, out _sun);
            _cam = AssetScreenshots.MakeCamera(new Vector3(0, 1f, 8f), new Vector3(0, 1f, 0), 22f);
            var hd = _cam.GetComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(0.22f, 0.25f, 0.30f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "QaFloor"; floor.transform.position = new Vector3(0, -0.02f, 0); floor.transform.localScale = new Vector3(14f, 0.04f, 14f);
            var fm = new Material(Shader.Find("HDRP/Lit")); fm.SetColor("_BaseColor", new Color(0.14f, 0.2f, 0.13f)); fm.SetFloat("_Smoothness", 0.15f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
        }

        static Texture2D Shot(Vector3 camPos, Vector3 look, float fov, int w, int h)
        {
            _cam.transform.position = camPos; _cam.transform.LookAt(look); _cam.fieldOfView = fov; _cam.aspect = (float)w / h;
            return AssetScreenshots.Render(_cam, w, h);
        }

        static void Bake(GameObject go)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                var ls = smr.transform.lossyScale; child.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = child.AddComponent<MeshRenderer>(); mr.sharedMaterials = smr.sharedMaterials; mr.shadowCastingMode = smr.shadowCastingMode;
                smr.enabled = false;
            }
        }

        static void Normalize(GameObject go)
        {
            var a = go.GetComponent<Animator>();
            var L = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; var R = a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
            var fwd = Vector3.Cross(R - L, Vector3.up); fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-6f) go.transform.RotateAround(a.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.up, Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up));
            var hp = a.GetBoneTransform(HumanBodyBones.Hips).position;
            go.transform.position -= new Vector3(hp.x, 0f, hp.z);
        }

        static void PoseClip(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || clip == null) return;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(anim.gameObject, clip, t);
        }

        static void Save(Texture2D t, string name)
        {
            File.WriteAllBytes(OutDir + "/" + name + ".png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            Debug.Log("[RosterQa] wrote " + OutDir + "/" + name + ".png");
        }

        static Texture2D Sheet(Texture2D[] cells, int cols, int cw, int ch)
        {
            int rows = (cells.Length + cols - 1) / cols;
            var t = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
            var fill = new Color32[t.width * t.height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(28, 30, 34, 255); t.SetPixels32(fill);
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == null) continue;
                int cx = i % cols, cy = i / cols;
                t.SetPixels32(cx * cw, (rows - 1 - cy) * ch, cw, ch, cells[i].GetPixels32());
                UnityEngine.Object.DestroyImmediate(cells[i]);
            }
            t.Apply(false);
            return t;
        }

        static readonly string[] Surnames = { "ALDER", "BRANDT", "CALLOWAY", "DUNMORE", "EVERETT", "FAIRBANK", "GRAYSON", "HOLLIS", "IRVING", "JASPER", "KESSLER", "LOWELL", "MARSDEN", "NORTON", "OAKLEY", "PRESCOTT", "QUINN", "RANDALL", "STANTON", "TALBOT", "UPTON", "VANCE", "WESTON", "YARROW" };
        static PhotoBody BodyOf(string size) { return size == "Small" ? PhotoBody.Small : size == "Large" ? PhotoBody.Large : PhotoBody.Medium; }

        /// <summary>Spawns a prefab from a path (staging or live) with colours, appearance and jersey look.</summary>
        static GameObject Spawn(string prefabPath, PlayerAppearance a, PhotoPlayerQa.Kit kit, int number, string surname, bool helmet = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning("[RosterQa] missing prefab " + prefabPath); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.identity;
            var pp = g.GetComponent<PhotoPlayer>();
            pp.autoLod = false; pp.Init();
            pp.SetTeamColors(kit.p, kit.s, kit.t);
            pp.ApplyAppearance(a);
            var look = new JerseyLook { Number = number, HasNumber = true, Fill = kit.t, Trim = kit.s, Name = surname, Wordmark = kit.wordmark, Dirt = default(DirtState) };
            var jm = pp.JerseyMaterial;
            if (jm != null) { PixLook.ApplyJersey(jm, look); pp.ApplyLayout(jm); }
            var tint = g.GetComponent<PhotoPadTint>(); if (tint != null) tint.Sync(true);
            if (!helmet) foreach (var k in new[] { "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor" }) pp.SetPartVisible(k, false);
            pp.SetLevel(0);
            return g;
        }

        static GameObject Staged(string size, string b, PlayerAppearance a, PhotoPlayerQa.Kit kit, int number, string surname, bool helmet = true)
        {
            return Spawn(PhotoRosterV2.PrefabPath(size, b), a, kit, number, surname, helmet);
        }

        // ------------------------------------------------------------------ pads before / after
        static void PadsCompare()
        {
            Studio();
            var cells = new List<Texture2D>();
            var kit = PhotoPlayerQa.Kits[1];
            foreach (string size in PhotoRosterV2.Sizes)
                foreach (bool after in new[] { false, true })
                {
                    string path = after ? PhotoRosterV2.PrefabPath(size, "Std") : PhotoPlayerBuilder.PrefabDir + "/PIX_PhotoPlayer_" + size + ".prefab";
                    foreach (float yaw in new[] { 0f, 38f, 90f })
                    {
                        var g = Spawn(path, PlayerAppearance.FromSeed(77, BodyOf(size), 1), kit, 12, "QUINN");
                        if (g == null) { cells.Add(null); continue; }
                        Bake(g);
                        g.transform.RotateAround(Vector3.zero, Vector3.up, yaw);
                        float sc = size == "Large" ? 1.1f : 1f;
                        cells.Add(Shot(new Vector3(0, 1.25f, 4.6f * sc), new Vector3(0, 1.05f, 0), 24f, 320, 520));
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                }
            Save(Sheet(cells.ToArray(), 3, 320, 520), "pads_before_after");
            File.WriteAllText(OutDir + "/pads_before_after.txt", "rows: Small before (live v0.6.0), Small after (pads), Medium before, Medium after, Large before, Large after; columns: front, 3/4, side (A-pose rest)");
        }

        // ------------------------------------------------------------------ the 15 bodies
        static void Roster15(bool helmet, string name)
        {
            Studio();
            var cells = new List<Texture2D>();
            var sb = new StringBuilder("rows: Small, Medium, Large; columns: " + string.Join(", ", PhotoRosterV2.Bases) + "\n");
            int idx = 0;
            foreach (string size in PhotoRosterV2.Sizes)
                foreach (string b in PhotoRosterV2.Bases)
                {
                    Watchdog(size + b);
                    int bi = Array.IndexOf(PhotoRosterV2.Bases, b);
                    int seed = 2000 + idx * 53;
                    int role = b == "Tall" ? PlayerAppearance.RoleQuarterback : size == "Large" ? PlayerAppearance.RoleLine : b == "Runner" || b == "Athletic" ? PlayerAppearance.RoleReceiver : PlayerAppearance.RoleSkill;
                    var a = PlayerAppearance.FromSeed(seed, BodyOf(size), role, (BodyBase)bi);
                    var kit = PhotoPlayerQa.Kits[idx % PhotoPlayerQa.Kits.Length];
                    var g = Staged(size, b, a, kit, 4 + (idx * 13) % 95, Surnames[idx % Surnames.Length], helmet);
                    if (g == null) { cells.Add(null); idx++; continue; }
                    PoseClip(g, ArtLibrary.Load().ClipVariant("idle", 0), 0.6f);
                    Normalize(g);
                    Bake(g);
                    g.transform.RotateAround(Vector3.zero, Vector3.up, helmet ? 24f : 14f);
                    float h = b == "Tall" ? 1.07f : 1f;
                    if (helmet) cells.Add(Shot(new Vector3(0, 1.45f, 4.9f * h), new Vector3(0, 0.98f * h, 0), 22f, 320, 500));
                    else
                    {
                        var head = g.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head).position;
                        cells.Add(Shot(head + new Vector3(0.35f, 0.04f, 1.25f), head + new Vector3(0, 0.02f, 0), 18f, 320, 380));
                    }
                    sb.AppendLine(size + " " + b + " seed " + seed + " skin " + a.SkinTone + " head " + a.HeadA + "+" + a.HeadB);
                    UnityEngine.Object.DestroyImmediate(g);
                    idx++;
                }
            Save(Sheet(cells.ToArray(), PhotoRosterV2.Bases.Length, 320, helmet ? 500 : 380), name);
            File.WriteAllText(OutDir + "/" + name + ".txt", sb.ToString());
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // ------------------------------------------------------------------ position -> body mapping
        struct Pos { public string code; public int h, w; public Pos(string c, int h, int w) { code = c; this.h = h; this.w = w; } }
        static readonly Pos[] Positions =
        {
            new Pos("QB", 193, 98), new Pos("QB", 185, 92), new Pos("WR", 178, 82), new Pos("WR", 196, 100), new Pos("RB", 178, 95), new Pos("FB", 185, 112),
            new Pos("TE", 201, 114), new Pos("TE", 193, 125), new Pos("OT", 198, 150), new Pos("OG", 190, 145), new Pos("C", 188, 138), new Pos("DE", 193, 125),
            new Pos("DT", 190, 142), new Pos("NT", 188, 150), new Pos("OLB", 190, 108), new Pos("MLB", 185, 112), new Pos("ILB", 183, 105), new Pos("CB", 180, 86),
            new Pos("CB", 185, 92), new Pos("FS", 183, 92), new Pos("SS", 185, 100), new Pos("S", 180, 98), new Pos("K", 180, 84), new Pos("P", 188, 95),
        };

        static void PositionRoster()
        {
            Studio();
            var cells = new List<Texture2D>();
            var sb = new StringBuilder();
            for (int i = 0; i < Positions.Length; i++)
            {
                Watchdog("pos" + i);
                var p = Positions[i];
                PhotoBody size; BodyBase bs;
                PlayerAppearance.ChooseBody(p.code, p.h, p.w, out size, out bs);
                int role = p.code == "QB" ? 1 : (p.code == "OT" || p.code == "OG" || p.code == "C" || p.code == "DE" || p.code == "DT" || p.code == "NT") ? 2 : (p.code == "WR" || p.code == "CB") ? 3 : 0;
                var a = PlayerAppearance.FromSeed(5000 + i * 31, size, role, bs);
                var g = Staged(size.ToString(), bs.ToString(), a, PhotoPlayerQa.Kits[(i / 4) % PhotoPlayerQa.Kits.Length], 3 + (i * 11) % 96, Surnames[i % Surnames.Length], true);
                sb.AppendLine(p.code + " " + p.h + " cm " + p.w + " kg -> " + size + " " + bs + "  key " + PlayerAppearance.PrefabKey(size, bs));
                if (g == null) { cells.Add(null); continue; }
                PoseClip(g, ArtLibrary.Load().ClipVariant("idle", 0), 0.6f);
                Normalize(g); Bake(g);
                g.transform.RotateAround(Vector3.zero, Vector3.up, 22f);
                float h = bs == BodyBase.Tall ? 1.07f : size == PhotoBody.Large ? 1.12f : 1f;
                cells.Add(Shot(new Vector3(0, 1.5f, 4.9f * h), new Vector3(0, 0.98f * h, 0), 22f, 300, 480));
                UnityEngine.Object.DestroyImmediate(g);
            }
            Save(Sheet(cells.ToArray(), 8, 300, 480), "roster_positions");
            File.WriteAllText(OutDir + "/roster_positions.txt", sb.ToString());
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // ------------------------------------------------------------------ clips
        static readonly string[] ClipKeys =
        {
            "idle", "stance_2pt", "stance_3pt", "dl_stance", "ol_stance", "qb_stance_shotgun", "run", "sprint", "backpedal", "cut_hard", "stiff_arm", "tackle_wrap",
            "be_tackled", "ol_pass_set", "ol_block_engage", "dl_rush_swim", "qb_throw", "qb_drop_5", "catch_high", "catch_low", "catch_dive", "celebrate_td", "fall", "hurdle",
        };
        static readonly string[] SheetKeys = { "idle", "stance_3pt", "dl_stance", "run", "sprint", "backpedal", "tackle_wrap", "qb_throw", "catch_high", "catch_dive", "celebrate_td", "hurdle" };

        static void BodyClips()
        {
            Studio(11f);
            var lib = ArtLibrary.Load();
            foreach (string b in PhotoRosterV2.Bases)
            {
                var cells = new List<Texture2D>();
                foreach (string size in PhotoRosterV2.Sizes)
                    foreach (string key in SheetKeys)
                    {
                        Watchdog(b + size + key);
                        var clip = lib.ClipVariant(key, 0);
                        if (clip == null) { cells.Add(null); continue; }
                        var g = Staged(size, b, PlayerAppearance.FromSeed(77, BodyOf(size), 1, (BodyBase)Array.IndexOf(PhotoRosterV2.Bases, b)), PhotoPlayerQa.Kits[1], 21, "QUINN");
                        if (g == null) { cells.Add(null); continue; }
                        PoseClip(g, clip, clip.length * 0.45f);
                        Normalize(g); Bake(g);
                        cells.Add(Shot(new Vector3(0f, 1.2f, 6.0f), new Vector3(0, 0.95f, 0), 26f, 240, 290));
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                Save(Sheet(cells.ToArray(), SheetKeys.Length, 240, 290), "clips_" + b.ToLowerInvariant());
            }
            File.WriteAllText(OutDir + "/clips_index.txt", "per base sheet: rows Small, Medium, Large; columns: " + string.Join(", ", SheetKeys));
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        static readonly string[] StressKeys = { "catch_high", "qb_throw", "stiff_arm", "dl_rush_swim", "ol_block_engage", "celebrate_td", "tackle_wrap", "catch_dive" };

        static void PadMotion()
        {
            Studio(11f);
            var lib = ArtLibrary.Load();
            var cells = new List<Texture2D>();
            foreach (string size in PhotoRosterV2.Sizes)
                foreach (string key in StressKeys)
                {
                    var clip = lib.ClipVariant(key, 0);
                    var g = Staged(size, "Std", PlayerAppearance.FromSeed(77, BodyOf(size), 1), PhotoPlayerQa.Kits[2], 21, "QUINN", false);
                    if (g == null || clip == null) { cells.Add(null); continue; }
                    PoseClip(g, clip, clip.length * 0.45f);
                    Normalize(g); Bake(g);
                    var sp = g.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Chest).position;
                    cells.Add(Shot(sp + new Vector3(0.9f, 0.2f, 2.4f), sp + new Vector3(0, 0.05f, 0), 30f, 300, 300));
                    UnityEngine.Object.DestroyImmediate(g);
                }
            Save(Sheet(cells.ToArray(), StressKeys.Length, 300, 300), "pads_motion");
            File.WriteAllText(OutDir + "/pads_motion.txt", "rows Small, Medium, Large (Std base); columns: " + string.Join(", ", StressKeys));
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // ------------------------------------------------------------------ numeric pose check on every body
        static float LegRatio(Animator a, bool left)
        {
            var hip = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg).position;
            var knee = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg).position;
            var foot = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
            return Vector3.Distance(hip, foot) / (Vector3.Distance(hip, knee) + Vector3.Distance(knee, foot));
        }

        static float ArmRatio(Animator a, bool left)
        {
            var sh = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
            var el = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
            var hd = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
            return Vector3.Distance(sh, hd) / (Vector3.Distance(sh, el) + Vector3.Distance(el, hd));
        }

        static Bounds BakedBounds(SkinnedMeshRenderer smr)
        {
            var m = new Mesh(); smr.BakeMesh(m, false);
            var b = m.bounds; var t = smr.transform;
            var res = new Bounds(t.TransformPoint(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                res.Encapsulate(t.TransformPoint(c));
            }
            UnityEngine.Object.DestroyImmediate(m);
            return res;
        }

        static void NumericCheck()
        {
            Studio(11f);
            var lib = ArtLibrary.Load();
            var sb = new StringBuilder();
            int fails = 0, cellsN = 0;
            sb.AppendLine("Pose check of every body (AnimationMode.SampleAnimationClip at 45 % of each clip): leg / arm stretch ratio (hip-foot / bone chain) must agree with the Std body of the same size within 0.10,");
            sb.AppendLine("bone-driven heights within 6 % of the body height, and the baked LOD0 meshes must contain their bones (jersey / pads: chest, pants: hips + knee, cleats: feet, head / helmet: head bone).");
            foreach (string size in PhotoRosterV2.Sizes)
            {
                var refAnim = new Dictionary<string, float[]>();
                foreach (string b in PhotoRosterV2.Bases)
                {
                    var g = Staged(size, b, PlayerAppearance.FromSeed(77, BodyOf(size), 1, (BodyBase)Array.IndexOf(PhotoRosterV2.Bases, b)), PhotoPlayerQa.Kits[1], 21, "QUINN");
                    if (g == null) { sb.AppendLine("MISSING " + size + " " + b); fails++; continue; }
                    var an = g.GetComponent<Animator>();
                    var pp = g.GetComponent<PhotoPlayer>();
                    int bodyFails = 0;
                    foreach (string key in ClipKeys)
                    {
                        Watchdog(size + b + key);
                        var clip = lib.ClipVariant(key, 0);
                        if (clip == null) { sb.AppendLine("  " + size + " " + b + " " + key + " MISSING clip"); continue; }
                        PoseClip(g, clip, clip.length * 0.45f);
                        cellsN++;
                        float[] m = { LegRatio(an, true), LegRatio(an, false), ArmRatio(an, true), ArmRatio(an, false) };
                        float hipY = an.GetBoneTransform(HumanBodyBones.Hips).position.y, headY = an.GetBoneTransform(HumanBodyBones.Head).position.y;
                        string msg = null;
                        if (b == "Std") refAnim[key] = m;
                        else
                        {
                            float[] r;
                            if (refAnim.TryGetValue(key, out r)) for (int i = 0; i < 4; i++) if (Mathf.Abs(m[i] - r[i]) > 0.10f) msg = "stretch ratio " + i + " " + m[i].ToString("0.00") + " vs Std " + r[i].ToString("0.00");
                        }
                        if (m[0] < 0.18f || m[1] < 0.18f) msg = (msg ?? "") + " folded leg " + m[0].ToString("0.00") + "/" + m[1].ToString("0.00");
                        // mesh follows the skeleton
                        var head = an.GetBoneTransform(HumanBodyBones.Head).position;
                        foreach (var part in pp.parts)
                        {
                            var smr = part.lods[0] as SkinnedMeshRenderer;
                            if (smr == null || !smr.gameObject.activeInHierarchy) continue;
                            string k = part.key; Bounds bb;
                            if (k == "Jersey" || k == "Pads") { bb = BakedBounds(smr); if (bb.SqrDistance(an.GetBoneTransform(HumanBodyBones.Chest).position) > 0.0036f) msg = (msg ?? "") + " " + k + " away from chest"; }
                            else if (k == "Pants") { bb = BakedBounds(smr); if (bb.SqrDistance(an.GetBoneTransform(HumanBodyBones.Hips).position) > 0.0036f || bb.SqrDistance(an.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position) > 0.0100f) msg = (msg ?? "") + " Pants off the legs"; }
                            else if (k == "Cleats") { bb = BakedBounds(smr); if (bb.SqrDistance(an.GetBoneTransform(HumanBodyBones.LeftFoot).position) > 0.0100f || bb.SqrDistance(an.GetBoneTransform(HumanBodyBones.RightFoot).position) > 0.0100f) msg = (msg ?? "") + " Cleats off the feet"; }
                            else if (k == "Head") { bb = BakedBounds(smr); if (bb.SqrDistance(head) > 0.0036f) msg = (msg ?? "") + " Head off the head bone"; }
                        }
                        foreach (var part in pp.parts.Where(x => x.key == "Helmet_Shell"))
                        {
                            var mr = part.lods[0]; if (mr == null) continue;
                            var hb = mr.bounds; if (hb.SqrDistance(head) > 0.0025f) msg = (msg ?? "") + " Helmet off the head";
                        }
                        if (msg != null) { sb.AppendLine("  FAIL " + size + " " + b + " " + key + ": " + msg); bodyFails++; fails++; }
                    }
                    sb.AppendLine(size + " " + b + (bodyFails == 0 ? ": all " + ClipKeys.Length + " clips OK" : ": " + bodyFails + " clip(s) failed"));
                    UnityEngine.Object.DestroyImmediate(g);
                }
            }
            sb.AppendLine("TOTAL poses " + cellsN + " failures " + fails);
            File.WriteAllText(OutDir + "/pose_check.txt", sb.ToString());
            Debug.Log("[RosterQa] pose check: poses " + cellsN + " failures " + fails);
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }

        // ------------------------------------------------------------------ Biped clips on S / M / L
        static void BipedSheets()
        {
            string json = PhotoRosterV2.ClipDir + "/clips.json";
            if (!File.Exists(json)) { Debug.LogWarning("[RosterQa] no clips.json (run ImportAndClean first)"); return; }
            var man = JsonUtility.FromJson<PhotoRosterV2.ClipManifest>(File.ReadAllText(json));
            var recs = man.clips.Where(c => !c.key.EndsWith("_m")).ToList();
            Studio(11f);
            foreach (string size in PhotoRosterV2.Sizes)
            {
                var cells = new List<Texture2D>();
                foreach (var rc in recs)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PhotoRosterV2.ClipDir + "/" + rc.name + ".anim");
                    for (int k = 0; k < 6; k++)
                    {
                        Watchdog(size + rc.key);
                        if (clip == null) { cells.Add(null); continue; }
                        var g = Staged(size, "Std", PlayerAppearance.FromSeed(77, BodyOf(size), 1), PhotoPlayerQa.Kits[0], 21, "QUINN");
                        PoseClip(g, clip, clip.length * (k / 5f) * 0.999f);
                        Normalize(g); Bake(g);
                        cells.Add(Shot(new Vector3(2.4f, 1.15f, 5.4f), new Vector3(0, 0.95f, 0), 28f, 220, 280));
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                }
                Save(Sheet(cells.ToArray(), 6, 220, 280), "biped_clips_" + size.ToLowerInvariant());
            }
            File.WriteAllText(OutDir + "/biped_clips_index.txt", "rows: " + string.Join(", ", recs.Select(r => r.key + " (" + r.length.ToString("0.00") + " s)").ToArray()) + "; columns: 0, 20, 40, 60, 80, 100 % of the clip; three sheets Small / Medium / Large");
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        }
    }
}
