using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.4 "Contact": physics-blended tackles. Turns the simulation's tackle / sack / broken tackle / truck / pancake events into authored two-actor takes aligned to the simulated
    /// bodies (see <see cref="ContactSession"/>), drives both views through the take with the animation root motion warped on and off the simulation, attaches gang tacklers and drags,
    /// hands big hits to the active ragdoll and picks the get-up take (face up / face down) when a ragdoll settles. Falls back to the v0.3 in-place clips when the contact table is absent.
    /// v0.9.0 CONNECT: momentum-preserving takes, takes at the pace of the game, the carrier released when HIS take ends, a broken take ended by the next tackler, struggling drags instead of
    /// frozen statues, gang tacklers matched in time and place, impacts at the contact moment, contested catches and jams as short pairs, pancake finish, stagger before the ragdoll, QB hit
    /// flinch, fumble recovery, classified get-ups and a catch-up walk instead of a glide.
    /// </summary>
    public sealed partial class ContactDirector
    {
        readonly PlayStage _stage;
        readonly List<ContactSession> _sessions = new List<ContactSession>();
        readonly List<Joiner> _joiners = new List<Joiner>();
        readonly Dictionary<int, SoloTake> _solo = new Dictionary<int, SoloTake>();
        readonly List<OneShot> _oneShots = new List<OneShot>();
        readonly List<PendingKnock> _knocks = new List<PendingKnock>();
        readonly List<PendingImpact> _impacts = new List<PendingImpact>();
        readonly List<string> _recent = new List<string>();
        readonly List<ContactTable.Pair> _scratch = new List<ContactTable.Pair>();
        ContactTable _table;
        readonly List<GetUp> _getUps = new List<GetUp>();
        int _seed;

        public bool Enabled { get { return _table != null && _table.pairs.Count > 0; } }
        public int ActiveSessions { get { return _sessions.Count; } }
        /// <summary>A take, a joiner, a get-up, a one-shot or a ragdoll is still running (the post-play window waits for it: E-216).</summary>
        public bool Busy
        {
            get
            {
                if (_sessions.Count > 0 || _solo.Count > 0 || _joiners.Count > 0 || _oneShots.Count > 0 || _knocks.Count > 0 || _bookedSolo.Count > 0) return true;
                for (int i = 0; i < 22; i++) { var v = View(i); if (v != null && v.Ragdoll != null && v.Ragdoll.Active) return true; }
                return false;
            }
        }
        /// <summary>Raised when a big hit lands (carrier, tackler, impact 0..1): camera shake, rumble and hit sounds hook in here.</summary>
        public System.Action<ContactImpact> Impact;
        /// <summary>A player was handed to the active ragdoll (agent, intensity): the look layer dirties his kit.</summary>
        public System.Action<int, float> Knocked;
        void FireNow(Vector3 pos, float k, bool big, int a, int b, int hitter, int victim, bool sack = false)
        {
            if (Impact != null) Impact(new ContactImpact { Position = pos, Intensity = k, Big = big, AgentA = a, AgentB = b, Sack = sack, Hitter = hitter, Victim = victim, HasRoles = true });
        }
        /// <summary>Fires the impact now, or at the contact moment of the session that carries it (E-378).</summary>
        void Fire(Vector3 pos, float k, bool big, int a, int b, bool sack = false, ContactSession at = null)
        {
            // a = carrier / blocker / receiver, b = tackler / loser / defender
            if (at != null && !at.PassedContact) { _impacts.Add(new PendingImpact { S = at, Pos = pos, K = k, Big = big, A = a, B = b, Sack = sack }); return; }
            FireNow(pos, k, big, a, b, b, a, sack);
        }
        public static bool RagdollEnabled = true;
        static float Rate { get { return GamePace.AnimationRate; } }

        /// <summary>Last chosen take, for the autotest log.</summary>
        public string LastTake = "";

        struct GetUp { public ContactTable.Pair Pair; public bool ActorA; public float FaceUp; public float Start; }
        sealed class PendingImpact { public ContactSession S; public Vector3 Pos; public float K; public bool Big, Sack; public int A, B; }
        sealed class PendingKnock { public int Agent; public Vector3 Impulse; public float Intensity, At; }
        sealed class OneShot { public int Agent; public float End, T; }

        sealed class Joiner
        {
            public int Agent, Carrier;
            public ContactTable.Pair Pair;
            public Rigid2 Align;
            public float Start, Time;
            public bool Held; public float HoldAt;
            public float StartYaw;
            public bool Playing; public float YawOffset;
            public AnimationClip Clip;
        }

        /// <summary>A single actor playing a take on his own: the get-up after a ragdoll.</summary>
        sealed class SoloTake
        {
            public int Agent;
            public ContactTable.Pair Pair; public bool ActorA;
            public Rigid2 Align;
            public float Start, Time, End;
            public float Authority;
        }

        public ContactDirector(PlayStage stage)
        {
            _stage = stage;
            _table = ContactTable.Load();
            if (_table != null)
            {
                foreach (var p in _table.pairs)
                {
                    if (p == null) continue;
                    // takes where somebody lies down and gets up again are get-up material
                    if (p.getUpA > 0.3f && Mathf.Abs(p.faceUpA) > 0.3f) _getUps.Add(new GetUp { Pair = p, ActorA = true, FaceUp = p.faceUpA, Start = Mathf.Max(0f, p.getUpA - 0.45f) });
                    if (p.getUpB > 0.3f && Mathf.Abs(p.faceUpB) > 0.3f) _getUps.Add(new GetUp { Pair = p, ActorA = false, FaceUp = p.faceUpB, Start = Mathf.Max(0f, p.getUpB - 0.45f) });
                }
            }
        }

        public void Reset()
        {
            foreach (var s in _sessions) Release(s.Carrier, s.Tackler);
            foreach (var j in _joiners) Release(j.Agent, -1);
            foreach (var kv in _solo) Release(kv.Key, -1);
            foreach (var o in _oneShots) Release(o.Agent, -1);
            ResetBooked();      // v0.10.0 CHOREO
            _sessions.Clear(); _joiners.Clear(); _solo.Clear(); _stiff.Clear(); _oneShots.Clear(); _knocks.Clear(); _impacts.Clear();
        }

        void Release(int a, int b)
        {
            var va = View(a); if (va != null) { va.ContactDriven = false; if (va.Rig != null) va.Rig.SetScrub(false); }
            var vb = View(b); if (vb != null) { vb.ContactDriven = false; if (vb.Rig != null) vb.Rig.SetScrub(false); }
        }

        SimAgentView View(int id) { return id >= 0 && id < 22 ? _stage.Views[id] : null; }
        ContactSession SessionOf(int carrier) { foreach (var s in _sessions) if (s.Carrier == carrier) return s; return null; }
        bool InSession(int agent)
        {
            foreach (var s in _sessions) if ((s.Carrier == agent && !s.ReleasedA) || (s.Tackler == agent && !s.ReleasedB)) return true;
            foreach (var j in _joiners) if (j.Agent == agent) return true;
            foreach (var o in _oneShots) if (o.Agent == agent) return true;
            return _solo.ContainsKey(agent);
        }

        static float RadiusOf(SimAgentView v) { return v == null ? 0.29f : (v.Size == PlayerSize.Large ? 0.33f : (v.Size == PlayerSize.Small ? 0.26f : 0.29f)); }

        // ============================================================================================================ events
        /// <summary>Feeds one simulation event; returns true when the director took over the animation of the agents involved.</summary>
        public void OnEvent(in PlayEvent e, bool sackFollows)
        {
            if (!Enabled || _stage.Sim == null) return;
            switch (e.Type)
            {
                case SimEventType.Tackle:
                    if (e.AgentA >= 0 && e.AgentB >= 0) OnTackle(e, sackFollows);
                    break;
                case SimEventType.BrokenTackle:
                    if (e.AgentA >= 0 && e.AgentB >= 0) OnBroken(e);
                    break;
                case SimEventType.Pancake:
                    if (e.AgentA >= 0 && e.AgentB >= 0) OnPancake(e);
                    break;
                case SimEventType.CarrierMove:
                    if (e.AgentA >= 0 && e.AgentB >= 0 && e.Value > 0.5f && e.Note == "Truck") OnTruck(e);
                    else if (e.AgentA >= 0 && e.AgentB >= 0 && e.Note == "StiffArm") OnStiffArm(e);
                    break;
                case SimEventType.Jam:
                    if (e.AgentA >= 0 && e.AgentB >= 0 && e.AgentA < 22 && e.AgentB < 22) OnJam(e);
                    break;
                case SimEventType.PileOn:
                    if (e.AgentA >= 0 && e.AgentB >= 0 && e.AgentA < 22 && e.AgentB < 22) OnPileOnBooked(e);      // v0.10.0 CHOREO
                    break;
                case SimEventType.QbHit:
                    if (e.AgentB >= 0 && e.AgentB < 22) OnQbHit(e);
                    break;
                case SimEventType.Recovery:
                    if (e.AgentA >= 0 && e.AgentA < 22) OnRecovery(e);
                    break;
                case SimEventType.Deflection:
                case SimEventType.Interception:
                    // E-130: the receiver who lost the ball reacts (a flinch for a tip, the frustration take for a pick); the defender's own reach was started ahead by the stage
                    if (e.AgentB >= 0 && e.AgentB < 22 && !InSession(e.AgentB)) OnLostBall(e);
                    break;
            }
        }

        Vector3 WorldPos(int id) { var v = View(id); return v != null ? v.transform.position : Vector3.zero; }

        Vector2 Ground(Vector3 p) { return new Vector2(p.x, p.z); }

        /// <summary>Carrier's heading used to measure the contact angle: where he is going, or where he faces when standing.</summary>
        float CarrierYaw(int id)
        {
            var a = _stage.Sim.Agents[id]; var v = View(id);
            Vector3 vel = _stage.Frame.VelToUnity(a.Vel);
            if (vel.magnitude > 1.6f) return Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            return v != null ? v.Yaw : 0f;
        }

        void OnTackle(in PlayEvent e, bool sackFollows)
        {
            int tackler = e.AgentA, carrier = e.AgentB;
            if (tackler >= 22 || carrier >= 22) return;
            var vc = View(carrier); var vt = View(tackler);
            if (vc == null || vt == null) return;
            var existing = SessionOf(carrier);
            bool drag = e.Tackle == TackleType.Drag;
            if (!drag && existing != null && HasUnstartedBooking(carrier, tackler)) { EndSession(existing, false); existing = null; }      // v0.10.0 CHOREO: the booked take replaces the drag struggle (the simulation moves both bodies along it now)
            if (existing != null && existing.CarrierRunsOn && !existing.ReleasedA)
            {
                // E-206: a second tackler on a carrier who is still in a broken take brings him down: the broken take ends (blended into the wrap) and a real tackle starts
                EndSession(existing, false);
                existing = null;
            }
            if (existing != null)
            {
                if (existing.Tackler == tackler || !drag)
                {
                    // the drag ends: the same take plays on from where it was held (or a new gang member goes in)
                    if (existing.Held) ReleaseHold(existing);
                    if (existing.Tackler != tackler && !InSession(tackler)) AddJoiner(tackler, carrier, existing, false);
                    if (e.Tackle == TackleType.BigHit) FireNow(vc.transform.position, ImpactOf(e), true, carrier, tackler, tackler, carrier);
                    return;
                }
                if (!InSession(tackler)) AddJoiner(tackler, carrier, existing, true);
                return;
            }
            if (InSession(tackler) || InSession(carrier)) return;
            ContactKind kind = TackleVariants.KindFor(e.Tackle, sackFollows);
            // shoestrings that do not bring the carrier down: the tackler goes for his legs and the carrier stumbles on
            if (e.Tackle == TackleType.Shoestring) kind = ContactKind.Broken;
            if (!drag && TryStartBooked(tackler, carrier, e)) return;      // v0.10.0 CHOREO: the simulation booked this take and moves both bodies along it
            StartSession(kind, tackler, carrier, e, drag);
        }

        void ReleaseHold(ContactSession s)
        {
            if (!s.Held) return;
            var vc = View(s.Carrier); var vt = View(s.Tackler);
            s.Hold(false); s.HoldAt = 0f;
            // the take plays on from where the struggle left it
            if (vc != null && vc.Rig != null) { vc.Rig.Scrub(s.Time); vc.Rig.SetScrub(false); }
            if (vt != null && vt.Rig != null) { vt.Rig.Scrub(s.Time); vt.Rig.SetScrub(false); }
        }

        void OnBroken(in PlayEvent e)
        {
            int tackler = e.AgentA, carrier = e.AgentB;
            if (tackler >= 22 || carrier >= 22) return;
            var existing = SessionOf(carrier);
            if (existing != null && HasUnstartedBooking(carrier, tackler)) { EndSession(existing, false); existing = null; }      // v0.10.0 CHOREO
            if (existing != null && existing.Held) EndSession(existing, false);   // a dragger the carrier shook off: the take blends straight into the broken take (E-241)
            if (InSession(tackler) || InSession(carrier)) return;
            var sim = _stage.Sim;
            bool qb = carrier == 0 && sim.Kind == PlayKind.Scrimmage && !sim.PassIsThrown;
            if (TryStartBooked(tackler, carrier, e)) return;      // v0.10.0 CHOREO
            StartSession(qb ? ContactKind.SackBroken : ContactKind.Broken, tackler, carrier, e, false);
        }

        void OnTruck(in PlayEvent e)
        {
            int carrier = e.AgentA, defender = e.AgentB;
            if (defender >= 22 || carrier >= 22) return;
            // E-220: the Truck event plays a Truck pair (the carrier runs through the defender); the ragdoll is the fallback when no pair fits
            if (!InSession(carrier) && !InSession(defender) && StartSession(ContactKind.Truck, defender, carrier, e, false)) return;
            Vector3 dir = WorldPos(defender) - WorldPos(carrier); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Knock(defender, dir.normalized * 4.2f + Vector3.up * 1.2f, 0.75f);
            FireNow(WorldPos(defender), 0.85f, true, carrier, defender, carrier, defender);
        }

        readonly Dictionary<int, int> _stiff = new Dictionary<int, int>();

        /// <summary>The carrier's stiff arm reaches for the defender's chest (IK over the stiff-arm clip). E-147: a won stiff arm only stumbles the defender in the simulation, so he staggers (the Stumble take), he is not ragdolled to the ground.</summary>
        void OnStiffArm(in PlayEvent e)
        {
            int carrier = e.AgentA, defender = e.AgentB;
            if (carrier >= 22 || defender >= 22) return;
            var vc = View(carrier); var vd = View(defender);
            if (vc == null || vd == null || vc.Motion == null) return;
            vc.Motion.StiffArmAt(ChestPoint(vd), 0.55f);
            _stiff[carrier] = defender;
            if (e.Value > 0.5f) FireNow(vd.transform.position, 0.3f, false, carrier, defender, carrier, defender);
        }

        static Vector3 ChestPoint(SimAgentView v)
        {
            var c = v.Avatar != null ? v.Avatar.Chest : null;
            return c != null ? c.position : v.transform.position + Vector3.up * 1.3f;
        }

        /// <summary>
        /// E-122 / E-123 / E-129: a catch in traffic as a short pair. Asked by the stage when the intended receiver is about to catch: only when a defender is within a yard, the receiver is nearly stationary and
        /// the ball is within reach height; the pair starts so its contact frame IS the catch frame, and the receiver is let go 0.35 s after it (he runs on); only the defender stays in the take.
        /// Returns true when the director took the receiver over (the stage then does not play the plain catch).
        /// </summary>
        /// <summary>True when this receiver's catch is a duel in traffic (defender within a yard, receiver nearly stationary, ball low): the stage then waits for the pair instead of playing the plain catch.</summary>
        public bool IsContestedCandidate(int receiver)
        {
            { var ch0 = _stage.Sim != null ? _stage.Sim.Choreo : null; if (ch0 != null && !SimAgentView.DebugNoChoreo && !Choreographer.DebugNoDuel) return ch0.DuelOutcomeOf(receiver) == 1; }      // v0.10.0 CHOREO: only a duel the receiver wins is the pair
            int d; return Candidate(receiver, out d);
        }

        bool Candidate(int receiver, out int best)
        {
            best = -1;
            if (!Enabled || _stage.Sim == null) return false;
            var sim = _stage.Sim;
            if (sim.Kind != PlayKind.Scrimmage || receiver < 1 || receiver >= 11) return false;
            var r = sim.Agents[receiver];
            if (r.Speed * PlayFrame.YardsToMeters > 2.0f || sim.Ball.Pos.Z > 3.0f) return false;
            float bd = 1.0f;
            for (int i = 11; i < 22; i++)
            {
                var d = sim.Agents[i];
                if (d.IsDown || d.State == AgentState.Engaged) continue;
                float dist = (d.Pos - r.Pos).Length;
                if (dist < bd) { bd = dist; best = i; }
            }
            return best >= 0;
        }

        public bool WantsContestedCatch(int receiver, float eta)
        {
            if (!Enabled || _stage.Sim == null) return false;
            var sim = _stage.Sim;
            if (InSession(receiver)) return true;
            if (sim.Choreo != null && !SimAgentView.DebugNoChoreo && !Choreographer.DebugNoDuel)
            {
                // v0.10.0 CHOREO: the simulation rolled the duel and booked the pair: play it (its contact frame is the arrival); until it is booked the receiver waits
                var bk = sim.Choreo.DuelBookingOf(receiver);
                if (bk == null || bk.Phase != ChoreoPhase.Playing) return false;
                if (bk.ViewStarted) return true;
                return TryStartBooked(bk.B, receiver, new PlayEvent { Type = SimEventType.Catch, AgentA = receiver, AgentB = bk.B, Value = 0f });
            }
            var r = sim.Agents[receiver];
            int best;
            if (!Candidate(receiver, out best)) return false;
            if (InSession(best)) return false;
            // start so the pair's contact lands on the catch
            var vc = View(receiver); var vt = View(best);
            if (vc == null || vt == null) return false;
            var sit = new ContactSituation { Kind = ContactKind.Contested, Bearing = ContactMath.BearingDeg(Ground(vt.transform.position), Ground(vc.transform.position), vc.Yaw), CarrierSpeed = r.Speed * PlayFrame.YardsToMeters, Closing = 0f, Seed = ++_seed * 5 + receiver };
            var pair = TackleVariants.Choose(_table.pairs, sit);
            if (pair == null) return false;
            if (eta > Mathf.Min(0.36f, pair.tContact) + 0.04f) return false;     // not yet: the stage asks again every frame
            var ev = new PlayEvent { Type = SimEventType.Catch, AgentA = receiver, AgentB = best, Value = 0f };
            var s = StartSessionWith(pair, ContactKind.Contested, best, receiver, ev, false, sit);
            if (s == null) return false;
            s.ReleaseAAfter = 0.35f; s.ReleaseBAfter = 0f;
            Fire(WorldPos(receiver), 0.35f, false, receiver, best, false, s);
            return true;
        }

        /// <summary>E-188: the press jam as a short collision pair at the line: the receiver is let go at 0.4 s after contact and runs his release, the corner stays in the take a little longer.</summary>
        void OnJam(in PlayEvent e)
        {
            int cb = e.AgentA, wr = e.AgentB;
            if (InSession(cb) || InSession(wr)) return;
            var s = StartSession(ContactKind.Collision, cb, wr, e, false, true);
        }

        void OnQbHit(in PlayEvent e)
        {
            int qb = e.AgentB, hitter = e.AgentA;
            var vq = View(qb);
            if (vq == null || InSession(qb) || vq.Rig == null || !vq.Rig.Valid) return;
            // E-232: the quarterback flinches after the throw (upper body: the legs keep their follow-through)
            var plan = AnimKit.Stumble(_seed++, 0f); plan.UpperBody = true;
            if (plan.Valid) PlayOneShot(qb, plan, 0.6f);
            FireNow(vq.transform.position, 0.4f, false, qb, hitter, hitter, qb);
        }

        void OnLostBall(in PlayEvent e)
        {
            var v = View(e.AgentB); if (v == null || v.Rig == null || !v.Rig.Valid) return;
            ClipPlan plan;
            if (e.Type == SimEventType.Interception) plan = AnimKit.Reaction("frustration", e.AgentB + _seed, false);
            else { plan = AnimKit.Stumble(e.AgentB + _seed, 0f); plan.UpperBody = true; }
            if (plan.Valid) PlayOneShot(e.AgentB, plan, e.Type == SimEventType.Interception ? 1.8f : 0.5f);
        }

        void OnRecovery(in PlayEvent e)
        {
            int a = e.AgentA;
            var v = View(a);
            if (v == null || InSession(a) || v.Rig == null || !v.Rig.Valid) return;
            // E-233: the player who falls on the ball dives for it
            var lib = ArtLibrary.Load(); if (lib == null) return;
            var c = lib.ClipVariantCount(ArtKeys.FumbleDive) > 0 ? lib.ClipVariant(ArtKeys.FumbleDive, 0) : null;
            if (c == null) return;
            var plan = new ClipPlan { Clip = c, Start = 0f, End = Mathf.Min(c.length, 2.4f), Speed = Rate, FadeIn = 0.12f, FadeOut = 0.4f };
            PlayOneShot(a, plan, 2.0f);
        }

        /// <summary>Plays a clip over one player for a while (the director keeps the match animation logic off him meanwhile).</summary>
        public void PlayOnce(int agent, ClipPlan plan, float seconds) { PlayOneShot(agent, plan, seconds); }

        void PlayOneShot(int agent, ClipPlan plan, float seconds)
        {
            var v = View(agent); if (v == null || v.Rig == null || !v.Rig.Valid) return;
            for (int i = 0; i < _oneShots.Count; i++) if (_oneShots[i].Agent == agent) { _oneShots.RemoveAt(i); break; }
            v.ContactDriven = true;
            v.Rig.Play(plan);
            _oneShots.Add(new OneShot { Agent = agent, End = seconds, T = 0f });
        }

        void OnPancake(in PlayEvent e)
        {
            int blocker = e.AgentA, loser = e.AgentB;
            if (loser >= 22 || blocker >= 22) return;
            Vector3 dir = WorldPos(loser) - WorldPos(blocker); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            // E-279: the loser staggers back for a beat before the ragdoll takes him
            _knocks.Add(new PendingKnock { Agent = loser, Impulse = dir.normalized * 3.2f + Vector3.up * 0.8f, Intensity = 0.55f, At = 0.12f });
            // E-172: the blocker follows through (the drive finish: arms through, steps over) instead of playing nothing
            var vb = View(blocker);
            if (vb != null && vb.Rig != null && vb.Rig.Valid && !InSession(blocker))
            {
                var lib = ArtLibrary.Load();
                var c = lib != null && lib.ClipVariantCount("ol_block_push") > 0 ? lib.ClipVariant("ol_block_push", blocker + _seed) : null;
                if (c != null && c.length < 4f)
                {
                    var plan = new ClipPlan { Clip = c, Start = Mathf.Min(0.6f, c.length * 0.3f), End = Mathf.Min(c.length, 1.9f), Speed = Rate, FadeIn = 0.15f, FadeOut = 0.35f, UpperBody = true };
                    PlayOneShot(blocker, plan, 1.1f);
                }
            }
            FireNow(WorldPos(loser), 0.45f, false, blocker, loser, blocker, loser);
        }

        float ImpactOf(in PlayEvent e) { return Mathf.Clamp01(e.Value * PlayFrame.YardsToMeters / 8f + (e.Tackle == TackleType.BigHit ? 0.45f : 0.15f)); }      // E-349: the closing speed is in yards per second

        // ============================================================================================================ a pair take
        bool StartSession(ContactKind kind, int tackler, int carrier, in PlayEvent e, bool drag, bool shortTake = false)
        {
            var sim = _stage.Sim; var vc = View(carrier); var vt = View(tackler);
            if (vc == null || vt == null || vc.Rig == null || vt.Rig == null || !vc.Rig.Valid || !vt.Rig.Valid) return false;
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            Vector3 vel = _stage.Frame.VelToUnity(sim.Agents[carrier].Vel);
            Vector2 simVelC = new Vector2(vel.x, vel.z);
            float cyaw = CarrierYaw(carrier);
            // the speed the carrier ran into the hit with: the simulation has already slowed a carrier it brought down (x0.55 plus a slide), the animated velocity still remembers
            float animSp = vc.Anim != null ? vc.Anim.SmoothedLocalVelocity.magnitude : 0f;
            float sp = Mathf.Max(animSp, simVelC.magnitude * (sim.Agents[carrier].State == AgentState.Down ? 1.7f : 1f));
            Vector2 dirC = simVelC.sqrMagnitude > 0.25f ? simVelC.normalized : new Vector2(Mathf.Sin(cyaw * Mathf.Deg2Rad), Mathf.Cos(cyaw * Mathf.Deg2Rad));
            float entry = sp;      // the session predicts with the simulated velocity (what the simulation will really do); the entry speed only scales the carried slide
            var sit = new ContactSituation
            {
                Kind = kind,
                Bearing = ContactMath.BearingDeg(Ground(pt), Ground(pc), cyaw),
                CarrierSpeed = sp,
                Closing = Mathf.Max(0f, e.Value * PlayFrame.YardsToMeters),      // E-349: yards per second to metres per second
                Seed = ++_seed * 7 + carrier * 13 + tackler,
                WantLow = kind == ContactKind.Dive ? 0.9f : (kind == ContactKind.Wrap || kind == ContactKind.Sack ? 0.15f : 0f),
                Distance = (RadiusOf(vc) + RadiusOf(vt)) * 1.15f,
                Avoid = _recent
            };
            var pair = TackleVariants.Choose(_table.pairs, sit);
            if (pair == null && kind != ContactKind.Wrap && kind != ContactKind.Contested && kind != ContactKind.Collision) { sit.Kind = ContactKind.Wrap; pair = TackleVariants.Choose(_table.pairs, sit); }
            if (pair == null) return false;
            var s = StartSessionWith(pair, kind, tackler, carrier, e, drag, sit, null); if (s != null) { s.EntrySpeed = entry; }
            if (s == null) return false;
            if (shortTake) { s.ReleaseAAfter = 0.4f; s.ReleaseBAfter = 0.9f; }
            if (e.Tackle == TackleType.BigHit || kind == ContactKind.BigHit) Fire(pc, ImpactOf(e), true, carrier, tackler, false, s);
            else if (kind != ContactKind.Contested) Fire(pc, ImpactOf(e) * 0.7f, false, carrier, tackler, kind == ContactKind.Sack, s);
            return true;
        }

        ContactSession StartSessionWith(ContactTable.Pair pair, ContactKind kind, int tackler, int carrier, in PlayEvent e, bool drag, ContactSituation sit, Vector2? velOverride = null)
        {
            var sim = _stage.Sim; var vc = View(carrier); var vt = View(tackler);
            var lib = ArtLibrary.Load(); if (lib == null) return null;
            var clipA = lib.ClipByName(pair.clipA); var clipB = lib.ClipByName(pair.clipB);
            if (clipA == null || clipB == null) return null;
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            Vector3 vel = _stage.Frame.VelToUnity(sim.Agents[carrier].Vel);
            Vector2 simVelC = velOverride.HasValue ? velOverride.Value : new Vector2(vel.x, vel.z);
            float cyaw = CarrierYaw(carrier);
            var s = ContactSession.Create(pair, kind, carrier, tackler, Ground(pc), Ground(pt), cyaw, vt.Yaw, simVelC);
            s.SepScale = Mathf.Clamp((RadiusOf(vc) + RadiusOf(vt)) / 0.58f, 0.85f, 1.25f);
            s.ClipA = clipA; s.ClipB = clipB;
            if (drag) s.HoldAt = pair.tContact + 0.5f;
            _sessions.Add(s);
            vc.ContactDriven = true; vt.ContactDriven = true;
            float rate = Rate;
            // E-362 / E-376: the takes play at the pace of the game, and the contact take is fully in at the contact frame (the lead is long enough for a short fade)
            vc.Rig.Play(clipA, s.Start, s.EndA, rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
            vt.Rig.Play(clipB, s.Start, s.EndB, rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
            _recent.Add(pair.clipA); if (_recent.Count > 6) _recent.RemoveAt(0);
            LastTake = kind + " " + pair.clipA + " bearing=" + sit.Bearing.ToString("0") + " speed=" + sit.CarrierSpeed.ToString("0.0");
            return s;
        }

        /// <summary>Ends a take. stop = false lets the next take cross-fade straight from the held / playing pose (E-241: no Stop followed by a Play).</summary>
        void EndSession(ContactSession s, bool stop = true)
        {
            var vc = View(s.Carrier); var vt = View(s.Tackler);
            if (vc != null) { vc.ContactDriven = false; if (vc.Rig != null) { vc.Rig.SetScrub(false); vc.Rig.SetFrozen(false); if (stop) vc.Rig.Stop(0.2f); } }
            if (vt != null) { vt.ContactDriven = false; if (vt.Rig != null) { vt.Rig.SetScrub(false); vt.Rig.SetFrozen(false); if (stop) vt.Rig.Stop(0.2f); } }
            for (int i = _joiners.Count - 1; i >= 0; i--)
                if (_joiners[i].Carrier == s.Carrier) { var vj = View(_joiners[i].Agent); if (vj != null) { vj.ContactDriven = false; if (vj.Rig != null) { vj.Rig.SetScrub(false); vj.Rig.SetFrozen(false); vj.Rig.Stop(0.2f); } } _joiners.RemoveAt(i); }
            _sessions.Remove(s);
        }

        // ============================================================================================================ gang tackles
        void AddJoiner(int tackler, int carrier, ContactSession primary, bool hold)
        {
            var vt = View(tackler); var vc = View(carrier);
            if (vt == null || vc == null || vt.Rig == null || !vt.Rig.Valid) return;
            var lib = ArtLibrary.Load(); if (lib == null) return;
            // E-223: the joiner goes in AFTER the primary hit has landed (0.25 s past its contact), takes a pair the others did not use and stands at his own angle
            var used = new List<string>(_recent); used.Add(primary.Pair.clipA);
            foreach (var j0 in _joiners) if (j0.Carrier == carrier && j0.Pair != null) used.Add(j0.Pair.clipA);
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            float cyaw = primary.Align.Yaw + primary.Pair.a[Mathf.Clamp(Mathf.RoundToInt(primary.Time / primary.Pair.step), 0, primary.Pair.CountA - 1) * ContactTable.Stride + 4];
            var sit = new ContactSituation { Kind = ContactKind.Wrap, Bearing = ContactMath.BearingDeg(Ground(pt), Ground(pc), cyaw), CarrierSpeed = 0.5f, Closing = 2f, Seed = ++_seed * 11 + tackler, Avoid = used, Distance = (RadiusOf(vc) + RadiusOf(vt)) * 1.15f };
            var pair = TackleVariants.Choose(_table.pairs, sit);
            if (pair == null || pair == primary.Pair) { sit.Seed += 5; pair = TackleVariants.Choose(_table.pairs, sit); }
            if (pair == null) return;
            var clipB = lib.ClipByName(pair.clipB); if (clipB == null) return;
            int ic = Mathf.Clamp(Mathf.RoundToInt(pair.tContact / pair.step), 0, Mathf.Min(pair.CountA, pair.CountB) - 1);
            int S = ContactTable.Stride;
            Vector2 hA = new Vector2(pair.a[ic * S + 2], pair.a[ic * S + 3]), hB = new Vector2(pair.b[ic * S + 2], pair.b[ic * S + 3]);
            int n = 0; foreach (var j0 in _joiners) if (j0.Carrier == carrier) n++;
            var j = new Joiner { Agent = tackler, Carrier = carrier, Pair = pair, StartYaw = vt.Yaw, Clip = clipB, YawOffset = (n % 2 == 0 ? 1f : -1f) * (28f + 14f * n) };
            j.Start = Mathf.Max(0f, pair.tContact - 0.3f); j.Time = j.Start;
            j.Align = ContactMath.AlignPair(hB, hA, pair.a[ic * S + 4], Ground(pt), Ground(pc), cyaw + j.YawOffset, 0.6f);
            if (hold) j.HoldAt = pair.tContact + 0.5f;
            _joiners.Add(j);
            // the tackler keeps his simulated tackle clip until the primary hit has landed
        }

        void StartJoiner(Joiner j)
        {
            var vt = View(j.Agent); var vc = View(j.Carrier);
            if (vt == null || vt.Rig == null || !vt.Rig.Valid) return;
            // re-align at the moment he really goes in: where the two bodies are now
            int S = ContactTable.Stride;
            int ic = Mathf.Clamp(Mathf.RoundToInt(j.Pair.tContact / j.Pair.step), 0, Mathf.Min(j.Pair.CountA, j.Pair.CountB) - 1);
            Vector2 hA = new Vector2(j.Pair.a[ic * S + 2], j.Pair.a[ic * S + 3]), hB = new Vector2(j.Pair.b[ic * S + 2], j.Pair.b[ic * S + 3]);
            var s = SessionOf(j.Carrier);
            float cyaw = s != null ? s.Align.Yaw : (vc != null ? vc.Yaw : 0f);
            Vector3 pt = vt.transform.position, pc = vc != null ? vc.transform.position : pt;
            j.StartYaw = vt.Yaw;
            j.Align = ContactMath.AlignPair(hB, hA, j.Pair.a[ic * S + 4], Ground(pt), Ground(pc), cyaw + j.YawOffset, 0.6f);
            j.Playing = true; vt.ContactDriven = true;
            vt.Rig.Play(j.Clip, j.Start, Mathf.Min(j.Pair.lengthB, 6.5f), Rate, 0.14f, 0.35f, false, false, false, 0f, AnimKit.ImpactFloor);
        }

        // ============================================================================================================ ragdoll
        /// <summary>Hands a player to the active ragdoll (hit reactions): impulse is a velocity change in m/s.</summary>
        public bool Knock(int agent, Vector3 impulse, float intensity)
        {
            if (!RagdollEnabled) return false;
            var v = View(agent);
            if (v == null || v.Ragdoll == null || !v.Ragdoll.Available || v.Ragdoll.Active) return false;
            if (InSession(agent)) return false;
            Vector3 vel = _stage.Frame.VelToUnity(_stage.Sim.Agents[agent].Vel);
            v.ContactDriven = true;
            v.Ragdoll.RecoverNeeded = faceUp => StartGetUp(agent, faceUp);
            if (!v.Ragdoll.Begin(vel, impulse, intensity)) { v.ContactDriven = false; return false; }
            if (Knocked != null) Knocked(agent, intensity);
            return true;
        }

        void StartGetUp(int agent, bool faceUp)
        {
            var v = View(agent);
            if (v == null) return;
            var lib = ArtLibrary.Load();
            GetUp pick = default(GetUp); bool found = false; float best = float.MinValue;
            int seed = ++_seed * 31 + agent;
            // E-229: the get-up is classified by how the body lies (face up / face down / on the side: the chest normal's height), then chosen among its class by a stable variety hash
            float front = v.Ragdoll.FrontY();
            for (int i = 0; i < _getUps.Count; i++)
            {
                var g = _getUps[i];
                if ((g.FaceUp > 0f) != faceUp) continue;
                float sc = -Mathf.Abs(g.FaceUp - Mathf.Clamp(front * 1.4f, -1f, 1f)) * 3f + ((seed * 97 + i * 131) & 0xFFFF) / 65535f * 0.6f;
                if (sc > best) { best = sc; pick = g; found = true; }
            }
            Vector2 xz; float yaw;
            v.Ragdoll.PelvisPose(out xz, out yaw);
            if (!found || lib == null) { v.ContactDriven = false; v.SnapToGround(xz, yaw); return; }
            var clip = lib.ClipByName(pick.ActorA ? pick.Pair.clipA : pick.Pair.clipB);
            if (clip == null) { v.ContactDriven = false; v.SnapToGround(xz, yaw); return; }
            float len = pick.ActorA ? pick.Pair.lengthA : pick.Pair.lengthB;
            var arr = pick.ActorA ? pick.Pair.a : pick.Pair.b;
            int ic = Mathf.Clamp(Mathf.RoundToInt(pick.Start / pick.Pair.step), 0, arr.Length / ContactTable.Stride - 1);
            int S = ContactTable.Stride;
            var st = new SoloTake { Agent = agent, Pair = pick.Pair, ActorA = pick.ActorA, Start = pick.Start, Time = pick.Start };
            st.End = Mathf.Min(len, (pick.ActorA ? pick.Pair.getUpA : pick.Pair.getUpB) + 1.1f);
            st.Align = ContactMath.Align(new Vector2(arr[ic * S + 2], arr[ic * S + 3]), arr[ic * S + 4], xz, yaw);
            _solo[agent] = st;
            v.Rig.Play(clip, st.Start, st.End, Rate, 0.05f, 0.3f, false, false);
        }

        // ============================================================================================================ per frame
        public void Tick(float dt)
        {
            if (!Enabled || _stage.Sim == null) return;
            TickBooked(dt);      // v0.10.0 CHOREO
            float rate = Rate;
            // ---- delayed knocks (the stagger before the ragdoll)
            for (int i = _knocks.Count - 1; i >= 0; i--)
            {
                var k = _knocks[i]; k.At -= dt;
                if (k.At <= 0f) { Knock(k.Agent, k.Impulse, k.Intensity); _knocks.RemoveAt(i); }
            }
            // ---- one-shots (pancake finish, QB flinch, recovery dive)
            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                var o = _oneShots[i]; o.T += dt;
                if (o.T >= o.End) { var v = View(o.Agent); if (v != null) { v.ContactDriven = false; if (v.Rig != null) v.Rig.Stop(0.3f); } _oneShots.RemoveAt(i); }
            }
            if (_stiff.Count > 0)
            {
                List<int> gone = null;
                foreach (var kv in _stiff)
                {
                    var vc = View(kv.Key); var vd = View(kv.Value);
                    if (vc == null || vd == null || vc.Motion == null || !vc.Motion.StiffArmActive) { (gone ?? (gone = new List<int>())).Add(kv.Key); continue; }
                    vc.Motion.UpdateStiffArmTarget(ChestPoint(vd));
                }
                if (gone != null) foreach (int g in gone) _stiff.Remove(g);
            }
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var s = _sessions[i];
                var vc = View(s.Carrier); var vt = View(s.Tackler);
                if (vc == null || vt == null) { _sessions.RemoveAt(i); continue; }
                // A-009 (tackle bug): at the whistle every held take plays on (fall, lie, get up)
                bool whistled = _stage.Sim != null && _stage.Sim.Phase == PlayPhase.Dead;
                if (whistled && s.Held) ReleaseHold(s);
                if (s.HoldAt > 0f && !s.Held && !whistled && s.Time >= s.HoldAt && !s.ReleasedA)
                {
                    // E-222: the drag is a struggle, not a statue: a short window of the take plays back and forth while the tackler hangs on
                    s.Hold(true); s.StruggleT = 0f; s.HeldFrom = Mathf.Max(s.Start, s.HoldAt - 0.45f);
                    vc.Rig.SetScrub(true); vt.Rig.SetScrub(true);
                }
                if (s.Held)
                {
                    s.StruggleT += dt;
                    float t = s.HeldFrom + Mathf.PingPong(s.StruggleT * 0.9f, Mathf.Max(0.05f, s.HoldAt - s.HeldFrom));
                    if (!s.ReleasedA) vc.Rig.Scrub(t);
                    if (!s.ReleasedB) vt.Rig.Scrub(t);
                }
                else
                {
                    s.Advance(dt, rate);
                    // E-377: the playing clip is the clock (the session never drifts from the animation)
                    if (!s.ReleasedA && vc.Rig.CurrentClip == s.ClipA) s.SyncTo(vc.Rig.ClipTime);
                    else if (!s.ReleasedB && vt.Rig.CurrentClip == s.ClipB) s.SyncTo(vt.Rig.ClipTime);
                }
                s.TickHeld(dt);
                // impacts at the contact moment (E-378)
                for (int k = _impacts.Count - 1; k >= 0; k--)
                    if (_impacts[k].S == s && s.PassedContact) { var p = _impacts[k]; FireNow(p.Pos, p.K, p.Big, p.A, p.B, p.B, p.A, p.Sack); _impacts.RemoveAt(k); }
                // the simulation's positions this frame (ApplyMatch just set them)
                Vector3 pc = vc.transform.position, pt = vt.transform.position;
                ContactActorPose a, b;
                float bookedYaw = s.Booked ? BookedYaw(s.Booking) : 0f;      // v0.10.0 CHOREO: a booked pair is drawn where the simulation has it, at the yaw of its placement
                s.Evaluate(Ground(pc), s.Booked ? bookedYaw : vc.Yaw, Ground(pt), s.Booked ? bookedYaw : vt.Yaw, out a, out b);
                if (!s.ReleasedA) vc.SetContactPose(new Vector3(a.Root.x, pc.y, a.Root.y), a.Yaw);
                if (!s.ReleasedB) vt.SetContactPose(new Vector3(b.Root.x, pt.y, b.Root.y), b.Yaw);
                // E-215 / E-123: each actor is let go when HIS take ends (or at the early release of a contested catch / jam), the other keeps playing
                if (!s.ReleasedA && !s.Held && (s.Booked ? !s.Booking.OwnsA : (s.ReleaseAAfter > 0f && s.Time >= s.Contact + s.ReleaseAAfter)) || (!s.ReleasedA && !s.Held && s.Time >= s.EndA)) ReleaseActor(s, true, a.Root, pc);
                if (!s.ReleasedB && !s.Held && (s.Booked ? !s.Booking.OwnsB : (s.ReleaseBAfter > 0f && s.Time >= s.Contact + s.ReleaseBAfter)) || (!s.ReleasedB && !s.Held && s.Time >= s.EndB)) ReleaseActor(s, false, b.Root, pt);
                if (s.Finished) EndSession(s);
            }
            for (int i = _joiners.Count - 1; i >= 0; i--)
            {
                var j = _joiners[i];
                var vj = View(j.Agent);
                var s = SessionOf(j.Carrier);
                if (vj == null || s == null) { if (vj != null) vj.ContactDriven = false; _joiners.RemoveAt(i); continue; }
                if (!j.Playing)
                {
                    if (s.Time >= s.Contact + 0.25f || s.Held) StartJoiner(j); else continue;
                }
                if (j.HoldAt > 0f && !j.Held && j.Time >= j.HoldAt) { j.Held = true; vj.Rig.SetFrozen(true); }
                if (j.Held && !s.Held) { j.Held = false; vj.Rig.SetFrozen(false); j.HoldAt = 0f; }
                if (!j.Held) j.Time += dt * rate;
                int S = ContactTable.Stride;
                float w = ContactMath.WarpIn(j.Time, j.Start, j.Pair.tContact);
                Vector2 root = j.Align.Apply(ContactMath.SampleXZ(j.Pair.b, S, 0, j.Pair.step, Mathf.Min(j.Time, j.Pair.lengthB)));
                // ride along with the carrier: the take was aligned at the join, the carrier has moved (or been animated) since
                var vc = View(j.Carrier);
                if (vc != null)
                {
                    int ic = Mathf.Clamp(Mathf.RoundToInt(j.Pair.tContact / j.Pair.step), 0, j.Pair.CountA - 1);
                    Vector2 animA = j.Align.Apply(new Vector2(j.Pair.a[ic * S + 2], j.Pair.a[ic * S + 3]));
                    Vector2 carrierNow = Ground(vc.transform.position);
                    root += carrierNow - animA;
                }
                Vector3 pj = vj.transform.position;
                Vector2 sim = Ground(pj);
                vj.SetContactPose(new Vector3(Mathf.Lerp(sim.x, root.x, w), pj.y, Mathf.Lerp(sim.y, root.y, w)), Mathf.LerpAngle(j.StartYaw, j.Align.Yaw, w));
                if (j.Time >= Mathf.Min(j.Pair.lengthB, 6.5f)) { vj.ContactDriven = false; vj.Rig.Stop(0.2f); _joiners.RemoveAt(i); }
            }
            if (_solo.Count > 0)
            {
                List<int> done = null;
                foreach (var kv in _solo)
                {
                    var st = kv.Value; var v = View(kv.Key);
                    if (v == null) { (done ?? (done = new List<int>())).Add(kv.Key); continue; }
                    st.Time += dt * rate;
                    var arr = st.ActorA ? st.Pair.a : st.Pair.b;
                    Vector2 root = st.Align.Apply(ContactMath.SampleXZ(arr, ContactTable.Stride, 0, st.Pair.step, st.Time));
                    Vector3 p = v.transform.position;
                    // E-231: no glide to the simulated spot over the last 0.7 s: the take plays where the body lay, and at its end he walks on to the simulation (the view offset)
                    if (st.Time >= st.End)
                    {
                        v.AddViewOffset(new Vector3(root.x - p.x, 0f, root.y - p.z));
                        v.SetContactPose(new Vector3(root.x, p.y, root.y), st.Align.Yaw);      // this frame stays where the take had him; the next one starts from there (sim + offset)
                        (done ?? (done = new List<int>())).Add(kv.Key);
                    }
                    else v.SetContactPose(new Vector3(root.x, p.y, root.y), st.Align.Yaw);
                }
                if (done != null) foreach (int id in done) { var v = View(id); if (v != null) { v.ContactDriven = false; if (v.Rig != null) v.Rig.Stop(0.25f); } _solo.Remove(id); }
            }
        }

        void ReleaseActor(ContactSession s, bool carrier, Vector2 takeRoot, Vector3 simPos)
        {
            var v = View(carrier ? s.Carrier : s.Tackler);
            if (carrier) s.ReleasedA = true; else s.ReleasedB = true;
            if (v == null) return;
            v.ContactDriven = false;
            // the body that left the take at a different spot than the simulation walks on to it (not a pop)
            Vector3 gap = new Vector3(takeRoot.x - simPos.x, 0f, takeRoot.y - simPos.z);
            if (gap.sqrMagnitude > 0.0025f) v.AddViewOffset(gap);      // every kind: the body leaves the take where the take had him and walks on to the simulated spot (no pop)
            if (v.Rig != null) { v.Rig.SetScrub(false); v.Rig.SetFrozen(false); v.Rig.Stop(0.25f); }
        }
    }
}
