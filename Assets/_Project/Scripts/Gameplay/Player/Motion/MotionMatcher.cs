using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 MOTION: locomotion motion matching (replaces the v0.9.0 <see cref="GaitPlayer"/>; <c>-pixLocoLegacy</c> brings the old one back for A/B).
    /// The SIMULATION is authoritative: it hands in its velocity and planned path (+0.33 / +0.66 / +1.0 s); the matcher searches the feature database (clean Mixamo locomotion takes) every
    /// <see cref="SearchInterval"/> seconds for the frame whose feet, foot velocities, root velocity and future trajectory fit, plays that frame (one clip at full weight, no cross-fade between gaits),
    /// hides every jump with inertialization, scales the stride to the simulated speed (rate x stride warp, both small because the search only picks frames within about 15 percent of the speed),
    /// warps the legs toward the travel direction (orientation warp) and tells the foot planter which foot is on the ground (contact tags of the database).
    /// Actions (throw, catch, tackle, block, hand-off, kick) are not locomotion: <see cref="Suspend"/> gives the body to an action and reports the foot phase it left in <see cref="Exit"/>;
    /// <see cref="Resume"/> takes it back (a masked search on the live pose and the planned path).
    /// </summary>
    public sealed class MotionMatcher
    {
        public static bool Wanted = true;
        public const float SearchInterval = 0.10f, MinDwell = 0.16f, StayFactor = 0.78f;
        /// <summary>Search weights beyond the feature weights: the stride warp must stay within +-15 percent of the speed of the matched frame.</summary>
        public const float WarpLimit = 0.15f, WarpPenalty = 6f;

        /// <summary>Foot state at the moment control leaves the matcher (or is asked for): the action picks its entry frame from it.</summary>
        public struct FootPhase
        {
            public bool Valid, LeftPlanted, RightPlanted;
            /// <summary>0..1 through the stride (left plant = 0), -1 when not in a cycle.</summary>
            public float Phase;
            public float Speed, Angle;
            public string Clip; public float ClipTime;
        }

        readonly MotionDb _db;
        PlayableGraph _graph; AnimationMixerPlayable _mix; AnimationLayerMixerPlayable _parent; int _parentInput;
        AnimationClipPlayable[] _cp; int _active = -1;
        bool _attached;
        static AvatarMask _lower, _full;

        // ---- state
        int _clip = -1; float _t; int _row = -1;
        float _sinceSwitch = 9f, _sinceSearch = 9f;
        bool _switched, _ended;
        bool _suspended;
        bool _carryMask;
        float _vSmooth; bool _vHave;
        readonly float[] _q = new float[MotionDb.Dim], _raw = new float[MotionDb.Dim];
        Vector2 _prevDirLocal;

        // ---- outputs (read by the view)
        /// <summary>1 = the matcher owns the legs (a layer over the animator tree).</summary>
        public float Layer { get; private set; }
        public float Warp { get; private set; } = 1f;
        public float Rate { get; private set; } = 1f;
        /// <summary>v0.12.0 NATURAL: this player's own cadence factor for the loops (set by the view each frame, 1 = the database's).</summary>
        public float StyleRate = 1f;
        public bool LeftContact { get; private set; }
        public bool RightContact { get; private set; }
        /// <summary>Orientation warp: degrees the legs are turned toward the travel direction (the upper body keeps the facing).</summary>
        public float OrientationWarp { get; private set; }
        /// <summary>v0.11.0: athletic stance of a back-pedal / shuffle take (0..1) and the forward lean of the trunk in degrees; the Mixamo strafes and back-runs stand upright, a defender in coverage sits low with the chest over the knees.</summary>
        public float PostureW { get; private set; }
        public float PostureLean { get; private set; }
        /// <summary>The share of the posture that lowers the pelvis (back-pedal only: a lowered pelvis over side-stepping feet makes them slide, the shuffle gets the trunk lean alone).</summary>
        public float PostureDropW { get; private set; }
        public const float PostureDrop = 0.035f;
        public static bool NoPosture;
        /// <summary>QA (-pixMmNoSettle): a stop take plays to its end even when the body is already at rest.</summary>
        public static bool SettleStops = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixMmNoSettle") < 0;
        /// <summary>Largest playback rate of a take that shares by cadenceK (1.38 on a 3.5 steps/s run = 4.9 steps/s).</summary>
        public static float RateMax = 1.36f;
        /// <summary>QA (-pixMmNoTrans): the start / stop / cut takes of the database are never searched (A/B against loops only).</summary>
        public static bool NoTransitions;
        /// <summary>How many source frames ahead of the playing frame the contact tag is read (the foot is stopped on the frame it lands).</summary>
        public static float ContactLead = 1.5f;
        /// <summary>The feet of the playing take point along the pelvis, not along the way the body travels (strafes, back-pedals): more than 45 degrees between the two.</summary>
        public bool BackTravel { get { return _clip >= 0 && Mathf.Abs(_db.clips[_clip].angle) > 100f; } }
        public bool FeetOffTravel { get { return _clip >= 0 && Mathf.Abs(_db.clips[_clip].angle) > 45f; } }
        public string ClipName { get { return _clip >= 0 ? _db.clips[_clip].name : ""; } }
        /// <summary>How far the pose of the playing take must be lowered so its planted sole is on the turf (m, reference body; times the leg scale of the player).</summary>
        public bool LegacyConv { get { return _clip >= 0 && _db.clips[_clip].legacyConv; } }
        public float Floor { get { return _clip >= 0 ? _db.clips[_clip].floorY : 0f; } }
        public float ClipTime { get { return _t; } }
        public int Row { get { return _row; } }
        public int Switches { get; private set; }
        public float LastCost { get; private set; }
        public bool Ready { get { return _attached && _graph.IsValid(); } }
        public bool Suspended { get { return _suspended; } }
        public FootPhase Exit { get; private set; }
        public Inertializer Inertial;
        public MotionDb Db { get { return _db; } }
        public int SizeKey;
        /// <summary>QA: no orientation warp (-pixMmNoOrientWarp).</summary>
        public bool NoOrientWarp;
        /// <summary>QA (calibration): play only this database clip.</summary>
        public int ForceClip = -1;

        public MotionMatcher(MotionDb db) { _db = db; _cp = new AnimationClipPlayable[db.clips.Count]; }

        public static MotionMatcher TryCreate()
        {
            if (!Wanted) return null;
            var db = MotionDb.Load();
            return db != null && db.Rows > 0 ? new MotionMatcher(db) : null;
        }

        static AvatarMask Mask(bool lower)
        {
            if (lower ? _lower != null : _full != null) return lower ? _lower : _full;
            var m = new AvatarMask { name = lower ? "PIX_LowerBody" : "PIX_FullBody" };
            foreach (AvatarMaskBodyPart p in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (p == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = !lower || p == AvatarMaskBodyPart.Root || p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.LeftLeg || p == AvatarMaskBodyPart.RightLeg;
                m.SetHumanoidBodyPartActive(p, on);
            }
            if (lower) _lower = m; else _full = m;
            return m;
        }

        // ------------------------------------------------------------------------------------------ graph
        public void Attach(PlayableGraph graph, AnimationLayerMixerPlayable parent, int input)
        {
            _graph = graph; _parent = parent; _parentInput = input;
            _mix = AnimationMixerPlayable.Create(graph, Mathf.Max(1, _db.clips.Count));
            graph.Connect(_mix, 0, parent, input);
            parent.SetInputWeight(input, 0f);
            _attached = true; Layer = 0f; _active = -1; _clip = -1; _row = -1;
            for (int i = 0; i < _cp.Length; i++) _cp[i] = default(AnimationClipPlayable);
            _carryMask = false;
        }

        public void Detach() { _attached = false; }

        AnimationClipPlayable PlayableOf(int c)
        {
            if (!_cp[c].IsValid())
            {
                var p = AnimationClipPlayable.Create(_graph, _db.clips[c].clip);
                p.SetApplyFootIK(false); p.SetSpeed(0.0);
                _graph.Connect(p, 0, _mix, c);
                _mix.SetInputWeight(c, 0f);
                _cp[c] = p;
            }
            return _cp[c];
        }

        void SetActive(int c)
        {
            if (_active == c) return;
            if (_active >= 0 && _cp[_active].IsValid()) _mix.SetInputWeight(_active, 0f);
            if (c >= 0) { PlayableOf(c); _mix.SetInputWeight(c, 1f); }
            _active = c;
        }

        // ------------------------------------------------------------------------------------------ hand-off API (CHOREO)
        /// <summary>An action takes the body. The legs are released at once (inertialization hides the change of pose source) and the foot phase is kept in <see cref="Exit"/>.</summary>
        public void Suspend()
        {
            if (_suspended) return;
            Exit = Phase();
            _suspended = true;
            Park();
        }

        /// <summary>The action is over: the next Update searches from the live pose and the planned path.</summary>
        public void Resume() { _suspended = false; _row = -1; _clip = -1; _vHave = false; }

        FootPhase Phase()
        {
            var fp = new FootPhase { Valid = _row >= 0 && Layer > 0.5f };
            if (!fp.Valid) { fp.Phase = -1f; return fp; }
            fp.LeftPlanted = LeftContact; fp.RightPlanted = RightContact; fp.Clip = ClipName; fp.ClipTime = _t;
            var c = _db.clips[_clip];
            fp.Phase = c.loop && c.count > 1 ? Mathf.Repeat(_t * MotionDb.Fps / c.count, 1f) : -1f;
            fp.Speed = c.speed; fp.Angle = c.angle;
            return fp;
        }

        void Park()
        {
            _row = -1; _clip = -1;
            if (_attached) { SetActive(-1); _parent.SetInputWeight(_parentInput, 0f); }
            if (Layer > 0f) _switched = true;
            Layer = 0f; Warp = 1f; Rate = 1f; LeftContact = RightContact = false; OrientationWarp = 0f; PostureW = 0f;
        }

        /// <summary>The pose source under the matcher changed (a clip layer gave the legs back): the inertializer takes the jump.</summary>
        public void SourceChanged() { _switched = true; }

        public void Reset() { Park(); _suspended = false; _vHave = false; if (Inertial != null) Inertial.Reset(); OrientationWarp = 0f; _switched = false; }

        // ------------------------------------------------------------------------------------------ per frame
        public struct Query
        {
            /// <summary>Simulated velocity in the body frame (x right, z forward, m/s) and the planned positions at +0.33 / +0.66 / +1.0 s relative to the body (metres, same frame).</summary>
            public Vector2 Vel, P1, P2, P3;
            /// <summary>Planned facing change at the three times (degrees, + = to the right).</summary>
            public float Yaw1, Yaw2, Yaw3;
            public float LegScale;
            public bool Enable, Carry;
            /// <summary>Optional live foot features for the first search after a resume (normalised, weighted, dims 0..9); null = masked search.</summary>
            public float[] LiveFeet;
        }

        /// <summary>One frame. Returns true while the matcher owns the legs.</summary>
        public bool Update(float dt, in Query qy)
        {
            if (!Ready || dt <= 1e-5f) return false;
            float speed = qy.Vel.magnitude;
            if (!_vHave) { _vSmooth = speed; _vHave = true; }
            _vSmooth = Mathf.Lerp(_vSmooth, speed, 1f - Mathf.Exp(-dt / 0.035f));
            float planSpeed = (qy.P3 - qy.P1).magnitude / 0.67f;
            bool stopHold = _row >= 0 && (_db.clips[_clip].kind & (int)MotionDb.Kind.Stop) != 0 && !_ended;
            // the matched takes start at a jog: below that the animator tree walks (pre-snap shifts, walking after the whistle); a standing start towards a run has a fast planned path and takes the start take
            bool fast = _vSmooth > 0.85f || (_vSmooth > 0.6f && planSpeed > 1.3f);
            bool keep = Layer > 0.5f && _vSmooth > 0.7f;
            bool want = qy.Enable && !_suspended && (fast || keep || stopHold);
            if (!want)
            {
                if (Layer > 0f || _row >= 0) Park();
                return false;
            }
            float ls = Mathf.Max(0.5f, qy.LegScale);
            bool fresh = _row < 0;
            if (fresh) { _switched = true; Layer = 1f; _parent.SetInputWeight(_parentInput, 1f); }
            if (_carryMask != qy.Carry) { _parent.SetLayerMaskFromAvatarMask((uint)_parentInput, Mask(qy.Carry)); _carryMask = qy.Carry; }
            // ---- advance the playing frame
            if (_row >= 0)
            {
                var c = _db.clips[_clip];
                _t += dt * Rate;
                float last = (c.count - 1) / MotionDb.Fps, loopLen = c.count / MotionDb.Fps;
                if (c.loop) { if (_t >= loopLen) _t -= loopLen * Mathf.Floor(_t / loopLen); }
                else if (_t >= last) { _t = last; _ended = true; }
                _row = c.first + Mathf.Clamp(Mathf.FloorToInt(_t * MotionDb.Fps + 0.5f), 0, c.count - 1);
                // v0.12.0: the simulation brakes harder than a stop take: with the body at rest the rest of the take would slide its feet at the root speed it still carries - the take ends here and the standing animator takes over (inertialized)
                if (!c.loop && (c.kind & (int)MotionDb.Kind.Stop) != 0 && !_ended && SettleStops && _vSmooth < 0.25f && Mathf.Sqrt(_db.vel[_row * 2] * _db.vel[_row * 2] + _db.vel[_row * 2 + 1] * _db.vel[_row * 2 + 1]) * ls > 0.45f) _ended = true;
            }
            _sinceSwitch += dt; _sinceSearch += dt;
            // ---- search
            if (fresh || _ended || _sinceSearch >= SearchInterval)
            {
                _sinceSearch = 0f;
                Search(qy, ls, fresh);
            }
            if (_row < 0) { Park(); return false; }
            // ---- play the frame
            var cur = _db.clips[_clip];
            if (SelLog != null) { float sc; _selSeconds.TryGetValue(cur.name, out sc); _selSeconds[cur.name] = sc + dt; }
            SetActive(_clip);
            var pl = _cp[_clip];
            pl.SetTime(Mathf.Clamp(_t, 0f, cur.clip.length));
            // ---- stride: rate x warp, both from the speed of the matched frame
            float gvx = _db.vel[_row * 2] * ls, gvz = _db.vel[_row * 2 + 1] * ls, gv = Mathf.Sqrt(gvx * gvx + gvz * gvz);
            float r = 1f; bool shot = !cur.loop;
            if (shot) { if (gv > 0.35f && _vSmooth > 0.2f) r = Mathf.Clamp(_vSmooth / gv, 0.45f, 2.2f); }      // a start / stop / cut changes speed through the clip: it plays by distance (time runs as fast as the body covers the ground the take covers), the feet stay on the turf
            else if (gv > 0.9f && _vSmooth > 0.5f) r = Mathf.Clamp(_vSmooth / gv, 0.5f, 2f);
            float rate = RateFor(r, cur.cadenceK, shot);      // cadence and stride share the difference (a faster runner takes quicker AND longer steps); a long-stride take takes more of it as cadence
            if (!shot && Mathf.Abs(StyleRate - 1f) > 1e-4f) rate = Mathf.Clamp(rate * StyleRate, 0.85f, RateMax * 1.08f);      // v0.12.0 NATURAL: his own cadence (the warp below takes the difference back: same ground speed, his own step rate and stride)
            Rate = Mathf.Lerp(Rate, rate, 1f - Mathf.Exp(-dt / 0.06f));
            float warpT = Mathf.Clamp(r / Mathf.Max(0.5f, Rate) * TrimOf(_clip), 0.5f, 1.6f);
            Warp = fresh ? warpT : Mathf.Lerp(Warp, warpT, 1f - Mathf.Exp(-dt / 0.045f));      // a jump to another take must not jerk the planted feet
            // ---- orientation warp: turn the legs from the travel direction of the clip to the simulated travel direction
            float ow = 0f;
            if (gv > 1.2f && _vSmooth > 1.2f)
            {
                float a = Mathf.Atan2(qy.Vel.x, qy.Vel.y) * Mathf.Rad2Deg, ac = Mathf.Atan2(gvx, gvz) * Mathf.Rad2Deg;
                ow = Mathf.Clamp(Mathf.DeltaAngle(ac, a), -35f, 35f);
            }
            OrientationWarp = Mathf.Lerp(OrientationWarp, ow, 1f - Mathf.Exp(-dt / 0.07f));
            {
                float ab = Mathf.Abs(cur.angle); int tk = cur.kind;
                float tw = (ab > 55f && (tk & MotionDb.TransitionKinds) == 0 && !NoPosture) ? Mathf.SmoothStep(0f, 1f, (ab - 55f) / 25f) * Mathf.Clamp01((_vSmooth - 0.8f) / 0.6f) : 0f;
                PostureW = Mathf.Lerp(PostureW, tw, 1f - Mathf.Exp(-dt / 0.15f));
                PostureLean = Mathf.Lerp(8f, 11f, Mathf.Clamp01((ab - 90f) / 60f));
                PostureDropW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((ab - 105f) / 25f));
            }
            // ---- foot contacts (one source frame ahead: the foot is stopped on the frame it lands)
            int fi = Mathf.FloorToInt((_t + ContactLead / MotionDb.Fps) * MotionDb.Fps); var cc = _db.clips[_clip];
            int cr = cc.first + (cc.loop ? fi % cc.count : Mathf.Clamp(fi, 0, cc.count - 1));
            byte cb = _db.contact[cr];
            LeftContact = (cb & 1) != 0; RightContact = (cb & 2) != 0;
            return true;
        }

        /// <summary>Playback rate for a speed ratio <paramref name="r"/> (wanted ground speed / speed of the matched frame): a one-shot plays by distance; a loop takes the ratio to the power <paramref name="cadenceK"/> as cadence (0.5: both equally, the v0.11 split), the stride warp takes the rest.</summary>
        public static float RateFor(float r, float cadenceK, bool shot)
        {
            if (shot) return Mathf.Clamp(r, 0.6f, 1.8f);
            if (cadenceK > 0.51f) return Mathf.Clamp(Mathf.Pow(r, cadenceK), 0.9f, RateMax);
            return Mathf.Clamp(Mathf.Sqrt(r), 0.9f, 1.22f);
        }

        void BuildQuery(in Query qy, float ls, bool fresh)
        {
            float inv = 1f / ls;
            _raw[10] = qy.Vel.x * inv; _raw[11] = qy.Vel.y * inv;
            _raw[12] = qy.P1.x * inv; _raw[13] = qy.P1.y * inv;
            _raw[14] = qy.P2.x * inv; _raw[15] = qy.P2.y * inv;
            _raw[16] = qy.P3.x * inv; _raw[17] = qy.P3.y * inv;
            _raw[18] = qy.Yaw1; _raw[19] = qy.Yaw2; _raw[20] = qy.Yaw3;
            for (int i = 10; i < MotionDb.Dim; i++) _q[i] = (_raw[i] - _db.mean[i]) * _db.scale[i];
            if (!fresh && _row >= 0)
            {
                int o = _row * MotionDb.Dim;
                for (int i = 0; i < 10; i++) _q[i] = _db.feat[o + i];
            }
            else if (qy.LiveFeet != null) for (int i = 0; i < 10; i++) _q[i] = qy.LiveFeet[i];
        }

        void Search(in Query qy, float ls, bool fresh)
        {
            BuildQuery(qy, ls, fresh);
            bool masked = fresh && qy.LiveFeet == null;
            // v0.11.0: explicit transitions are only candidates when the simulation says so: a stop when the planned path brakes to a standstill, a start when the body stands and the path runs away, a cut / turn when the path turns.
            float spd = qy.Vel.magnitude, p3 = qy.P3.magnitude;
            int forbid = NoTransitions ? MotionDb.TransitionKinds : MotionDb.TransitionKinds;
            if (!NoTransitions)
            {
                if (spd > 2.0f && p3 < 0.55f * spd) forbid &= ~(int)MotionDb.Kind.Stop;
                if (spd < 1.5f && p3 > 1.8f) forbid &= ~(int)MotionDb.Kind.Start;
                if (spd > 2.0f && (Mathf.Abs(qy.Yaw1) > 25f || Mathf.Abs(qy.Yaw2) > 30f || Mathf.Abs(qy.Yaw3) > 45f)) forbid &= ~((int)MotionDb.Kind.Turn | (int)MotionDb.Kind.Cut);
            }
            float best; int bi;
            float speedNow = qy.Vel.magnitude / ls;
            int cont = _row >= 0 && !_ended ? _db.Next(_row) : -1;
            float turnPen = Mathf.Abs(qy.Yaw3) < 15f && Mathf.Abs(qy.Yaw2) < 15f ? 2.5f : 0f;
            bi = _db.Search(_q, speedNow, WarpLimit, WarpPenalty, masked ? 10 : 0, forbid, out best, ForceClip, turnPen);
            if (bi < 0) return;
            LastCost = best;
            if (cont >= 0)
            {
                float cc = _db.Distance(_q, cont) + _db.SpeedPenalty(cont, speedNow, WarpLimit, WarpPenalty);
                if (turnPen > 0f && (_db.clips[_db.clipOf[cont]].kind & (int)MotionDb.Kind.Turn) != 0) cc += turnPen;
                bool sameClip = _db.clipOf[bi] == _clip && Mathf.Abs(bi - cont) <= 1;
                if (sameClip || best >= cc * StayFactor || _sinceSwitch < MinDwell && best >= cc * 0.45f) return;     // keep playing
            }
            Jump(bi);
        }

        void Jump(int row)
        {
            int c = _db.clipOf[row];
            bool newClip = c != _clip;
            _clip = c; _row = row; _t = (row - _db.clips[c].first) / MotionDb.Fps; _ended = false;
            _sinceSwitch = 0f; Switches++; _switched = true;
            if (newClip && SelLog != null) Selected(_db.clips[c].name);
        }

        // ------------------------------------------------------------------------------------------ QA: which takes the matcher chooses
        /// <summary>v0.12.0 (-pixMmSelLog path.csv): how often each take of the database was entered (a jump to another take) and for how many seconds it played, over all matchers of the run. Written every 400 entries and when the player quits.</summary>
        public static string SelLog = ArgOf("-pixMmSelLog");
        static string ArgOf(string name) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        static readonly System.Collections.Generic.Dictionary<string, int> _selEnter = new System.Collections.Generic.Dictionary<string, int>();
        static readonly System.Collections.Generic.Dictionary<string, float> _selSeconds = new System.Collections.Generic.Dictionary<string, float>();
        static int _selTotal; static bool _selHooked;

        static void Selected(string name)
        {
            int n; _selEnter.TryGetValue(name, out n); _selEnter[name] = n + 1;
            if (!_selHooked) { _selHooked = true; Application.quitting += FlushSelections; }
            if (++_selTotal % 400 == 0) FlushSelections();
        }

        public static void FlushSelections()
        {
            if (SelLog == null) return;
            try
            {
                var sb = new System.Text.StringBuilder("clip,entered,seconds\n");
                foreach (var kv in _selEnter) { float s; _selSeconds.TryGetValue(kv.Key, out s); sb.Append(kv.Key).Append(',').Append(kv.Value).Append(',').Append(s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n'); }
                System.IO.File.WriteAllText(SelLog, sb.ToString());
            }
            catch (System.Exception) { }
        }



        // ------------------------------------------------------------------------------------------ closed loop on the planted foot
        // The database speed of a take is an estimate (the feet of the retargeted body sweep at a speed the root motion does not tell). The planted foot tells the truth: when the body moves at v and the animated foot
        // slides forward at u over the middle of its stance (the touch-down and lift-off frames skid), the stride warp was too small by u / v. Each stance nudges the trim of the take that plays (shared by every player of the same body size).
        public static readonly System.Collections.Generic.Dictionary<string, float[]> Trim = new System.Collections.Generic.Dictionary<string, float[]>();
        public static bool LearnEnabled = true;
        public int LearnEvents;
        readonly float[] _slipL = new float[48], _slipR = new float[48]; int _nL, _nR; int _clipL = -1, _clipR = -1;
        float TrimOf(int clip) { float[] f; return clip >= 0 && Trim.TryGetValue(_db.clips[clip].name, out f) ? f[Mathf.Clamp(SizeKey, 0, 2)] : 1f; }

        /// <summary>Called by the view each frame after the planted-foot solver ran (it reads that frame's slip).</summary>
        public void Learn(FootPlant plant, float v, float dt)
        {
            if (!LearnEnabled || plant == null || Layer < 0.9f || v < 1.5f || _row < 0) { _nL = _nR = 0; return; }
            Collect(plant.PlantedLeft, plant.SlipLeft, _slipL, ref _nL, ref _clipL, v);
            Collect(plant.PlantedRight, plant.SlipRight, _slipR, ref _nR, ref _clipR, v);
        }

        void Collect(bool planted, float slip, float[] buf, ref int n, ref int clip, float v)
        {
            if (planted)
            {
                if (n == 0) clip = _clip;
                if (clip != _clip) { n = 0; return; }          // the take changed during the stance: nothing learned from it
                if (n < buf.Length) buf[n++] = slip;
                return;
            }
            if (n >= 5 && clip == _clip)
            {
                int a = (int)(n * 0.3f), b = (int)(n * 0.7f); float s = 0f; int c = 0;
                for (int i = a; i < b; i++) { s += buf[i]; c++; }
                if (c > 0) Nudge(s / c, v);
            }
            n = 0;
        }

        void Nudge(float slip, float v)
        {
            string key = _db.clips[_clip].name; float[] arr; if (!Trim.TryGetValue(key, out arr)) Trim[key] = arr = new float[] { 1f, 1f, 1f };
            int k = Mathf.Clamp(SizeKey, 0, 2);
            float r = Mathf.Clamp(slip / v, -0.35f, 0.35f);
            arr[k] = Mathf.Clamp(arr[k] * (1f + 0.15f * r), 0.6f, 1.8f);
            LearnEvents++;
        }

        /// <summary>LateUpdate, first thing after the animation: the facing convention of the takes, inertialization, then the orientation warp of the legs.</summary>
        /// <param name="convYaw">Degrees the whole pose is turned about the avatar root's vertical axis so a take authored facing the avatar's own forward faces the way the match bodies (and every older take) face: the body model is
        /// turned by <see cref="PlayerAvatar.ModelYaw"/> and the older takes carry that turn in their hips; the Mixamo takes do not.</param>
        public void Late(float dt, Transform hips, Transform spine, Transform pivot, float convYaw, float drop, Vector3 lateral = default(Vector3))
        {
            if (Mathf.Abs(convYaw) > 0.01f) lateral = Quaternion.AngleAxis(convYaw, Vector3.up) * lateral;      // the pose is about to be turned back by the facing convention: the lateral axis read before it is turned with it
            if (hips != null && pivot != null && Mathf.Abs(convYaw) > 0.01f)
            {
                var r = Quaternion.AngleAxis(convYaw, Vector3.up);
                hips.position = pivot.position + r * (hips.position - pivot.position);
                hips.rotation = r * hips.rotation;
            }
            if (hips != null && Mathf.Abs(drop) > 1e-4f) hips.position -= Vector3.up * drop;
            if (Inertial != null && Inertial.Ok) { Inertial.Apply(dt, _switched); _switched = false; }
            if (Layer > 0.5f && !NoOrientWarp && Mathf.Abs(OrientationWarp) > 0.3f && hips != null)
            {
                // turn the pelvis (and the legs with it) about the vertical; the spine, chest and head turn back so the upper body keeps its heading
                var q = Quaternion.AngleAxis(OrientationWarp, Vector3.up);
                hips.rotation = q * hips.rotation;
                if (spine != null) spine.rotation = Quaternion.Inverse(q) * spine.rotation;
            }
            if (Layer > 0.5f && PostureW > 0.01f && spine != null && lateral.sqrMagnitude > 0.002f) spine.rotation = Quaternion.AngleAxis(PostureLean * PostureW, lateral.normalized) * spine.rotation;      // trunk forward about the pelvis lateral axis (positive about +right tips the up vector toward the front)
        }
    }
}
