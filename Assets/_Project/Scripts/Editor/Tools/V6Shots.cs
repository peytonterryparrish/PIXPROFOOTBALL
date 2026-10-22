using System;
using System.Collections.Generic;
using System.IO;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Edit-mode look development renders for the v0.6 graphics pass (no play mode needed, runs in batch mode without -nographics):
    /// <c>Unity -batchmode -executeMethod PixFootball.EditorTools.V6Shots.Dev -v6weather 0..4 -v6time 0..2 -v6legacy 0|1 -v6set a,b,c</c>. Output: Logs/v6shots/*.png.
    /// </summary>
    public static class V6Shots
    {
        const string Out = "Logs/v6shots";

        static string Arg(string name, string def) { string[] a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }

        static Texture2D Render(Camera cam, int w, int h, int frames = 3)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < frames; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release();
            return tex;
        }

        static void Save(Camera cam, string name, int w = 1920, int h = 1080)
        {
            var t = Render(cam, w, h);
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(Out + "/" + name + ".png", t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            Debug.Log("[V6Shots] " + name);
        }

        static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || clip == null) return;
            PlayableGraph graph;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f); graph.Evaluate(t); graph.Destroy();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = child.AddComponent<MeshRenderer>(); mr.sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
            }
        }

        static Camera MakeCam()
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f; cam.allowHDR = true;
            return cam;
        }

        static void Look(Camera cam, Vector3 pos, Vector3 target, float fov) { cam.transform.position = pos; cam.transform.LookAt(target); cam.fieldOfView = fov; }

        static PlayerAvatar Spawn(PlayerSize size, Vector3 pos, float yaw, int number, UniformSet kit, DirtState dirt, AnimationClip clip, float t)
        {
            var av = PlayerAvatar.Spawn(null, size, kit.Uniform, "P" + number);
            av.SetColors(kit.Uniform); av.SetHelmetColors(kit.Helmet);
            var jl = JerseyLook.For(number, kit, kit.Home, dirt); jl.Name = number % 2 == 0 ? "ALDER" : "MC-BRIDE"; av.SetJersey(jl);
            av.transform.position = pos; av.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            { var hm = av.HelmetInstance; if (hm != null) Debug.Log("[V6Shots] helmet mat " + hm.name + " shader=" + hm.shader.name + " sec=" + hm.GetColor("_PixSecondary") + " ter=" + hm.GetColor("_PixTertiary") + " pri=" + hm.GetColor("_PixPrimary") + " HW0=" + hm.GetVector("_PixHW0") + " HW1=" + hm.GetVector("_PixHW1") + " HW2=" + hm.GetVector("_PixHW2") + " dec=" + hm.GetVector("_PixHelmetDecal")); }
            Pose(av.gameObject, clip, t); av.UpdateHelmetFrame();   // edit mode has no LateUpdate
            return av;
        }

        static UniformSet HomeKit() { var u = new UniformSet(); u.Uniform = TeamColors.FromHex("#0C3CB4", "#FC0C24", "#FFFFFF"); u.Helmet = TeamColors.FromHex("#0C3CB4", "#FC0C24", "#FFFFFF"); u.Home = true; u.Wordmark = "IRONHORNS"; u.CrestKey = "crest_buffalo_ironhorns"; return u; }
        static UniformSet AwayKit() { var u = new UniformSet(); var c = TeamColors.FromHex("#E8751A", "#101820", "#FFFFFF"); u.Uniform = new TeamColors(new Color(0.78f, 0.79f, 0.82f), new Color(0.74f, 0.75f, 0.78f), c.Primary); u.Helmet = c; u.Home = false; u.Wordmark = "REEFRUNNERS"; u.CrestKey = "crest_miami_reefrunners"; return u; }

        static DirtState Dirty(float amount, bool wet)
        {
            var s = new DirtState();
            var ctx = wet ? new DirtContext { Wetness = 1f } : DirtContext.Dry;
            int n = Mathf.RoundToInt(amount * 14f);
            for (int i = 0; i < n; i++) { DirtModel.Apply(ref s, (DirtEvent)(1 + (i % 9)), 0.9f, ctx); }
            return s;
        }

        /// <summary>Sets up the scene (lighting, weather, wear) for one preset.</summary>
        static WeatherSystem Setup(WeatherKind kind, TimeOfDay time, bool legacy, float snow, bool artificial)
        {
            var lc = UnityEngine.Object.FindFirstObjectByType<LightingController>();
            PixLook.Legacy = legacy;
            PixLookAssets.ResetCache();
            var ws = WeatherSystem.Ensure(lc);
            ws.Apply(kind, time, artificial);
            if (snow >= 0f) ws.SetSnowCover(snow);
            return ws;
        }

        /// <summary>Rebuilds the uniform textures and materials, then renders (one editor launch per iteration).</summary>
        public static void BuildAndDev()
        {
            if (Arg("-v6art", "1") == "1") { V6ArtBuilder.BuildUniformTextures(); AssetDatabase.Refresh(); V6ArtBuilder.BuildMaterials(); AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
            Dev();
        }

        public static void Dev()
        {
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            AssetDatabase.Refresh();
            int wi = int.Parse(Arg("-v6weather", "0")), ti = int.Parse(Arg("-v6time", "2"));
            bool legacy = Arg("-v6legacy", "0") == "1";
            bool wet = wi == 2;
            string tag = Arg("-v6tag", "") + WeatherPresets.Kinds[wi] + "_" + WeatherPresets.Times[ti] + (legacy ? "_legacy" : "");
            float snow = WeatherPresets.Kinds[wi] == WeatherKind.Snow ? float.Parse(Arg("-v6snow", "0.75")) : -1f;
            string tierArg = Arg("-v6tier", ""); if (tierArg != "") QualityTiers.Override(int.Parse(tierArg), -1f);
            var ws = Setup(WeatherPresets.Kinds[wi], WeatherPresets.Times[ti], legacy, snow, Arg("-v6turf", "natural") == "artificial");
            { var lcc = UnityEngine.Object.FindFirstObjectByType<LightingController>(); Debug.Log("[V6Shots] diag: daySun=" + (lcc.DaySun != null ? lcc.DaySun.intensity + " lux, active=" + lcc.DaySun.gameObject.activeInHierarchy + " enabled=" + lcc.DaySun.enabled : "null") + " nightSun=" + (lcc.NightSun != null ? lcc.NightSun.intensity + " active=" + lcc.NightSun.gameObject.activeInHierarchy : "null") + " dayRoot=" + lcc.DayRoot.activeSelf + " flood=" + lcc.FloodRoot.activeSelf + " renderSun=" + (RenderSettings.sun != null ? RenderSettings.sun.name : "null")); }
            var wear = FieldWear.Create(null);
            string dbg = Arg("-v6dbg", "");
            if (dbg != "")
            {
                var cam0 = MakeCam(); var home0 = HomeKit();
                var av = Spawn(PlayerSize.Medium, new Vector3(2f, 0f, -22f), 0f, 12, home0, Dirty(0.25f, true), AnimatorControllerBuilder.FindClipByName("Player_Standing_Idle"), 0.2f);
                foreach (string d in dbg.Split(','))
                {
                    Shader.SetGlobalFloat("_PixDebug", float.Parse(d));
                    Look(cam0, new Vector3(3.2f, 1.9f, -21.2f), new Vector3(2f, 1.75f, -22f), 22f); Save(cam0, "dbg_" + d, 960, 540);
                }
                Shader.SetGlobalFloat("_PixDebug", 0f);
                UnityEngine.Object.DestroyImmediate(av.gameObject); UnityEngine.Object.DestroyImmediate(cam0.gameObject);
                return;
            }
            var set = Arg("-v6set", "portrait,back,dirt,helmet,field,broadcast,crowd").Split(',');
            var idle = AnimatorControllerBuilder.FindClipByName("Player_Standing_Idle");
            var cam = MakeCam();
            var home = HomeKit(); var away = AwayKit();

            // a worn pitch: divots and footsteps around the line of scrimmage, a muddy / snowy trail
            if (wear != null && wear.Ready)
            {
                var rnd = new System.Random(4);
                for (int i = 0; i < 90; i++) wear.Divot(new Vector3((float)(rnd.NextDouble() - 0.5) * 22f, 0, -22f + (float)(rnd.NextDouble() - 0.5) * 16f), 0.4f + (float)rnd.NextDouble() * 0.5f, 0.5f, wet ? 1f : 0f);
                for (int i = 0; i < 400; i++) wear.Footstep(new Vector3(-4f + i * 0.02f + (float)(rnd.NextDouble() - 0.5) * 2f, 0, -26f + i * 0.03f), 0.2f, 0.012f * 3, wet ? 0.1f : 0f, snow > 0f ? 0.6f : 0f);
                wear.Flush();
            }

            Vector3 P = new Vector3(2f, 0f, -22f);
            var players = new List<GameObject>();
            Func<PlayerSize, Vector3, float, int, UniformSet, DirtState, GameObject> add = (size, pos, yaw, num, kit, dirt) => { var a = Spawn(size, pos, yaw, num, kit, dirt, idle, 0.2f); players.Add(a.gameObject); return a.gameObject; };

            foreach (string s in set)
            {
                switch (s.Trim())
                {
                    case "portrait":
                    {
                        var g = add(PlayerSize.Medium, P, 0f, 12, home, Dirty(0.25f, wet));
                        Look(cam, P + new Vector3(1.0f, 1.5f, 3.2f), P + new Vector3(0f, 1.25f, 0f), 26f); Save(cam, "portrait_" + tag);
                        Look(cam, P + new Vector3(2.4f, 1.3f, 2.3f), P + new Vector3(0f, 1.3f, 0f), 18f); Save(cam, "torso_" + tag);
                        UnityEngine.Object.DestroyImmediate(g);
                        break;
                    }
                    case "back":
                    {
                        var g = add(PlayerSize.Medium, P, 0f, 87, away, Dirty(0.55f, wet));
                        Look(cam, P + new Vector3(-0.8f, 1.5f, -3.2f), P + new Vector3(0f, 1.25f, 0f), 26f); Save(cam, "back_" + tag);
                        UnityEngine.Object.DestroyImmediate(g);
                        var g2 = add(PlayerSize.Medium, P, 0f, 7, home, Dirty(0.1f, wet));
                        Look(cam, P + new Vector3(-0.8f, 1.5f, -3.2f), P + new Vector3(0f, 1.25f, 0f), 26f); Save(cam, "backhome_" + tag);
                        UnityEngine.Object.DestroyImmediate(g2);
                        break;
                    }
                    case "dirt":
                    {
                        var list = new List<GameObject>();
                        float[] levels = { 0f, 0.2f, 0.45f, 0.75f, 1.2f };
                        for (int i = 0; i < levels.Length; i++) list.Add(add(PlayerSize.Medium, P + new Vector3((i - 2) * 1.5f, 0f, 0f), 0f, 10 + i * 11, i % 2 == 0 ? home : away, Dirty(levels[i], wet)));
                        Look(cam, P + new Vector3(0f, 1.5f, 6.5f), P + new Vector3(0f, 1.1f, 0f), 34f); Save(cam, "dirt_front_" + tag);
                        Look(cam, P + new Vector3(0f, 1.5f, -6.5f), P + new Vector3(0f, 1.1f, 0f), 34f); Save(cam, "dirt_back_" + tag);
                        foreach (var g in list) UnityEngine.Object.DestroyImmediate(g);
                        break;
                    }
                    case "helmet":
                    {
                        var d = Dirty(1.0f, wet);
                        var g = add(PlayerSize.Medium, P, 0f, 55, home, d);
                        Look(cam, P + new Vector3(0.7f, 1.78f, 1.0f), P + new Vector3(0f, 1.7f, 0f), 22f); Save(cam, "helmet_" + tag);
                        Look(cam, P + new Vector3(-0.9f, 1.78f, 0.7f), P + new Vector3(0f, 1.7f, 0f), 22f); Save(cam, "helmet_side2_" + tag);
                        Look(cam, P + new Vector3(0.35f, 1.8f, -1.05f), P + new Vector3(0f, 1.7f, 0f), 22f); Save(cam, "helmet_back_" + tag);
                        Look(cam, P + new Vector3(-0.8f, 1.72f, -0.8f), P + new Vector3(0f, 1.7f, 0f), 22f); Save(cam, "helmet_backleft_" + tag);
                        Look(cam, P + new Vector3(0.1f, 2.5f, -0.25f), P + new Vector3(0f, 1.78f, 0f), 28f); Save(cam, "helmet_top_" + tag);
                        UnityEngine.Object.DestroyImmediate(g);
                        break;
                    }
                    case "field":
                    {
                        Look(cam, new Vector3(-3f, 0.55f, -26f), new Vector3(3f, 0f, -22f), 38f); Save(cam, "field_macro_" + tag);
                        Look(cam, new Vector3(-10f, 6f, -32f), new Vector3(0f, 0f, -20f), 55f); Save(cam, "field_mid_" + tag);
                        break;
                    }
                    case "broadcast":
                    {
                        for (int i = 0; i < 11; i++)
                        {
                            float z = -22f - (i == 0 ? 6f : (i < 6 ? 0.8f : (i < 8 ? 4f : 0.9f)));
                            float x = (i - 5) * 2.6f; if (i == 0) x = 0f; if (i >= 6 && i < 8) x = (i == 6 ? -14f : 14f);
                            add(PlayerSize.Medium, new Vector3(x, 0f, z), 0f, 1 + i * 8, home, Dirty(0.2f * (i % 4), wet));
                            add(PlayerSize.Medium, new Vector3(x * 0.9f, 0f, -21f + 1.0f + (i > 6 ? 5f : 0f)), 180f, 90 - i * 7, away, Dirty(0.15f * (i % 3), wet));
                        }
                        Look(cam, new Vector3(-50f, 19f, -22f), new Vector3(0f, 1f, -22f), 26f); Save(cam, "broadcast_" + tag);
                        Look(cam, new Vector3(-26f, 6.5f, -30f), new Vector3(0f, 1.2f, -22f), 32f); Save(cam, "broadcast_low_" + tag);
                        break;
                    }
                    case "crowd":
                    {
                        Look(cam, new Vector3(-40f, 12f, -35f), new Vector3(36f, 10f, 5f), 55f); Save(cam, "crowd_" + tag);
                        Look(cam, new Vector3(-95f, 38f, -20f), new Vector3(60f, 20f, 10f), 50f); Save(cam, "crowd_wide_" + tag);
                        break;
                    }
                }
            }
            foreach (var g in players) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            PixLook.Legacy = false;
        }
    }
}
