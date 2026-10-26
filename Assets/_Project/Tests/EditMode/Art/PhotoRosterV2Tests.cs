using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>Photoreal roster v2 (shoulder pads, 15 bodies, Biped clips). The staging checks run on Assets/_Licensed/PhotorealPlayer/Staging_v07 and, after PhotoRosterV2.Promote, on the live folder; they are skipped when the licensed art is absent.</summary>
    public class PhotoRosterV2Tests
    {
        const string Root = "Assets/_Licensed/PhotorealPlayer", Staging = Root + "/Staging_v07";
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly string[] Bases = { "Std", "Stocky", "Athletic", "Tall", "Runner" };

        // ---- appearance / body mapping (engine free, always runs)
        [Test]
        public void ChooseBodyMapsPositionHeightAndWeightToSizeAndBase()
        {
            PhotoBody s; BodyBase b;
            PlayerAppearance.ChooseBody("QB", 196, 100, out s, out b); Assert.AreEqual(BodyBase.Tall, b); Assert.AreEqual(PhotoBody.Small, s);
            PlayerAppearance.ChooseBody("QB", 183, 92, out s, out b); Assert.AreEqual(BodyBase.Std, b);
            PlayerAppearance.ChooseBody("WR", 178, 80, out s, out b); Assert.AreEqual(BodyBase.Runner, b); Assert.AreEqual(PhotoBody.Small, s);
            PlayerAppearance.ChooseBody("CB", 180, 86, out s, out b); Assert.AreEqual(BodyBase.Athletic, b); Assert.AreEqual(PhotoBody.Small, s);
            PlayerAppearance.ChooseBody("LB", 188, 108, out s, out b); Assert.AreEqual(BodyBase.Stocky, b); Assert.AreEqual(PhotoBody.Medium, s);
            PlayerAppearance.ChooseBody("SS", 185, 101, out s, out b); Assert.AreEqual(BodyBase.Stocky, b);
            PlayerAppearance.ChooseBody("FS", 183, 88, out s, out b); Assert.AreEqual(BodyBase.Athletic, b);
            PlayerAppearance.ChooseBody("TE", 203, 112, out s, out b); Assert.AreEqual(BodyBase.Tall, b);
            foreach (var line in new[] { "OT", "OG", "C", "DE", "DT", "NT" })
            { PlayerAppearance.ChooseBody(line, 190, 140, out s, out b); Assert.AreEqual(PhotoBody.Large, s, line); Assert.AreEqual(BodyBase.Std, b, line); }
            PlayerAppearance.ChooseBody("???", 175, 80, out s, out b); Assert.AreEqual(PhotoBody.Small, s);
            PlayerAppearance.ChooseBody(null, 175, 130, out s, out b); Assert.AreEqual(PhotoBody.Large, s);
        }

        [Test]
        public void PrefabKeysKeepTheOldNamesForTheStdBase()
        {
            Assert.AreEqual("photo_player_small", PlayerAppearance.PrefabKey(PhotoBody.Small, BodyBase.Std));
            Assert.AreEqual("photo_player_large", PlayerAppearance.PrefabKey(PhotoBody.Large, BodyBase.Std));
            Assert.AreEqual("photo_player_medium_tall", PlayerAppearance.PrefabKey(PhotoBody.Medium, BodyBase.Tall));
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (PhotoBody sz in Enum.GetValues(typeof(PhotoBody))) foreach (BodyBase bb in Enum.GetValues(typeof(BodyBase))) keys.Add(PlayerAppearance.PrefabKey(sz, bb));
            Assert.AreEqual(15, keys.Count, "5 bases x 3 sizes = 15 body shapes");
        }

        [Test]
        public void TheBaseDoesNotChangeTheFaceOrTheOldKeys()
        {
            var old = PlayerAppearance.FromSeed(4242, PhotoBody.Medium, 1);
            Assert.AreEqual(BodyBase.Std, old.Base);
            var tall = PlayerAppearance.FromSeed(4242, PhotoBody.Medium, 1, BodyBase.Tall);
            Assert.AreEqual(old.FaceKey(), tall.FaceKey(), "same seed = same person");
            Assert.AreNotEqual(old.Key(), tall.Key(), "different base = different appearance");
            Assert.AreEqual(old.Key(), PlayerAppearance.FromSeed(4242, PhotoBody.Medium, 1, BodyBase.Std).Key());
            var bad = tall; bad.Base = (BodyBase)99;
            Assert.AreEqual(BodyBase.Std, bad.Sanitized().Base);
        }

        // ---- staging / live assets
        static string[] Dirs()
        {
            return new[] { Staging, Root };
        }

        static string PrefabPath(string dir, string size, string b) { return (dir == Root ? Root + "/Prefabs" : dir + "/Prefabs") + "/PIX_PhotoPlayer_" + size + (b == "Std" ? "" : "_" + b) + ".prefab"; }

        static System.Collections.Generic.List<GameObject> Prefabs(out string where)
        {
            where = null;
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var dir in Dirs())
            {
                list.Clear();
                foreach (string s in Sizes) foreach (string b in Bases)
                    {
                        var p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(dir, s, b));
                        if (p == null) { list.Clear(); break; }
                        if (b == "Std" && dir == Root && p.GetComponent<PhotoPadTint>() == null) { list.Clear(); break; }   // live Std prefabs without pads = not promoted yet
                        list.Add(p);
                    }
                if (list.Count == 15) { where = dir; return list; }
            }
            Assert.Ignore("roster v2 prefabs not built (Staging_v07 absent)");
            return null;
        }

        static int Tris(Renderer r)
        {
            Mesh m = null; var smr = r as SkinnedMeshRenderer;
            if (smr != null) m = smr.sharedMesh; else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
            if (m == null) return 0;
            int t = 0; for (int i = 0; i < m.subMeshCount; i++) t += (int)(m.GetIndexCount(i) / 3);
            return t;
        }

        static readonly string[] Optional = { "Visor", "HeadBeard", "HeadScalp", "HeadBrows", "SleeveL", "SleeveR", "Towel", "Pouch", "PlayCard", "Wristband" };

        [Test]
        public void FifteenHumanoidBodiesWithPadsAndFourLodsEach()
        {
            string where; var all = Prefabs(out where);
            int[] budget = { 90000, 42000, 19000, 10000 };
            foreach (var p in all)
            {
                var an = p.GetComponent<Animator>();
                Assert.IsNotNull(an.avatar, p.name);
                Assert.IsTrue(an.avatar.isValid && an.avatar.isHuman, p.name + " must be a valid Humanoid");
                var pp = p.GetComponent<PhotoPlayer>();
                var pads = pp.parts.FirstOrDefault(x => x.key == "Pads");
                Assert.IsNotNull(pads, p.name + " has no Pads part");
                Assert.AreEqual(2, pads.lods.Length, p.name + " pads LOD0..1 (v0.7: an under-layer of the jersey, hidden beyond LOD1)");
                Assert.IsTrue(pads.hideBeyond, p.name + " pads hidden beyond LOD1");
                Assert.IsNotNull(p.GetComponent<PhotoPadTint>(), p.name);
                var tot = new int[4];
                foreach (var part in pp.parts)
                {
                    if (Optional.Contains(part.key)) continue;
                    for (int l = 0; l < 4; l++) { if (part.hideBeyond && l >= part.lods.Length) continue; tot[l] += Tris(part.lods[Mathf.Min(l, part.lods.Length - 1)]); }
                }
                for (int l = 0; l < 4; l++) Assert.LessOrEqual(tot[l], budget[l], p.name + " LOD" + l + " triangles " + tot[l]);
                Assert.Greater(tot[0], tot[1]); Assert.Greater(tot[1], tot[2]); Assert.Greater(tot[2], tot[3]);
                Assert.AreEqual(3, ((SkinnedMeshRenderer)pads.lods[0]).sharedMaterials.Length, p.name + " pads: shell / trim / metal");
                // the pad LODs are skinned to the rig and follow it (no stray bones)
                foreach (var smr in pads.lods.OfType<SkinnedMeshRenderer>())
                {
                    Assert.AreEqual(smr.sharedMesh.bindposes.Length, smr.bones.Length, p.name + " " + smr.name);
                    foreach (var b in smr.bones) Assert.IsNotNull(b, p.name + " " + smr.name + " null bone");
                    Assert.GreaterOrEqual(smr.bones.Length, 4, p.name + " " + smr.name + " should be skinned to chest / clavicles / arms");
                }
            }
        }

        [Test]
        public void PaddedJerseyAndPadSilhouettesAgreeAcrossLods()
        {
            string where; var all = Prefabs(out where);
            foreach (var p in all)
            {
                var pp = p.GetComponent<PhotoPlayer>();
                foreach (var key in new[] { "Jersey", "Pads" })
                {
                    var part = pp.parts.First(x => x.key == key);
                    var b0 = ((SkinnedMeshRenderer)part.lods[0]).sharedMesh.bounds;
                    for (int l = 1; l < Mathf.Min(4, part.lods.Length); l++)
                    {
                        var bl = ((SkinnedMeshRenderer)part.lods[l]).sharedMesh.bounds;
                        float tol = key == "Jersey" ? (l == 1 ? 0.03f : l == 2 ? 0.06f : 0.08f) : 0.08f;   // measured worst: jersey bounds width -5.3 % at LOD2 (Large), -4.1 % LOD3 (Small Stocky): decimated epaulet corners (PH2-012)
                        Assert.AreEqual(b0.size.x, bl.size.x, b0.size.x * tol, p.name + " " + key + " LOD" + l + " width");
                        Assert.AreEqual(b0.size.y + b0.size.z, bl.size.y + bl.size.z, (b0.size.y + b0.size.z) * tol, p.name + " " + key + " LOD" + l + " height + depth");
                    }
                }
                // the padded jersey is broad: wider than the pad body alone would be, and the shoulders are wider than a 0.62 m player
                var jb = ((SkinnedMeshRenderer)pp.parts.First(x => x.key == "Jersey").lods[0]).sharedMesh.bounds;
                Assert.Greater(jb.size.x, 0.62f, p.name + " padded shoulder width");     // v0.13 (BODIES-2): shoulders narrowed toward R1 (owner: "too broad"); Small measures 0.638 m, was > 0.68
                var padb = ((SkinnedMeshRenderer)pp.parts.First(x => x.key == "Pads").lods[0]).sharedMesh.bounds;
                Assert.LessOrEqual(padb.size.x, jb.size.x + 0.03f, p.name + " the jersey drapes over the pads (pads not wider than the jersey)");
            }
        }

        static readonly string[] Banned =
        {
            "burrow", "bengal", "bengals", "nfl", "nike", "wilson", "cincinnati", "tiger", "joe", "james", "ramsey", "love", "jefferson", "derwin", "jalen", "jordan", "justin",
            "chargers", "steelers", "packers", "vikings", "lightning", "bolt", "riddell", "adidas", "reebok", "outsource2", "o2u", "shield", "swoosh",
        };

        static bool HasBanned(string name, out string hit)
        {
            hit = null;
            foreach (var tok in Regex.Split(name.ToLowerInvariant(), "[^a-z0-9]+"))
                foreach (var b in Banned) if (tok == b) { hit = b; return true; }
            return false;
        }

        [Test]
        public void NothingOfTheRealPlayersOrTeamsShipsInTheRosterV2Assets()
        {
            if (!Directory.Exists(Staging) && !Directory.Exists(Root + "/Prefabs")) Assert.Ignore("licensed folder absent");
            string hit;
            foreach (var dir in new[] { Staging, Root + "/Clips", Root + "/Textures/Pads" })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string path in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")))
                    Assert.IsFalse(HasBanned(Path.GetFileName(path), out hit), "forbidden name (" + hit + ") in " + path);
            }
            string where; var all = Prefabs(out where);
            foreach (var p in all)
            {
                foreach (var t in p.GetComponentsInChildren<Transform>(true)) Assert.IsFalse(HasBanned(t.name, out hit), "forbidden node name " + t.name + " (" + hit + ")");
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) Assert.IsFalse(HasBanned(m.name, out hit), "forbidden material name " + m.name);
                foreach (var smr in p.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null) Assert.IsFalse(HasBanned(smr.sharedMesh.name, out hit), "forbidden mesh name " + smr.sharedMesh.name);
            }
            // the purchased bodies' heads / hair / beards / eyes / glasses / visor decals are never part of the build: only the Draft Fever heads exist
            foreach (var p in all)
                foreach (var t in p.GetComponentsInChildren<Transform>(true))
                    foreach (var forbidden in new[] { "beard_tr", "hair_tr", "eyelashes_tr", "wristcoach", "armband", "padding_" })
                        Assert.IsFalse(t.name.ToLowerInvariant() == forbidden, "leftover node of a purchased body: " + t.name);
        }

        [Test]
        public void BipedClipsAreHumanoidMuscleClipsWithTheExpectedLengths()
        {
            string dir = Directory.Exists(Staging + "/Clips") ? Staging + "/Clips" : Root + "/Clips";
            if (!File.Exists(dir + "/clips.json")) Assert.Ignore("biped clips not imported yet");
            var expected = new System.Collections.Generic.Dictionary<string, Vector2>   // key -> min / max length (s)
            {
                { "kick_off_biped", new Vector2(1.6f, 2.8f) }, { "qb_throw_biped", new Vector2(1.0f, 1.8f) }, { "sprint_biped", new Vector2(0.4f, 0.7f) }, { "bc_sprint_biped", new Vector2(0.45f, 0.8f) },
                { "helmet_off", new Vector2(2.5f, 4.1f) }, { "clinch_a", new Vector2(2.0f, 2.8f) }, { "clinch_b", new Vector2(2.0f, 2.8f) }, { "walk_macho", new Vector2(1.0f, 1.5f) },
            };
            var txt = File.ReadAllText(dir + "/clips.json");
            foreach (var kv in expected)
            {
                Assert.IsTrue(txt.Contains("\"" + kv.Key + "\""), "clips.json lacks " + kv.Key);
                var guid = AssetDatabase.FindAssets(kv.Key + " t:AnimationClip", new[] { dir }).FirstOrDefault();
                Assert.IsNotNull(guid, "clip asset " + kv.Key);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsTrue(clip.isHumanMotion, kv.Key + " must be a Humanoid (muscle space) clip");
                Assert.GreaterOrEqual(clip.length, kv.Value.x, kv.Key + " length"); Assert.LessOrEqual(clip.length, kv.Value.y, kv.Key + " length");
            }
        }
    }
}
