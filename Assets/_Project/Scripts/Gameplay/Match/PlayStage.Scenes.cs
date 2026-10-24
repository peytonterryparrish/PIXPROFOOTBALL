using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: dead-ball reactions chosen by the situation (all deterministic, from the play number): the first-down point of the runner or receiver, the receiver's frustration on an incompletion,
    /// the quarterback's after a sack, the defense's cheer for a stop on third or fourth down. Reference rates (NFL broadcast): about half of the first downs get a point from the man who made them,
    /// a third of the incompletions a visible reaction from the target, a sack a reaction from the passer about half the time, a fourth-down or third-down stop a cheer from the tacklers most of the time.
    /// </summary>
    public sealed partial class PlayStage
    {
        public enum SceneKind { Point, Frustration, Cheer }
        struct Scene { public int Agent; public float At; public SceneKind Kind; public int Seed; public bool Started, Done; public float Until; }
        readonly List<Scene> _scenes = new List<Scene>();
        /// <summary>QA: scenes started per kind.</summary>
        public static int PointsStarted, FrustrationsStarted, CheersStarted;
        public static bool ScenesEnabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoFlowScenes") < 0;

        /// <summary>Called by the director once the play's outcome is known (the whistle just blew).</summary>
        public void AftermathScenes(PlayOutcome o, int downNow, Team possessionNow)
        {
            _scenes.Clear();
            if (!ScenesEnabled || !ContinuityEnabled || Sim == null || o == null || Sim.Kind != PlayKind.Scrimmage || o.Result == null) return;
            uint h = Fnv(_playIndex * 3 + 1, 9001);
            int carrier = Sim.Carrier != null ? Sim.Carrier.Id : -1;
            if (o.FirstDownGained && !o.Result.Touchdown && carrier >= 0 && Sim.Agents[carrier].Side == SimSide.Offense && h % 100 < 55)
                _scenes.Add(new Scene { Agent = carrier, At = 1.6f + (h % 5) * 0.12f, Kind = SceneKind.Point, Seed = (int)h });
            if (o.Headline == Headline.Incomplete)
            {
                int tgt = Sim.Ball.Intended;
                if (tgt > 0 && tgt < 11 && (h >> 3) % 100 < 36) _scenes.Add(new Scene { Agent = tgt, At = 0.5f, Kind = SceneKind.Frustration, Seed = (int)h });
            }
            if (o.Headline == Headline.Sack && (h >> 4) % 100 < 50)
                _scenes.Add(new Scene { Agent = 0, At = 1.9f, Kind = SceneKind.Frustration, Seed = (int)h });
            int tkId = Sim.EndInfo != null && Sim.EndInfo.Kind == PlayEndKind.Tackle ? Sim.EndInfo.Primary : -1;
            bool stop = (downNow == 4 && possessionNow == o.Offense && !o.FirstDownGained && o.Headline != Headline.Touchdown) || o.Headline == Headline.TurnoverOnDowns;
            if (stop && tkId >= 11 && tkId < 22 && (h >> 6) % 100 < 70)
            {
                _scenes.Add(new Scene { Agent = tkId, At = 1.5f, Kind = SceneKind.Cheer, Seed = (int)h });
                int mate = -1; float bd = 99f; Vec2 sp = Sim.Agents[tkId].Pos;
                for (int i = 11; i < 22; i++) { if (i == tkId) continue; float d = (Sim.Agents[i].Pos - sp).Length; if (d < bd) { bd = d; mate = i; } }
                if (mate >= 0) _scenes.Add(new Scene { Agent = mate, At = 1.9f, Kind = SceneKind.Cheer, Seed = (int)h + 5 });
            }
        }

        bool OnTheGround(SimAgentView v, SimAgent a)
        {
            var an = v.Avatar != null ? v.Avatar.Animator : null;
            var hb = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hb != null) return hb.position.y - v.transform.position.y < 0.7f;
            return a.Action == AgentAction.Tackled || a.Action == AgentAction.Fallen || a.Action == AgentAction.GetUp;
        }

        void ServiceScenes()
        {
            if (_scenes.Count == 0 || Sim == null) return;
            float t = Time.time - _afAt;
            for (int k = 0; k < _scenes.Count; k++)
            {
                var s = _scenes[k]; if (s.Done) continue;
                var v = _views[s.Agent]; if (v == null || v.Rig == null || !v.Rig.Valid) { s.Done = true; _scenes[k] = s; continue; }
                var a = Sim.Agents[s.Agent];
                if (s.Started)
                {
                    if (Time.time >= s.Until || Sim.Phase != PlayPhase.Dead)
                    {
                        s.Done = true; _scenes[k] = s;
                        if (s.Kind != SceneKind.Point) { v.ContactDriven = false; v.Rig.Stop(0.3f); _afSent[s.Agent] = false; _chWalked.Remove(s.Agent); }
                    }
                    continue;
                }
                if (Sim.Phase != PlayPhase.Dead) { s.Done = true; _scenes[k] = s; continue; }
                if (t < s.At) continue;
                if (v.ContactDriven || OnTheGround(v, a) || IsTackleAction(a.Action) || a.Action == AgentAction.Celebrate) { if (t > s.At + 4f) { s.Done = true; _scenes[k] = s; } continue; }
                if (s.Kind == SceneKind.Point)
                {
                    var plan = AnimKit.FirstDownPoint(s.Seed);
                    if (!plan.Valid) { s.Done = true; _scenes[k] = s; continue; }
                    v.NoteClip(plan.Clip); v.Rig.Play(plan);
                    s.Started = true; s.Until = Time.time + (plan.End - plan.Start) + 0.2f; PointsStarted++;
                }
                else
                {
                    var plan = AnimKit.Reaction(s.Kind == SceneKind.Cheer ? "cheer" : "frustration", s.Seed, false);
                    if (!plan.Valid) { s.Done = true; _scenes[k] = s; continue; }
                    plan.End = Mathf.Min(plan.Clip.length, plan.Start + 2.2f); plan.Hold = false; plan.FadeOut = 0.35f;
                    _afSent[s.Agent] = true; _chWalked.Add(s.Agent); _walks[s.Agent].Active = false;
                    v.ContactDriven = true; v.NoteClip(plan.Clip); v.Rig.Play(plan);
                    s.Started = true; s.Until = Time.time + (plan.End - plan.Start) + 0.1f;
                    if (s.Kind == SceneKind.Cheer) CheersStarted++; else FrustrationsStarted++;
                }
                _scenes[k] = s;
            }
        }
    }
}
