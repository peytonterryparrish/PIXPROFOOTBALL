using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 CHOREO: the stage side of the sequence planner. The simulation books the catches (and the tackles, see ContactDirector); the stage hands the booking to the animation (the booked clip at
    /// the booked clip time), keeps the clip's speed on the booking while the forecast of the catch is refined, and for a standing catch (the take owns the body) draws the body at the take's yaw.
    /// </summary>
    public sealed partial class PlayStage
    {
        PlaySimulation _chSim;
        /// <summary>Closest the quarterback and the back are drawn at the hand-off mesh (m, root to root): a body width (hips 0.5 m) plus the arms' slack.</summary>
        public static float MeshMinGap = 0.66f;
        int _hoPickPlay = -1; string _hoPick;
        readonly System.Collections.Generic.HashSet<int> _chWalked = new System.Collections.Generic.HashSet<int>();
        readonly bool[] _chDriven = new bool[22];

        float UnityYaw(float simYaw) { return simYaw + (Frame.TowardsPlusZ ? 0f : 180f); }

        void ChoreoHints(ref AnimHints h, SimAgent a, AgentAction act)
        {
            if (Sim.Choreo == null || SimAgentView.DebugNoChoreo) return;
            if (act == AgentAction.Handoff && Sim.QbHandoffTargetId > 0 && !SimAgentView.DebugNoChoreo)
            {
                if (_hoPickPlay != _playIndex) { _hoPickPlay = _playIndex; _hoPick = AnimKit.PickHandoffPair(Sim.Agents[0].Speed * PlayFrame.YardsToMeters, Sim.Agents[Sim.QbHandoffTargetId].Speed * PlayFrame.YardsToMeters, _playIndex * 7 + 1); }
                h.HandoffPick = _hoPick;
                return;
            }
            if (act != AgentAction.Catch && act != AgentAction.Jump) return;
            var bk = Sim.Choreo.CatchBookingOf(a.Id);
            if (bk == null || bk.Phase != ChoreoPhase.Playing) return;
            h.Booked = bk;
            h.BookedClipTime = bk.ClipTime(Sim.Choreo.Clock + Sim.StepAlpha * PlaySimulation.Dt);
        }

        void ServiceChoreo(float dt)
        {
            if (Sim == null) return;
            if (_chSim != Sim) { _chSim = Sim; System.Array.Clear(_chDriven, 0, 22); _chWalked.Clear(); }
            ServicePadHands();      // v0.12.0 CONTACT: hands on the pads of the man across (Final IK, PlayStage.Hands.cs)
            var ch = Sim.Choreo;
            if (ch == null || SimAgentView.DebugNoChoreo) return;
            ServiceWarp(ch);      // the lead-in of a booked take plays at the bodies' speed
            ServiceCelebrations(ch);      // a sack: the sacker celebrates before he walks off
            WalkBackReleased(ch);
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null || v.Rig == null || !v.Rig.Valid) continue;
                var bk = ch.CatchBookingOf(i);
                if (bk == null || bk.ClipA == null)
                {
                    if (_chDriven[i]) { _chDriven[i] = false; v.ContactDriven = false; }
                    continue;
                }
                // the booked clip runs at the booked speed (the forecast of the catch is refined every tick; the speed keeps the hand-close frame on the ball)
                if (v.Rig.CurrentClip != null && v.Rig.CurrentClip.name == bk.ClipA.Name) v.Rig.SetSpeed(bk.Rate);
                if (bk.OwnsA && bk.Phase == ChoreoPhase.Playing && ch.Clock >= bk.StartSim - 0.02f && !v.ContactDriven && !_chDriven[i])
                {
                    // a standing catch: the take owns the body (the simulation moves him along its root path); the view draws him at the take's yaw
                    var h = new AnimHints { Booked = bk, BookedClipTime = bk.ClipTime(ch.Clock + Sim.StepAlpha * PlaySimulation.Dt), Defender = bk.Defender };
                    var plan = AnimKit.CatchBooked(h);
                    if (plan.Valid) { v.ContactDriven = true; _chDriven[i] = true; v.Rig.Play(plan); bk.ViewStarted = true; }
                }
                if (_chDriven[i])
                {
                    if (!bk.OwnsA) { _chDriven[i] = false; v.ContactDriven = false; }
                    else v.SetContactPose(v.transform.position, UnityYaw(bk.Align.Yaw));
                }
            }
        }

        /// <summary>
        /// The lead-in warp of a booked pair take (<see cref="ChoreoBooking.RateIn"/>): until the contact both rigs are scrubbed to the booking's clip time (the clip plays faster or slower than its own rate so its travel matches
        /// the bodies'); at the contact the scrub is released and the clip plays on at its rate from where it is.
        /// </summary>
        void ServiceWarp(Choreographer ch)
        {
            if (Choreographer.DebugNoLeadWarp) return;
            var books = ch.Bookings; float now = ch.Clock + Sim.StepAlpha * PlaySimulation.Dt;
            for (int bi = 0; bi < books.Count; bi++)
            {
                var b = books[bi];
                if (b.RateIn <= 0f || b.WarpDone || !b.ViewStarted || b.Kind != ChoreoKind.Tackle) continue;
                bool pre = now < b.SyncSim;
                float ct = b.ClipTime(now);
                for (int k = 0; k < 2; k++)
                {
                    int id = k == 0 ? b.A : b.B; bool owns = k == 0 ? b.OwnsA : b.OwnsB;
                    if (id < 0 || id >= 22 || _views[id] == null || _views[id].Rig == null || !_views[id].Rig.Valid) continue;
                    if (pre && owns) _views[id].Rig.Scrub(ct); else _views[id].Rig.SetScrub(false);
                }
                if (!pre) b.WarpDone = true;
            }
        }

        public static bool NoCelebrate = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoChoreoCelebrate") >= 0;
        readonly bool[] _chCel = new bool[22]; readonly float[] _chCelEnd = new float[22]; readonly System.Collections.Generic.HashSet<int> _chCelDone = new System.Collections.Generic.HashSet<int>();
        int _chCelPlay = -1;
        /// <summary>QA: sack celebrations started.</summary>
        public static int CelebrationsStarted;

        /// <summary>
        /// The dead-ball celebration of a sack (the touchdown set is the simulation's own): when the sacker's take has handed him back he plays a cheer from the reaction pool (AnimKit.Reaction) for up to 2.6 s, holding
        /// his place at the pile, and then walks off with the others.
        /// </summary>
        void ServiceCelebrations(Choreographer ch)
        {
            if (NoCelebrate || SimAgentView.DebugNoChoreo) return;
            if (_chCelPlay != _playIndex) { _chCelPlay = _playIndex; System.Array.Clear(_chCel, 0, 22); _chCelDone.Clear(); }
            for (int i = 0; i < 22; i++)
            {
                if (!_chCel[i]) continue;
                var v = _views[i];
                if (v == null || !v.Rig.Valid || Time.time > _chCelEnd[i] || Sim.Phase != PlayPhase.Dead) { _chCel[i] = false; if (v != null) { v.ContactDriven = false; if (v.Rig != null) v.Rig.Stop(0.3f); } _chWalked.Remove(i); }
            }
            if (Sim.Phase != PlayPhase.Dead || Sim.Kind != PlayKind.Scrimmage) return;
            var er = Sim.EndReason;
            if (er != EndReason.Sack && er != EndReason.Tackle) return;
            var books = ch.Bookings;
            for (int bi = 0; bi < books.Count; bi++)
            {
                var b = books[bi];
                if (b.Kind != ChoreoKind.Tackle || b.Joiner) continue;
                int who = -1;
                if (er == EndReason.Sack && b.PairKind == PairKind.Sack && b.B >= 0 && !b.OwnsB) who = b.B;                                   // the sacker
                else if (er == EndReason.Tackle && b.PairKind != PairKind.Sack && b.A >= 0 && !b.OwnsA && Sim.Result != null)
                {
                    // an interception: the interceptor (he is the carrier who was brought down); a big gain: the runner or the receiver who got up after 25 yards or more
                    var ag = Sim.Agents[b.A];
                    bool inter = Sim.Result.Type == PlayType.Interception && ag.Side == SimSide.Defense;
                    bool big = ag.Side == SimSide.Offense && !Sim.Result.Turnover && Sim.Result.Yards >= 25;
                    if (inter || big) who = b.A;
                }
                if (who < 0 || _chCelDone.Contains(who)) continue;
                var v = _views[who]; if (v == null || v.Rig == null || !v.Rig.Valid || v.ContactDriven) continue;
                _chCelDone.Add(who);
                var plan = AnimKit.Reaction("cheer", who * 7 + _playIndex, false);
                if (!plan.Valid) continue;
                plan.End = Mathf.Min(plan.Clip.length, plan.Start + 2.6f); plan.Hold = false; plan.FadeOut = 0.35f;
                _chWalked.Add(who);
                v.ContactDriven = true; v.Rig.Play(plan);
                _chCel[who] = true; _chCelEnd[who] = Time.time + (plan.End - plan.Start) + 0.1f; CelebrationsStarted++;
            }
        }

        /// <summary>After the whistle the players a take hands back (the tackler and the carrier who got up, the late tacklers) walk away from the pile: the offense back toward its huddle, the defense to its next alignment.</summary>
        void WalkBackReleased(Choreographer ch)
        {
            if (Sim.Phase != PlayPhase.Dead || Sim.Kind != PlayKind.Scrimmage || !GlideEnabled) return;
            var er = Sim.EndReason;
            if (er != EndReason.Tackle && er != EndReason.Sack && er != EndReason.Safety && er != EndReason.OutOfBounds) return;
            Vec2 spot = Sim.Carrier != null ? Sim.Carrier.Pos : Sim.Ball.Pos.XY;
            var books = ch.Bookings;
            for (int bi = 0; bi < books.Count; bi++)
            {
                var b = books[bi];
                if (b.Kind != ChoreoKind.Tackle) continue;
                if (b.A >= 0 && !b.OwnsA && b.ClipA != null && !b.Joiner) SendWalking(b.A, spot);
                if (b.B >= 0 && !b.OwnsB) SendWalking(b.B, spot);
            }
        }

        void SendWalking(int id, Vec2 spot)
        {
            if (id < 0 || id >= 22 || _chWalked.Contains(id) || _views[id] == null) return;
            _chWalked.Add(id);
            var a = Sim.Agents[id];
            if (_walks[id].Active) return;
            float side = ((id * 5) % 7 - 3) * 0.8f;
            Vec2 target = a.Side == SimSide.Offense ? new Vec2(spot.X + side, spot.Y - 4.5f - (id % 3) * 0.7f) : new Vec2(spot.X + side, spot.Y + 2.6f + (id % 3) * 0.6f);
            WalkTo(id, Frame.ToUnity(target), 1.9f, Frame.ToUnity(spot));
        }
    }
}
