using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-003): the gait synthesizer that replaces the 2D blend tree for running. It picks the mocap cycles of <see cref="LocoCycleLibrary"/> whose MEASURED native ground speed brackets the simulated
    /// speed (only the two neighbours of each travel-direction ray are ever used), plays a single cycle at 0.88 - 1.12 of its rate whenever that suffices (a short cross-fade only in the gap between two cycles),
    /// and keeps every blended cycle on ONE normalised gait phase whose left-plant / left-lift / right-plant / right-lift markers are blended too, so a cross-fade never mixes mismatched feet.
    /// Whatever speed difference the rate band cannot absorb is returned as <see cref="Warp"/> for the stride warper (FootPlant). Pure speed-in, pose-out: nothing here assumes a game pace.
    /// Round 2 (LOCO-010 ..): back-pedal rays (travel angle +-180), free arms for takes that exist only as ball-carrier takes (a donor cycle on the same gait phase drives the arms through an arm mask),
    /// a cadence boost for sprint takes (higher step rate, shorter stride through the stride warp), the contact hint leads the touchdown by one frame.
    /// </summary>
    public sealed class GaitPlayer
    {
        public const float RateBand = 0.12f, ExtremeBand = 0.45f;
        /// <summary>Gap (degrees) between two rays beyond which they are not blended with each other (the animator tree owns the side directions).</summary>
        const float RayGap = 62f;

        sealed class Ray { public float Angle, Eff; public List<LocoCycleLibrary.Cycle> Ladder = new List<LocoCycleLibrary.Cycle>(); }
        sealed class Slot { public LocoCycleLibrary.Cycle C; public AnimationClipPlayable Cp; public int Input; public float W, Target; public float Lift, RPlant, RLift, Boost = 1f; }

        readonly LocoCycleLibrary _lib;
        readonly List<Ray> _freeRays = new List<Ray>(), _carryRays = new List<Ray>();
        readonly Dictionary<LocoCycleLibrary.Cycle, Slot> _slots = new Dictionary<LocoCycleLibrary.Cycle, Slot>(), _upSlots = new Dictionary<LocoCycleLibrary.Cycle, Slot>();
        readonly List<Slot> _list = new List<Slot>(), _upList = new List<Slot>();
        PlayableGraph _graph; AnimationMixerPlayable _mix, _mixUp; AnimationLayerMixerPlayable _parent; int _parentInput;
        bool _attached; int _nextInput, _nextUp;
        readonly float _seedPhase;
        static AvatarMask _armMask;

        // ---- outputs (read by the view / FootPlant after Update)
        /// <summary>Weight of the gait layer over the animator tree (0 = the tree owns the legs).</summary>
        public float Layer { get; private set; }
        /// <summary>Normalised gait phase 0..1: left foot plant = 0.</summary>
        public float Phase { get; private set; }
        /// <summary>Stride length factor the pose must still absorb (target speed over the playing cycles' speed after the rate band, divided by the cadence boost): 1 = none.</summary>
        public float Warp { get; private set; } = 1f;
        /// <summary>Playback rate of the cycles relative to native (diagnostic).</summary>
        public float Rate { get; private set; } = 1f;
        /// <summary>Cadence boost of the playing mixture (1 = native cadence).</summary>
        public float Boost { get; private set; } = 1f;
        /// <summary>Degrees the body must be turned from the simulated facing so the playing cycles travel along the simulated velocity.</summary>
        public float YawBias { get; private set; }
        public bool LeftContact { get; private set; }
        public bool RightContact { get; private set; }
        /// <summary>Contact markers of the blended cycles (phase of left lift-off, right plant, right lift-off).</summary>
        public Vector3 Knots { get; private set; } = new Vector3(0.3f, 0.5f, 0.8f);
        /// <summary>Native ground speed of the playing mixture (m/s, already scaled to the body).</summary>
        public float MixSpeed { get; private set; }
        /// <summary>Weight of the donor arms over the arms of the playing cycles (0 = the cycles' own arms).</summary>
        public float DonorArms { get; private set; }
        public string DebugCycles { get; private set; } = "";
        public bool Ready { get { return _attached && _graph.IsValid(); } }
        public int Count { get { return _list.Count; } }

        public GaitPlayer(LocoCycleLibrary lib, int seed)
        {
            _lib = lib;
            _seedPhase = ((uint)seed * 2654435761u >> 8 & 0xFFFFu) / 65535f;
            Phase = _seedPhase;
            ResolveDonors(lib);
            BuildRays(false, _freeRays); BuildRays(true, _carryRays);
        }

        public static bool DebugNoDonor;
        static void ResolveDonors(LocoCycleLibrary lib)
        {
            foreach (var c in lib.cycles)
            {
                if (c == null || string.IsNullOrEmpty(c.donor) || DebugNoDonor) { if (c != null) c.donorRef = null; continue; }
                c.donorRef = null;
                foreach (var d in lib.cycles) if (d != null && d.clip != null && d.name == c.donor && string.IsNullOrEmpty(d.donor)) { c.donorRef = d; break; }
            }
        }

        // ------------------------------------------------------------------------------------------ ray tables
        void BuildRays(bool carry, List<Ray> rays)
        {
            var all = new List<LocoCycleLibrary.Cycle>(_lib.cycles);
            all.RemoveAll(c => c == null || c.clip == null);
            all.Sort((a, b) => a.Angle.CompareTo(b.Angle));
            var clusters = new List<List<LocoCycleLibrary.Cycle>>();
            foreach (var c in all)
            {
                List<LocoCycleLibrary.Cycle> hit = null;
                float ca = Mathf.Abs(c.Angle) > 150f ? 180f : c.Angle;
                foreach (var cl in clusters) { float m = 0f; foreach (var x in cl) m += Mathf.Abs(x.Angle) > 150f ? 180f : x.Angle; m /= cl.Count; if (Mathf.Abs(Mathf.DeltaAngle(m, ca)) < 9f) { hit = cl; break; } }
                if (hit == null) { hit = new List<LocoCycleLibrary.Cycle>(); clusters.Add(hit); }
                hit.Add(c);
            }
            foreach (var cl in clusters)
            {
                var ray = new Ray(); float m = 0f;
                float minCarry = 99f; bool anyFreeArms = false;
                foreach (var x in cl) { if (x.carry) minCarry = Mathf.Min(minCarry, x.speed); if (!x.carry) anyFreeArms = true; }
                bool anyCarry = minCarry < 98f;
                foreach (var x in cl)
                {
                    if (!carry) { if (!x.carry) ray.Ladder.Add(x); }
                    else if (x.carry || (!anyCarry && string.IsNullOrEmpty(x.donor)) || (!string.IsNullOrEmpty(x.donor) ? false : x.speed < minCarry * 0.9f)) ray.Ladder.Add(x);
                }
                if (ray.Ladder.Count == 0) continue;
                foreach (var x in ray.Ladder) m += Mathf.Abs(x.Angle) > 150f ? 180f : x.Angle;
                ray.Angle = m / ray.Ladder.Count;
                ray.Eff = Mathf.Abs(ray.Angle) < 12f ? 0f : ray.Angle;      // forward cycles are turned by YawBias, not blended
                ray.Ladder.Sort((a, b) => a.speed.CompareTo(b.speed));
                rays.Add(ray);
                if (ray.Eff >= 150f) rays.Add(new Ray { Angle = -ray.Angle, Eff = -180f, Ladder = ray.Ladder });      // the back ray is reachable from both sides of +-180
                _ = anyFreeArms;
            }
            rays.Sort((a, b) => a.Eff.CompareTo(b.Eff));
        }

        // ------------------------------------------------------------------------------------------ graph
        static AvatarMask ArmMask()
        {
            if (_armMask != null) return _armMask;
            _armMask = new AvatarMask { name = "PIX_ArmsOnly" };
            foreach (AvatarMaskBodyPart p in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (p == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers;
                _armMask.SetHumanoidBodyPartActive(p, on);
            }
            return _armMask;
        }

        /// <summary>Hooks the gait mixers into the player's graph: input <paramref name="input"/> of <paramref name="parent"/> is the full-body cycle layer, input + 1 the donor-arm layer (an arm mask over it); both are override layers over the animator controller.</summary>
        public void Attach(PlayableGraph graph, AnimationLayerMixerPlayable parent, int input)
        {
            _graph = graph; _parent = parent; _parentInput = input;
            int n = Mathf.Max(4, _lib.cycles.Count);
            _mix = AnimationMixerPlayable.Create(graph, n);
            graph.Connect(_mix, 0, parent, input);
            parent.SetInputWeight(input, 0f);
            _mixUp = AnimationMixerPlayable.Create(graph, 4);
            graph.Connect(_mixUp, 0, parent, input + 1);
            parent.SetInputWeight(input + 1, 0f);
            parent.SetLayerMaskFromAvatarMask((uint)(input + 1), ArmMask());
            _slots.Clear(); _list.Clear(); _upSlots.Clear(); _upList.Clear(); _nextInput = 0; _nextUp = 0; _attached = true; Layer = 0f;
        }

        public void Detach() { _attached = false; _slots.Clear(); _list.Clear(); _upSlots.Clear(); _upList.Clear(); }

        static float BoostOf(LocoCycleLibrary.Cycle c)
        {
            // sprint takes have a native step rate of 3.5 - 3.8 per second; a runner at that speed takes 4.3 - 4.6: play them faster and shrink the stride by the same factor (stride warp), the planted foot still sweeps at the ground speed
            if (Mathf.Abs(c.Angle) > 60f || c.speed < 4.5f) return 1f;
            float stepsNative = 2f / Mathf.Max(0.1f, c.length);
            float target = Mathf.Lerp(3.4f, 4.6f, Mathf.InverseLerp(4.5f, 8f, c.speed));
            return Mathf.Clamp(target / stepsNative, 1f, 1.25f);
        }

        Slot GetSlot(LocoCycleLibrary.Cycle c)
        {
            Slot s;
            if (_slots.TryGetValue(c, out s)) return s;
            if (_nextInput >= _mix.GetInputCount()) return null;
            s = new Slot { C = c, Input = _nextInput++, Boost = BoostOf(c) };
            s.Cp = AnimationClipPlayable.Create(_graph, c.clip);
            s.Cp.SetApplyFootIK(false); s.Cp.SetSpeed(0.0); s.Cp.SetTime(c.tStart);
            _graph.Connect(s.Cp, 0, _mix, s.Input);
            _mix.SetInputWeight(s.Input, 0f);
            s.Lift = c.lLift; s.RPlant = c.rPlant; s.RLift = c.rLift;
            _slots[c] = s; _list.Add(s);
            return s;
        }

        Slot GetUpSlot(LocoCycleLibrary.Cycle c)
        {
            Slot s;
            if (_upSlots.TryGetValue(c, out s)) return s;
            if (_nextUp >= _mixUp.GetInputCount()) return null;
            s = new Slot { C = c, Input = _nextUp++ };
            s.Cp = AnimationClipPlayable.Create(_graph, c.clip);
            s.Cp.SetApplyFootIK(false); s.Cp.SetSpeed(0.0); s.Cp.SetTime(c.tStart);
            _graph.Connect(s.Cp, 0, _mixUp, s.Input);
            _mixUp.SetInputWeight(s.Input, 0f);
            s.Lift = c.lLift; s.RPlant = c.rPlant; s.RLift = c.rLift;
            _upSlots[c] = s; _upList.Add(s);
            return s;
        }

        // ------------------------------------------------------------------------------------------ selection
        struct Pick { public LocoCycleLibrary.Cycle C; public float W; }
        readonly Pick[] _picks = new Pick[8]; int _nPicks;
        bool _satHigh, _satLow;

        void AddPick(LocoCycleLibrary.Cycle c, float w)
        {
            if (w <= 1e-4f) return;
            for (int i = 0; i < _nPicks; i++) if (_picks[i].C == c) { _picks[i].W += w; return; }
            if (_nPicks < _picks.Length) _picks[_nPicks++] = new Pick { C = c, W = w };
        }

        /// <summary>Two neighbouring cycles of a ray around speed v (m/s): inside the +-12 percent rate band of a cycle only that cycle plays; the cross-fade covers the gap between the bands.</summary>
        void Bracket(List<LocoCycleLibrary.Cycle> ladder, float v, float ls, out LocoCycleLibrary.Cycle a, out LocoCycleLibrary.Cycle b, out float tb)
        {
            a = b = ladder[0]; tb = 0f;
            int n = ladder.Count;
            if (n == 1 || v <= Sp(ladder[0], ls)) return;
            if (v >= Sp(ladder[n - 1], ls)) { a = b = ladder[n - 1]; return; }
            int i = 0; while (i < n - 2 && v >= Sp(ladder[i + 1], ls)) i++;
            a = ladder[i]; b = ladder[i + 1];
            tb = GaitMath.PairWeight(Sp(a, ls), Sp(b, ls), v);
        }

        /// <summary>Direction coverage and ray weights for a travel angle (degrees from the facing, -180..180). Neighbouring rays closer than 62 degrees are blended with each other (a ray within a few degrees plays alone); a ray with no neighbour on a
        /// side fades out over 18 degrees and the animator tree takes over (the side directions). A ray whose ladder has no cycle anywhere near the speed does not take part.</summary>
        float PickRays(List<Ray> rays, float theta, float v, float ls)
        {
            int n = rays.Count; if (n == 0) return 0f;
            var rw = _rayW; rw.Clear();
            for (int i = 0; i < n; i++) rw.Add(0f);
            var ok = _rayOk; ok.Clear();
            for (int i = 0; i < n; i++)
            {
                var l = rays[i].Ladder; float lo = Sp(l[0], ls) * 0.5f, hi = Sp(l[l.Count - 1], ls) * 1.6f;
                ok.Add(v >= lo && v <= hi);
            }
            int below = -1, above = -1;
            for (int i = 0; i < n; i++) { if (!ok[i]) continue; if (rays[i].Eff <= theta) below = i; }
            for (int i = n - 1; i >= 0; i--) { if (!ok[i]) continue; if (rays[i].Eff >= theta) above = i; }
            float cov;
            if (below < 0 && above < 0) return 0f;
            if (below >= 0 && above >= 0 && below == above) { rw[below] = 1f; cov = 1f; }
            else if (below >= 0 && above >= 0 && rays[above].Eff - rays[below].Eff <= RayGap)
            {
                float t = Mathf.InverseLerp(rays[below].Eff, rays[above].Eff, theta);
                t = Mathf.Clamp01((t - 0.15f) / 0.7f); t = t * t * (3f - 2f * t);
                rw[below] = 1f - t; rw[above] = t; cov = 1f;
            }
            else
            {
                int near = below < 0 ? above : above < 0 ? below : (theta - rays[below].Eff <= rays[above].Eff - theta ? below : above);
                rw[near] = 1f; float d = Mathf.Abs(Mathf.DeltaAngle(rays[near].Eff, theta));
                cov = 1f - Mathf.Clamp01((d - 4f) / 18f);
            }
            _satHigh = _satLow = false;
            for (int i = 0; i < n; i++)
            {
                if (rw[i] <= 1e-4f) continue;
                LocoCycleLibrary.Cycle a, b; float tb; var lad = rays[i].Ladder;
                Bracket(lad, v, ls, out a, out b, out tb);
                AddPick(a, rw[i] * (1f - tb)); AddPick(b, rw[i] * tb);
                if (rw[i] > 0.4f && a == b)
                {
                    if (a == lad[lad.Count - 1] && v > Sp(a, ls) * (1f + RateBand)) _satHigh = true;
                    if (a == lad[0] && v < Sp(a, ls) * (1f - RateBand)) _satLow = true;
                }
            }
            return cov;
        }
        readonly List<float> _rayW = new List<float>(); readonly List<bool> _rayOk = new List<bool>();

        // ------------------------------------------------------------------------------------------ per frame
        float _vSm; bool _vHave;
        /// <summary>One frame. <paramref name="local"/> is the simulated velocity in the body frame (x right, z forward, m/s); <paramref name="legScale"/> scales the native speeds to this body; <paramref name="enable"/> = false parks the layer.</summary>
        public void Update(float dt, Vector2 local, bool carry, float legScale, bool enable)
        {
            if (!Ready || dt <= 1e-5f) return;
            float v = local.magnitude;
            if (!_vHave) { _vSm = v; _vHave = true; }
            _vSm = Mathf.Lerp(_vSm, v, 1f - Mathf.Exp(-dt / 0.035f));
            float theta = v > 0.25f ? Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg : 0f;
            _nPicks = 0; _satHigh = _satLow = false;
            float cov = 0f;
            if (enable && v > 0.2f) cov = PickRays(carry ? _carryRays : _freeRays, theta, _vSm, legScale);
            if (ForceCycle != null && enable) { _nPicks = 0; AddPick(ForceCycle, 1f); cov = 1f; _satHigh = _satLow = false; }
            float layerT = enable ? cov * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.95f, _vSm)) : 0f;
            // slot targets
            foreach (var s in _list) s.Target = 0f;
            for (int i = 0; i < _nPicks; i++) { var s = GetSlot(_picks[i].C); if (s != null) s.Target = _picks[i].W; }
            float rate = 1f - Mathf.Exp(-dt / 0.07f), sum = 0f;
            foreach (var s in _list) { s.W += (s.Target - s.W) * rate; if (s.W < 0.002f && s.Target <= 0f) s.W = 0f; sum += s.W; }
            float lr = layerT > Layer ? 1f - Mathf.Exp(-dt / 0.06f) : 1f - Mathf.Exp(-dt / 0.10f);
            Layer += (layerT - Layer) * lr;
            if (Layer < 0.002f && layerT <= 0f) Layer = 0f;
            _parent.SetInputWeight(_parentInput, Layer);
            if (sum <= 1e-4f || Layer <= 0f)
            {
                foreach (var s in _list) _mix.SetInputWeight(s.Input, 0f);
                foreach (var s in _upList) _mixUp.SetInputWeight(s.Input, 0f);
                _parent.SetInputWeight(_parentInput + 1, 0f);
                Warp = 1f; Rate = 1f; Boost = 1f; YawBias = 0f; LeftContact = RightContact = false; MixSpeed = 0f; DonorArms = 0f; DebugCycles = "";
                return;
            }
            // mixture: speed, cadence, contact knots, direction
            float sMix = 0f, cMix = 0f, k1 = 0f, k2 = 0f, k3 = 0f, ang = 0f, boost = 0f, up = 0f; float inv = 1f / sum;
            foreach (var s in _list)
            {
                if (s.W <= 0f) continue;
                float w = s.W * inv;
                sMix += w * Sp(s.C, legScale); cMix += w / Mathf.Max(0.05f, s.C.length); boost += w * s.Boost;
                k1 += w * s.Lift; k2 += w * s.RPlant; k3 += w * s.RLift; ang += w * s.C.Angle;
                if (s.C.donorRef != null) up += w;
            }
            // contact knots are blended around +-180 too (back cycles are at 172..180 / -180)
            k1 = Mathf.Clamp(k1, 0.05f, 0.9f); k2 = Mathf.Clamp(k2, k1 + 0.01f, 0.97f); k3 = Mathf.Clamp(k3, k2 + 0.04f, 0.995f);
            Knots = new Vector3(k1, k2, k3);
            MixSpeed = sMix; Boost = boost;
            float ratio = _vSm / Mathf.Max(0.2f, sMix);
            // the band widens only beyond the ladder of the playing ray (there is no neighbour to hand over to)
            float lo = _satLow || sMix <= 0f ? 0.45f : 1f - RateBand, hi = _satHigh ? 1f + ExtremeBand : 1f + RateBand;
            float rho = Mathf.Clamp(ratio, lo, hi);
            if (ForceCycle != null) { rho = ForceRate; ratio = rho; }
            Rate = rho; Warp = Mathf.Clamp(ratio / rho / boost, 0.7f, 1.45f);
            float dAng = Mathf.Abs(ang) > 150f ? Mathf.DeltaAngle(ang, theta) : theta - ang;
            YawBias = Mathf.Clamp(dAng, -25f, 25f) * Mathf.Clamp01((_vSm - 0.8f) / 1.2f);
            float step = cMix * rho * boost * StanceMul * dt;
            Phase = Mathf.Repeat(Phase + step, 1f);
            float ph = Phase;
            // the contact hint leads the take by one frame: the foot is stopped on the frame it lands instead of the frame after
            float lead = Mathf.Min(0.12f, cMix * rho * boost * StanceMul * dt * 1.0f);
            float phL = Mathf.Repeat(ph + lead, 1f);
            LeftContact = phL < k1; RightContact = phL >= k2 && phL < k3;
            var sb = DebugOn ? new System.Text.StringBuilder() : null;
            foreach (var s in _list)
            {
                if (s.W <= 0f) { _mix.SetInputWeight(s.Input, 0f); continue; }
                float local01 = GaitMath.MapPhase(ph, k1, k2, k3, s.Lift, s.RPlant, s.RLift);
                float len = s.C.clip.length;
                float t = Mathf.Repeat(s.C.tStart + local01 * s.C.length, len);
                s.Cp.SetTime(t);
                _mix.SetInputWeight(s.Input, s.W * inv);
                if (sb != null) sb.Append(s.C.name).Append('=').Append((s.W * inv).ToString("0.00")).Append(' ');
            }
            UpdateDonors(dt, ph, k1, k2, k3, up, sb);
            if (sb != null) DebugCycles = sb.ToString();
        }

        void UpdateDonors(float dt, float ph, float k1, float k2, float k3, float wUp, System.Text.StringBuilder sb)
        {
            foreach (var s in _upList) s.Target = 0f;
            float tsum = 0f;
            foreach (var m in _list)
            {
                if (m.W <= 0f || m.C.donorRef == null) continue;
                var d = GetUpSlot(m.C.donorRef); if (d != null) { d.Target += m.W; tsum += m.W; }
            }
            float rate = 1f - Mathf.Exp(-dt / 0.07f), sum = 0f;
            foreach (var s in _upList) { s.W += (s.Target - s.W) * rate; if (s.W < 0.002f && s.Target <= 0f) s.W = 0f; sum += s.W; }
            DonorArms = Mathf.Clamp01(wUp);
            _parent.SetInputWeight(_parentInput + 1, sum > 1e-4f ? Layer * DonorArms : 0f);
            if (sum <= 1e-4f) { foreach (var s in _upList) _mixUp.SetInputWeight(s.Input, 0f); return; }
            float inv = 1f / sum;
            foreach (var s in _upList)
            {
                if (s.W <= 0f) { _mixUp.SetInputWeight(s.Input, 0f); continue; }
                float local01 = GaitMath.MapPhase(ph, k1, k2, k3, s.Lift, s.RPlant, s.RLift);
                float t = Mathf.Repeat(s.C.tStart + local01 * s.C.length, s.C.clip.length);
                s.Cp.SetTime(t);
                _mixUp.SetInputWeight(s.Input, s.W * inv);
                if (sb != null) sb.Append("arms:").Append(s.C.name).Append(' ');
            }
        }

        // ------------------------------------------------------------------------------------------ learned native speeds
        // The measured native speed of a take (library build) is only an estimate: the retarget to each body and the heel / ball roll-over shift it by several percent. The planted foot tells the truth: when the body moves at v
        // and the animated foot slides forward at u over the first half of its stance, the take's real native speed is (1 - u / v) of what was assumed. Each stance nudges the factor of the cycle that played alone
        // (shared by every player of the same body size), so the table converges within a few strides and stays correct whatever the game pace is.
        public static readonly Dictionary<LocoCycleLibrary.Cycle, float[]> Factors = new Dictionary<LocoCycleLibrary.Cycle, float[]>();
        public static bool LearnEnabled = true;
        public int SizeKey = 1;
        float Sp(LocoCycleLibrary.Cycle c, float ls) { float[] f; return c.speed * ls * (Factors.TryGetValue(c, out f) ? f[SizeKey] : 1f); }
        public static float FactorOf(LocoCycleLibrary.Cycle c, int size) { float[] f; return Factors.TryGetValue(c, out f) ? f[size] : 1f; }
        float _prevPhase = -1f;
        public int LearnEvents;

        float _slipSumL, _slipTimeL, _slipSumR, _slipTimeR; bool _doneL, _doneR;

        /// <summary>Called by the view after the planted-foot solver ran this frame. The slide of the animated foot over the first 30 - 75 ms of a stance (the sweep under the body, the foot is flat and not yet lifting) updates the cycle that plays alone.</summary>
        public void Learn(FootPlant plant, Vector2 travel, float v, float dt)
        {
            if (!LearnEnabled || plant == null || Layer < 0.9f || v < 1.0f) { _prevPhase = Phase; _slipTimeL = _slipTimeR = 0f; return; }
            LocoCycleLibrary.Cycle only = null; float best = 0f, sum = 0f;
            foreach (var s in _list) { if (s.W > best) { best = s.W; only = s.C; } sum += s.W; }
            bool single = only != null && sum > 1e-4f && best / sum > 0.88f && Rate > 0.89f && Rate < 1.11f;
            if (ForceCycle != null) { only = ForceCycle; single = true; }
            if (single)
            {
                Sample(plant.PlantedLeft, plant.AgeLeft, plant.SlipLeft, dt, ref _slipSumL, ref _slipTimeL, ref _doneL, only, v);
                Sample(plant.PlantedRight, plant.AgeRight, plant.SlipRight, dt, ref _slipSumR, ref _slipTimeR, ref _doneR, only, v);
            }
            _prevPhase = Phase;
        }

        void Sample(bool planted, float age, float slip, float dt, ref float sumS, ref float time, ref bool done, LocoCycleLibrary.Cycle c, float v)
        {
            if (!planted) { sumS = 0f; time = 0f; done = false; return; }
            if (done || age < 0.03f) return;
            if (age < 0.075f) { sumS += slip * dt; time += dt; return; }
            if (time > 0.02f) Nudge(c, sumS / time, v);
            done = true;
        }


        static bool Crossed(float a, float b, float x) { return a >= 0f && ((a <= b && a < x && x <= b) || (a > b && (a < x || x <= b))); }

        void Nudge(LocoCycleLibrary.Cycle c, float slip, float v)
        {
            float[] arr; if (!Factors.TryGetValue(c, out arr)) Factors[c] = arr = new float[] { 1f, 1f, 1f };
            float f = arr[SizeKey];
            float r = Mathf.Clamp(slip / v, -0.35f, 0.35f);
            arr[SizeKey] = Mathf.Clamp(f * (1f - (ForceCycle != null ? 0.30f : 0.12f) * r), ForceCycle != null ? 0.45f : 0.88f, ForceCycle != null ? 1.8f : 1.12f);
            LearnEvents++;
        }
        /// <summary>Playback rate multiplier of the stance part of the cycle (closed loop on the slide of the planted foot).</summary>
        public float StanceMul { get; private set; } = 1f;
        /// <summary>Closed loop per stance: the planted foot of the animated pose slides forward at u over the turf while the body moves at v, so the take sweeps the foot back too slowly (its real native speed is v - u): the stance plays v / (v - u) faster, which pulls the sole to a standstill. Released outside the stance.</summary>
        public void Feedback(FootPlant plant, float v, float dt)
        {
            float target = 1f;
            if (plant != null && v > 1.2f && Layer > 0.5f)
            {
                float slip = 0f; int n = 0;
                if (plant.PlantedLeft && plant.AgeLeft > 0.035f) { slip += plant.SlipLeft; n++; }
                if (plant.PlantedRight && plant.AgeRight > 0.035f) { slip += plant.SlipRight; n++; }
                if (n > 0) { slip = Mathf.Clamp(slip / n, -0.5f * v, 0.45f * v); target = Mathf.Clamp(StanceMul * v / (v - slip), 0.6f, 1.8f); }
            }
            StanceMul += (target - StanceMul) * (1f - Mathf.Exp(-dt / (target == 1f ? 0.05f : 0.025f)));
        }

        public static bool DebugOn = false;
        /// <summary>QA (calibration): play only this cycle at ForceRate x its native cadence.</summary>
        public LocoCycleLibrary.Cycle ForceCycle; public float ForceRate = 1f;
        public void Reset()
        {
            Layer = 0f; Phase = _seedPhase; _vHave = false; Warp = 1f; Rate = 1f; Boost = 1f; LeftContact = RightContact = false;
            if (_attached)
            {
                foreach (var s in _list) { s.W = 0f; s.Target = 0f; _mix.SetInputWeight(s.Input, 0f); }
                foreach (var s in _upList) { s.W = 0f; s.Target = 0f; _mixUp.SetInputWeight(s.Input, 0f); }
                _parent.SetInputWeight(_parentInput, 0f); _parent.SetInputWeight(_parentInput + 1, 0f);
            }
        }
    }
}
