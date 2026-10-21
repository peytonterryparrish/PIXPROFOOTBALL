using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.4 autotest segment "contact gallery": scripted scrimmage plays that produce each tackle type (wrap-up, big hit, dive, shoestring, broken tackle, drag into a gang tackle,
    /// sack), each carrier move (juke, spin, stiff-arm, truck, hurdle), a contested catch, a pancake and a big-hit ragdoll, with a close-up screenshot sequence of every one
    /// (Logs/autotest/contact_*.png). Run with -pixAutotestContactOnly [-pixAutotestContactCases wrap,bighit,...].
    /// </summary>
    public partial class AutoTest
    {
        struct GalleryCase
        {
            public string Name; public TackleType? Force; public string Move; public bool Pass, Blitz, QbHolds;
            public SimEventType Trigger; public int Shots; public bool Ragdoll;
        }

        static GalleryCase[] GalleryCases()
        {
            return new[]
            {
                new GalleryCase { Name = "wrap", Force = TackleType.WrapUp, Trigger = SimEventType.Tackle },
                new GalleryCase { Name = "bighit", Force = TackleType.BigHit, Trigger = SimEventType.Tackle },
                new GalleryCase { Name = "dive", Force = TackleType.DiveTackle, Trigger = SimEventType.Tackle },
                new GalleryCase { Name = "shoestring", Force = TackleType.Shoestring, Trigger = SimEventType.Tackle },
                new GalleryCase { Name = "broken", Force = TackleType.BrokenTackle, Trigger = SimEventType.BrokenTackle },
                new GalleryCase { Name = "drag", Force = TackleType.Drag, Trigger = SimEventType.Tackle },
                new GalleryCase { Name = "sack", Force = TackleType.WrapUp, Pass = true, Blitz = true, QbHolds = true, Trigger = SimEventType.Sack },
                new GalleryCase { Name = "juke", Move = "JukeLeft", Trigger = SimEventType.CarrierMove },
                new GalleryCase { Name = "spin", Move = "Spin", Trigger = SimEventType.CarrierMove },
                new GalleryCase { Name = "stiffarm", Move = "StiffArm", Trigger = SimEventType.CarrierMove },
                new GalleryCase { Name = "truck", Move = "Truck", Trigger = SimEventType.CarrierMove },
                new GalleryCase { Name = "hurdle", Move = "Hurdle", Trigger = SimEventType.CarrierMove },
                new GalleryCase { Name = "catch", Pass = true, Trigger = SimEventType.Catch },
                new GalleryCase { Name = "ragdoll", Ragdoll = true, Trigger = SimEventType.PlayEnd },
            };
        }

        static int MoveCode(string m) { switch (m) { case "JukeLeft": return (int)CarrierMove.JukeLeft; case "JukeRight": return (int)CarrierMove.JukeRight; case "Spin": return (int)CarrierMove.Spin; case "StiffArm": return (int)CarrierMove.StiffArm; case "Truck": return (int)CarrierMove.Truck; case "Hurdle": return (int)CarrierMove.Hurdle; default: return -1; } }

        static int NearestDefender(MatchDirector dir, int agent)
        {
            var sim = dir.Sim; if (sim == null) return -1;
            var a = sim.Agents[agent]; int best = -1; float bd = 99f;
            for (int i = 0; i < 22; i++) { var o = sim.Agents[i]; if (o.Side == a.Side) continue; float d = (o.Pos - a.Pos).Length; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Default autotest run: the contact gallery with the cases that matter most (the full set runs with -pixAutotestContactOnly).</summary>
        const string BriefCases = ",wrap,bighit,broken,drag,sack,spin,truck,catch,ragdoll,";

        IEnumerator ContactPart(bool brief = false)
        {
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 15, UserTeam = CoreTeam.Home, Seed = 20261022, CpuVsCpu = false, Difficulty = Difficulty.Pro };
            MatchDirector.ForceAutopilot = true;
            var previous = MatchDirector.Instance;   // the default autotest run reaches this after other matches: wait for the NEW director
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance == previous || MatchDirector.Instance.Flow == null) && wait < 90f) { wait += Time.unscaledDeltaTime; yield return null; }
            var dir = MatchDirector.Instance;
            if (dir == null || dir.Flow == null) { Log("FAIL: match did not start"); yield break; }
            Log("contact gallery: match started in " + wait.ToString("0.0") + "s");

            string only = brief ? BriefCases : null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-pixAutotestContactCases") only = "," + args[i + 1] + ",";

            GalleryCase cur = default(GalleryCase); bool active = false;
            int finished = 0, triggered = 0;
            float triggeredAt = 0f;
            var seq = new List<string>();
            dir.PlayFinished += o => { finished++; };
            dir.SimEvent += e =>
            {
                if (!active || cur.Trigger != e.Type || triggered > 0) return;
                bool ok = false; int focus = -1;
                switch (e.Type)
                {
                    case SimEventType.Tackle: ok = e.AgentA >= 0 && e.AgentB >= 0; focus = e.AgentB; break;
                    case SimEventType.BrokenTackle: ok = e.AgentA >= 0 && e.AgentB >= 0; focus = e.AgentB; break;
                    case SimEventType.Sack: ok = e.AgentB >= 0; focus = e.AgentB; break;
                    case SimEventType.CarrierMove: ok = e.AgentA >= 0 && ((e.AgentB < 0 && ((int)e.Value == MoveCode(cur.Move) || (cur.Move.StartsWith("Juke") && ((int)e.Value == 1 || (int)e.Value == 2)))) || (e.AgentB >= 0 && !string.IsNullOrEmpty(e.Note) && (e.Note == cur.Move || (cur.Move.StartsWith("Juke") && e.Note.StartsWith("Juke"))))); focus = e.AgentA; break;
                    case SimEventType.Catch: ok = e.AgentA >= 0 && e.AgentA < 11; focus = e.AgentA; break;
                }
                if (!ok) return;
                triggered++; triggeredAt = Time.unscaledTime;
                StartCoroutine(GalleryShots(dir, cur.Name, focus, e));
            };

            MatchFlow.SimOptionsHook = o => { if (active) { o.ForceTackle = cur.Force; o.QbHoldsBall = cur.QbHolds; o.ForceTackleAfter = cur.Pass ? 0f : (cur.Move != null ? 5f : 5f); } };
            MatchDirector.ForcedCall = fl =>
            {
                if (!active) return null;
                var st = fl.State;
                var book = fl.Book;
                Play off = null, def = null;
                var offList = book.PlaysByCategory(cur.Pass ? PlayCategory.MediumPass : (cur.Move != null ? PlayCategory.OutsideRun : PlayCategory.InsideRun));
                if (offList.Count > 0) off = offList[0];
                var defList = book.PlaysByCategory(cur.Blitz ? PlayCategory.Blitz : PlayCategory.ZoneCoverage);
                if (defList.Count > 0) def = defList[0];
                if (off == null || def == null) return null;
                return new KeyValuePair<Play, Play>(off, def);
            };
            dir.PrePlayHook = fl =>
            {
                if (!active) return;
                var s = fl.State;
                s.Phase = GamePhase.Scrimmage; s.Possession = fl.UserTeam; s.BallSpot = 28; s.Down = 1; s.Distance = 10; s.ClockSeconds = 800f; s.Quarter = 1;
            };
            Time.timeScale = 1f;
            MatchDirector.AutoMove = null;
            int caseNo = 0;
            foreach (var gc in GalleryCases())
            {
                if (only != null && !only.Contains("," + gc.Name + ",")) continue;
                caseNo++;
                // let the game reach a clean point (the kickoff and the first snaps play normally), then take over
                cur = gc; triggered = 0; active = true; MatchDirector.AutoMove = gc.Move;
                int startFinished = finished; float t0 = Time.unscaledTime; int attempts = 0;
                string lastTake = "";
                while (Time.unscaledTime - t0 < 120f)
                {
                    // one scripted play at a time: wait until it ends (or the trigger event fired and the play ended)
                    int before = finished;
                    if (gc.Ragdoll) StartCoroutine(RagdollKick(dir, gc, ev => { triggered++; StartCoroutine(GalleryShots(dir, gc.Name, ev.AgentB, ev)); }));
                    float p0 = Time.unscaledTime;
                    while (finished == before && Time.unscaledTime - p0 < 45f) { if (triggered > 0 && Time.unscaledTime - triggeredAt > 4.5f) break; yield return null; }
                    attempts++;
                    if (triggered > 0) break;
                    if (attempts >= 6) break;
                }
                yield return new WaitForSecondsRealtime(2.2f);
                string take = dir.Stage != null && dir.Stage.Contacts != null ? dir.Stage.Contacts.LastTake : "";
                Log("gallery " + gc.Name + ": triggered=" + (triggered > 0) + " plays=" + (finished - startFinished) + " take=" + take);
                seq.Add(gc.Name + (triggered > 0 ? "" : " (not triggered)"));
                active = false; MatchDirector.AutoMove = null;
                // two plain plays so the next case starts from a clean state
            }
            MatchFlow.SimOptionsHook = null; MatchDirector.ForcedCall = null; dir.PrePlayHook = null; MatchDirector.AutoMove = null;
            Log("contact gallery done: " + string.Join(", ", seq.ToArray()));
        }

        /// <summary>Gallery case "ragdoll": a defender next to the carrier takes a big hit (an impulse into the active ragdoll), then gets up.</summary>
        IEnumerator RagdollKick(MatchDirector dir, GalleryCase gc, System.Action<PlayEvent> start)
        {
            float w = 0f;
            while (w < 30f && !(dir.Phase == MatchPhase.Live && dir.Sim != null && dir.Sim.Time > 1.6f && dir.Sim.Carrier != null)) { w += Time.unscaledDeltaTime; yield return null; }
            var sim = dir.Sim; if (sim == null || sim.Carrier == null) yield break;
            var c = sim.Carrier; int best = -1; float bd = 99f;
            for (int i = 11; i < 22; i++) { var d = sim.Agents[i]; if (d.IsDown) continue; float dd = (d.Pos - c.Pos).Length; if (dd < bd) { bd = dd; best = i; } }
            if (best < 0) yield break;
            Vector3 away = dir.Stage.Views[best].transform.position - dir.Stage.Views[c.Id].transform.position; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            bool ok = dir.Stage.Contacts.Knock(best, away.normalized * 6.5f + Vector3.up * 2.4f, 1f);
            Log("  ragdoll knock on agent " + best + " accepted=" + ok);
            if (ok) start(new PlayEvent { Type = SimEventType.Tackle, AgentA = c.Id, AgentB = best });
        }

        /// <summary>Close-up screenshot sequence of one contact: the moment, then +0.25 s, +0.6 s, +1.2 s.</summary>
        Vector3 _galleryCamSide = Vector3.right;
        IEnumerator GalleryShots(MatchDirector dir, string name, int focus, PlayEvent e)
        {
            float[] at = { 0.02f, 0.28f, 0.65f, 1.25f, 2.1f };
            Vector3 off0 = Vector3.zero;
            float t0 = Time.unscaledTime;
            for (int i = 0; i < at.Length; i++)
            {
                while (Time.unscaledTime - t0 < at[i]) yield return null;
                if (dir.Stage == null || focus < 0 || dir.Stage.Views[focus] == null) yield break;
                Vector3 p = dir.Stage.Views[focus].transform.position;
                // look at the pair: halfway between the two actors when the second one is known; the camera sits to the side of the line between them
                int other = e.Type == SimEventType.CarrierMove ? NearestDefender(dir, focus) : (e.Type == SimEventType.Catch ? -1 : e.AgentA);
                Vector3 axis = Vector3.forward;
                if (other >= 0 && other < 22 && dir.Stage.Views[other] != null)
                {
                    Vector3 q = dir.Stage.Views[other].transform.position;
                    axis = q - p; axis.y = 0f; if (axis.sqrMagnitude < 0.01f) axis = Vector3.forward;
                    p = (p + q) * 0.5f;
                }
                else { var fv = dir.Stage.Views[focus].Forward; axis = fv; }
                axis.Normalize();
                if (i == 0) { Vector3 side = new Vector3(axis.z, 0f, -axis.x); if (Vector3.Dot(side, dir.Cams.Main.transform.right) < 0f) side = -side; _galleryCamSide = side; }
                Vector3 off = _galleryCamSide * 5.8f + Vector3.up * 1.7f - axis * 0.4f;
                dir.Cams.CloseUp(p, off);
                yield return new WaitForEndOfFrame();
                yield return Shot("contact_" + name + "_" + (i + 1));
                string take = dir.Stage.Contacts != null ? dir.Stage.Contacts.LastTake : "";
                if (i == 0) Log("  " + name + " event " + e.Type + " a=" + e.AgentA + " b=" + e.AgentB + " tackle=" + e.Tackle + " take=" + take);
            }
            dir.Cams.CloseUp(null, off0);
        }
    }
}
