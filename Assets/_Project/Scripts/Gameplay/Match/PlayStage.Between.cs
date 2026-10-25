using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: the dead-ball flow BETWEEN plays. A play used to end with a cut to black, the 22 views went back to the pool and the next 22 appeared in their formation spots (every body teleported, nobody ever jogged back,
    /// there was no huddle in watch mode and a substitution was a pop). Now the bodies of the finished play are carried over:
    ///  - <see cref="PrepareCarry"/> keeps the 22 views on the field (position and yaw);
    ///  - <see cref="Begin"/> matches the new lineup to them by player identity: a returning player starts where he stands and walks, a new player starts at his team's bench and jogs on, a player who is not in the next lineup becomes a
    ///    <see cref="Leaver"/> and jogs off to his bench (substitutions, special-teams units, a turnover's unit change);
    ///  - <see cref="PlaceBetween"/> sends the offense to a huddle 8 yd behind the ball (the quarterback in front as the leader), the defense to a short huddle 8.5 yd behind its line; both break together and walk to the line (no huddle in a hurry-up);
    ///  - the pre-snap glide of <c>PlayStage.Connect</c> does the walk (jog profile), so the snap waits until everybody is set.
    /// Everything is a pure function of the sim state and a stable hash (no RNG). Switch off with <c>-pixNoContinuity</c>.
    /// </summary>
    public sealed partial class PlayStage
    {
        /// <summary>QA / A-B: bodies are NOT carried between plays (the v0.10 behaviour: cut, pool, teleport).</summary>
        public static bool ContinuityEnabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoContinuity") < 0;

        struct CarryRec { public SimAgentView View; public int Identity; public Vector3 Pos; public float Yaw; public bool Home; }
        readonly List<CarryRec> _carry = new List<CarryRec>();

        /// <summary>The next <see cref="Begin"/> builds on the bodies kept by <see cref="PrepareCarry"/>.</summary>
        public bool CarryPending { get { return _carry.Count > 0; } }
        /// <summary>The play on the field was built from carried bodies (walks instead of teleports; snap waits for the break).</summary>
        public bool CarryActive { get; private set; }
        /// <summary>Seconds since the current play was built (drives the huddle break).</summary>
        public float BetweenClock { get { return _betweenClock; } }
        float _betweenClock;

        sealed class Leaver { public SimAgentView View; public Vector3 Target; public float Speed, Age; public bool Home; public float Yaw; }
        readonly List<Leaver> _leavers = new List<Leaver>();
        public int LeaversOnField { get { return _leavers.Count; } }
        /// <summary>QA: counters of the last play built from carried bodies.</summary>
        public int LastCarried, LastNewcomers, LastLeavers;
        public static int TotalNewcomers, TotalLeavers, TotalCarried;

        /// <summary>Ends the play but keeps the 22 bodies standing where they are for the next <see cref="Begin"/>.</summary>
        public void PrepareCarry()
        {
            if (!ContinuityEnabled || Sim == null) { End(); return; }
            if (Contacts != null) Contacts.Reset();
            ConnectEnd();
            if (Look != null) Look.EndPlay();
            _carry.Clear();
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) continue;
                bool home = Sim.Agents[i].Side == SimSide.Offense ? OffenseKit.Home : DefenseKit.Home;
                _carry.Add(new CarryRec { View = v, Identity = v.IdentityId, Pos = v.transform.position, Yaw = v.Yaw, Home = home });
                _views[i] = null;
            }
            Sim = null;
        }

        bool TakeCarried(int identity, PlayerSize size, PixFootball.Art.BodyBase baseShape, out CarryRec rec)
        {
            for (int k = 0; k < _side.Count; k++)
                if (_side[k].Identity == identity && _side[k].View != null) { var s = _side[k]; _side.RemoveAt(k); s.View.ContactDriven = false; rec = new CarryRec { View = s.View, Identity = identity, Pos = s.Pos, Yaw = s.Yaw, Home = s.Home }; return true; }
            for (int k = 0; k < _carry.Count; k++)
            {
                if (_carry[k].Identity != identity) continue;
                var v = _carry[k].View;
                if (v == null) return Fail(out rec);      // (a player keeps his body when the next defense calls him another position)
                rec = _carry[k]; _carry.RemoveAt(k); return true;
            }
            return Fail(out rec);
        }
        static bool Fail(out CarryRec rec) { rec = default(CarryRec); return false; }

        /// <summary>Where a new player comes on: at his bench, level with his spot, facing the field.</summary>
        Vector3 BenchStart(bool home, Vector3 target, out float yaw)
        {
            float sx = home ? -1f : 1f;
            Vector3 p = new Vector3(sx * 26.9f, 0f, Mathf.Clamp(target.z, -44f, 44f));
            yaw = sx > 0f ? -90f : 90f;
            return p;
        }

        /// <summary>The bodies of the finished play that are not in the next lineup walk to their bench and leave.</summary>
        void FinishCarry()
        {
            LastLeavers = _carry.Count;
            for (int k = 0; k < _carry.Count; k++)
            {
                var c = _carry[k]; if (c.View == null) continue;
                float sx = c.Home ? -1f : 1f;
                int idh = c.Identity; float zf = Mathf.Clamp(c.Pos.z + (((idh % 5) + 5) % 5 - 2) * 1.3f, -40f, 40f);
                _leavers.Add(new Leaver { View = c.View, Target = new Vector3(sx * 28.9f, 0f, zf), Home = c.Home, Yaw = c.Yaw });
                TotalLeavers++;
            }
            _carry.Clear();
        }

        /// <summary>Hard cleanup: every leaver goes back to the pool at once.</summary>
        public void ReleaseLeavers()
        {
            for (int i = 0; i < _leavers.Count; i++) Return(_leavers[i].View);
            _leavers.Clear();
            for (int k = 0; k < _carry.Count; k++) Return(_carry[k].View);
            for (int k = 0; k < _side.Count; k++) Return(_side[k].View);
            _side.Clear();
            _carry.Clear();
        }

        void TickLeavers(float dt)
        {
            for (int i = _leavers.Count - 1; i >= 0; i--)
            {
                var l = _leavers[i]; var v = l.View;
                if (v == null) { _leavers.RemoveAt(i); continue; }
                l.Age += dt;
                Vector3 pos = v.transform.position; Vector3 to = l.Target - pos; to.y = 0f;
                float d = to.magnitude;
                if (d < 0.5f || l.Age > 14f) { _leavers.RemoveAt(i); ToSideline(l); continue; }
                float want = Mathf.Min(d > 8f ? 4.2f : 3.0f, Mathf.Max(1.2f, d * 1.5f));
                l.Speed = Mathf.MoveTowards(l.Speed, want, 5.5f * Mathf.Max(dt, 0.0001f));
                Vector3 dir = to / d;
                pos += dir * Mathf.Min(d, l.Speed * Mathf.Max(dt, 0.0001f));
                var h = new AnimHints { Seed = i * 13 + 5, Side = 'C', Kind = PlayKind.Scrimmage, Speed = l.Speed };
                v.ApplyMatch(pos, dir * l.Speed, dir, l.Speed > 0.4f ? AgentAction.Run : AgentAction.Idle, h, 0f, false, false, dt);
            }
        }

        // ------------------------------------------------------------------------------------------ sideline life
        sealed class SideStander { public SimAgentView View; public int Identity; public Vector3 Pos; public float Yaw, Age; public bool Home; }
        readonly List<SideStander> _side = new List<SideStander>();
        public const int MaxSidelinePlayers = 6;
        public int SidelinePlayers { get { return _side.Count; } }
        public static int TotalSidelined;

        /// <summary>A player who came off stays on the sideline, standing (up to six at a time); when his number is called again he jogs on from where he stands.</summary>
        void ToSideline(Leaver l)
        {
            var v = l.View; if (v == null) return;
            if (_side.Count >= MaxSidelinePlayers) { var o = _side[0]; _side.RemoveAt(0); Return(o.View); }
            float sx = l.Home ? -1f : 1f;
            int id = v.IdentityId;
            float z = l.Target.z;
            var s = new SideStander { View = v, Identity = id, Pos = new Vector3(sx * 28.9f, 0f, z), Yaw = sx > 0f ? -90f : 90f, Home = l.Home };
            _side.Add(s); TotalSidelined++;
            var plan = AnimKit.SidelineStand(id);
            if (plan.Valid && v.Rig != null && v.Rig.Valid) { v.ContactDriven = true; v.NoteClip(plan.Clip); v.Rig.Play(plan); }
        }

        void TickSideline(float dt)
        {
            for (int i = _side.Count - 1; i >= 0; i--)
            {
                var s = _side[i]; var v = s.View;
                if (v == null) { _side.RemoveAt(i); continue; }
                s.Age += dt;
                if (s.Age > 90f) { _side.RemoveAt(i); Return(v); continue; }
                var h = new AnimHints { Seed = i * 7 + 3, Side = 'C', Kind = PlayKind.Scrimmage };
                Vector3 f = Quaternion.Euler(0f, s.Yaw, 0f) * Vector3.forward;
                v.ApplyMatch(s.Pos, Vector3.zero, f, AgentAction.Idle, h, 0f, false, false, dt);
            }
        }

        // ------------------------------------------------------------------------------------------ the huddles
        static uint Fnv(int a, int b)
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)(a >> 8)) * 16777619u; h = (h ^ (uint)b) * 16777619u; h = (h ^ (uint)(b >> 8)) * 16777619u;
            return h;
        }

        /// <summary>Hurry-up: no huddle (the two-minute drill, a quick-snap tempo).</summary>
        /// <summary>The next Begin has no bodies to carry (the first snap of the game, a half): everybody comes on from the benches.</summary>
        public bool NextBeginFromBench;
        public bool NextBeginHurry;
        bool _lastHurry;
        /// <summary>QA: the play on the field was built as a hurry-up (no huddle).</summary>
        public bool NextBeginHurryUsed { get { return _lastHurry; } }
        public static float HuddleScale = 1f;
        /// <summary>v0.12.0 (RULES): seconds the director gives the next play from its build to the snap count (the dead-ball time minus what the whistle and the aftermath already used); 0 = the old fixed holds. The huddle hold stretches to fill it, the walk-in and the set keep their own durations.</summary>
        public float NextBeginBudget;
        /// <summary>v0.12.0 (RULES): the budget the play on the field was built with (read by the walk-in: how long the break, the walk and the set may take).</summary>
        public float PreSnapBudget { get; private set; }
        /// <summary>Walk from the ring to the line, the quarterback's walk-in and crouch, and the set: what the huddle hold leaves out of the budget.</summary>
        const float WalkInAndSetSeconds = 5.5f;

        /// <summary>Called from <see cref="ConnectBegin"/> for a play built from carried bodies: starts every body's walk (huddle first for the offense and the defense, or straight to the line in a hurry).</summary>
        void PlaceBetween(PlaySimulation sim, PlayFrame frame, bool huddle)
        {
            _lastHurry = NextBeginHurry;
            PreSnapBudget = NextBeginBudget; NextBeginBudget = 0f;
            _betweenClock = 0f;
            bool scrim = sim.Kind == PlayKind.Scrimmage;
            var order = new List<int>();
            // ring slots, ordered by the lateral position of the final spot so the break does not cross paths
            for (int side = 0; side < 2; side++)
            {
                order.Clear();
                int lo = side == 0 ? 0 : 11, hi = side == 0 ? 11 : 22;
                for (int i = lo; i < hi; i++) if (_views[i] != null) order.Add(i);
                order.Sort((p, q) => sim.Agents[p].StartPos.X.CompareTo(sim.Agents[q].StartPos.X));
                bool doHuddle = huddle && scrim && !NextBeginHurry && (side == 0 || DefenseHuddles);
                Vec2 cen = side == 0 ? new Vec2(Mathf.Clamp(sim.BallX0, 9f, 44f), Mathf.Max(-4f, sim.LosY - 8.2f)) : new Vec2(Mathf.Clamp(sim.BallX0, 9f, 44f), sim.LosY + 8.8f);
                float maxArrive = 0f;
                var ar = new float[order.Count];
                int n = order.Count;
                // the quarterback is the leader and stands in the open front of the ring; the rest fill an arc of 280 degrees behind him
                int rank = 0;
                for (int k = 0; k < n; k++)
                {
                    int i = order[k];
                    var v = _views[i]; var a = sim.Agents[i];
                    var g = _g[i];
                    g.Init = true; g.Active = true; g.Speed = 0f; g.Wait = 0f; g.Huddle = false; g.ToHuddle = false;
                    g.Shown = v.transform.position; g.LastTarget = _cP[i];
                    bool newcomer = Mathf.Abs(v.transform.position.x) > 25.5f;      // a substitute still on his way from the bench does not hold the huddle: he goes straight to the line
                    if (doHuddle && !newcomer)
                    {
                        bool leader = side == 0 ? i == 0 : a.PlayPos == Position.MLB || (k == n / 2 && !HasMlb(sim));
                        float ang;
                        if (leader) ang = 90f * Mathf.Deg2Rad;
                        else { float frac = rank / (float)Mathf.Max(1, n - 2); ang = (155f + 250f * Mathf.Clamp01(frac)) * Mathf.Deg2Rad; rank++; }
                        float rad = side == 0 ? 2.0f : 1.9f;
                        Vec2 sp = new Vec2(cen.X + Mathf.Cos(ang) * rad * 0.95f, cen.Y + Mathf.Sin(ang) * rad);
                        g.HuddleSpot = frame.ToUnity(sp);
                        Vector3 fc = frame.ToUnity(cen) - g.HuddleSpot; fc.y = 0f;
                        g.HuddleFace = fc.sqrMagnitude > 1e-4f ? fc.normalized : Vector3.forward;
                        g.ToHuddle = true; g.Leader = leader;
                        Vector3 dd = g.HuddleSpot - g.Shown; dd.y = 0f;
                        ar[k] = dd.magnitude / 3.6f + 0.4f;
                        maxArrive = Mathf.Min(4.2f, Mathf.Max(maxArrive, ar[k]));
                    }
                    _g[i] = g;
                }
                if (doHuddle)
                {
                    float hold = (side == 0 ? 2.4f : 1.5f) * HuddleScale;
                    if (PreSnapBudget > 0f)      // v0.12.0 (RULES): the huddle talks as long as the dead-ball time allows (the offense up to 14 s, the defense breaks a little earlier)
                    {
                        float fill = Mathf.Clamp(PreSnapBudget - maxArrive - WalkInAndSetSeconds, 0f, 14f);
                        hold = Mathf.Max(hold, side == 0 ? fill : fill * 0.85f);
                    }
                    for (int k = 0; k < n; k++)
                    {
                        int i = order[k]; var g = _g[i];
                        uint h = Fnv(i * 31 + side, _playIndex);
                        g.BreakAt = maxArrive + hold + (h % 100) * 0.0016f;
                        _g[i] = g;
                    }
                }
            }
        }

        static bool HasMlb(PlaySimulation sim) { for (int i = 11; i < 22; i++) if (sim.Agents[i].PlayPos == Position.MLB) return true; return false; }

        /// <summary>The defense also huddles (a short one around the middle linebacker).</summary>
        public static bool DefenseHuddles = true;

        // ------------------------------------------------------------------------------------------ QA
        /// <summary>QA: the agents the snap is waiting for (still walking or in the huddle).</summary>
        public string QaBreakBlockers()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 22; i++) if (_g[i].Active || _g[i].Wait > 0f) { sb.Append(i).Append(_g[i].ToHuddle ? "H" : (_g[i].Wait > 0f ? "W" : "G")); if (_g[i].Active && !_g[i].ToHuddle && _g[i].Wait <= 0f) sb.Append("(d" + (_g[i].LastTarget - _g[i].Shown).magnitude.ToString("0.0") + " v" + _g[i].Speed.ToString("0.0") + ")"); sb.Append(" "); }
            return sb.ToString();
        }

        /// <summary>QA: every body on the field right now (the 22 of the play plus the ones jogging on or off).</summary>
        public IEnumerable<SimAgentView> QaViews()
        {
            for (int i = 0; i < 22; i++) if (_views[i] != null) yield return _views[i];
            for (int i = 0; i < _leavers.Count; i++) if (_leavers[i].View != null) yield return _leavers[i].View;
            for (int i = 0; i < _side.Count; i++) if (_side[i].View != null) yield return _side[i].View;
        }

        /// <summary>Halftime / end of game: every carried body leaves for its bench; the next play is built from the benches.</summary>
        public void DismissAll()
        {
            FinishCarry();
            NextBeginFromBench = true;
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        // ------------------------------------------------------------------------------------------ the aftermath: after the pile, everybody heads for the next huddle
        bool _afOn; float _afAt; readonly bool[] _afSent = new bool[22];
        public static bool AftermathWalk = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoAftermathWalk") < 0;

        void AftermathBegin()
        {
            _afOn = AftermathWalk && Sim != null && Sim.Kind == PlayKind.Scrimmage && ContinuityEnabled; _afAt = Time.time;
            Array.Clear(_afSent, 0, 22);
        }

        /// <summary>Where a player of the dead play is likely to huddle for the next one (the offense 8 yd behind the spot, the defense 8.8 yd in front of it; a turnover swaps the sides). A guess: the next Begin re-plans from wherever he is.</summary>
        Vector3 HuddleGuess(int i, Vec2 spot)
        {
            var a = Sim.Agents[i];
            bool nextOffense = (a.Side == SimSide.Offense) != (Sim.Result != null && Sim.Result.Turnover);
            uint h = Fnv(i * 17 + 3, _playIndex);
            float ox = ((int)(h % 7) - 3) * 0.55f;
            float oy = ((int)((h >> 5) % 5) - 2) * 0.45f;
            Vec2 p = nextOffense ? new Vec2(spot.X + ox, spot.Y - 8.2f + oy) : new Vec2(spot.X + ox, spot.Y + 8.8f + oy);
            p = new Vec2(Mathf.Clamp(p.X, 6f, 47f), Mathf.Clamp(p.Y, -6f, 106f));
            return Frame.ToUnity(p);
        }

        void ServiceAftermath()
        {
            if (!_afOn || Sim == null || Sim.Phase != PlayPhase.Dead) { if (Sim == null || Sim.Phase != PlayPhase.Dead) _afOn = false; return; }
            float t = Time.time - _afAt;
            if (t < 1.4f) return;
            Vec2 spot = Sim.Carrier != null ? Sim.Carrier.Pos : Sim.Ball.Pos.XY;
            Vector3 spotU = Frame.ToUnity(spot);
            for (int i = 0; i < 22; i++)
            {
                if (_afSent[i]) continue;
                var v = _views[i]; if (v == null || v.ContactDriven) continue;
                var a = Sim.Agents[i];
                if (a.IsDown || a.Action == AgentAction.Tackled || a.Action == AgentAction.Fallen || a.Action == AgentAction.GetUp || a.Action == AgentAction.Celebrate) continue;
                if (IsTackleAction(a.Action) && a.ActionTime < 1.2f) continue;
                // the ones standing at the pile wait a beat longer than the rest
                float dp = (a.Pos - spot).Length;
                if (dp < 4f && t < 2.2f) continue;
                _afSent[i] = true;
                var w = _walks[i];
                if (w.Active && !WalkArrived(i) && t < 2.0f) { _afSent[i] = false; continue; }
                WalkTo(i, HuddleGuess(i, spot), dp > 12f ? 3.6f : 2.9f, spotU);
                _chWalked.Add(i);
            }
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        // ------------------------------------------------------------------------------------------ timeouts
        bool _toOn;
        public bool TimeoutActive { get { return _toOn; } }

        /// <summary>A timeout in a simulated game: both teams jog to their sideline and gather around the coach; the pre-snap flow resumes afterwards (<see cref="TimeoutEnd"/>).</summary>
        public void TimeoutBegin()
        {
            if (Sim == null) return;
            _toOn = true;
            float losZ = Frame.ToUnity(new Vec2(Sim.BallX0, Sim.LosY)).z;
            var cnt = new int[2];
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) continue;
                var a = Sim.Agents[i];
                bool home = a.Side == SimSide.Offense ? OffenseKit.Home : DefenseKit.Home;
                int hi = home ? 0 : 1; int k = cnt[hi]++;
                float sx = home ? -1f : 1f;
                Vector3 center = new Vector3(sx * 27.2f, 0f, Mathf.Clamp(losZ, -30f, 30f));
                float ang = k * (Mathf.PI * 2f / 11f) + 0.4f;
                Vector3 spot = center + new Vector3(Mathf.Cos(ang) * 1.55f * 0.8f - sx * 0.6f, 0f, Mathf.Sin(ang) * 1.55f);
                var g = _g[i]; g.Active = false; g.ToHuddle = false; g.Wait = 0f; _g[i] = g;
                WalkTo(i, spot, 4.2f, center);
            }
        }

        public bool TimeoutArrived()
        {
            for (int i = 0; i < 22; i++) if (_views[i] != null && !WalkArrived(i)) return false;
            return true;
        }

        /// <summary>The teams come back: the pre-snap glide takes over from where each man stands.</summary>
        public void TimeoutEnd()
        {
            _toOn = false;
            ClearWalks();
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) continue;
                if (v.Huddle) v.SetHuddle(false, 0);
                var g = _g[i]; g.Shown = v.transform.position; g.Active = true; g.Speed = 0f; g.Wait = 0f; g.ToHuddle = false; _g[i] = g;
            }
            _betweenClock = 0f;
        }

        void TimeoutHuddles()
        {
            if (!_toOn) return;
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) continue;
                if (WalkArrived(i)) v.SetHuddle(true, i * 5 + _playIndex, false);
            }
        }
    }
}
