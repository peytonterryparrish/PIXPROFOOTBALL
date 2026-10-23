using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 ANIMLIB: builds the clip tag table (PixClipTags.asset, runtime class ClipTagTable / ClipSelect) and its CSV twin for every clip of the library (registered under a key) and
    /// every cleaned clip: semantic key + category + positions, entry / exit foot and gait phase, sync moment (release, catch, contact, kick, snap, strike), duration, root speed and
    /// direction, turn, QA score. Also writes the final QA rows. Batch: PixFootball.EditorTools.AnimLibTags.Run
    /// </summary>
    public static class AnimLibTags
    {
        public const string CsvPath = "Docs/QA/v0100/animlib_clip_tags.csv";

        // ------------------------------------------------------------------------------------------------ classification
        static bool P(string k, string pre) { return k.StartsWith(pre, StringComparison.Ordinal); }

        public static void Classify(string key, string name, out string cat, out string roles)
        {
            string k = (key ?? "").ToLowerInvariant(), n = name.ToLowerInvariant();
            string s = k.Length > 0 ? k : n;
            cat = "misc"; roles = "ALL";
            if (P(s, "qb_stance") || P(s, "qb_cadence") || P(s, "qb_snap") || P(s, "qb_hand") || P(s, "qb_ready") || P(s, "qb_look") || P(s, "qb_drop") || P(s, "bones_qb") || P(s, "qb_")) { cat = "QB"; roles = "QB"; if (P(s, "qb_sack")) cat = "tackle"; if (P(s, "qb_toss")) cat = "handoff"; return; }
            if (s.Contains("center_snap") || s == "long_snap" || P(s, "snapper") || P(s, "stance_snapper") || P(s, "ls_") || s.Contains("long_snap")) { cat = "LS"; roles = "LS"; return; }
            if (P(s, "stance_center") || P(s, "c_idle")) { cat = "stance"; roles = "OL"; return; }
            if (P(s, "stance_ol") || P(s, "ol_stance") || P(s, "stance_te")) { cat = "stance"; roles = P(s, "stance_te") ? "TE,OL" : "OL,TE"; return; }
            if (P(s, "ol_block") || P(s, "ol_pass") || P(s, "ol_pull") || P(s, "press_jam") || P(s, "push_oob") || P(s, "trench_atk") || P(s, "block")) { cat = "block"; roles = "OL,TE,DL"; return; }
            if (P(s, "ol_")) { cat = "OL"; roles = "OL,TE"; return; }
            if (P(s, "dl_") || P(s, "stance_dl") || P(s, "stance_wide") || P(s, "trench_def") || P(s, "stance_dline") || P(s, "dline")) { cat = "DL"; roles = "DL"; if (P(s, "dl_rush") || P(s, "dl_bull") || P(s, "dl_shed") || P(s, "dl_wrong")) cat = "block"; return; }
            if (P(s, "stance_lb") || P(s, "lb_")) { cat = "LB"; roles = "LB"; return; }
            if (P(s, "interception")) { cat = "catch"; roles = "DB,LB"; return; }
            if (P(s, "swat")) { cat = "DB"; roles = "DB,LB,DL"; return; }
            if (P(s, "cb_") || P(s, "man_") || P(s, "zone_drop") || P(s, "backpedal_diag") || P(s, "stance_cb") || P(s, "stance_s") && !P(s, "stance_snapper")) { cat = "DB"; roles = "DB,LB"; if (P(s, "cb_break") || P(s, "cb_rotate")) cat = "transition"; return; }
            if (P(s, "wr_release")) { cat = "transition"; roles = "WR,TE,RB"; return; }
            if (P(s, "catch_") || P(s, "fair_catch") || P(s, "kick_return_catch")) { cat = "catch"; roles = P(s, "fair_catch") || P(s, "kick_return") ? "DB,WR,RB" : "WR,TE,RB,DB,LB"; return; }
            if (P(s, "route_")) { cat = "WR"; roles = "WR,TE,RB"; return; }
            if (P(s, "stance_wr")) { cat = "stance"; roles = "WR"; return; }
            if (P(s, "stance_rb") || P(s, "stance_halfback")) { cat = "stance"; roles = "RB"; return; }
            if (P(s, "kick_") || P(s, "punt") || P(s, "stance_kicker") || P(s, "stance_holder")) { cat = "KP"; roles = "K,P"; return; }
            if (P(s, "ref_")) { cat = "REF"; roles = "REF"; return; }
            if (P(s, "coach_")) { cat = "sideline"; roles = "COACH"; return; }
            if (P(s, "tackle") || P(s, "be_tackled") || P(s, "be_trucked") || P(s, "contact_") || P(s, "fall") || P(s, "takedown") || P(s, "injury")) { cat = P(s, "injury") ? "reaction" : "tackle"; roles = "ALL"; return; }
            if (P(s, "celebrate") || P(s, "dance") || P(s, "salute") || P(s, "cheer") || P(s, "spike") || P(s, "draft_night")) { cat = "celebration"; roles = "ALL"; return; }
            if (P(s, "frustration") || P(s, "react_") || P(s, "endzone_save")) { cat = "reaction"; roles = "ALL"; return; }
            if (P(s, "huddle") || P(s, "idle_conversation") || P(s, "kickoff_")) { cat = "huddle"; roles = "ALL"; return; }
            if (P(s, "bench") || P(s, "crowd_") || P(s, "idle_helmet") || P(s, "idle_lively") || P(s, "idle_look") || P(s, "idle")) { cat = "sideline"; roles = "ALL"; return; }
            if (P(s, "walk")) { cat = "walk"; roles = "ALL"; return; }
            if (P(s, "getup") || P(s, "get_up")) { cat = "transition"; roles = "ALL"; return; }
            if (P(s, "turn_") || P(s, "slowdown") || s.Contains("_to_sprint") || P(s, "bc_stop") || P(s, "bc_turn") || P(s, "sprint_turn") || P(s, "stop_sprint") || P(s, "shuffle_to") || P(s, "cut_") || P(s, "man_turn")) { cat = "transition"; roles = "ALL"; return; }
            if (P(s, "handoff")) { cat = "handoff"; roles = "QB,RB"; return; }
            if (P(s, "juke") || P(s, "spin") || P(s, "stiff") || P(s, "truck") || P(s, "hurdle") || P(s, "dive") || P(s, "rocker") || P(s, "ankle") || P(s, "fumble")) { cat = "carrier"; roles = "RB,WR,TE,QB"; return; }
            if (P(s, "jog") || P(s, "run") || P(s, "sprint") || P(s, "backpedal") || P(s, "shuffle") || P(s, "strafe") || P(s, "bc_")) { cat = "locomotion"; roles = "ALL"; return; }
            if (P(s, "stance")) { cat = "stance"; roles = "ALL"; return; }
        }

        // ------------------------------------------------------------------------------------------------ run
        public static void Run()
        {
            BatchWatchdog.Arm(120);
            Directory.CreateDirectory(AnimLibBatch.OutDir);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var keyList = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            if (lib != null)
                foreach (var e in lib.clips)
                {
                    if (e == null || e.clip == null) continue;
                    List<string> l; if (!keyList.TryGetValue(e.clip.name, out l)) keyList[e.clip.name] = l = new List<string>();
                    if (!l.Contains(e.key)) l.Add(e.key);
                    clips[e.clip.name] = e.clip;
                }
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g); if (p.Contains("/_rejected/")) continue;
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p); if (c != null && !clips.ContainsKey(c.name)) clips[c.name] = c;
            }
            var contact = ContactTable.Load();
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var table = AssetDatabase.LoadAssetAtPath<ClipTagTable>(ClipTagTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<ClipTagTable>(); AssetDatabase.CreateAsset(table, ClipTagTable.EditorPath); }
            table.entries.Clear();
            var csv = new StringBuilder("clip,key,keys,cat,roles,len,loop,entryFoot,exitFoot,entryPhase,exitPhase,syncT,syncKind,rootSpeed,gaitSpeed,rootDir,travel,turn,score,reject,faults,twin\n");
            var qa = new StringBuilder(QaRow.Header() + "\n");
            int n = 0;
            foreach (var kv in clips.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var c = kv.Value;
                try
                {
                    List<string> kl; keyList.TryGetValue(c.name, out kl);
                    string keys = kl != null ? string.Join("+", kl) : "";
                    var row = ClipQa.Analyse(rig, c, keys); row.source = AssetDatabase.GetAssetPath(c);
                    qa.AppendLine(row.Csv());
                    var e = Tag(rig, c, kl, row, contact, clips);
                    table.entries.Add(e);
                    Func<float, string> F = v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    csv.AppendLine(string.Join(",", new[] { Q(e.clip), Q(e.key), Q(e.keys), e.cat, Q(e.roles), F(e.len), e.loop ? "1" : "0", e.entryFoot.ToString(), e.exitFoot.ToString(), F(e.entryPhase), F(e.exitPhase), F(e.syncT), e.syncKind, F(e.rootSpeed), F(e.gaitSpeed), F(e.rootDir), F(e.travel), F(e.turn), F(e.score), e.reject ? "1" : "0", Q(row.faults), Q(e.twin) }));
                }
                catch (Exception ex) { Debug.LogWarning("[AnimLibTags] " + c.name + ": " + ex.Message); }
                if (++n % 100 == 0) Debug.Log("[AnimLibTags] " + n + "/" + clips.Count);
            }
            rig.Dispose();
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            // the Resources copy the player build loads next to PixClipTiming
            ClipTagTable.ResetCache();
            Directory.CreateDirectory(Path.GetDirectoryName(CsvPath));
            File.WriteAllText(CsvPath, csv.ToString());
            File.WriteAllText(AnimLibBatch.OutDir + "/qa_final.csv", qa.ToString());
            Debug.Log("[AnimLibTags] tags=" + table.entries.Count + " -> " + ClipTagTable.EditorPath + " and " + CsvPath);
            AnimLibBatch.Done();
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }

        static ClipTagTable.Entry Tag(AnimRig rig, AnimationClip c, List<string> keys, QaRow row, ContactTable contact, Dictionary<string, AnimationClip> all)
        {
            string key = keys != null && keys.Count > 0 ? keys[0] : "";
            string cat, roles; Classify(key, c.name, out cat, out roles);
            var e = new ClipTagTable.Entry { clip = c.name, key = key, keys = keys != null ? string.Join("+", keys) : "", cat = cat, roles = roles, len = c.length, loop = c.isLooping, score = row.score, reject = row.reject, twin = "" };
            e.rootSpeed = row.rootSpeed; e.gaitSpeed = row.gaitSpeed; e.rootDir = row.rootDir; e.travel = row.travel;
            float dur = Mathf.Min(c.length, 24f);
            var fr = rig.Sample(c, 0f, dur, 30f); int N = fr.Count; float dt = 1f / 30f;
            e.turn = Mathf.DeltaAngle(AnimRig.YawOf(AnimRig.Forward(fr[0])), AnimRig.YawOf(AnimRig.Forward(fr[N - 1])));
            // ---- feet
            var soleL = new float[N]; var soleR = new float[N];
            for (int i = 0; i < N; i++) { soleL[i] = ClipQa.Sole(fr[i], true); soleR[i] = ClipQa.Sole(fr[i], false); }
            e.entryFoot = Foot(soleL, soleR, 0, Mathf.Min(2, N - 1)); e.exitFoot = Foot(soleL, soleR, Mathf.Max(0, N - 3), N - 1);
            var ls = Strikes(soleL); var rs = Strikes(soleR);
            e.entryPhase = e.exitPhase = -1f;
            float period = Period(ls) > 0f ? Period(ls) : Period(rs);
            if (period > 0.2f && (ls.Count > 0 || rs.Count > 0))
            {
                float t0 = ls.Count > 0 ? ls[0] : rs[0] - period * 0.5f;
                e.entryPhase = Frac((0f - t0) / period); e.exitPhase = Frac(((N - 1) * dt - t0) / period);
            }
            // ---- sync moment
            string kk = key.Length > 0 ? key : c.name.ToLowerInvariant();
            var sk = SyncKind(kk, cat);
            e.syncKind = sk; e.syncT = 0f;
            if (sk != "none")
            {
                ClipTimingTable.Entry te;
                if (ClipTimingTable.TryEntry(c, out te) && te.t > 0.05f && (sk == "release" || sk == "catch" || sk == "kick")) e.syncT = te.t;
                else
                {
                    float tt = 0f;
                    ContactTable.Pair pair = null;
                    if (contact != null && (sk == "contact"))
                    {
                        foreach (var pr in contact.pairs) if (pr.clipA == c.name || pr.clipB == c.name) { pair = pr; break; }
                    }
                    if (pair != null && pair.tContact > 0.05f) tt = pair.tContact;
                    else tt = Moment(fr, sk, dt);
                    e.syncT = tt;
                }
            }
            if (c.name.EndsWith("_A") || c.name.EndsWith("_B"))
            {
                string other = c.name.Substring(0, c.name.Length - 1) + (c.name.EndsWith("A") ? "B" : "A");
                if (all.ContainsKey(other)) e.twin = other;
            }
            return e;
        }

        static char Foot(float[] l, float[] r, int a, int b)
        {
            float lm = 0f, rm = 0f; int n = 0; for (int i = a; i <= b; i++) { lm += l[i]; rm += r[i]; n++; }
            lm /= n; rm /= n;
            bool lp = lm < 0.05f, rp = rm < 0.05f;
            if (lp && rp) return 'B'; if (lp) return 'L'; if (rp) return 'R';
            return 'A';
        }

        static List<float> Strikes(float[] sole)
        {
            var res = new List<float>(); bool up = false;
            for (int i = 0; i < sole.Length; i++)
            {
                if (sole[i] > 0.06f) up = true;
                else if (sole[i] < 0.03f && up) { res.Add(i / 30f); up = false; }
            }
            return res;
        }

        static float Period(List<float> s)
        {
            if (s.Count < 2) return 0f;
            var d = new List<float>(); for (int i = 1; i < s.Count; i++) d.Add(s[i] - s[i - 1]); d.Sort();
            return d[d.Count / 2];
        }

        static float Frac(float x) { x -= Mathf.Floor(x); return x; }

        static string SyncKind(string k, string cat)
        {
            if (k.StartsWith("qb_throw") || k.StartsWith("qb_pump") || k.StartsWith("qb_toss") || k.StartsWith("qb_falling") || k.StartsWith("qb_hail")) return "release";
            if (k.StartsWith("catch_") || k.StartsWith("interception") || k.StartsWith("fair_catch") || k.StartsWith("kick_return_catch") || k.StartsWith("swat")) return "catch";
            if (k.StartsWith("kick_") || k.StartsWith("punt")) return "kick";
            if (k.StartsWith("handoff") || k.StartsWith("qb_fake")) return "exchange";
            if (k.StartsWith("tackle") || k.StartsWith("be_tackled") || k.StartsWith("be_trucked") || k.StartsWith("contact_") || k.StartsWith("qb_sack") || k.StartsWith("truck")) return "contact";
            if (k.StartsWith("ol_block") || k.StartsWith("ol_pass") || k.StartsWith("ol_pull") || k.StartsWith("dl_rush") || k.StartsWith("dl_bull") || k.StartsWith("dl_shed") || k.StartsWith("dl_wrong") || k.StartsWith("press_jam") || k.StartsWith("push_oob") || k.StartsWith("trench_")) return "contact";
            if (k.StartsWith("stiff_arm") || k.StartsWith("hurdle") || k.StartsWith("dive") || k.StartsWith("takedown")) return "strike";
            if (k.StartsWith("juke") || k.StartsWith("spin") || k.StartsWith("rocker") || k.StartsWith("ankle") || k.StartsWith("cut_") || k.StartsWith("route_") || k.StartsWith("bc_turn") || k.StartsWith("sprint_turn")) return "plant";
            if (k.StartsWith("qb_snap") || k.StartsWith("center_snap") || k.StartsWith("long_snap") || k.StartsWith("snapper")) return "snap";
            return "none";
        }

        /// <summary>Moment in the clip by kind from the sampled skeleton: release / strike = peak hand speed (relative to the hips), catch / exchange = hands closest while reaching, kick = peak foot speed, snap = first hand burst, plant = peak horizontal hip acceleration, contact = peak hand speed or chest deceleration.</summary>
        static float Moment(List<AnimRig.Frame> fr, string kind, float dt)
        {
            int N = fr.Count; int a = (int)(N * 0.1f), b = (int)(N * 0.92f);
            Func<int, int, Vector3> rel = (i, bone) => fr[i].P[bone] - fr[i].P[AnimRig.Hips];
            int best = N / 2; float bv = -1f;
            switch (kind)
            {
                case "catch":
                case "exchange":
                    {
                        float bd = float.MaxValue; int bi = N / 3;
                        for (int i = (int)(N * 0.12f); i < (int)(N * 0.78f); i++)
                        {
                            float d = Vector3.Distance(fr[i].P[AnimRig.LH], fr[i].P[AnimRig.RH]);
                            float reach = Mathf.Max(0.05f, (fr[i].P[AnimRig.LH].y + fr[i].P[AnimRig.RH].y) * 0.5f - fr[i].P[AnimRig.Hips].y + 0.4f);
                            float sc = d / reach; if (sc < bd) { bd = sc; bi = i; }
                        }
                        return bi * dt;
                    }
                case "kick":
                    for (int i = Mathf.Max(1, a); i < b; i++) for (int s = 0; s < 2; s++)
                        {
                            int f = s == 0 ? AnimRig.LF : AnimRig.RF; float v = (fr[i].P[f] - fr[i - 1].P[f]).magnitude / dt; if (v > bv) { bv = v; best = i; }
                        }
                    return best * dt;
                case "plant":
                    for (int i = Mathf.Max(2, a); i < b; i++)
                    {
                        Vector3 v1 = fr[i].P[AnimRig.Hips] - fr[i - 1].P[AnimRig.Hips], v0 = fr[i - 1].P[AnimRig.Hips] - fr[i - 2].P[AnimRig.Hips]; v1.y = v0.y = 0f;
                        float acc = (v1 - v0).magnitude / (dt * dt); if (acc > bv) { bv = acc; best = i; }
                    }
                    return best * dt;
                case "snap":
                    for (int i = Mathf.Max(1, a / 2); i < b; i++) for (int s = 0; s < 2; s++)
                        {
                            int h = s == 0 ? AnimRig.LH : AnimRig.RH; float v = ((rel(i, h) - rel(i - 1, h))).magnitude / dt; if (v > bv) { bv = v; best = i; }
                        }
                    return best * dt;
                default:       // release, strike, contact
                    for (int i = Mathf.Max(1, a); i < b; i++) for (int s = 0; s < 2; s++)
                        {
                            int h = s == 0 ? AnimRig.LH : AnimRig.RH; float v = ((rel(i, h) - rel(i - 1, h))).magnitude / dt; if (v > bv) { bv = v; best = i; }
                        }
                    return best * dt;
            }
        }
    }
}
