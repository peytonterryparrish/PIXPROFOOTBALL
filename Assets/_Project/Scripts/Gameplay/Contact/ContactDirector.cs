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
    /// </summary>
    public sealed class ContactDirector
    {
        readonly PlayStage _stage;
        readonly List<ContactSession> _sessions = new List<ContactSession>();
        readonly List<Joiner> _joiners = new List<Joiner>();
        readonly Dictionary<int, SoloTake> _solo = new Dictionary<int, SoloTake>();
        readonly List<ContactTable.Pair> _scratch = new List<ContactTable.Pair>();
        ContactTable _table;
        readonly List<GetUp> _getUps = new List<GetUp>();
        int _seed;

        public bool Enabled { get { return _table != null && _table.pairs.Count > 0; } }
        public int ActiveSessions { get { return _sessions.Count; } }
        /// <summary>Raised when a big hit lands (carrier, tackler, impact 0..1): camera shake, rumble and hit sounds hook in here.</summary>
        public System.Action<ContactImpact> Impact;
        /// <summary>A player was handed to the active ragdoll (agent, intensity): the look layer dirties his kit.</summary>
        public System.Action<int, float> Knocked;
        void Fire(Vector3 pos, float k, bool big, int a, int b, bool sack = false) { if (Impact != null) Impact(new ContactImpact { Position = pos, Intensity = k, Big = big, AgentA = a, AgentB = b, Sack = sack }); }
        public static bool RagdollEnabled = true;

        /// <summary>Last chosen take, for the autotest log.</summary>
        public string LastTake = "";

        struct GetUp { public ContactTable.Pair Pair; public bool ActorA; public float FaceUp; public float Start; }

        sealed class Joiner
        {
            public int Agent, Carrier;
            public ContactTable.Pair Pair;
            public Rigid2 Align;
            public float Start, Time;
            public bool Held; public float HoldAt;
            public float StartYaw;
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
            _sessions.Clear(); _joiners.Clear(); _solo.Clear(); _stiff.Clear();
        }

        void Release(int a, int b)
        {
            var va = View(a); if (va != null) { va.ContactDriven = false; }
            var vb = View(b); if (vb != null) { vb.ContactDriven = false; }
        }

        SimAgentView View(int id) { return id >= 0 && id < 22 ? _stage.Views[id] : null; }
        ContactSession SessionOf(int carrier) { foreach (var s in _sessions) if (s.Carrier == carrier) return s; return null; }
        bool InSession(int agent) { foreach (var s in _sessions) if (s.Carrier == agent || s.Tackler == agent) return true; foreach (var j in _joiners) if (j.Agent == agent) return true; return _solo.ContainsKey(agent); }

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
                case SimEventType.Catch:
                    if (e.AgentA >= 0 && e.AgentA < 22) OnCatch(e);
                    break;
                case SimEventType.Pancake:
                    if (e.AgentA >= 0 && e.AgentB >= 0) OnPancake(e);
                    break;
                case SimEventType.CarrierMove:
                    if (e.AgentA >= 0 && e.AgentB >= 0 && e.Value > 0.5f && e.Note == "Truck") OnTruck(e);
                    else if (e.AgentA >= 0 && e.AgentB >= 0 && e.Note == "StiffArm") OnStiffArm(e);
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
            if (existing != null)
            {
                if (existing.Tackler == tackler || !drag)
                {
                    // the drag ends: the same take plays on from where it was held (or a new gang member goes in)
                    if (existing.Held) { existing.Hold(false); existing.HoldAt = 0f; var rc = vc.Rig; if (rc != null) rc.SetFrozen(false); var rt = vt.Rig; if (rt != null) rt.SetFrozen(false); }
                    if (existing.Tackler != tackler && !InSession(tackler)) AddJoiner(tackler, carrier, existing, false);
                    if (e.Tackle == TackleType.BigHit) Fire(vc.transform.position, ImpactOf(e), true, carrier, tackler);
                    return;
                }
                if (!InSession(tackler)) AddJoiner(tackler, carrier, existing, true);
                return;
            }
            if (InSession(tackler) || InSession(carrier)) return;
            ContactKind kind = TackleVariants.KindFor(e.Tackle, sackFollows);
            // shoestrings that do not bring the carrier down: the tackler goes for his legs and the carrier stumbles on
            if (e.Tackle == TackleType.Shoestring) kind = ContactKind.Broken;
            StartSession(kind, tackler, carrier, e, drag);
        }

        void OnBroken(in PlayEvent e)
        {
            int tackler = e.AgentA, carrier = e.AgentB;
            if (tackler >= 22 || carrier >= 22) return;
            var existing = SessionOf(carrier);
            if (existing != null && existing.Held) EndSession(existing);   // a dragger the carrier shook off
            if (InSession(tackler) || InSession(carrier)) return;
            StartSession(ContactKind.Broken, tackler, carrier, e, false);
        }

        void OnTruck(in PlayEvent e)
        {
            int carrier = e.AgentA, defender = e.AgentB;
            if (defender >= 22 || carrier >= 22) return;
            Vector3 dir = WorldPos(defender) - WorldPos(carrier); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Knock(defender, dir.normalized * 4.2f + Vector3.up * 1.2f, 0.75f);
            Fire(WorldPos(defender), 0.85f, true, carrier, defender);
        }

        readonly Dictionary<int, int> _stiff = new Dictionary<int, int>();

        /// <summary>The carrier's stiff arm reaches for the defender's chest (IK over the stiff-arm clip); a won stiff arm shoves the defender off balance with a light ragdoll.</summary>
        void OnStiffArm(in PlayEvent e)
        {
            int carrier = e.AgentA, defender = e.AgentB;
            if (carrier >= 22 || defender >= 22) return;
            var vc = View(carrier); var vd = View(defender);
            if (vc == null || vd == null || vc.Motion == null) return;
            vc.Motion.StiffArmAt(ChestPoint(vd), 0.55f);
            _stiff[carrier] = defender;
            if (e.Value > 0.5f)
            {
                Vector3 dir = vd.transform.position - vc.transform.position; dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f) dir = vc.Forward;
                Knock(defender, dir.normalized * 2.6f + Vector3.up * 0.3f, 0.2f);
                Fire(vd.transform.position, 0.3f, false, carrier, defender);
            }
        }

        static Vector3 ChestPoint(SimAgentView v)
        {
            var c = v.Avatar != null ? v.Avatar.Chest : null;
            return c != null ? c.position : v.transform.position + Vector3.up * 1.3f;
        }

        /// <summary>A catch with a defender on the receiver: both play an authored contested catch pair (receiver wins) instead of the in-place catch clip.</summary>
        void OnCatch(in PlayEvent e)
        {
            var sim = _stage.Sim;
            var r = sim.Agents[e.AgentA];
            if (r.Side != SimSide.Offense || sim.Kind != PlayKind.Scrimmage) return;
            int best = -1; float bd = 1.9f;
            for (int i = 11; i < 22; i++)
            {
                var d = sim.Agents[i];
                if (d.IsDown || d.State == AgentState.Engaged) continue;
                float dist = (d.Pos - r.Pos).Length;
                if (dist < bd) { bd = dist; best = i; }
            }
            if (best < 0 || InSession(best) || InSession(e.AgentA)) return;
            StartSession(ContactKind.Contested, best, e.AgentA, e, false);
            Fire(WorldPos(e.AgentA), 0.35f, false, e.AgentA, best);
        }

        void OnPancake(in PlayEvent e)
        {
            int blocker = e.AgentA, loser = e.AgentB;
            if (loser >= 22 || blocker >= 22) return;
            Vector3 dir = WorldPos(loser) - WorldPos(blocker); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Knock(loser, dir.normalized * 3.2f + Vector3.up * 0.8f, 0.55f);
            Fire(WorldPos(loser), 0.45f, false, blocker, loser);
        }

        float ImpactOf(in PlayEvent e) { return Mathf.Clamp01(e.Value / 8f + (e.Tackle == TackleType.BigHit ? 0.45f : 0.15f)); }

        // ============================================================================================================ a pair take
        void StartSession(ContactKind kind, int tackler, int carrier, in PlayEvent e, bool drag)
        {
            var sim = _stage.Sim; var vc = View(carrier); var vt = View(tackler);
            if (vc == null || vt == null || vc.Rig == null || vt.Rig == null || !vc.Rig.Valid || !vt.Rig.Valid) return;
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            Vector2 simVelC = new Vector2(0f, 0f);
            Vector3 vel = _stage.Frame.VelToUnity(sim.Agents[carrier].Vel);
            simVelC = new Vector2(vel.x, vel.z);
            float cyaw = CarrierYaw(carrier);
            var sit = new ContactSituation
            {
                Kind = kind,
                Bearing = ContactMath.BearingDeg(Ground(pt), Ground(pc), cyaw),
                // the simulation has already slowed a carrier it brought down (x0.55 plus a slide): estimate how fast he was running into the hit
                CarrierSpeed = simVelC.magnitude * (sim.Agents[carrier].State == AgentState.Down ? 1.7f : 1f),
                Closing = Mathf.Max(0f, e.Value),
                Seed = ++_seed * 7 + carrier * 13 + tackler
            };
            var pair = TackleVariants.Choose(_table.pairs, sit);
            if (pair == null && kind != ContactKind.Wrap && kind != ContactKind.Contested) { sit.Kind = ContactKind.Wrap; pair = TackleVariants.Choose(_table.pairs, sit); }
            if (pair == null) return;
            var lib = ArtLibrary.Load(); if (lib == null) return;
            var clipA = lib.ClipByName(pair.clipA); var clipB = lib.ClipByName(pair.clipB);
            if (clipA == null || clipB == null) return;
            var s = ContactSession.Create(pair, kind, carrier, tackler, Ground(pc), Ground(pt), cyaw, vt.Yaw, simVelC);
            if (drag) s.HoldAt = pair.tContact + 0.5f;
            _sessions.Add(s);
            vc.ContactDriven = true; vt.ContactDriven = true;
            vc.Rig.Play(clipA, s.Start, s.EndA, 1f, 0.07f, 0.35f, false, false);
            vt.Rig.Play(clipB, s.Start, s.EndB, 1f, 0.07f, 0.35f, false, false);
            LastTake = kind + " " + pair.clipA + " bearing=" + sit.Bearing.ToString("0") + " speed=" + sit.CarrierSpeed.ToString("0.0");
            if (e.Tackle == TackleType.BigHit || kind == ContactKind.BigHit) Fire(pc, ImpactOf(e), true, carrier, tackler);
            else Fire(pc, ImpactOf(e) * 0.7f, false, carrier, tackler, kind == ContactKind.Sack);
        }

        void EndSession(ContactSession s)
        {
            var vc = View(s.Carrier); var vt = View(s.Tackler);
            if (vc != null) { vc.ContactDriven = false; if (vc.Rig != null) { vc.Rig.SetFrozen(false); vc.Rig.Stop(0.2f); } }
            if (vt != null) { vt.ContactDriven = false; if (vt.Rig != null) { vt.Rig.SetFrozen(false); vt.Rig.Stop(0.2f); } }
            for (int i = _joiners.Count - 1; i >= 0; i--)
                if (_joiners[i].Carrier == s.Carrier) { var vj = View(_joiners[i].Agent); if (vj != null) { vj.ContactDriven = false; if (vj.Rig != null) { vj.Rig.SetFrozen(false); vj.Rig.Stop(0.2f); } } _joiners.RemoveAt(i); }
            _sessions.Remove(s);
        }

        // ============================================================================================================ gang tackles
        void AddJoiner(int tackler, int carrier, ContactSession primary, bool hold)
        {
            var vt = View(tackler); var vc = View(carrier);
            if (vt == null || vc == null || vt.Rig == null || !vt.Rig.Valid) return;
            var lib = ArtLibrary.Load(); if (lib == null) return;
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            float cyaw = primary.Align.Yaw + primary.Pair.a[Mathf.Clamp(Mathf.RoundToInt(primary.Time / primary.Pair.step), 0, primary.Pair.CountA - 1) * ContactTable.Stride + 4];
            var sit = new ContactSituation { Kind = ContactKind.Wrap, Bearing = ContactMath.BearingDeg(Ground(pt), Ground(pc), cyaw), CarrierSpeed = 0.5f, Closing = 2f, Seed = ++_seed * 11 + tackler };
            var pair = TackleVariants.Choose(_table.pairs, sit);
            if (pair == null || pair == primary.Pair) { sit.Seed += 5; pair = TackleVariants.Choose(_table.pairs, sit); }
            if (pair == null) return;
            var clipB = lib.ClipByName(pair.clipB); if (clipB == null) return;
            int ic = Mathf.Clamp(Mathf.RoundToInt(pair.tContact / pair.step), 0, Mathf.Min(pair.CountA, pair.CountB) - 1);
            int S = ContactTable.Stride;
            Vector2 hA = new Vector2(pair.a[ic * S + 2], pair.a[ic * S + 3]), hB = new Vector2(pair.b[ic * S + 2], pair.b[ic * S + 3]);
            var j = new Joiner { Agent = tackler, Carrier = carrier, Pair = pair, StartYaw = vt.Yaw };
            j.Start = Mathf.Max(0f, pair.tContact - 0.18f); j.Time = j.Start;
            j.Align = ContactMath.AlignPair(hB, hA, pair.a[ic * S + 4], Ground(pt), Ground(pc), cyaw, 0.6f);
            if (hold) j.HoldAt = pair.tContact + 0.5f;
            _joiners.Add(j);
            vt.ContactDriven = true;
            vt.Rig.Play(clipB, j.Start, Mathf.Min(pair.lengthB, 6.5f), 1f, 0.07f, 0.35f, false, false);
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
            for (int i = 0; i < _getUps.Count; i++)
            {
                var g = _getUps[i];
                if ((g.FaceUp > 0f) != faceUp) continue;
                float sc = ((seed * 97 + i * 131) & 0xFFFF) / 65535f;
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
            v.Rig.Play(clip, st.Start, st.End, 1f, 0.05f, 0.3f, false, false);
        }

        // ============================================================================================================ per frame
        public void Tick(float dt)
        {
            if (!Enabled || _stage.Sim == null) return;
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
                // A-009 (tackle bug): a dragged carrier's take is frozen until the next tackle event releases it; after the whistle no event ever comes, so the carrier and his tackler stood frozen in the wrap pose for the rest of the broadcast. At the whistle every held take plays on (fall, lie, get up).
                bool whistled = _stage.Sim != null && _stage.Sim.Phase == PlayPhase.Dead;
                if (whistled && s.Held) { s.Hold(false); s.HoldAt = 0f; vc.Rig.SetFrozen(false); vt.Rig.SetFrozen(false); }
                if (s.HoldAt > 0f && !s.Held && !whistled && s.Time >= s.HoldAt) { s.Hold(true); vc.Rig.SetFrozen(true); vt.Rig.SetFrozen(true); }
                s.Advance(dt);
                // the simulation's positions this frame (ApplyMatch just set them)
                Vector3 pc = vc.transform.position, pt = vt.transform.position;
                ContactActorPose a, b;
                s.Evaluate(Ground(pc), vc.Yaw, Ground(pt), vt.Yaw, out a, out b);
                vc.SetContactPose(new Vector3(a.Root.x, pc.y, a.Root.y), a.Yaw);
                vt.SetContactPose(new Vector3(b.Root.x, pt.y, b.Root.y), b.Yaw);
                if (s.Finished) EndSession(s);
            }
            for (int i = _joiners.Count - 1; i >= 0; i--)
            {
                var j = _joiners[i];
                var vj = View(j.Agent);
                var s = SessionOf(j.Carrier);
                if (vj == null || s == null) { if (vj != null) vj.ContactDriven = false; _joiners.RemoveAt(i); continue; }
                if (j.HoldAt > 0f && !j.Held && j.Time >= j.HoldAt) { j.Held = true; vj.Rig.SetFrozen(true); }
                if (j.Held && !s.Held) { j.Held = false; vj.Rig.SetFrozen(false); j.HoldAt = 0f; }
                if (!j.Held) j.Time += dt;
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
                    st.Time += dt;
                    var arr = st.ActorA ? st.Pair.a : st.Pair.b;
                    Vector2 root = st.Align.Apply(ContactMath.SampleXZ(arr, ContactTable.Stride, 0, st.Pair.step, st.Time));
                    Vector3 p = v.transform.position;
                    float k = ContactMath.Smooth01((st.Time - (st.End - 0.7f)) / 0.7f);   // hand back to the simulation at the end of the get-up
                    root = Vector2.Lerp(root, Ground(p), k);
                    v.SetContactPose(new Vector3(root.x, p.y, root.y), st.Align.Yaw);
                    if (st.Time >= st.End) (done ?? (done = new List<int>())).Add(kv.Key);
                }
                if (done != null) foreach (int id in done) { var v = View(id); if (v != null) { v.ContactDriven = false; if (v.Rig != null) v.Rig.Stop(0.2f); } _solo.Remove(id); }
            }
        }
    }
}
