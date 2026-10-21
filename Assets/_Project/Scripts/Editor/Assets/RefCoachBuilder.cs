using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Referee (pre-rigged "BGS" referee + 14 gesture / 21 locomotion clips from the Draft Fever Yan files) and sideline coaches
    /// (modular coach kit, rigged in Blender - see Tools/Blender): Humanoid import setup, HDRP materials, prefabs.
    /// </summary>
    public static class RefCoachBuilder
    {
        const string Root = CharacterSetup.Root;
        public const string RefDir = Root + "/Characters/Referee/Rigged";
        public const string CoachDir = Root + "/Characters/Coaches/Rigged";
        const string PrefabDir = Root + "/Prefabs";

        // ------------------------------------------------------------------------------------------------ import setup
        static bool ForceHumanoid(string path, bool noMaterials, out string report)
        {
            report = "";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) { report = "no importer"; return false; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.optimizeGameObjects = false;
            if (noMaterials) mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
            // Unity.s automap picks the root control bone (c_traj / c_root) as Hips on these rigs: map explicitly (root.x / hip).
            if (!HumanoidMapper.Apply(mi, out report)) return false;
            WidenElbowLimits(mi);
            mi.SaveAndReimport();
            return HumanoidMapper.IsGoodAvatar(path);
        }

        /// <summary>Clip-only FBX (re-baked in Blender on exactly the rig of `rigPath`): Humanoid with the avatar COPIED from the rig FBX, so the clips are
        /// interpreted against the character's own bind pose (no second avatar to disagree with).</summary>
        static bool ClipsFromAvatarOf(string clipPath, string rigPath, out string report)
        {
            report = "";
            var mi = AssetImporter.GetAtPath(clipPath) as ModelImporter;
            var av = HumanoidMapper.GetAvatar(rigPath);
            if (mi == null || av == null) { report = "missing importer/avatar"; return false; }
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = av;
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
            return true;
        }

        /// <summary>Unity's default forearm range (about +-80 degrees) clamps hands-on-hips / hand-to-ear / whistle poses (they need 110-140 degrees of elbow
        /// flexion): the muscle value is clamped and the hand stays 20 cm too far away. Widen the flexion side on every referee / coach avatar (clip and
        /// character avatars must share the limits so the muscle values mean the same thing).</summary>
        public static void WidenElbowLimits(ModelImporter mi)
        {
            var desc = mi.humanDescription;
            var human = desc.human;
            for (int i = 0; i < human.Length; i++)
            {
                if (human[i].humanName != "LeftLowerArm" && human[i].humanName != "RightLowerArm") continue;
                int bone = System.Array.IndexOf(HumanTrait.BoneName, human[i].humanName);
                var lim = human[i].limit;
                var min = Vector3.zero; var max = Vector3.zero;
                for (int a = 0; a < 3; a++)
                {
                    int m = HumanTrait.MuscleFromBone(bone, a);
                    if (m < 0) continue;
                    min[a] = HumanTrait.GetMuscleDefaultMin(m); max[a] = HumanTrait.GetMuscleDefaultMax(m);
                    if (HumanTrait.MuscleName[m].Contains("Forearm Stretch")) min[a] = -150f;
                }
                lim.useDefaultValues = false; lim.min = min; lim.max = max; lim.center = Vector3.zero;
                human[i].limit = lim;
            }
            desc.human = human;
            mi.humanDescription = desc;
        }

        static string CleanClipName(string n)
        {
            int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
            n = n.Replace("#", "").Replace("BGS_", "");
            return n.ToLowerInvariant();
        }

        [MenuItem("PIX Football/Assets/Referee/Configure Referee Imports")]
        public static void ConfigureReferee()
        {
            string rep;
            string rig = RefDir + "/Referee_Rigged.fbx", ges = RefDir + "/Referee_Gestures.fbx";
            AssetDatabase.ImportAsset(rig, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ges, ImportAssetOptions.ForceUpdate);
            bool ok1 = ForceHumanoid(rig, true, out rep); Debug.Log("[RefCoach] rig humanoid=" + ok1 + " " + rep);
            bool ok2 = ForceHumanoid(ges, true, out rep); Debug.Log("[RefCoach] gestures humanoid=" + ok2 + " " + rep);

            // clip flags for the gesture file
            var mi = (ModelImporter)AssetImporter.GetAtPath(ges);
            var list = new List<ModelImporterClipAnimation>();
            foreach (var d in mi.defaultClipAnimations)
            {
                var c = d;
                string raw = c.takeName ?? c.name;
                if (raw.Contains("DELETE")) continue; // old duplicate whistle take
                c.name = CleanClipName(raw);
                bool gesture = c.name.Contains("gesture");
                bool loop = c.name.EndsWith("_loop") || c.name.Contains("_loop_");
                c.loopTime = loop; c.loopPose = loop;
                c.lockRootRotation = true; c.lockRootHeightY = true;
                c.lockRootPositionXZ = gesture;           // gestures are performed in place, locomotion keeps its root motion
                c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = false;
                c.heightFromFeet = false;
                list.Add(c);
            }
            mi.clipAnimations = list.ToArray();
            mi.SaveAndReimport();
            Debug.Log("[RefCoach] gesture clips configured: " + list.Count);
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Texture2D Tex(string path) { return AssetDatabase.LoadAssetAtPath<Texture2D>(path); }

        public static Material RefMaterial(string name, string bc, string n, float smooth)
        {
            return MaterialFactory.CreateLit(MaterialFactory.MatDir + "/Referee/" + name + ".mat", Tex(bc), null, Tex(n), Color.white, 0f, smooth);
        }

        static void SetMat(Renderer r, Material m)
        {
            if (m == null) return;
            r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
        }

        [MenuItem("PIX Football/Assets/Referee/Build Rigged Referee Prefab")]
        public static void BuildReferee()
        {
            string tx = Root + "/Characters/Referee/Textures";
            var uniform = RefMaterial("PIX_RefRig_Uniform", RefDir + "/Textures/PIX_Referee_Uniform_BC.png", tx + "/Uniform/T_Referee_Uniform_N.png", 0.3f);
            var arms = RefMaterial("PIX_RefRig_Arms", tx + "/Arms/T_Referee_Arms_BC.png", tx + "/Arms/T_Referee_Arms_N.png", 0.35f);
            var head = RefMaterial("PIX_RefRig_Head", tx + "/Head/T_Referee_Head_01_BC.png", tx + "/Head/T_Referee_Head_01_N.png", 0.35f);
            var eyes = RefMaterial("PIX_RefRig_Eyes", tx + "/Eyes/T_Referee_Eyes_BC.png", null, 0.9f);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(RefDir + "/Referee_Rigged.fbx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            inst.name = "PIX_Referee_Rigged";
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true).ToArray())
            {
                string n = r.name;
                if (n.Contains("_02_")) { Object.DestroyImmediate(r.gameObject); continue; } // alternate head/eyes, no texture shipped
                if (n.Contains("Uniform")) SetMat(r, uniform);
                else if (n.Contains("Arms")) SetMat(r, arms);
                else if (n.Contains("Head")) SetMat(r, head);
                else if (n.Contains("Eyes")) SetMat(r, eyes);
            }
            // The whistle lanyard chain (cc_Necklace_* / cc_Whistle*) hangs off the root bone c_traj and is only animated by the BGS clips; with any other
            // clip (crouch, hands on hips...) it would stay at its rest position, floating above the head. Parent it to the chest instead.
            var traj = FindDeep(inst.transform, "c_traj"); var chest = FindDeep(inst.transform, "spine_03.x");
            if (traj != null && chest != null)
                foreach (var c in traj.Cast<Transform>().ToArray())
                    if (c.name.StartsWith("cc_Necklace") || c.name.StartsWith("cc_Whistle")) c.SetParent(chest, true);
            // Referee_Uniform_PocketStuff (pen / notebook in the chest pocket) ships with no skin weights, so it would stay at its bind height while the body
            // crouches: make it a plain mesh child of the chest bone.
            var pocket = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name.Contains("PocketStuff"));
            if (pocket != null && chest != null)
            {
                var go = new GameObject("Referee_Uniform_PocketStuff");
                go.transform.SetParent(inst.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = pocket.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = pocket.sharedMaterials;
                go.transform.SetParent(chest, true);
                Object.DestroyImmediate(pocket.gameObject);
            }
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            MaterialFactory.EnsureFolder(PrefabDir);
            PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/PIX_Referee_Rigged.prefab");
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log("[RefCoach] saved PIX_Referee_Rigged.prefab");
        }

        public static void RefereeAll() { ConfigureReferee(); AssetDatabase.Refresh(); BuildReferee(); }

        // ================================================================================================ coaches
        class CoachSpec
        {
            public string id, headTex, armsTex; public Color headTint, top, pants, shoes, cap, hair;
        }

        static readonly CoachSpec[] Coaches =
        {
            new CoachSpec { id = "A", headTex = Root + "/Characters/Bodies/mediumPlayer.fbm/T_Head_BC_01.png", armsTex = Root + "/Characters/SkinTextures/Arms/T_Arms_Skin_01.png", headTint = Color.white,
                top = new Color(0.78f, 0.12f, 0.14f), pants = new Color(0.36f, 0.33f, 0.27f), shoes = new Color(0.9f, 0.9f, 0.9f), cap = new Color(0.07f, 0.09f, 0.18f), hair = new Color(0.16f, 0.1f, 0.06f) },
            new CoachSpec { id = "B", headTex = CoachDir + "/Textures/PIX_Coach_Head_Light.png", armsTex = Root + "/Characters/SkinTextures/Arms/T_Arms_Skin_06.png", headTint = Color.white,
                top = new Color(0.1f, 0.22f, 0.56f), pants = new Color(0.12f, 0.17f, 0.3f), shoes = new Color(0.85f, 0.85f, 0.85f), cap = new Color(0.92f, 0.92f, 0.94f), hair = new Color(0.5f, 0.34f, 0.18f) },
            new CoachSpec { id = "C", headTex = Root + "/Characters/Bodies/mediumPlayer.fbm/T_Head_BC_01.png", armsTex = Root + "/Characters/SkinTextures/Arms/T_Arms_Skin_12.png", headTint = new Color(0.72f, 0.71f, 0.70f),
                top = new Color(0.08f, 0.45f, 0.24f), pants = new Color(0.12f, 0.12f, 0.14f), shoes = new Color(0.8f, 0.8f, 0.8f), cap = new Color(0.95f, 0.8f, 0.1f), hair = new Color(0.07f, 0.06f, 0.05f) },
        };

        static string CoachMat(string name) { return MaterialFactory.MatDir + "/Coaches/" + name + ".mat"; }

        static Material Solid(string name, Color c, float metallic, float smooth)
        {
            return MaterialFactory.CreateSolid(CoachMat(name), c, metallic, smooth);
        }

        static Material Hair(string name, Color tint, Texture2D atlas)
        {
            var m = MaterialFactory.CreateLit(CoachMat(name), atlas, null, null, tint, 0f, 0.3f);
            m.SetFloat("_AlphaCutoffEnable", 1f);
            m.SetFloat("_AlphaCutoff", 0.28f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f); m.SetFloat("_CullModeForward", 0f);
            m.SetFloat("_AlphaToMaskInspectorValue", 1f); m.SetFloat("_AlphaToMask", 1f);
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("PIX Football/Assets/Coaches/Configure Coach Imports")]
        public static void ConfigureCoaches()
        {
            string rep;
            foreach (var c in Coaches)
            {
                string p = CoachDir + "/PIX_Coach_" + c.id + ".fbx";
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                var mi = AssetImporter.GetAtPath(p) as ModelImporter;
                mi.importAnimation = false;
                mi.SaveAndReimport();
                bool ok = ForceHumanoid(p, true, out rep);
                Debug.Log("[RefCoach] coach " + c.id + " humanoid=" + ok + " " + rep);
            }
            string anims = CoachDir + "/Coach_Anims.fbx";
            AssetDatabase.ImportAsset(anims, ImportAssetOptions.ForceUpdate);
            bool ok2 = ForceHumanoid(anims, true, out rep);
            Debug.Log("[RefCoach] coach anims humanoid=" + ok2 + " " + rep);
            var ami = (ModelImporter)AssetImporter.GetAtPath(anims);
            var list = new List<ModelImporterClipAnimation>();
            foreach (var d in ami.defaultClipAnimations)
            {
                var cl = d;
                cl.name = CleanClipName(cl.takeName ?? cl.name);
                bool loop = cl.name.Contains("idle") || cl.name.Contains("clap");
                cl.loopTime = loop; cl.loopPose = loop;
                cl.lockRootRotation = true; cl.lockRootHeightY = true; cl.lockRootPositionXZ = true;
                cl.keepOriginalOrientation = true; cl.keepOriginalPositionY = true; cl.keepOriginalPositionXZ = false;
                list.Add(cl);
            }
            ami.clipAnimations = list.ToArray();
            ami.SaveAndReimport();
            Debug.Log("[RefCoach] coach clips: " + string.Join(", ", list.Select(l => l.name).ToArray()));
        }

        [MenuItem("PIX Football/Assets/Coaches/Build Coach Prefabs")]
        public static void BuildCoaches()
        {
            var atlas = Tex(CoachDir + "/Textures/PIX_Coach_HairCards.png");
            var eyeTex = Tex(Root + "/Characters/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png");
            MaterialFactory.EnsureFolder(PrefabDir);
            foreach (var c in Coaches)
            {
                string p = "PIX_Coach_" + c.id;
                var mats = new Dictionary<string, Material[]>
                {
                    { "Head", new[] { MaterialFactory.CreateLit(CoachMat(p + "_Head"), Tex(c.headTex), null, null, c.headTint, 0f, 0.35f) } },
                    { "Arms", new[] { MaterialFactory.CreateLit(CoachMat(p + "_Arms"), Tex(c.armsTex), null, null, Color.white, 0f, 0.35f) } },
                    { "Eyes", new[] { MaterialFactory.CreateLit(CoachMat(p + "_Eyes"), eyeTex, null, null, Color.white, 0f, 0.9f) } },
                    { "Top", new[] { Solid(p + "_Top", c.top, 0f, 0.25f) } },
                    { "Pants", new[] { Solid(p + "_Pants", c.pants, 0f, 0.2f) } },
                    { "Shoes", new[] { Solid(p + "_Shoes", c.shoes, 0f, 0.4f) } },
                    { "Cap", new[] { Solid(p + "_Cap", c.cap, 0f, 0.2f) } },
                    { "Hair", new[] { Hair(p + "_Hair", c.hair, atlas) } },
                    { "Headset", new[] { Solid(p + "_Headset", new Color(0.05f, 0.05f, 0.06f), 0.4f, 0.55f) } },
                    { "Watch", new[] { Solid(p + "_Watch", new Color(0.75f, 0.75f, 0.78f), 1f, 0.7f) } },
                    { "Glasses", new[] { Solid(p + "_Glasses", new Color(0.05f, 0.06f, 0.08f), 0f, 0.9f) } },
                    { "Clipboard", new[] { Solid("PIX_Coach_Clipboard_Board", new Color(0.32f, 0.2f, 0.1f), 0f, 0.35f), Solid("PIX_Coach_Clipboard_Paper", new Color(0.93f, 0.93f, 0.9f), 0f, 0.3f), Solid("PIX_Coach_Clipboard_Clip", new Color(0.7f, 0.7f, 0.72f), 1f, 0.6f) } },
                    { "Tablet", new[] { Solid("PIX_Coach_Tablet_Body", new Color(0.06f, 0.06f, 0.07f), 0.3f, 0.5f), Solid("PIX_Coach_Tablet_Screen", new Color(0.12f, 0.2f, 0.32f), 0f, 0.9f) } },
                };
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CoachDir + "/" + p + ".fbx");
                if (fbx == null) { Debug.LogWarning("[RefCoach] missing " + p + ".fbx"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                inst.name = p;
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var primary = new List<Renderer>(); var secondary = new List<Renderer>();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    string slot = r.name.Substring(r.name.LastIndexOf('_') + 1);
                    Material[] arr;
                    if (!mats.TryGetValue(slot, out arr)) { Debug.LogWarning("[RefCoach] no material for " + r.name); continue; }
                    int n = r.sharedMaterials.Length;
                    var assign = new Material[n];
                    for (int i = 0; i < n; i++) assign[i] = arr[Mathf.Min(i, arr.Length - 1)];
                    r.sharedMaterials = assign;
                    if (slot == "Top") primary.Add(r);
                    if (slot == "Cap") secondary.Add(r);
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
                }
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var tint = inst.AddComponent<PixFootball.Art.TeamTint>();
                tint.primaryRenderers = primary.ToArray(); tint.secondaryRenderers = secondary.ToArray();
                tint.primary = c.top; tint.secondary = c.cap;   // authored colours; gameplay calls SetTeamColors(primary, secondary)
                PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/" + p + ".prefab");
                Object.DestroyImmediate(inst);
                Debug.Log("[RefCoach] saved " + p + ".prefab");
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- hand-held props (clipboard A, tablet B)
        static Vector3 BlenderToUnity(Vector3 v) { return new Vector3(-v.x, v.z, -v.y); }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        /// <summary>
        /// Parents the clipboard / tablet to the coach's left hand bone. The local transform is calibrated on the arm-fitted coach_clipboard_idle pose so
        /// the board lies where it was modelled in Blender (Tools/Blender/pix_props.py constants) while the clip plays; in other clips the prop simply
        /// follows the hand. Run after ArmFit.BuildFittedClips.
        /// </summary>
        [MenuItem("PIX Football/Assets/Coaches/Attach Coach Props")]
        public static void AttachProps()
        {
            var fitted = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArmFit.OutDir + "/coach_clipboard_idle.anim");
            if (fitted == null) { Debug.LogWarning("[RefCoach] fitted coach_clipboard_idle.anim missing, props not attached"); return; }
            Vector3 ax = new Vector3(1f, 0f, 0f), ay = new Vector3(0f, -0.87f, 0.5f).normalized, az = Vector3.Cross(ax, ay).normalized;
            var specs = new[] { new { id = "A", fbx = "Coach_Clipboard", center = new Vector3(0f, -0.30f, 1.22f) }, new { id = "B", fbx = "Coach_Tablet", center = new Vector3(0.03f, -0.30f, 1.22f) } };
            foreach (var sp in specs)
            {
                string propPath = CoachDir + "/" + sp.fbx + ".fbx";
                AssetDatabase.ImportAsset(propPath, ImportAssetOptions.ForceUpdate);
                var pmi = AssetImporter.GetAtPath(propPath) as ModelImporter;
                if (pmi == null) { Debug.LogWarning("[RefCoach] missing " + propPath); continue; }
                pmi.importAnimation = false; pmi.materialImportMode = ModelImporterMaterialImportMode.None; pmi.SaveAndReimport();
                var propAsset = AssetDatabase.LoadAssetAtPath<GameObject>(propPath);
                string prefabPath = PrefabDir + "/PIX_Coach_" + sp.id + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                // calibration on a throw-away instance
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var anim = temp.GetComponent<Animator>();
                var handler = new HumanPoseHandler(anim.avatar, temp.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                for (int m = 0; m < HumanTrait.MuscleCount; m++)
                {
                    var cv = AnimationUtility.GetEditorCurve(fitted, EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[m]));
                    if (cv != null) pose.muscles[m] = cv.Evaluate(1.0f);
                }
                handler.SetHumanPose(ref pose);
                var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                Vector3 cx = BlenderToUnity(ax), cu = BlenderToUnity(ay), cn = BlenderToUnity(az);
                // the Unity mesh has local axes (lx, ly, lz) = (-vx, vz, -vy) of the Blender board frame
                var D = Matrix4x4.identity;
                D.SetColumn(0, new Vector4(-cx.x, -cx.y, -cx.z, 0)); D.SetColumn(1, new Vector4(cn.x, cn.y, cn.z, 0)); D.SetColumn(2, new Vector4(-cu.x, -cu.y, -cu.z, 0));
                Vector3 cc = BlenderToUnity(sp.center);
                D.SetColumn(3, new Vector4(cc.x, cc.y, cc.z, 1));
                float det = Vector3.Dot(Vector3.Cross(D.GetColumn(0), D.GetColumn(1)), D.GetColumn(2));
                if (det < 0f) D.SetColumn(1, -D.GetColumn(1) + new Vector4(0, 0, 0, 0));
                Matrix4x4 local = hand.worldToLocalMatrix * D;
                Vector3 lp = local.GetColumn(3);
                Quaternion lr = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
                Object.DestroyImmediate(temp);

                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                var handBone = FindDeep(contents.transform, "hand.l");
                var old = handBone.Find("Prop_" + sp.fbx.Replace("Coach_", ""));
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var prop = (GameObject)PrefabUtility.InstantiatePrefab(propAsset, handBone);
                prop.name = "Prop_" + sp.fbx.Replace("Coach_", "");
                prop.transform.localPosition = lp; prop.transform.localRotation = lr; prop.transform.localScale = Vector3.one;
                string kind = sp.fbx.Replace("Coach_", "");
                foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
                {
                    int n = r.sharedMaterials.Length;
                    var mats = new Material[n];
                    for (int i = 0; i < n; i++)
                    {
                        string mn = kind == "Clipboard" ? (i == 0 ? "PIX_Coach_Clipboard_Board" : i == 1 ? "PIX_Coach_Clipboard_Paper" : "PIX_Coach_Clipboard_Clip") : (i == 0 ? "PIX_Coach_Tablet_Body" : "PIX_Coach_Tablet_Screen");
                        mats[i] = AssetDatabase.LoadAssetAtPath<Material>(CoachMat(mn));
                    }
                    r.sharedMaterials = mats;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
                Debug.Log("[RefCoach] attached Prop_" + kind + " to PIX_Coach_" + sp.id + " local pos " + lp.ToString("F3") + " det " + det);
            }
        }

        public static void CoachesAll() { ConfigureCoaches(); AssetDatabase.Refresh(); BuildCoaches(); }
    }
}
