using System.Collections.Generic;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Verification renders for the referee / coaches (Docs/Screens/ref_coach_*.png).</summary>
    public static class RefCoachShots
    {
        static AnimationClip FindClip(string fbxPath, string contains)
        {
            var fitted = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArmFit.OutDir + "/" + contains + ".anim");
            if (fitted != null) return fitted;
            return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.ToLowerInvariant().Contains(contains));
        }

        static GameObject Place(string prefabName, float x, AnimationClip clip, float t)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/" + prefabName + ".prefab");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(p);
            g.transform.position = new Vector3(x, 0, 0);
            if (clip != null) AssetScreenshots.Pose(g, clip, t);
            return g;
        }

        static void Floor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(4, 1, 1);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
        }

        /// <summary>One row per call: referee in every gesture clip name given (substring match), mid-clip.</summary>
        public static void ShootRefereeGestures()
        {
            string ges = RefCoachBuilder.RefDir + "/Referee_Gestures.fbx";
            string[][] sheets =
            {
                new[] { "gesture_touchdown", "gesture_firstdown", "gesture_incompletepass", "gesture_timeout", "gesture_ballready", "gesture_deadball" },
                new[] { "gesture_whistle_01", "gesture_safety", "gesture_personalfoul", "gesture_illegalcontact", "gesture_falsestart", "gesture_delayofgame" },
            };
            float[] fr = { 0.0f, 0.5f, 0.85f };
            for (int s = 0; s < sheets.Length; s++)
            {
                Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
                var made = new List<GameObject>();
                int n = sheets[s].Length;
                for (int i = 0; i < n; i++)
                {
                    var clip = FindClip(ges, sheets[s][i]);
                    Debug.Log("[RefCoachShots] " + sheets[s][i] + " -> " + (clip != null ? clip.name + " " + clip.length : "MISSING"));
                    made.Add(Place("PIX_Referee_Rigged", (i - (n - 1) / 2f) * 1.5f, clip, clip != null ? clip.length * 0.55f : 0f));
                }
                var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.1f, 8.5f), new Vector3(0, 0.95f, 0), 30f);
                AssetScreenshots.Save(AssetScreenshots.Render(cam, 1800, 700), "ref_coach_referee_gestures_" + (s + 1));
                Object.DestroyImmediate(cam.gameObject);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
        }

        public static void RefereeBuildAndShoot()
        {
            RefCoachBuilder.RefereeAll();
            AssetDatabase.Refresh();
            ShootRefereeGestures();
            ShootRefereeBind();
        }

        static GameObject PlaceAt(string prefabName, float x, float z, AnimationClip clip, float t)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/" + prefabName + ".prefab");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(p);
            g.transform.position = new Vector3(x, 0, z);
            if (clip != null) AssetScreenshots.Pose(g, clip, t);
            return g;
        }

        public static void ShootRefereeBind()
        {
            Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
            var g = Place("PIX_Referee_Rigged", -1f, null, 0);
            var g2 = Place("PIX_Referee_Rigged", 1f, FindClip(RefCoachBuilder.RefDir + "/Referee_Gestures.fbx", "walkf_loop"), 0.3f);
            var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.1f, 5.5f), new Vector3(0, 0.95f, 0), 30f);
            AssetScreenshots.Save(AssetScreenshots.Render(cam, 1600, 900), "ref_coach_referee_bind_walk");
            Object.DestroyImmediate(cam.gameObject);
        }

        // ---------------------------------------------------------------- coaches + referee keyframed clips
        public static void ShootCoaches()
        {
            string anims = RefCoachBuilder.CoachDir + "/Coach_Anims.fbx";
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var cheer = lib != null ? lib.Clip(ArtKeys.Cheer) : null;
            var lookAround = lib != null ? lib.Clip(ArtKeys.QbLookAround) : null;
            string[] coaches = { "PIX_Coach_A", "PIX_Coach_B", "PIX_Coach_C" };
            // 1) bind pose row + head close-ups
            {
                Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
                var made = new List<GameObject>();
                for (int i = 0; i < 3; i++) made.Add(Place(coaches[i], (i - 1) * 1.3f, null, 0));
                var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.0f, 6.2f), new Vector3(0, 0.95f, 0), 30f);
                AssetScreenshots.Save(AssetScreenshots.Render(cam, 1600, 900), "ref_coach_coaches_bind");
                cam.fieldOfView = 12f;
                cam.transform.position = new Vector3(0, 1.72f, 5.2f); cam.transform.LookAt(new Vector3(0, 1.68f, 0));
                AssetScreenshots.Save(AssetScreenshots.Render(cam, 1800, 600), "ref_coach_coaches_heads");
                Object.DestroyImmediate(cam.gameObject);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            // 2) each coach in 4 poses
            string[] names = { "coach_clipboard_idle", "coach_headset_idle", "coach_clap" };
            float[] times = { 0.5f, 0.6f, 0.4f };
            for (int c = 0; c < 3; c++)
            {
                Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
                var made = new List<GameObject>();
                var clips = new List<AnimationClip>();
                for (int i = 0; i < 3; i++) clips.Add(FindClip(anims, names[i]));
                clips.Add(cheer);
                for (int i = 0; i < 4; i++)
                {
                    float t = clips[i] != null ? (i == 3 ? clips[i].length * 0.3f : clips[i].length * times[Mathf.Min(i, 2)]) : 0f;
                    made.Add(Place(coaches[c], (i - 1.5f) * 1.3f, clips[i], t));
                }
                var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.1f, 7.5f), new Vector3(0, 0.95f, 0), 30f);
                AssetScreenshots.Save(AssetScreenshots.Render(cam, 1800, 700), "ref_coach_" + coaches[c].ToLower().Replace("pix_", "") + "_poses");
                Object.DestroyImmediate(cam.gameObject);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            // 3) referee: keyframed clips
            {
                Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
                var made = new List<GameObject>();
                string[] rc = { "ref_idle", "ref_spot_ball", "ref_penalty_flag" };
                float[] rt = { 0.3f, 0.45f, 0.4f };
                for (int i = 0; i < 3; i++)
                {
                    var clip = FindClip(anims, rc[i]);
                    made.Add(Place("PIX_Referee_Rigged", (i - 1) * 1.5f, clip, clip != null ? clip.length * rt[i] : 0f));
                }
                var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.0f, 6.2f), new Vector3(0, 0.95f, 0), 30f);
                AssetScreenshots.Save(AssetScreenshots.Render(cam, 1600, 800), "ref_coach_referee_keyframed");
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        /// <summary>Armpit / shoulder / knee stress poses: referee arms up + crouch, coaches in library celebrate / sprint clips (humanoid retarget onto the coach rig).</summary>
        public static void ShootDeform()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            Light sun; AssetScreenshots.NewStudio(12.5f, out sun); Floor();
            string anims = RefCoachBuilder.CoachDir + "/Coach_Anims.fbx", ges = RefCoachBuilder.RefDir + "/Referee_Gestures.fbx";
            var td = lib != null ? lib.Clip(ArtKeys.CelebrateTd) : null;
            var sprint = lib != null ? lib.Clip(ArtKeys.Sprint) : null;
            var made = new List<GameObject>();
            var c1 = FindClip(ges, "gesture_touchdown");
            made.Add(Place("PIX_Referee_Rigged", 2.4f, c1, c1 != null ? c1.length * 0.45f : 0f));
            var c2 = FindClip(anims, "ref_spot_ball");
            made.Add(Place("PIX_Referee_Rigged", 0.8f, c2, c2 != null ? c2.length * 0.45f : 0f));
            made.Add(Place("PIX_Coach_B", -0.8f, td, td != null ? td.length * 0.4f : 0f));
            made.Add(Place("PIX_Coach_C", -2.4f, sprint, sprint != null ? sprint.length * 0.3f : 0f));
            var cam = AssetScreenshots.MakeCamera(new Vector3(0, 1.1f, 7.0f), new Vector3(0, 0.95f, 0), 30f);
            AssetScreenshots.Save(AssetScreenshots.Render(cam, 1800, 800), "ref_coach_deform_check");
            Object.DestroyImmediate(cam.gameObject);
        }

        public static void FullBuildAndShoot()
        {
            RefCoachBuilder.RefereeAll();
            RefCoachBuilder.CoachesAll();
            AssetDatabase.Refresh();
            ArmFit.BuildFittedClips();
            RefCoachBuilder.AttachProps();
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            ArtLibrary.ResetCache();
            ShootRefereeGestures();
            ShootRefereeBind();
            ShootCoaches();
            ShootDeform();
        }
    }
}
