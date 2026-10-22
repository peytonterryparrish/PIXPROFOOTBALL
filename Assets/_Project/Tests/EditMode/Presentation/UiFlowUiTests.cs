using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Gameplay;
using PixFootball.Presentation;
using PixFootball.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.Tests
{
    /// <summary>UI / flow QA pass (ui_flow.md): prompt parsing, settings persistence, display helpers, quality tiers, pause handling.</summary>
    public class UiFlowUiTests
    {
        // ---- prompts
        [Test]
        public void HintTokensBecomeGlyphItems()
        {
            var items = UiPrompts.ParseHint("[A][B][X][Y][LB][RB] THROW (TAP = BULLET)   [RS] LEAD   [LS] SCRAMBLE");
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(6, items[0].Glyphs.Length);
            Assert.AreEqual("glyph_a", items[0].Glyphs[0]);
            Assert.AreEqual("THROW (TAP = BULLET)", items[0].Text);
            Assert.AreEqual("glyph_rs", items[1].Glyphs[0]); Assert.AreEqual("LEAD", items[1].Text);
            Assert.AreEqual("glyph_ls", items[2].Glyphs[0]); Assert.AreEqual("SCRAMBLE", items[2].Text);
        }

        [Test]
        public void PlainTextHintHasNoGlyphsAndUnknownBracketsStayText()
        {
            var items = UiPrompts.ParseHint("GET READY TO RETURN THE KICK [NOPE] now");
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual(0, items[0].Glyphs.Length);
            StringAssert.Contains("[NOPE]", items[0].Text);
            Assert.AreEqual(0, UiPrompts.ParseHint("").Count);
            Assert.AreEqual(0, UiPrompts.ParseHint(null).Count);
        }

        [Test]
        public void PlainHintFollowsTheActiveDevice()
        {
            Assert.AreEqual("A KICK   LT TIMEOUT", UiPrompts.PlainHint("[A] KICK   [LT] TIMEOUT", true));
            Assert.AreEqual("SPACE KICK   V TIMEOUT", UiPrompts.PlainHint("[A] KICK   [LT] TIMEOUT", false));
        }

        [Test]
        public void EveryGlyphTheUiDrawsHasAKeyboardCap()
        {
            string[] glyphs = { "glyph_a", "glyph_b", "glyph_x", "glyph_y", "glyph_lb", "glyph_rb", "glyph_lt", "glyph_rt", "glyph_ls", "glyph_rs", "glyph_view", "glyph_menu",
                                "glyph_dpad", "glyph_dpad_up", "glyph_dpad_down", "glyph_dpad_left", "glyph_dpad_right", "glyph_analog_all", "glyph_analog_click" };
            foreach (var g in glyphs) Assert.AreNotEqual("?", UiPrompts.KeyOf(g), g + " has no keyboard cap");
            foreach (string tok in new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "LS", "RS", "VIEW", "MENU", "UP", "DOWN", "LEFT", "RIGHT", "DPAD", "NAV" })
                Assert.IsNotNull(UiPrompts.GlyphOfToken(tok), tok);
        }

        [Test]
        public void KeyboardBindingsDoNotClash()
        {
            // one key per glyph: no two controller buttons share a keyboard key (the audit found F and Space doing double duty)
            var seen = new Dictionary<string, string>();
            foreach (var g in new[] { "glyph_a", "glyph_b", "glyph_x", "glyph_y", "glyph_lb", "glyph_rb", "glyph_lt", "glyph_rt", "glyph_view", "glyph_menu", "glyph_dpad_up", "glyph_dpad_down", "glyph_dpad_left", "glyph_dpad_right" })
            {
                string k = UiPrompts.KeyOf(g);
                Assert.IsFalse(seen.ContainsKey(k), g + " and " + (seen.ContainsKey(k) ? seen[k] : "") + " share " + k);
                seen[k] = g;
            }
        }

        [Test]
        public void ForcedInputDeviceDrivesTheScheme()
        {
            InputDeviceTracker.Force(false);
            Assert.IsFalse(InputDeviceTracker.GamepadActive); Assert.IsFalse(MatchInput.GamepadActive); Assert.AreEqual("KeyboardMouse", PixInput.ActiveScheme);
            InputDeviceTracker.Force(true);
            Assert.IsTrue(MatchInput.GamepadActive); Assert.AreEqual("Gamepad", PixInput.ActiveScheme);
        }

        [Test]
        public void SwallowedInputIsEmpty()
        {
            var f = MatchInput.Instance.Frame;
            f.Down[(int)Btn.A] = true; f.LTDown = true;
            MatchInput.SwallowInput(1);
            Assert.IsFalse(f.Down[(int)Btn.A]); Assert.IsFalse(f.LTDown);
            var polled = MatchInput.Instance.Poll(0.016f);
            Assert.IsFalse(polled.AnyDown);
        }

        // ---- layout helpers
        [Test]
        public void FitScaleShrinksButNeverEnlarges()
        {
            Assert.AreEqual(1f, FitScale.ScaleFor(new Vector2(1920, 1080), new Vector2(800, 700)), 1e-4f);
            Assert.AreEqual(0.5f, FitScale.ScaleFor(new Vector2(400, 1000), new Vector2(800, 700)), 1e-4f);
            Assert.AreEqual(0.7f, FitScale.ScaleFor(new Vector2(2000, 490), new Vector2(800, 700)), 1e-4f);
            Assert.GreaterOrEqual(FitScale.ScaleFor(new Vector2(0, 0), new Vector2(800, 700)), 0.3f);
        }

        [Test]
        public void LineScoreFoldsAllOvertimesAndToleratesShortArrays()
        {
            var by = new int[8]; by[1] = 7; by[2] = 3; by[3] = 0; by[4] = 10; by[5] = 6; by[6] = 3; by[7] = 7;
            CollectionAssert.AreEqual(new[] { 7, 3, 0, 10, 16 }, MatchHud.LineScoreCells(by));
            CollectionAssert.AreEqual(new[] { 7, 3, 0, 0, 0 }, MatchHud.LineScoreCells(new[] { 0, 7, 3, 0 }));
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0 }, MatchHud.LineScoreCells(null));
            // the OT cell stays clear of the 180 px total box on the right of a 1300 px row
            Assert.Less(MatchHud.QuarterCellX(4) + 45f, 1300f - 40f - 180f);
        }

        [Test]
        public void ShowTextHelpers()
        {
            Assert.AreEqual("QB  -  BUF  -  5/6", BroadcastDirector.JoinNonEmpty("  -  ", "QB", "BUF", "5/6"));
            Assert.AreEqual("K  -  BUF", BroadcastDirector.JoinNonEmpty("  -  ", "K", "BUF", ""));
            Assert.AreEqual("", BroadcastDirector.JoinNonEmpty("  -  ", "", null));
            Assert.AreEqual(12, BroadcastDirector.AdjustTemperature(40, PixFootball.Core.WeatherKind.Snow), "snow games are cold");
            Assert.Less(BroadcastDirector.AdjustTemperature(70, PixFootball.Core.WeatherKind.Rain), 70);
            Assert.AreEqual(70, BroadcastDirector.AdjustTemperature(70, PixFootball.Core.WeatherKind.Clear));
        }

        // ---- team select
        [Test]
        public void TeamSelectRepairsAStoredSetup()
        {
            var teams = TeamCatalog.Teams;
            Assert.GreaterOrEqual(teams.Count, 2);
            var stored = new MatchConfig { HomeId = "gone", AwayId = "gone", QuarterMinutes = 99, CpuVsCpu = true, Seed = 5 };
            var cfg = TeamSelectPanel.InitialConfig(teams, stored, null, teams[0].Id);
            Assert.IsNotNull(TeamCatalog.ById(cfg.HomeId)); Assert.AreNotEqual(cfg.HomeId, cfg.AwayId);
            Assert.AreEqual(5, cfg.QuarterMinutes); Assert.IsFalse(cfg.CpuVsCpu); Assert.AreEqual(0, cfg.Seed);
            var good = new MatchConfig { HomeId = teams[3].Id, AwayId = teams[4].Id, QuarterMinutes = 8, CollegeRules = true };
            var kept = TeamSelectPanel.InitialConfig(teams, good, null, teams[0].Id);
            Assert.AreEqual(teams[3].Id, kept.HomeId); Assert.AreEqual(teams[4].Id, kept.AwayId); Assert.AreEqual(8, kept.QuarterMinutes); Assert.IsTrue(kept.CollegeRules);
        }

        [Test]
        public void TeamSelectSummaryIsUpperCase()
        {
            string s = TeamSelectPanel.SummaryText(new MatchConfig { QuarterMinutes = 5, CollegeRules = true, Difficulty = Difficulty.AllPro }, true);
            Assert.AreEqual(s, s.ToUpperInvariant());
            StringAssert.Contains("COLLEGE RULES", s); StringAssert.Contains("ALL-PRO", s); StringAssert.Contains("PICK TWO DIFFERENT TEAMS", s);
        }

        // ---- settings
        [Test]
        public void SettingsRoundTripAndResetToDefaults()
        {
            var master = PlayerPrefs.GetFloat("pix.vol_master", 0.9f); var cine = PixSettings.Cinematics; var invert = PixSettings.InvertLookY; var sfx = PixSettings.SfxVolume; var look = PixSettings.LookSensitivity;
            try
            {
                PixSettings.SfxVolume = 0.25f; PixSettings.LookSensitivity = 2.5f; PixSettings.InvertLookY = true; PixSettings.Cinematics = 2; PixSettings.VSync = 1; PixSettings.FrameCapIndex = 3;
                Assert.AreEqual(0.25f, PixSettings.SfxVolume, 1e-4f); Assert.AreEqual(2.5f, PixSettings.LookSensitivity, 1e-4f); Assert.IsTrue(PixSettings.InvertLookY);
                Assert.AreEqual(2, PixSettings.Cinematics); Assert.AreEqual(1, PixSettings.VSync); Assert.AreEqual(90, PixSettings.FrameCaps[PixSettings.FrameCapIndex]);
                Assert.IsTrue(PixSettings.HasPending, "a setter schedules a save");
                PixSettings.ResetToDefaults();
                Assert.AreEqual(0.9f, PixSettings.SfxVolume, 1e-4f); Assert.AreEqual(1f, PixSettings.LookSensitivity, 1e-4f); Assert.IsFalse(PixSettings.InvertLookY);
                Assert.AreEqual(0, PixSettings.Cinematics); Assert.AreEqual(-1, PixSettings.VSync); Assert.AreEqual(0, PixSettings.FrameCapIndex);
                PixSettings.SfxVolume = 5f; Assert.AreEqual(1f, PixSettings.SfxVolume, 1e-4f, "clamped");
            }
            finally
            {
                PixSettings.ResetToDefaults();
                PixSettings.Cinematics = cine; PixSettings.InvertLookY = invert; PixSettings.SfxVolume = sfx; PixSettings.LookSensitivity = look; PlayerPrefs.SetFloat("pix.vol_master", master);
                PixSettings.Save();
            }
        }

        [Test]
        public void MatchConfigIsStoredWhole()
        {
            string before = PixSettings.LastMatchConfigJson;
            try
            {
                var c = new MatchConfig { HomeId = "a_team", AwayId = "b_team", QuarterMinutes = 8, CollegeRules = true, Difficulty = Difficulty.AllPro, UserTeam = PixFootball.Core.Team.Away,
                                          Weather = PixFootball.Core.WeatherChoice.Snow, Time = PixFootball.Core.TimeChoice.Dusk };
                PixSettings.StoreMatchConfig(c);
                var r = PixSettings.LoadMatchConfig();
                Assert.IsNotNull(r);
                Assert.AreEqual("a_team", r.HomeId); Assert.AreEqual("b_team", r.AwayId); Assert.AreEqual(8, r.QuarterMinutes); Assert.IsTrue(r.CollegeRules);
                Assert.AreEqual(Difficulty.AllPro, r.Difficulty); Assert.AreEqual(PixFootball.Core.Team.Away, r.UserTeam);
                Assert.AreEqual(PixFootball.Core.WeatherChoice.Snow, r.Weather); Assert.AreEqual(PixFootball.Core.TimeChoice.Dusk, r.Time);
                PixSettings.LastMatchConfigJson = "not json";
                Assert.IsNull(PixSettings.LoadMatchConfig(), "unreadable JSON falls back to nothing");
            }
            finally { PixSettings.LastMatchConfigJson = before; PixSettings.Save(); }
        }

        // ---- game session
        [Test]
        public void ResumingRestoresTheTimeScaleThatWasRunning()
        {
            float before = Time.timeScale;
            try
            {
                GameSession.ResetTime();
                Time.timeScale = 0.25f;
                GameSession.SetPaused(true);
                Assert.AreEqual(0f, Time.timeScale); Assert.IsTrue(GameSession.Paused);
                GameSession.SetPaused(false);
                Assert.AreEqual(0.25f, Time.timeScale, 1e-4f, "slow motion is restored, not forced to 1");
                GameSession.SetPaused(true); GameSession.ResetTime();
                Assert.AreEqual(1f, Time.timeScale); Assert.IsFalse(GameSession.Paused); Assert.IsFalse(AudioListener.pause);
            }
            finally { GameSession.ResetTime(); Time.timeScale = before; }
        }

        [Test]
        public void VersionComesFromThePlayerSettings()
        {
            Assert.AreEqual(Application.version, GameplayInfo.Version);
            Assert.IsFalse(string.IsNullOrEmpty(GameplayInfo.Version));
        }

        // ---- graphics tiers go both ways
        [Test]
        public void ChoosingAHigherTierSwitchesFeaturesBackOn()
        {
            int level = QualitySettings.GetQualityLevel();
            var go = new GameObject("tier-test-volume");
            try
            {
                var vol = go.AddComponent<Volume>(); vol.isGlobal = true;
                var prof = vol.profile;   // the instance the tier code works on (ApplyScene reads Volume.profile)
                var ao = prof.Add<ScreenSpaceAmbientOcclusion>(true); ao.active = true;
                var mb = prof.Add<MotionBlur>(true); mb.active = true;
                QualityTiers.Override(0, -1f);
                Assert.IsFalse(ao.active, "Low switches ambient occlusion off");
                Assert.IsFalse(mb.active);
                QualityTiers.Override(3, -1f);
                Assert.IsTrue(ao.active, "Ultra switches it back on");
                Assert.IsTrue(mb.active);
                QualityTiers.Override(1, -1f);
                Assert.IsTrue(ao.active, "Medium keeps SSAO"); Assert.IsFalse(mb.active, "Medium has no motion blur");
            }
            finally
            {
                Object.DestroyImmediate(go);
                QualityTiers.ClearOverride();
                QualitySettings.SetQualityLevel(level, true);
                QualityTiers.ApplyScene();
            }
        }
    }
}
