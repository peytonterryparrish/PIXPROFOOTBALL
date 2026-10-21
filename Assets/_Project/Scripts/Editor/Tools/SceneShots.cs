using System.Collections.Generic;
using System.IO;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>Edit-mode renders of the built scenes (lighting / branding checks). Run without -nographics. Output: Docs/Screens/v010_*.png</summary>
    public static class SceneShots
    {
        const string Out = "Docs/Screens";

        static Texture2D Render(Camera cam, int w, int h, int frames = 4)
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

        static void Save(Camera cam, string name, int w = 1600, int h = 900)
        {
            var t = Render(cam, w, h);
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(Out + "/v010_" + name + ".png", t.EncodeToPNG());
            Object.DestroyImmediate(t);
            Debug.Log("[Shots] " + name);
        }

        static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || clip == null) return;
            PlayableGraph graph;
            AnimationPlayableUtilities.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f); graph.Evaluate(t); graph.Destroy();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                child.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
            }
        }

        static Camera Cam()
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.nearClipPlane = 0.2f; cam.farClipPlane = 1500f; cam.allowHDR = true;
            return cam;
        }

        static void Look(Camera cam, Vector3 pos, Vector3 target, float fov) { cam.transform.position = pos; cam.transform.LookAt(target); cam.fieldOfView = fov; }

        public static void Stadium()
        {
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            var lc = Object.FindFirstObjectByType<LightingController>();
            var lib = ArtLibrary.Load();
            // player
            var player = PlayerAvatar.Spawn(null, PlayerSize.Medium, TeamColors.FromHex("#0C3CB4", "#E9EDF2", "#FC0C24"), "ShotPlayer");
            player.transform.position = new Vector3(2f, 0f, -22f);
            var clipRun = AnimatorControllerBuilder.FindClipByName("RunCycleJR_1");
            Pose(player.gameObject, clipRun, 0.3f);
            var cam = Cam();
            foreach (var preset in new[] { LightingPreset.Night, LightingPreset.Day })
            {
                lc.Apply(preset);
                string tag = preset == LightingPreset.Night ? "night" : "day";
                Vector3 p = player.transform.position;
                Look(cam, new Vector3(-42f, 15.5f, p.z), p + new Vector3(0, 1.1f, 0), 21f); Save(cam, tag + "_broadcast");
                Look(cam, p + new Vector3(0.5f, 2.7f, -6.4f), p + new Vector3(0, 1.35f, 3f), 58f); Save(cam, tag + "_behind");
                Look(cam, new Vector3(-20f, 12f, -60f), new Vector3(0f, 2f, 10f), 55f); Save(cam, tag + "_wide");
                Look(cam, new Vector3(-95f, 38f, -20f), new Vector3(60f, 20f, 10f), 50f); Save(cam, tag + "_crowd");
                Look(cam, new Vector3(-250f, 140f, -180f), new Vector3(0f, 10f, 0f), 42f); Save(cam, tag + "_aerial");
                Look(cam, new Vector3(-28f, 2.2f, 34f), new Vector3(0f, 3f, 55f), 50f); Save(cam, tag + "_goalpost");
                Look(cam, new Vector3(-27f, 1.7f, -4f), new Vector3(-30.4f, 1.0f, 8f), 55f); Save(cam, tag + "_sideline");
                Look(cam, new Vector3(0f, 40f, -2f), new Vector3(0f, 0f, 0f), 50f); cam.transform.rotation = Quaternion.Euler(90f, 90f, 0f); Save(cam, tag + "_midfield");
            }
            // close-ups of the player (uniform branding check)
            {
                Vector3 p = player.transform.position;
                player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                lc.Apply(LightingPreset.Day);
                Look(cam, p + new Vector3(1.2f, 1.4f, 3.4f), p + new Vector3(0f, 1.15f, 0f), 30f); Save(cam, "player_front");
                Look(cam, p + new Vector3(-3.2f, 1.4f, 0.6f), p + new Vector3(0f, 1.15f, 0f), 30f); Save(cam, "player_side");
                Look(cam, p + new Vector3(0.8f, 1.5f, -3.2f), p + new Vector3(0f, 1.2f, 0f), 30f); Save(cam, "player_back");
            }
            // HUD overlay
            {
                lc.Apply(LightingPreset.Night);
                var ui = GameObject.Find("FreeRoamUI");
                if (ui != null)
                {
                    ui.GetComponent<PixFootball.UI.FreeRoamHud>().SendMessage("Start");
                    foreach (var cv in ui.GetComponentsInChildren<Canvas>(true)) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1f; }
                    Canvas.ForceUpdateCanvases();
                    Vector3 p = player.transform.position;
                    Look(cam, new Vector3(-42f, 15.5f, p.z), p + new Vector3(0, 1.1f, 0), 21f); Save(cam, "hud");
                }
            }
            Object.DestroyImmediate(cam.gameObject);
        }

        public static void Menu()
        {
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            var mm = Object.FindFirstObjectByType<PixFootball.UI.MainMenuController>();
            mm.SendMessage("Awake");
            var anim = mm.StageAnchor.GetComponentInChildren<PlayerAvatar>();
            var cam = Camera.main;
            Pose(mm.StageAnchor.gameObject, AnimatorControllerBuilder.FindClipByName("Player_Standing_Idle"), 1.0f);
            foreach (var cv in mm.GetComponentsInChildren<Canvas>(true)) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1f; }
            Canvas.ForceUpdateCanvases();
            Save(cam, "menu", 1920, 1080);
            foreach (var cv in mm.GetComponentsInChildren<Canvas>(true)) cv.enabled = false;
            Debug.Log("[Shots] stage avatars: " + mm.StageAnchor.GetComponentsInChildren<SkinnedMeshRenderer>().Length + " smr, cam=" + cam.transform.position);
            Save(cam, "menu_stage", 1920, 1080);
            foreach (var cv in mm.GetComponentsInChildren<Canvas>(true)) cv.enabled = true;
            // settings screen
            var sp = mm.GetComponentInChildren<PixFootball.UI.SettingsPanel>(true);
            var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
            if (sp != null) { if (menuRoot != null) menuRoot.gameObject.SetActive(false); sp.gameObject.SetActive(true); Canvas.ForceUpdateCanvases(); Save(cam, "settings", 1920, 1080); }
        }
    }
}
