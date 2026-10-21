using System.IO;
using System.Linq;
using NUnit.Framework;
using PixFootball.Art;
using PixFootball.EditorTools;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.Tests
{
    public class GameplayTests
    {
        [Test]
        public void InputMapHasAllActionsAndBothSchemes()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputMapBuilder.AssetPath);
            Assert.IsNotNull(asset, "PixControls.inputactions missing");
            var map = asset.FindActionMap("Gameplay");
            Assert.IsNotNull(map);
            foreach (string a in InputMapBuilder.ExpectedActions) Assert.IsNotNull(map.FindAction(a), "missing action " + a);
            Assert.IsNotNull(asset.FindControlScheme("Gamepad"));
            Assert.IsNotNull(asset.FindControlScheme("KeyboardMouse"));
            foreach (var act in map.actions) Assert.Greater(act.bindings.Count, 0, act.name + " has no bindings");
        }

        [Test]
        public void MoveBindingsCoverStickAndKeys()
        {
            var map = InputMapBuilder.Create().FindActionMap("Gameplay");
            Assert.IsTrue(map.FindAction("Move").bindings.Any(b => b.path == "<Gamepad>/leftStick"));
            Assert.IsTrue(map.FindAction("Sprint").bindings.Any(b => b.path == "<Gamepad>/rightTrigger"));
            Assert.IsTrue(map.FindAction("Hurdle").bindings.Any(b => b.path == "<Gamepad>/buttonSouth"));
        }

        [Test]
        public void ArtKeysUsedByMovesExist()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) Assert.Ignore("licensed art not present");
            foreach (var key in new[] { ArtKeys.Idle, ArtKeys.Jog, ArtKeys.Run, ArtKeys.Sprint, ArtKeys.Backpedal, ArtKeys.StrafeLeft, ArtKeys.StrafeRight })
                Assert.IsTrue(lib.HasClip(key), "missing clip key " + key);
            Assert.IsNotNull(lib.Prefab("player_medium"));
            foreach (var spec in PlayerMoves.All)
                Assert.IsTrue(spec.Move == PlayerMove.Spin || AnimatorControllerBuilder.ResolveClip(spec, lib) != null, "no clip for " + spec.Move);
        }

        [Test]
        public void SimSpaceRoundTrips()
        {
            var u = new Vector3(5f, 0f, -20f);
            var back = SimSpace.ToUnity(SimSpace.ToSim(u));
            Assert.AreEqual(u.x, back.x, 1e-3f); Assert.AreEqual(u.z, back.z, 1e-3f);
            var mid = SimSpace.ToUnity(new PixFootball.Core.Vec2(26.665f, 50f));
            Assert.AreEqual(0f, mid.x, 1e-3f); Assert.AreEqual(0f, mid.z, 1e-3f);
        }

        [Test]
        public void LocomotionAcceleratesAndRespectsMomentum()
        {
            var loco = new AgentBodyLocomotion(AgentBodyLocomotion.DefaultRatings());
            loco.Teleport(Vector3.zero, 0f);
            var forward = new LocomotionInput { Move = new Vector2(0f, 1f), Sprint = true };
            for (int i = 0; i < 120; i++) loco.Step(1f / 60f, forward);
            float v = loco.Velocity.magnitude;
            Assert.Greater(v, 6.5f, "should be near top speed after 2s");
            Assert.Less(v, 13f);
            var back = new LocomotionInput { Move = new Vector2(0f, -1f), Sprint = true };
            loco.Step(1f / 60f, back);
            Assert.Greater(loco.Velocity.z, 0f, "momentum should carry forward one tick after reversing");
        }

        [Test]
        public void TeamCatalogLoads32Teams()
        {
            Assert.AreEqual(32, TeamCatalog.Teams.Count);
            Assert.IsNotNull(TeamCatalog.ById(TeamCatalog.DefaultTeamId));
        }

        [Test]
        public void SettingsRoundTrip()
        {
            PixSettings.InvertLookY = true; PixSettings.MasterVolume = 0.33f;
            Assert.IsTrue(PixSettings.InvertLookY); Assert.AreEqual(0.33f, PixSettings.MasterVolume, 1e-4f);
            PixSettings.InvertLookY = false; PixSettings.MasterVolume = 0.9f;
        }

        static GameObject FindAny(string n)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t.name == n && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }

        [Test]
        public void BuiltScenesContainExpectedObjects()
        {
            if (!File.Exists(SceneBuilder.StadiumScenePath)) Assert.Ignore("scenes not built");
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath, OpenSceneMode.Single);
            foreach (string n in SceneBuilder.ExpectedStadiumObjects) Assert.IsNotNull(FindAny(n), "stadium scene missing " + n);
            var crowd = Object.FindFirstObjectByType<CrowdInfo>();
            Assert.IsNotNull(crowd);
            Assert.GreaterOrEqual(crowd.FanCount, 20000, "crowd should fill 20k+ seats");
            Assert.IsNotNull(Object.FindFirstObjectByType<LightingController>());
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath, OpenSceneMode.Single);
            foreach (string n in SceneBuilder.ExpectedMenuObjects) Assert.IsNotNull(FindAny(n), "menu scene missing " + n);
            var scenes = EditorBuildSettings.scenes.Select(x => x.path).ToArray();
            CollectionAssert.Contains(scenes, SceneBuilder.MenuScenePath);
            CollectionAssert.Contains(scenes, SceneBuilder.StadiumScenePath);
        }

        [Test]
        public void StadiumMaterialsUseCleanedBrandingTextures()
        {
            if (!File.Exists(SceneBuilder.StadiumScenePath)) Assert.Ignore("scenes not built");
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath, OpenSceneMode.Single);
            string[] banned = { "/Banner01_Text.png", "/bannerTest_BaseMap.png", "/adBanner01_Emissive.png", "/Endzone_Tigers_02.png", "/screenTest.png" };
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var sh = m.shader;
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                        var t = m.GetTexture(sh.GetPropertyName(i)); if (t == null) continue;
                        string p = AssetDatabase.GetAssetPath(t);
                        foreach (string b in banned) Assert.IsFalse(p.EndsWith(b) && !p.Contains("/PIX/"), "original branded texture used: " + p + " on " + r.name);
                    }
                }
        }
    }
}
