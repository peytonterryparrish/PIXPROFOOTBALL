using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.12.0 (RULES): the knobs of the causal pre-snap model, calibrated with the 4,000-play lab run (Docs/QA/v0120/rules.md). Each is the chance per snap and per unit of weight before the
    /// awareness, cadence, hard-count, fatigue and crowd factors (see <see cref="PlaySimulation"/> PreSnapFouls).
    /// </summary>
    public static class PreSnapTuning
    {
        /// <summary>QA: <c>-pixPreSnapJump 8</c> multiplies the jump chances (a probe run that wants many false starts and offsides on the broadcast camera).</summary>
        static PreSnapTuning()
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                float f;
                if (a[i] == "-pixPreSnapJump" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) { JumpOffense *= f; JumpDefense *= f; }
            }
        }

        /// <summary>An offensive player leaves early (false start): chance per snap, weight 1 = a lineman.</summary>
        public static float JumpOffense = 0.0025f;
        /// <summary>A defender jumps the count (offside / neutral zone / encroachment when he gets into the zone).</summary>
        public static float JumpDefense = 0.0024f;
        /// <summary>Under a hard count the defense bites this much more; the offense jumps this much less (it is ready).</summary>
        public static float HardCountBite = 2.6f, HardCountOffense = 0.8f;
        /// <summary>Chance of a hard count on an ordinary snap, and the extra in short yardage.</summary>
        public static float HardCountChance = 0.05f, HardCountShortYardage = 0.35f;
        /// <summary>The man across from a jumping defender flinches (neutral zone infraction), scaled by his lack of awareness; and the chance of contact in the zone (encroachment) when the two overlap.</summary>
        public static float NeutralZoneReact = 0.30f, EncroachContact = 0.17f;
        /// <summary>A receiver or lineman lines up wrong (late substitution, wrong spot): chance per player on the line per snap.</summary>
        public static float LineupMistake = 0.0007f;
        /// <summary>The quarterback is slow to snap (delay of game): chance per snap at awareness 70.</summary>
        public static float SlowSnap = 0.0022f;
        /// <summary>A shift (two receivers change places) per pass / run snap, and the quarterback's wait after it: mean and spread (s) beyond the 1 s set.</summary>
        public static float ShiftPass = 0.07f, ShiftRun = 0.04f, ShiftGapMean = 0.55f, ShiftGapSpread = 0.27f;
        /// <summary>The man in motion turns upfield early (motion toward the line), a second man goes in motion too: chance per motion play at awareness 70.</summary>
        public static float MotionDrift = 0.0015f, MotionBoth = 0.0015f;
        /// <summary>Seconds on the play clock when a headless count begins (the director passes the real clock).</summary>
        public static float PlayClockSlack = 5f;
    }

    /// <summary>
    /// v0.12.0 (RULES): the pre-snap fouls are CAUSAL. A false start, offside, neutral zone infraction or encroachment is a body that actually moves early (the sim moves it, the view can play a
    /// flinch from <see cref="EarlyMoverId"/> / <see cref="EarlyMoveAt"/>); an illegal formation, shift or motion is computed from the alignment and movement the sim really has at the snap
    /// (<see cref="PreSnapRules"/>); delay of game is the play clock running out on a quarterback who is slow. Nothing is rolled at the snap itself.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        Rng _preRng;
        /// <summary>
        /// The pre-snap model's own random stream, derived from the play's stream without consuming it: the live play then draws exactly what it drew in v0.11 (see <see cref="DoSnap"/>), so every calibration and
        /// census that was fitted on the old sequence still reads the same, and a pre-snap foul never changes what the play would have done.
        /// </summary>
        Rng PreRng
        {
            get
            {
                if (_preRng == null) { ulong a, b; Rng.GetState(out a, out b); _preRng = new Rng(unchecked(a * 0x9E3779B97F4A7C15UL ^ b ^ 0x5DEECE66DUL)); }
                return _preRng;
            }
        }
        sealed class Jump { public int Idx; public float At, Dir, Lead; public bool Started, Done; public float Started0; }
        readonly List<Jump> _jumps = new List<Jump>();
        bool _countPlanned;
        float _snapAt, _delayAt = -1f;
        bool _hardCount;
        readonly float[] _lastMoving = new float[22];
        readonly bool[] _joined = new bool[22];
        readonly bool[] _hasWalk = new bool[22];
        readonly Vec2[] _walkTo = new Vec2[22];
        float _walkSpeed = 2.6f;
        int _motion2 = -1, _motion2Dir;
        float _driftFrom = -1f, _driftSpeed;
        bool _shiftPlanned;
        MotionVerdict _atPlanned;

        /// <summary>The offensive or defensive player who moved first before the snap (a false start, an offside, a neutral-zone move) or -1. For the view: play the flinch from <see cref="EarlyMoveAt"/>.</summary>
        public int EarlyMoverId { get; private set; } = -1;
        /// <summary>Simulation pre-snap time (<see cref="PreSnapTime"/>) at which <see cref="EarlyMoverId"/> started to move, -1 when nobody did.</summary>
        public float EarlyMoveAt { get; private set; } = -1f;
        /// <summary>True when the offense called a hard count on this snap (the defense is more likely to bite).</summary>
        public bool HardCount { get { return _hardCount; } }
        /// <summary>Absolute <see cref="PreSnapTime"/> of the snap the count has planned, -1 before <see cref="BeginCount"/>.</summary>
        public float PlannedSnapAt { get { return _countPlanned ? _snapAt : -1f; } }
        /// <summary>The last foul the pre-snap rules called (type), or null.</summary>
        public PenaltyType? PreSnapFoul { get; private set; }

        static float Disc(float awareness) { float d = (1.6f - awareness / 99f) / 0.9f; return d * d; }

        // ======================================================================================================== the count
        /// <summary>
        /// The quarterback starts the snap count. Returns the seconds from now until the ball is snapped (the cadence the director and the QB animation play). This is where the offense's choices are made: a man in
        /// motion, a shift (and how long the quarterback waits after it), a slow quarterback who lets the play clock run down, a hard count, and which players jump the count. <paramref name="cpuOffense"/> false = a person
        /// calls the offense (no computer motion, shift or slow snap). <paramref name="clockLeft"/> = seconds left on the play clock (the director's real one).
        /// </summary>
        public float BeginCount(float cadenceSeconds, float clockLeft = -1f, bool cpuOffense = true, bool theatre = true)
        {
            if (Phase != PlayPhase.PreSnap) return 0f;
            if (_countPlanned) return Math.Max(0f, _snapAt - PreSnapTime);
            PlanCount(cadenceSeconds, clockLeft, cpuOffense, theatre);
            return Math.Max(0f, _snapAt - PreSnapTime);
        }

        /// <summary>Plans the count. <paramref name="theatre"/>: motion, shift and the show blitz (the director's count and <see cref="PlaySimOptions.Cadence"/>); off = the headless fixed delay (jumps, slow snap).</summary>
        void PlanCount(float cadence, float clockLeft, bool cpuOffense, bool theatre)
        {
            _countPlanned = true;
            float t0 = PreSnapTime;
            float len = cadence;
            float aw = Agents[0].R.Awareness;
            bool scrim = Kind == PlayKind.Scrimmage;
            float qbDisc = Disc(aw);
            if (scrim && cpuOffense && theatre)
            {
                len = PlanTheatre(len, aw);
            }
            // hard count: the offense tries to draw the defense offside (short yardage and goal line most of all)
            if (scrim && cpuOffense)
            {
                float hc = PreSnapTuning.HardCountChance + (State != null && (State.Distance <= 2 || State.YardsToGoal <= 3) ? PreSnapTuning.HardCountShortYardage : 0f);
                _hardCount = PreRng.NextFloat() < hc;
            }
            _snapAt = t0 + len;
            // the movement the officials see is the movement at the moment the count meant to snap (the director may hold the ball a moment longer for the bodies of the stage to settle: that must not heal a shift)
            if (_countPlanned && _atPlanned == null && PreSnapTime >= _snapAt) _atPlanned = OffenseMovement();
            // the play clock
            if (scrim && cpuOffense)
            {
                float left = clockLeft > 0f ? clockLeft : PreSnapTuning.PlayClockSlack;
                _delayAt = t0 + left;
                float complex = 1f + (MotionSlot >= 0 ? 0.5f : 0f) + (_shiftPlanned ? 0.5f : 0f) + (_hotRoutes.Count > 0 ? 0.5f : 0f);
                if (PreRng.NextFloat() < PreSnapTuning.SlowSnap * qbDisc * complex)
                    _snapAt = _delayAt + 0.3f + 1.2f * PreRng.NextFloat();       // he waits too long: the clock runs out first
            }
            DrawJumps(Math.Min(cadence, 0.9f));
        }

        /// <summary>The computer offense's pre-snap theatre: a man in motion, a shift, the defense showing a blitz. Returns the (possibly longer) count in seconds.</summary>
        float PlanTheatre(float len, float qbAwareness)
        {
            float snapIn = len;
            float disc = Disc(qbAwareness);
            // a shift: two receivers change places; everybody must then be still for ShiftSetSeconds
            if (MotionSlot < 0 && PreRng.NextFloat() < (OffPlay.IsPass ? PreSnapTuning.ShiftPass : PreSnapTuning.ShiftRun) && PlanShift(out float shiftEnd))
            {
                _shiftPlanned = true;
                float gap = PreSnapTuning.ShiftGapMean + PreSnapTuning.ShiftGapSpread * Math.Max(0.5f, disc) * PreRng.Gaussian();
                snapIn = Math.Max(snapIn, shiftEnd + Rules.ShiftSetSeconds + gap);
            }
            // a man in motion: a third of the passes and a fifth of the runs, from a back or receiver who starts off the line
            else if (MotionSlot < 0 && PreRng.NextFloat() < (OffPlay.IsPass ? 0.30f : 0.20f))
            {
                var cands = new List<int>();
                for (int s = 1; s < 11; s++) if (CanMotion(s) && !_hotRoutes.ContainsKey(s)) cands.Add(s);
                if (cands.Count > 0)
                {
                    int slot = cands[PreRng.Range(0, cands.Count)];
                    int dir = Agents[slot].Pos.X >= BallX0 ? -1 : 1;
                    SetMotion(slot, dir);
                    snapIn = Math.Max(snapIn, 1.9f + 0.4f * PreRng.NextFloat());
                    // the mistakes: he turns upfield early (toward the line at the snap), or a second man goes too
                    float mdisc = Disc(Agents[slot].R.Awareness);
                    if (PreRng.NextFloat() < PreSnapTuning.MotionDrift * mdisc) { _driftSpeed = 0.7f + 0.5f * PreRng.NextFloat(); _driftFrom = PreSnapTime + snapIn - 0.3f; }
                    if (PreRng.NextFloat() < PreSnapTuning.MotionBoth * disc)
                    {
                        var others = new List<int>();
                        for (int s = 1; s < 11; s++) if (s != slot && CanMotion(s)) others.Add(s);
                        if (others.Count > 0) { _motion2 = others[PreRng.Range(0, others.Count)]; _motion2Dir = -dir; }
                    }
                }
            }
            else if (MotionSlot >= 0) snapIn = Math.Max(snapIn, 1.9f + 0.4f * PreRng.NextFloat());       // a jet-sweep man (AutoJetMotion) needs his strides
            // the defense disguises: a blitz call shows the blitz about a third of the time
            bool blitz = false;
            for (int i = 0; i < 11; i++) if (DefPlay.Assignments[i] is BlitzAssignment) blitz = true;
            if (blitz && PreRng.NextFloat() < 0.35f)
            {
                DefenseAdjust adj = DefAdjust != null ? DefAdjust.Clone() : new DefenseAdjust();
                adj.Blitz = BlitzShow.Show;
                SetDefenseAdjust(adj);
            }
            return snapIn;
        }

        /// <summary>
        /// A shift: two off-the-line receivers (or a back and a receiver) squeeze toward each other (a tighter split, a bunch), both walking at once; the shift ends when they stop. Their routes keep their own stems
        /// (the squeeze is a few yards), so the play is the play that was called. False when there is no pair 3 to 14 yd apart.
        /// </summary>
        bool PlanShift(out float endIn)
        {
            endIn = 0f;
            var c = new List<int>();
            for (int s = 1; s < 11; s++) if (CanMotion(s) && !_hotRoutes.ContainsKey(s)) c.Add(s);
            int ba = -1, bb = -1; float bd = 99f;
            for (int a = 0; a < c.Count; a++)
                for (int b = a + 1; b < c.Count; b++)
                {
                    float d = (Agents[c[a]].Pos - Agents[c[b]].Pos).Length;
                    if (d >= 3f && d < bd) { bd = d; ba = c[a]; bb = c[b]; }
                }
            if (ba < 0 || bd > 14f) return false;
            Vec2 pa = Agents[ba].Pos, pb = Agents[bb].Pos;
            Vec2 dir = (pb - pa) * (1f / bd);
            float s2 = SimMath.Clamp(0.35f * bd, 1.0f, 3.0f);
            _walkTo[ba] = pa + dir * s2; _walkTo[bb] = pb - dir * s2; _hasWalk[ba] = _hasWalk[bb] = true;
            _joined[ba] = _joined[bb] = true;
            _walkSpeed = 2.6f;
            endIn = s2 / _walkSpeed + 0.25f;
            return true;
        }

        // ======================================================================================================== early movers
        void DrawJumps(float window)
        {
            _jumps.Clear();
            if (Options == null || !Options.Penalties) return;
            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Kneel || Kind == PlayKind.Spike || IsExtraPoint) return;
            bool scrim = Kind == PlayKind.Scrimmage;
            float cad = SimMath.Clamp(window / 0.8f, 0.6f, 1.5f);
            float crowd = scrim && State != null && State.Possession == Core.Team.Away ? 1.2f : 1f;
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = Agents[i];
                if (a.ControlledByUser) continue;      // the person who plays him decides when he moves
                float w;
                if (!scrim) w = (PositionGroup.IsOffensiveLine(a.PlayPos) || a.PlayPos == Position.TE) ? 1f : 0.2f;
                else if (PositionGroup.IsOffensiveLine(a.PlayPos)) w = 1f;
                else if (a.PlayPos == Position.QB) w = 0.2f;
                else w = PreSnapRules.IsOnLine(a.Pos.Y - LosY, Rules) ? 0.8f : 0.35f;
                float p = PreSnapTuning.JumpOffense * w * Disc(a.R.Awareness) * cad * crowd * (_hardCount ? PreSnapTuning.HardCountOffense : 1f) * (1.15f - 0.15f * a.Body.Energy);
                float u = PreRng.NextFloat(), lead = 0.10f + 0.5f * PreRng.NextFloat();
                if (u < p) AddJump(i, 1f, lead);
            }
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = Agents[i];
                if (a.Assign is ReturnerAssignment || a.ControlledByUser) continue;
                float depth = a.Pos.Y - LosY;
                float w = PositionGroup.IsDefensiveLine(a.PlayPos) && depth < 2.2f ? 1f : (PositionGroup.IsLinebacker(a.PlayPos) && depth < 2.4f ? 0.4f : 0f);
                float p = PreSnapTuning.JumpDefense * w * Disc(a.R.Awareness) * cad * (_hardCount ? PreSnapTuning.HardCountBite : 1f) * (1.15f - 0.15f * a.Body.Energy);
                float u = PreRng.NextFloat(), lead = 0.10f + 0.5f * PreRng.NextFloat();
                if (u < p) AddJump(i, -1f, lead);
            }
        }

        void AddJump(int idx, float dir, float lead)
        {
            float at = Math.Max(PreSnapTime, _snapAt - lead);
            _jumps.Add(new Jump { Idx = idx, At = at, Dir = dir, Lead = _snapAt - at });
        }

        /// <summary>Moves a jumping player out of his stance this tick (a lurch toward the line); false when he is not jumping.</summary>
        bool JumpStep(int i)
        {
            for (int k = 0; k < _jumps.Count; k++)
            {
                Jump j = _jumps[k];
                if (j.Idx != i || !j.Started || j.Done) continue;
                SimAgent a = Agents[i];
                float t = PreSnapTime - j.At;
                if (t > 0.45f) { j.Done = true; continue; }      // a lurch that drew no flag ends here (the get-off of a man who beat the snap by a hair)
                float v = Math.Min(i < 11 ? 4.2f : 5.5f, 26f * t);
                a.Body.Vel = new Vec2(0f, j.Dir * v);
                a.Body.Pos = a.Body.Pos + a.Body.Vel * Dt;
                a.Intent = AgentAction.Run;
                a.SetAction(AgentAction.Run);
                return true;
            }
            return false;
        }

        // ======================================================================================================== per tick
        /// <summary>Called after the pre-snap bodies have stepped. Starts the jumps that are due, tracks who moves and who is still, and calls the fouls that the movement has just caused. True = the play is dead.</summary>
        bool RulesAfterStep()
        {
            // who moves, who is still (shift set time)
            for (int i = 0; i < 22; i++)
                if (Agents[i].Speed > Rules.MovingSpeed) _lastMoving[i] = PreSnapTime;
            if (Options == null || !Options.Penalties) return false;
            // the play clock
            if (_delayAt > 0f && PreSnapTime >= _delayAt && Kind == PlayKind.Scrimmage)
            {
                CallFoul(PenaltyType.DelayOfGame, 0, State.Possession, "DelayOfGame");
                return true;
            }
            for (int k = 0; k < _jumps.Count; k++)
            {
                Jump j = _jumps[k];
                if (j.Done) continue;
                if (!j.Started)
                {
                    if (PreSnapTime < j.At) continue;
                    j.Started = true; j.Started0 = PreSnapTime;
                    if (EarlyMoverId < 0) { EarlyMoverId = j.Idx; EarlyMoveAt = PreSnapTime; }
                }
                SimAgent a = Agents[j.Idx];
                if (j.Idx < 11)
                {
                    // an offensive player who has been out of his stance for 0.2 s has started the play: false start
                    if (PreSnapTime - j.At >= 0.2f)
                    {
                        PenaltyType t = PenaltyType.FalseStart;
                        CallFoul(t, j.Idx, State.Possession, "FalseStart");
                        return true;
                    }
                }
                else if (PreSnapRules.InNeutralZone(a.Pos.Y - LosY, a.Body.Radius, Rules))
                {
                    ClassifyDefenderInZone(j.Idx);
                    return true;
                }
            }
            return false;
        }

        /// <summary>A defender has got into the neutral zone before the snap: he made the man across from him flinch (neutral zone infraction), made contact (encroachment), or is simply offside.</summary>
        void ClassifyDefenderInZone(int d)
        {
            SimAgent def = Agents[d];
            int across = -1; float bestDx = 99f;
            for (int i = 0; i < 11; i++)
            {
                SimAgent o = Agents[i];
                if (!PositionGroup.IsOffensiveLine(o.PlayPos) && o.PlayPos != Position.TE) continue;
                if (!PreSnapRules.IsOnLine(o.Pos.Y - LosY, Rules)) continue;
                float dx = Math.Abs(o.Pos.X - def.Pos.X);
                if (dx < bestDx) { bestDx = dx; across = i; }
            }
            Team defTeam = State.Possession.Opponent();
            bool kickplay = Kind != PlayKind.Scrimmage;
            if (across >= 0 && bestDx < 1.3f)
            {
                float react = PreSnapTuning.NeutralZoneReact * Disc(Agents[across].R.Awareness);
                if (PreRng.NextFloat() < react)
                {
                    // the man across flinches: he is the one who moved first, the defender drew him in
                    CallFoul(PenaltyType.NeutralZoneInfraction, d, defTeam, "NeutralZoneInfraction");
                    return;
                }
                if (bestDx < Agents[across].Body.Radius + def.Body.Radius && PreRng.NextFloat() < PreSnapTuning.EncroachContact)
                {
                    CallFoul(PenaltyType.Encroachment, d, defTeam, "Encroachment");
                    return;
                }
            }
            CallFoul(PenaltyType.Offside, d, defTeam, "Offside");
        }

        void CallFoul(PenaltyType type, int agent, Team by, string note)
        {
            var call = new PenaltyCall(type, by, 0f, Agents[agent].PlayerId);
            LogEvent(SimEventType.Penalty, agent, -1, TackleType.None, (float)type, note);
            PreSnapFoul = type;
            FinishPreSnap(PlayResult.PenaltyOnly(call));
        }

        /// <summary>The jump / shift walk / second motion this tick: the velocity the pre-snap body should aim for.</summary>
        Vec2 RulesWant(int i, Vec2 want)
        {
            if (_hasWalk[i])
            {
                Vec2 to = _walkTo[i] - Agents[i].Pos;
                float d = to.Length;
                if (d < 0.12f) { _hasWalk[i] = false; return Vec2.Zero; }
                return to * (_walkSpeed / d);
            }
            if (i == _motion2 && _motion2Dir != 0)
            {
                float x = Agents[i].Pos.X - BallX0;
                if (_motion2Dir > 0 && x >= _motionLimitRight || _motion2Dir < 0 && x <= _motionLimitLeft) return Vec2.Zero;
                return new Vec2(_motion2Dir * 4.5f, 0f);
            }
            if (i == MotionSlot && _driftFrom >= 0f && PreSnapTime >= _driftFrom) return new Vec2(want.X, _driftSpeed);
            // S1 fix: a motion man that is not a foul comes to rest 0.9 s before the snap (the body turns back to face the line); moving at the snap stays legal but nobody is snapped half turned
            if (i == MotionSlot && _countPlanned && _driftFrom < 0f && _motion2 < 0 && PreSnapTime >= _snapAt - 0.9f) return Vec2.Zero;
            return want;
        }

        /// <summary>A re-layout (audible, flip) puts everybody on his spot: pending shift walks, second motion and the early turn upfield are gone.</summary>
        void ResetPreSnapMoves()
        {
            for (int i = 0; i < 22; i++) { _hasWalk[i] = false; _joined[i] = false; }
            _motion2 = -1; _motion2Dir = 0; _driftFrom = -1f; _shiftPlanned = false;
        }

        // ======================================================================================================== lineup
        /// <summary>A late substitute or a receiver who lines up wrong: a player of the line stands a yard off it (causal, at the lineup; the legality check at the snap decides whether it is a foul).</summary>
        void ApplyLineupMistakes()
        {
            if (Options == null || !Options.Penalties || Kind != PlayKind.Scrimmage) return;
            for (int i = 1; i < 11; i++)
            {
                SimAgent a = Agents[i];
                if (!PreSnapRules.IsOnLine(a.Pos.Y - LosY, Rules) || a.PlayPos == Position.QB) continue;
                float p = PreSnapTuning.LineupMistake * Disc(a.R.Awareness) * (PositionGroup.IsOffensiveLine(a.PlayPos) ? 0.5f : 1f);
                float u = PreRng.NextFloat(), depth = 0.95f + 0.3f * PreRng.NextFloat();
                if (u >= p) continue;
                Vec2 np = new Vec2(a.Pos.X, LosY - depth);
                a.Body.Pos = np; a.StartPos = np; a.ClearHistory(np);
                PreSnapStances.Refresh(this);
            }
        }

        // ======================================================================================================== the snap
        /// <summary>The alignment of the offense as the rules see it (the 11 players, relative to the ball).</summary>
        public AlignReport OffenseAlignment()
        {
            var al = new List<Aligned>(11);
            for (int i = 0; i < 11; i++) al.Add(new Aligned(i, Agents[i].PlayPos, Agents[i].Pos.X - BallX0, Agents[i].Pos.Y - LosY));
            return PreSnapRules.Analyze(al, Rules);
        }

        /// <summary>The movement of the offense at this instant (who moves, who is still after a shift).</summary>
        public MotionVerdict OffenseMovement()
        {
            var ms = new List<MoveState>(11);
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = Agents[i];
                ms.Add(new MoveState
                {
                    Id = i, Speed = a.Speed, ForwardSpeed = a.Vel.Y, SettledFor = PreSnapTime - _lastMoving[i], JoinedShift = _joined[i],
                    OnLine = PreSnapRules.IsOnLine(a.Pos.Y - LosY, Rules)
                });
            }
            return PreSnapRules.CheckMovement(ms, Rules);
        }

        /// <summary>The fouls the officials see at the snap, from the actual positions and movement. True = the snap is dead.</summary>
        bool EvaluateSnap()
        {
            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Kneel || Kind == PlayKind.Spike || IsExtraPoint) return false;
            // offside by alignment: a defender whose front is in the neutral zone at the snap (a user-controlled lineman who creeps, a body pushed there)
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = Agents[i];
                if (d.Assign is ReturnerAssignment) continue;
                if (PreSnapRules.InNeutralZone(d.Pos.Y - LosY, d.Body.Radius, Rules) && d.Pos.Y - LosY > -2f) { CallFoul(PenaltyType.Offside, i, State.Possession.Opponent(), "Offside"); return true; }
            }
            if (Kind != PlayKind.Scrimmage) return false;
            AlignReport rep = OffenseAlignment();
            if (!rep.Legal)
            {
                int cg = rep.Culprit >= 0 ? rep.Culprit : 10;
                CallFoul(PenaltyType.IllegalFormation, cg, State.Possession, "IllegalFormation");
                return true;
            }
            MotionVerdict mv = _atPlanned ?? OffenseMovement();
            if (mv.Issue != MotionIssue.None)
            {
                PenaltyType t = mv.Issue == MotionIssue.IllegalShift ? PenaltyType.IllegalShift : PenaltyType.IllegalMotion;
                CallFoul(t, mv.Culprit >= 0 ? mv.Culprit : 1, State.Possession, t.ToString());
                return true;
            }
            return false;
        }
    }
}
