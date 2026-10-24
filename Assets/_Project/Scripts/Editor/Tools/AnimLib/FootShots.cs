using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB: feet close-up strip, before / after on the real player. For each clip of Logs/animlib/footshots.txt (clip name) the longest planted foot of the cleaned take is found and six frames
    /// across that plant are rendered from the side, close to the foot, with a thin vertical marker line standing where the foot landed (the planted foot should stay on the line). Row 1 = the v0.10 clip
    /// (Assets/AnimBackup/v0100/NAME.anim), row 2 = the clip now. Output D:/PIXFootball/Renders/animlib_v0110/feet_strip.png
    /// </summary>
    public static class FootShots
    {
        public const string BackupDir = "Assets/AnimBackup/v0100";

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            var names = AnimLibV11.Names("Logs/animlib/footshots.txt");
            HandLab.Setup();
            var rig = FootAxes.NewRig();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
            const int S = 320, F = 6;
            var rows = new List<Texture2D[]>();
            foreach (string nm in names)
            {
                var after = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(nm));
                var before = AssetDatabase.LoadAssetAtPath<AnimationClip>(BackupDir + "/" + ClipCleaner.SafeName(nm) + ".anim");
                if (after == null || before == null) { Debug.LogWarning("[FootShots] missing " + nm + " after=" + (after != null) + " before=" + (before != null)); continue; }
                var fr = rig.Sample(after, 0f, Mathf.Min(after.length, 12f), 30f);
                var sole = Plants.Soles(fr); float ground = Plants.Ground(sole);
                var plants = Plants.Find(sole, ground, 1f / 30f).OrderByDescending(p => p.i1 - p.i0).ToList();
                if (plants.Count == 0) continue;
                // the plant with the largest displacement in the old take (where the improvement shows)
                var fb = rig.Sample(before, 0f, Mathf.Min(before.length, 12f), 30f);
                Plant best = plants[0]; float bd = -1f;
                foreach (var p in plants.Take(6))
                {
                    if (p.i1 - p.i0 < 4) continue;
                    int fi = p.side == 0 ? AnimRig.LF : AnimRig.RF; int a = Mathf.Min(p.i0, fb.Count - 1), b = Mathf.Min(p.i1, fb.Count - 1);
                    float d = Vector3.Distance(new Vector3(fb[a].P[fi].x, 0f, fb[a].P[fi].z), new Vector3(fb[b].P[fi].x, 0f, fb[b].P[fi].z));
                    if (d > bd) { bd = d; best = p; }
                }
                int foot = best.side == 0 ? AnimRig.LF : AnimRig.RF;
                var pair = new Texture2D[2 * F];
                for (int row = 0; row < 2; row++)
                {
                    var clip = row == 0 ? before : after; var frames = row == 0 ? fb : fr;
                    for (int k = 0; k < F; k++)
                    {
                        int i = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(best.i0 - 1, best.i1 + 1, k / (F - 1f))), 0, frames.Count - 1);
                        float t = i / 30f;
                        // reference: where the foot is at the middle of the plant in the AFTER take (same marker for both rows)
                        int im = Mathf.Clamp((best.i0 + best.i1) / 2, 0, fr.Count - 1);
                        Vector3 anchor = fr[im].P[foot];
                        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        g.transform.position = Vector3.zero; g.transform.rotation = AnimRig.StartRot;
                        HandLab.Pose(g, clip, t);
                        var an = g.GetComponent<Animator>(); Transform ft = an.GetBoneTransform(best.side == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                        // side view across the direction of the body's travel / facing
                        Vector3 fwd = Vector3.forward; var hips = an.GetBoneTransform(HumanBodyBones.Hips); Vector3 lr = an.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - an.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position; lr.y = 0f; fwd = Vector3.Cross(lr.normalized, Vector3.up);
                        Vector3 side = Vector3.Cross(Vector3.up, fwd);
                        Vector3 look = new Vector3(anchor.x, 0.2f, anchor.z);
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.transform.position = new Vector3(anchor.x, 0.25f, anchor.z); marker.transform.localScale = new Vector3(0.006f, 0.5f, 0.006f);
                        marker.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotMarker.mat", new Color(0.95f, 0.15f, 0.1f), 0f, 0.2f);
                        var cam = new GameObject("Cam").AddComponent<Camera>(); cam.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                        cam.fieldOfView = 24f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.14f, 0.15f, 0.17f); cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f; cam.aspect = 1f;
                        cam.transform.position = look + side * 1.6f + Vector3.up * 0.05f; cam.transform.LookAt(look);
                        pair[row * F + k] = HandLab.Render(cam, S, S);
                        UnityEngine.Object.DestroyImmediate(cam.gameObject); UnityEngine.Object.DestroyImmediate(marker); UnityEngine.Object.DestroyImmediate(g);
                    }
                }
                rows.Add(pair);
                Debug.Log("[FootShots] " + nm + " plant " + best.i0 + "-" + best.i1 + (best.side == 0 ? " L" : " R"));
            }
            if (rows.Count > 0)
            {
                var sheet = new Texture2D(F * S, rows.Count * 2 * S, TextureFormat.RGB24, false);
                for (int r = 0; r < rows.Count; r++)
                    for (int row = 0; row < 2; row++)
                        for (int k = 0; k < F; k++) { var c = rows[r][row * F + k]; sheet.SetPixels(k * S, (rows.Count * 2 - 1 - (r * 2 + row)) * S, S, S, c.GetPixels()); UnityEngine.Object.DestroyImmediate(c); }
                string dir = Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animlib_v0110" : "Logs/animlib_sheets";
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(dir + "/feet_strip.png", sheet.EncodeToPNG());
                Debug.Log("[FootShots] wrote " + dir + "/feet_strip.png");
            }
            rig.Dispose();
            AnimLibBatch.Done();
        }
    }
}
