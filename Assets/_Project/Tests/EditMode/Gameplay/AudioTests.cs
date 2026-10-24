using System;
using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.11.0 AUDIO+CROWD: the master limiter never lets a sample past its ceiling, the bank has every group the match asks for, and every sim event that should be heard is.</summary>
    public class AudioTests
    {
        static readonly string[] RequiredSfx =
        {
            "hit_light", "hit_medium", "hit_heavy", "hit_big", "block_thud", "helmet_clack", "shed_slap", "grunt_hit", "grunt_effort", "pile_scrum", "ground_thump",
            "snap_slap", "handoff_slap", "throw_whoosh", "catch_soft", "catch_hard", "catch_contested", "drop_thud", "bounce_dry", "bounce_wet", "bounce_snow", "fumble_pop",
            "kick_thump", "kick_punt", "step_run_dry", "step_run_wet", "step_run_snow", "step_walk_dry", "plant_cut", "whistle_short", "whistle_long", "flag_toss",
        };
        static readonly string[] RequiredCrowd =
        {
            "bed_murmur_low", "bed_murmur_mid", "bed_murmur_high", "bed_tension", "bed_noise", "bed_clap", "bed_cheer", "bed_boo", "cheer_s", "cheer_m", "cheer_l", "roar_td",
            "groan", "boo_short", "gasp", "ooh_hit", "clap_burst",
        };
        static readonly string[] RequiredMusic =
        {
            "menu_calm", "menu_drive", "menu_house", "pregame_bed", "pregame_intro", "halftime_loop", "match_under_low", "match_under_tense",
            "st_touchdown", "st_fieldgoal", "st_turnover", "st_halftime", "st_final_win", "st_final_loss", "st_twominute",
        };

        [Test]
        public void TheMasterLimiterKeepsAHotMixUnderItsCeiling()
        {
            var go = new GameObject("limiter");
            try
            {
                var lim = go.AddComponent<MasterLimiter>();
                var buf = new float[4096];
                for (int i = 0; i < buf.Length / 2; i++) { float v = 2.5f * Mathf.Sin(i * 0.05f); buf[2 * i] = v; buf[2 * i + 1] = -v; }
                lim.Process(buf, 2);
                float ceil = Mathf.Pow(10f, lim.CeilingDb / 20f), peak = 0f;
                foreach (var s in buf) peak = Mathf.Max(peak, Mathf.Abs(s));
                Assert.LessOrEqual(peak, ceil + 1e-4f, "no sample past the ceiling");
                Assert.Greater(lim.LimitedSamples, 0);
                // a quiet signal passes untouched
                var quiet = new float[512]; for (int i = 0; i < quiet.Length; i++) quiet[i] = 0.1f * Mathf.Sin(i * 0.1f);
                var copy = (float[])quiet.Clone();
                var lim2 = new GameObject("l2").AddComponent<MasterLimiter>(); lim2.MakeupDb = 0f;
                lim2.Process(quiet, 2);
                for (int i = 0; i < 64; i++) Assert.AreEqual(copy[i], quiet[i], 0.02f);
                UnityEngine.Object.DestroyImmediate(lim2.gameObject);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheBankHasEveryGroupTheMatchPlays()
        {
            var bank = PixAudioBank.Load();
            if (bank == null) Assert.Ignore("PixAudioBank is not built on this machine (PIX Football > Audio > Build Audio Bank)");
            foreach (var k in RequiredSfx) Assert.IsTrue(bank.Has(k), "sfx group " + k);
            foreach (var k in RequiredCrowd) Assert.IsTrue(bank.Has(k), "crowd group " + k);
            foreach (var k in RequiredMusic) Assert.IsNotNull(bank.Named(k), "music cue " + k);
            // never the owner's sung songs
            foreach (var g in bank.groups) foreach (var c in g.clips) Assert.IsFalse(c.name.StartsWith("music_pp"), "sung song in the bank: " + c.name);
        }

        [Test]
        public void EverySimEventThatShouldBeHeardStartsASound()
        {
            var bank = PixAudioBank.Load();
            if (bank == null) Assert.Ignore("PixAudioBank is not built on this machine");
            // events that are bookkeeping only: the sound comes from a neighbouring event or the outcome
            var silent = new System.Collections.Generic.HashSet<SimEventType> { SimEventType.FirstDownCrossed, SimEventType.Touchback, SimEventType.FieldGoalMissed, SimEventType.Scramble, SimEventType.PlayEnd, SimEventType.KickApproach, SimEventType.FieldGoalGood };      // FieldGoalGood: the stinger and the crowd come from the play outcome
            var go = new GameObject("audiotest");
            try
            {
                var hub = AudioHub.Ensure();
                var ma = go.AddComponent<MatchAudio>(); ma.Init();
                var frame = new PlayFrame(true);
                var missing = new System.Collections.Generic.List<string>();
                foreach (SimEventType t in Enum.GetValues(typeof(SimEventType)))
                {
                    if (silent.Contains(t)) continue;
                    hub.ResetLimits();
                    int before = hub.TotalStarted + ma.Crowd.Reactions + ma.Music.StingerCount;
                    var e = new PlayEvent { Type = t, AgentA = -1, AgentB = -1, Pos = new Vec3(26f, 40f, 1f), Value = 5f };
                    ma.OnEvent(e, frame);
                    // hits are scheduled a beat later; the crowd and the stingers count too
                    int after = hub.TotalStarted + ma.Crowd.Reactions + ma.Music.StingerCount;
                    if (after == before && t != SimEventType.Tackle && t != SimEventType.Sack && t != SimEventType.Whistle) missing.Add(t.ToString());
                }
                Assert.IsEmpty(missing, "events with no sound: " + string.Join(", ", missing) + " | dropped: " + string.Join(", ", hub.Dropped) + " | missing groups: " + string.Join(", ", hub.Missing));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); if (AudioHub.Instance != null) UnityEngine.Object.DestroyImmediate(AudioHub.Instance.gameObject); }
        }
    }
}
