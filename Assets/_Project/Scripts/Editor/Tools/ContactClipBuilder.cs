using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Measures every authored two-actor clip pair (tackles, sacks, big hits, trucks, stiff-arms, spins, juke dodges, contested catches) and stores both actors' root
    /// paths, pelvis headings, the contact moment, approach geometry and get-up timing in PixContact.asset (see ContactTable). Menu: PIX Football/Assets/Build Contact Table.
    /// The pair clips keep the project's normal Center-of-Mass import (they play in place at the view origin); the authored layout between the two actors comes from a temporary
    /// "Original" root-position import that is measured first and reverted. Also provides a contact sheet renderer (Logs/contactsheet_in.txt: "clipNameA|t0|t1|frames").
    /// </summary>
    public static class ContactClipBuilder
    {
        public const float ModelYaw = -90f;
        const float Step = 0.05f, ColSpacing = 5.5f;
        const float MaxLength = 8.0f;
        static readonly string[] PairKeys =
        {
            ArtKeys.ContactTackle, ArtKeys.ContactSack, ArtKeys.ContactBigHit, ArtKeys.ContactCollision,
            ArtKeys.StiffArm, ArtKeys.Spin, ArtKeys.JukeRight, ArtKeys.JukeTempo, ArtKeys.JukeBox, ArtKeys.RockerStep, ArtKeys.Truck, ArtKeys.CatchContested,
            ArtKeys.TackleSuccess, ArtKeys.TackleFail, ArtKeys.TackleBigHit
        };

        struct Sample { public Vector3 root, hips; public float yaw; public float faceY; }

        public static string PartnerName(string a)
        {
            if (a == null) return null;
            if (a.EndsWith("_A")) return a.Substring(0, a.Length - 2) + "_B";
            if (a.EndsWith("_a")) return a.Substring(0, a.Length - 2) + "_b";
            return null;
        }

        static readonly System.Text.RegularExpressions.Regex PairFile = new System.Text.RegularExpressions.Regex(@"/(Tackle(Wrap|Broken)|Sack(Wrap|Broken)|Collision(Jog|Run|Walk)).*_(A|B)\.fbx$|/BoNES_Retargets/(BigHits|Trucks|StiffArm|JukeSpindodge|JukeBoxAND|JukeTempo|ContestedCatches)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Switches "Root Transform Position (XZ) Based Upon" of every pair take between Original (authored layout preserved) and Center of Mass (the project default: plays in place).</summary>
        public static void SetOriginalPositions(bool original)
        {
            int changed = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Licensed/DraftFever/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!PairFile.IsMatch(path)) continue;
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null || mi.animationType != ModelImporterAnimationType.Human) continue;
                var clips = mi.clipAnimations;
                if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
                bool dirty = false;
                for (int i = 0; i < clips.Length; i++) if (clips[i].keepOriginalPositionXZ != original) { clips[i].keepOriginalPositionXZ = original; dirty = true; }
                if (!dirty) continue;
                mi.clipAnimations = clips;
                mi.SaveAndReimport(); changed++;
            }
            // v0.8: cleaned pair takes (Animations/Clean/*.anim) carry the same switch in their clip settings
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Licensed/DraftFever/Animations/Clean" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var ac = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (ac == null || !(ac.name.EndsWith("_A") || ac.name.EndsWith("_B") || ac.name.EndsWith("_a") || ac.name.EndsWith("_b"))) continue;
                var st = AnimationUtility.GetAnimationClipSettings(ac);
                if (st.keepOriginalPositionXZ == original) continue;
                st.keepOriginalPositionXZ = original; AnimationUtility.SetAnimationClipSettings(ac, st); EditorUtility.SetDirty(ac); changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Contact] keepOriginalPositionXZ=" + original + " on " + changed + " models");
        }

        class PairRef { public string key, nameA, nameB; }

        static List<PairRef> CollectPairs(ArtLibrary lib)
        {
            var list = new List<PairRef>(); var seen = new HashSet<string>();
            foreach (string key in PairKeys)
                foreach (var clipA in lib.ClipVariants(key))
                {
                    if (clipA == null || !seen.Add(clipA.name)) continue;
                    string bn = PartnerName(clipA.name);
                    if (bn == null) continue;
                    var clipB = lib.ClipByName(bn);
                    if (clipB == null || clipA.length > MaxLength || clipB.length > MaxLength) { Debug.Log("[Contact] skipped " + key + " " + clipA.name + " (" + clipA.length.ToString("0.0") + "s) partner=" + (clipB != null ? clipB.length.ToString("0.0") + "s" : "missing")); continue; }
                    list.Add(new PairRef { key = key, nameA = clipA.name, nameB = bn });
                }
            return list;
        }

        [MenuItem("PIX Football/Assets/Build Contact Table")]
        public static void Run()
        {
            BatchWatchdog.Arm(30);
            // QA-1045: whatever happens between the two import passes, the pair takes go back to the project import (Center of Mass)
            try { RunInner(); } finally { SetOriginalPositions(false); }
        }

        static void RunInner()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[Contact] no ArtLibrary"); return; }
            var refs = CollectPairs(lib);
            var prefab = lib.Prefab("player_medium");
            var go = Object.Instantiate(prefab); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var anim = go.GetComponent<Animator>(); anim.applyRootMotion = true;

            // pass 1: authored layout (Original import): where each actor stands at the first frame of the shared mocap scene
            SetOriginalPositions(true);
            ArtLibrary.ResetCache(); lib = ArtLibrary.Load();
            var origin0 = new Dictionary<string, Vector2>();
            foreach (var r in refs)
                foreach (string n in new[] { r.nameA, r.nameB })
                {
                    if (origin0.ContainsKey(n)) continue;
                    var s = Measure(go, anim, lib.ClipByName(n), 1);
                    origin0[n] = new Vector2(s[0].hips.x, s[0].hips.z);
                }
            // pass 2: the project import (Center of Mass): the clips as they play at runtime
            SetOriginalPositions(false);
            ArtLibrary.ResetCache(); lib = ArtLibrary.Load();
            prefab = lib.Prefab("player_medium");
            Object.DestroyImmediate(go);
            go = Object.Instantiate(prefab); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            anim = go.GetComponent<Animator>(); anim.applyRootMotion = true;

            var table = AssetDatabase.LoadAssetAtPath<ContactTable>(ContactTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<ContactTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, ContactTable.EditorPath); }
            table.pairs.Clear();
            var log = new StringBuilder();
            foreach (var r in refs)
            {
                var ca = lib.ClipByName(r.nameA); var cb = lib.ClipByName(r.nameB);
                var pair = MeasurePair(r.key, go, anim, ca, cb, origin0[r.nameA], origin0[r.nameB]);
                table.pairs.Add(pair);
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-16} {1,-50} lenA={2:0.00} lenB={3:0.00} tc={4:0.00} dist={5:0.00} bearB={6,5:0} closeA={7,5:0.0} closeB={8,5:0.0} spdA={9,4:0.0} lowA={10:0.00} lowB={11:0.00} getUp=({12:0.0},{13:0.0}) faceUp=({14:0.0},{15:0.0}) hipsA0=({16:0.00},{17:0.00}) hipsB0=({18:0.00},{19:0.00})",
                    r.key, r.nameA, pair.lengthA, pair.lengthB, pair.tContact, pair.distance, pair.bearing, pair.closingA, pair.closingB, pair.speedA, pair.lowA, pair.lowB, pair.getUpA, pair.getUpB, pair.faceUpA, pair.faceUpB,
                    pair.a[2], pair.a[3], pair.b[2], pair.b[3]));
            }
            Object.DestroyImmediate(go);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            ContactTable.ResetCache();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/contact_table.txt", log.ToString());
            Debug.Log("[Contact] " + table.pairs.Count + " pairs measured -> Logs/contact_table.txt");
        }

        static AnimationClip FindAsset(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains("|")) return null;
            foreach (string g in AssetDatabase.FindAssets(name + " t:Model", new[] { "Assets/_Licensed/DraftFever/Animations" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>()) if (c.name == name) return c;
            }
            return null;
        }

        static ContactTable.Pair MeasurePair(string key, GameObject go, Animator anim, AnimationClip ca, AnimationClip cb, Vector2 originA, Vector2 originB)
        {
            int na = Mathf.Max(2, Mathf.FloorToInt(ca.length / Step) + 1), nb = Mathf.Max(2, Mathf.FloorToInt(cb.length / Step) + 1);
            var sa = Measure(go, anim, ca, na); var sb = Measure(go, anim, cb, nb);
            // shared authored frame: Center-of-Mass takes start at the origin, the authored start position of each actor is added back
            Vector2 offA = originA - new Vector2(sa[0].hips.x, sa[0].hips.z), offB = originB - new Vector2(sb[0].hips.x, sb[0].hips.z);
            var p = new ContactTable.Pair { key = key, clipA = ca.name, clipB = cb.name, lengthA = ca.length, lengthB = cb.length, step = Step, a = new float[na * ContactTable.Stride], b = new float[nb * ContactTable.Stride] };
            for (int i = 0; i < na; i++) Put(p.a, i, sa[i], offA);
            for (int i = 0; i < nb; i++) Put(p.b, i, sb[i], offB);
            int n = Mathf.Min(na, nb);
            // contact = first time the hips come within 0.25 m of their closest approach (searched in the first 60% of the shorter take, after the first 0.1 s)
            int i0 = Mathf.Min(n - 1, Mathf.Max(1, Mathf.RoundToInt(0.1f / Step))), i1 = Mathf.Max(i0 + 1, Mathf.Min(n - 1, Mathf.RoundToInt(n * 0.6f)));
            float dmin = float.MaxValue;
            for (int i = i0; i <= i1; i++) dmin = Mathf.Min(dmin, Dist(p, i));
            int ic = i0;
            for (int i = i0; i <= i1; i++) if (Dist(p, i) <= dmin + 0.25f) { ic = i; break; }
            p.tContact = ic * Step; p.distance = Dist(p, ic);
            Vector2 hA = H(p.a, ic), hB = H(p.b, ic);
            p.bearing = BearingOf(hB, hA, p.a[ic * ContactTable.Stride + 4]);
            int w0 = Mathf.Max(0, ic - 6), w1 = Mathf.Max(w0 + 1, ic - 1);
            Vector2 va = (H(p.a, w1) - H(p.a, w0)) / ((w1 - w0) * Step), vb = (H(p.b, w1) - H(p.b, w0)) / ((w1 - w0) * Step);
            Vector2 toB = hB - hA; toB = toB.sqrMagnitude > 1e-4f ? toB.normalized : Vector2.up;
            p.closingA = Vector2.Dot(va, toB); p.closingB = Vector2.Dot(vb, -toB); p.speedA = va.magnitude;
            p.lowA = Mathf.Clamp01((0.95f - sa[Mathf.Min(ic, na - 1)].hips.y) / 0.5f); p.lowB = Mathf.Clamp01((0.95f - sb[Mathf.Min(ic, nb - 1)].hips.y) / 0.5f);
            GetUp(sa, ic, out p.getUpA, out p.faceUpA); GetUp(sb, ic, out p.getUpB, out p.faceUpB);
            return p;
        }

        static float BearingOf(Vector2 subject, Vector2 observer, float observerYaw)
        {
            Vector2 d = subject - observer;
            return d.sqrMagnitude < 1e-6f ? 0f : Mathf.DeltaAngle(observerYaw, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
        }

        /// <summary>Lying detection: the hips drop under 0.45 m after contact; get-up = they rise past 0.6 m again. faceUp from the chest normal while lying.</summary>
        static void GetUp(Sample[] s, int ic, out float getUp, out float faceUp)
        {
            getUp = 0f; faceUp = 0f;
            int lie = -1;
            for (int i = ic; i < s.Length; i++) if (s[i].hips.y < 0.45f) { lie = i; break; }
            if (lie < 0) return;
            float acc = 0f; int cnt = 0;
            for (int i = lie; i < Mathf.Min(s.Length, lie + 6); i++) { acc += s[i].faceY; cnt++; }
            faceUp = cnt > 0 ? acc / cnt : 0f;
            for (int i = lie + 4; i < s.Length; i++) if (s[i].hips.y > 0.6f) { getUp = i * Step; break; }
        }

        static Vector2 H(float[] a, int i) { int o = i * ContactTable.Stride; return new Vector2(a[o + 2], a[o + 3]); }
        static float Dist(ContactTable.Pair p, int i) { return Vector2.Distance(H(p.a, i), H(p.b, i)); }

        static void Put(float[] arr, int i, Sample s, Vector2 off)
        {
            int o = i * ContactTable.Stride;
            arr[o] = s.root.x + off.x; arr[o + 1] = s.root.z + off.y; arr[o + 2] = s.hips.x + off.x; arr[o + 3] = s.hips.z + off.y; arr[o + 4] = s.yaw;
        }

        static Sample[] Measure(GameObject go, Animator anim, AnimationClip clip, int n)
        {
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var r = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var res = new Sample[n];
            PlayableGraph g;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out g);
            g.Evaluate(0f);
            float prevYaw = 90f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) g.Evaluate(Step);
                Vector3 rt = r.position - l.position;
                float yaw = prevYaw;
                if (new Vector2(rt.x, rt.z).magnitude > 0.12f) yaw = Mathf.Atan2(-rt.z, rt.x) * Mathf.Rad2Deg;
                prevYaw = yaw;
                Vector3 up = head.position - hips.position;
                Vector3 front = Vector3.Cross(rt, up).normalized;
                res[i] = new Sample { root = go.transform.position, hips = hips.position, yaw = yaw, faceY = front.y };
            }
            g.Destroy();
            return res;
        }

        // =========================================================================================================== move takes
        /// <summary>Finds the spins inside the long BoNES spin / dodge takes (a window where the pelvis turns 250 degrees or more inside one second) and stores their trim ranges.</summary>
        [MenuItem("PIX Football/Assets/Build Move Takes")]
        public static void BuildMoveTakes()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var prefab = lib.Prefab("player_medium");
            var go = Object.Instantiate(prefab); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var anim = go.GetComponent<Animator>(); anim.applyRootMotion = true;
            var table = AssetDatabase.LoadAssetAtPath<MoveTakeTable>(MoveTakeTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<MoveTakeTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, MoveTakeTable.EditorPath); }
            table.takes.Clear();
            var log = new StringBuilder();
            foreach (var clip in lib.ClipVariants(ArtKeys.Spin))
            {
                if (clip == null || clip.length > 60f) continue;
                int n = Mathf.Max(3, Mathf.FloorToInt(clip.length / Step) + 1);
                var s = Measure(go, anim, clip, n);
                var u = new float[n]; u[0] = s[0].yaw;
                for (int i = 1; i < n; i++) u[i] = u[i - 1] + Mathf.DeltaAngle(s[i - 1].yaw, s[i].yaw);
                int w = Mathf.RoundToInt(1.0f / Step);
                int i0 = -1, i1 = -1;
                var groups = new List<int[]>();
                for (int i = 0; i + w < n; i++)
                {
                    bool hit = Mathf.Abs(u[i + w] - u[i]) >= 250f;
                    if (hit) { if (i0 < 0) i0 = i; i1 = i; }
                    else if (i0 >= 0 && i - i1 > w) { groups.Add(new[] { i0, i1 + w }); i0 = -1; }
                }
                if (i0 >= 0) groups.Add(new[] { i0, i1 + w });
                foreach (var g in groups)
                {
                    float t0 = g[0] * Step, t1 = g[1] * Step;
                    if (t1 - t0 > 1.9f) continue;
                    var take = new MoveTakeTable.Take { key = ArtKeys.Spin, clip = clip.name, start = Mathf.Max(0f, t0 - 0.6f), end = Mathf.Min(clip.length, t1 + 0.7f), t0 = t0, t1 = t1, turnDegrees = u[g[1]] - u[g[0]] };
                    table.takes.Add(take);
                    log.AppendLine("spin " + clip.name + " len=" + clip.length.ToString("0.0") + " move=" + t0.ToString("0.00") + ".." + t1.ToString("0.00") + " turn=" + take.turnDegrees.ToString("0"));
                }
            }
            Object.DestroyImmediate(go);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            MoveTakeTable.ResetCache();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/move_takes.txt", log.ToString());
            Debug.Log("[MoveTakes] " + table.takes.Count + " takes -> Logs/move_takes.txt");
        }

        // =========================================================================================================== sheet
        [MenuItem("PIX Football/Assets/Render Contact Sheet")]
        public static void RunSheet()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var volGo = new GameObject("ShotVolume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = prof.Add<Exposure>(true); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(12.5f);
            var env = prof.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.Gradient); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = prof.Add<GradientSky>(true);
            sky.top.Override(new Color(0.35f, 0.55f, 0.95f)); sky.middle.Override(new Color(0.75f, 0.82f, 0.9f)); sky.bottom.Override(new Color(0.45f, 0.45f, 0.42f));
            var dpl = prof.Add<DiffusionProfileList>(true); dpl.diffusionProfiles.overrideState = true; dpl.diffusionProfiles.value = PlayerPrefabBuilder.FindDiffusionProfiles();
            vol.sharedProfile = prof;
            var lg = new GameObject("Sun"); var sun = lg.AddComponent<Light>(); sun.type = LightType.Directional;
            lg.AddComponent<HDAdditionalLightData>(); sun.lightUnit = LightUnit.Lux; sun.intensity = 90000f;
            lg.transform.rotation = Quaternion.Euler(42f, 250f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
            var prefab = lib.Prefab("player_medium");
            var line = File.ReadAllLines("Logs/contactsheet_in.txt");
            foreach (string raw in line)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = raw.Split(';');
                var ca = lib.ClipByName(parts[0]) ?? FindAsset(parts[0]);
                var cb = lib.ClipByName(PartnerName(parts[0])) ?? FindAsset(PartnerName(parts[0]));
                if (ca == null || cb == null) { Debug.LogWarning("[ContactSheet] missing " + parts[0]); continue; }
                float t0 = float.Parse(parts[1], CultureInfo.InvariantCulture), t1 = float.Parse(parts[2], CultureInfo.InvariantCulture);
                int frames = int.Parse(parts[3]);
                bool zoomed = parts.Length > 4 && parts[4] == "zoom"; float spacing = zoomed ? 2.6f : ColSpacing, viewH = zoomed ? 2.4f : 3.8f;
                if (t1 <= 0f) t1 = Mathf.Min(ca.length, cb.length);
                var made = new List<GameObject>();
                // one authored frame per column: both actors, root motion applied; columns are spread along world +Z so the camera (looking along -X) sees them side by side
                for (int f = 0; f < frames; f++)
                {
                    float t = Mathf.Lerp(t0, t1, frames == 1 ? 0f : f / (float)(frames - 1));
                    var ga = (GameObject)PrefabUtility.InstantiatePrefab(prefab); var gb = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Tint(gb, new Color(0.8f, 0.1f, 0.1f));
                    foreach (var gg in new[] { ga, gb }) { gg.transform.position = Vector3.zero; gg.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f); gg.GetComponent<Animator>().applyRootMotion = true; }
                    PoseAt(ga, ca, t); PoseAt(gb, cb, t);
                    Vector3 shift = new Vector3(0f, 0f, (f - (frames - 1) * 0.5f) * spacing);
                    ga.transform.position += shift; gb.transform.position += shift;
                    made.Add(ga); made.Add(gb);
                }
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.gameObject.AddComponent<HDAdditionalCameraData>();
                cam.fieldOfView = 26f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                int W = 2600, H = Mathf.RoundToInt(W * (viewH / (frames * spacing)));  // side view: 3.8 m tall strip
                cam.aspect = W / (float)H;
                float width = frames * spacing;
                bool top = parts.Length > 4 && parts[4] == "top";
                cam.orthographic = true; cam.orthographicSize = top ? width / (2f * cam.aspect) : viewH * 0.5f;
                if (top) { cam.transform.position = new Vector3(0f, 20f, 0f); cam.transform.rotation = Quaternion.Euler(90f, 90f, 0f); }
                else { cam.transform.position = new Vector3(20f, zoomed ? 0.8f : 1.3f, 0f); cam.transform.rotation = Quaternion.Euler(0f, -90f, 0f); }
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
                var tex = Render(cam, W, H);
                Directory.CreateDirectory("Docs/Screens");
                string outName = "Docs/Screens/contactsheet_" + parts[0].Replace("|", "_") + (top ? "_top" : "") + ".png";
                File.WriteAllBytes(outName, tex.EncodeToPNG());
                Debug.Log("[ContactSheet] " + outName + " lenA=" + ca.length.ToString("0.00") + " lenB=" + cb.length.ToString("0.00"));
                Object.DestroyImmediate(tex); Object.DestroyImmediate(cam.gameObject);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
        }

        static void Tint(GameObject go, Color c)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r.name.StartsWith("Torso") || r.name.StartsWith("Legs"))) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) { var m = new Material(mats[i]); if (m.HasProperty("_Primary_Color")) m.SetColor("_Primary_Color", c); mats[i] = m; }
                r.sharedMaterials = mats;
            }
        }

        static void PoseAt(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponent<Animator>();
            PlayableGraph graph;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f);
            const float s = 0.05f;
            float acc = 0f; while (acc + s <= t) { graph.Evaluate(s); acc += s; }
            if (t - acc > 1e-4f) graph.Evaluate(t - acc);
            graph.Destroy();
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

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release();
            return tex;
        }
    }
}
