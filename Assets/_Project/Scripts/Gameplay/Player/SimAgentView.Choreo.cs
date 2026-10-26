using PixFootball.Art;
using UnityEngine;
namespace PixFootball.Gameplay
{
    public partial class SimAgentView
    {
        /// <summary>QA: switch the view side of the sim choreographer off (the bookings are ignored; the sim may still follow them).</summary>
        public static bool DebugNoChoreo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoChoreoView") >= 0;

        /// <summary>
        /// v0.10.0 CHOREO: a take that does not own the body's root must not own the legs of a body the simulation moves (that is what made engaged linemen and hand-offs moonwalk): the engagement
        /// hand fight, the hand-off and the running catch are upper-body takes that hand the legs to locomotion as soon as the body moves faster than a slow shuffle (0.5 to 0.7 m/s; the default is 2).
        /// </summary>
        /// <summary>A new pass-rush move (the engagement re-rolled it) does not cut the pair take that is still playing: it starts when the running take has played out (or has settled into its hand-fight loop).</summary>
        bool ChoreoDeferEngage()
        {
            if (DebugNoChoreo || Rig == null || !Rig.Valid || _engClip == null) return false;
            return Rig.CurrentClip == _engClip && Rig.InWindow && Rig.RemainingSeconds > 0.25f && Rig.ClipTime < 1.6f;
        }

        // v0.10.0 CHOREO + MOTION: the hand-off to and from the locomotion. When a full-body take or the ragdoll takes the legs the matcher is released and reports the foot phase it left (which foot is planted,
        // how far through the stride); when the take is over the locomotion takes the legs back and searches from the live pose and the planned path. The phase is kept for the audit.
        bool _chLocoHeld; public MotionMatcher.FootPhase ChoreoEntry; public int ChoreoEntries, ChoreoExits;
        void ChoreoLocoHandoff()
        {
            if (DebugNoChoreo) return;
            bool full = ContactDriven || (Ragdoll != null && Ragdoll.Active);
            if (full && !_chLocoHeld) { ChoreoEntry = LocoRelease(); _chLocoHeld = true; ChoreoEntries++; }
            else if (!full && _chLocoHeld) { LocoTakeBack(); _chLocoHeld = false; ChoreoExits++; }
        }

        /// <summary>QA: how far (m) the planned path may leave a get-off take's own root path before the take is released to the locomotion.</summary>
        public const float GetOffFitTol = 0.15f;
        public static int GetOffFitCut;
        static readonly bool NoGetOffFit = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoGetOffFit") >= 0;

        Vector3 PlannedAt(float t)
        {
            const float h1 = 0.33f, h2 = 0.66f, h3 = 1.0f;
            if (t <= 0f) return Vector3.zero;
            if (t <= h1) return Vector3.Lerp(Vector3.zero, _plan1, t / h1);
            if (t <= h2) return Vector3.Lerp(_plan1, _plan2, (t - h1) / (h2 - h1));
            if (t <= h3) return Vector3.Lerp(_plan2, _plan3, (t - h2) / (h3 - h2));
            return _plan3;
        }

        /// <summary>
        /// A get-off take is committed only as long as the simulation's planned path agrees with the take's own root path. The explosion out of the stance (DB back-turned break, lateral break, forward transition) is
        /// authored on the spot or on a short path; played over a body the simulation sends at 3 to 6 m/s it slid 3 to 5 m/s. The planned path (MOTION's look-ahead at +0.33 / +0.66 / +1.0 s) says when the body leaves
        /// the take: at that moment the take is released (fades out) and the locomotion takes the legs, entering from the live pose.
        /// </summary>
        ClipPlan ChoreoFitGetOff(ClipPlan plan, Vector3 facing)
        {
            if (DebugNoChoreo || NoGetOffFit || !plan.Valid || plan.Loop || !_hasPlan || _planAge > 0.25f) return plan;
            var tab = ChoreoTable.Load(); if (tab == null) return plan;
            string name = plan.Clip.name; if (tab.Get(name) == null) return plan;
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z); if (fwd.sqrMagnitude < 1e-4f) return plan; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float end = plan.End > 0f ? plan.End : plan.Clip.length, spd = plan.Speed > 0.01f ? plan.Speed : 1f;
            float span = (end - plan.Start) / spd, cut = span;
            for (float t = 0.15f; t <= span + 1e-3f; t += 0.05f)
            {
                Vector2 cd = tab.RootDelta(name, plan.Start, plan.Start + t * spd);
                Vector3 clipW = right * cd.x + fwd * cd.y, pl = PlannedAt(t);
                float gap = new Vector2(pl.x - clipW.x, pl.z - clipW.z).magnitude;
                if (gap > GetOffFitTol) { cut = Mathf.Max(0.36f, t); break; }
            }
            _getOffUntil = _clock + Mathf.Min(cut, span) + 0.15f;
            if (cut < span - 0.05f) { plan.End = plan.Start + cut * spd; plan.FadeIn = Mathf.Min(plan.FadeIn > 0f ? plan.FadeIn : 0.1f, 0.1f); plan.FadeOut = 0.2f; plan.FadeFloor = 0.1f; GetOffFitCut++; }      // (a take shorter than its fades would leave the stance clip under it at full weight)
            return plan;
        }

        // ---- v0.10.0 CHOREO: stepping legs for a pushed, engaged body ---------------------------------------------------------------------------------------
        public static bool NoEngageStep = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoEngageStep") >= 0;
        /// <summary>v0.12.0 CONTACT: body speed (m/s) below which the stepper has the legs of an engaged / blocking body, a wrap, a get-off. QA: -pixStepCap sets the first one.</summary>
        public static float StepCap = ArgF("-pixStepCap", 5.5f), StepCapWrap = ArgF("-pixStepCapWrap", 5.5f), StepCapGetOff = 4.5f;
        float _getOffUntil = -1f;
        /// <summary>v0.13.0 NATURAL-3 QA: -pixStepperOverMatcher lets the get-off stepper work over the matcher again.</summary>
        static readonly bool MatcherKeepsGetOff = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixStepperOverMatcher") < 0;
        EngageStepper _stepper; Animator _stepperAnim; Vector3 _stepPrev; bool _stepPrevHave;
        public int EngageSteps { get { return _stepper != null ? _stepper.Steps : 0; } }
        public float EngageStepWeight { get { return _stepper != null ? _stepper.Weight : 0f; } }
        /// <summary>QA: the stepper of this body (null until it has been used).</summary>
        public EngageStepper Stepper { get { return _stepper; } }
        void EngageStepLate(float dt)
        {
            if (Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman || dt <= 1e-5f) return;
            if (_stepper == null || _stepperAnim != Avatar.Animator) { _stepper = new EngageStepper(Avatar.Animator); _stepperAnim = Avatar.Animator; _stepPrevHave = false; }
            Vector3 p = transform.position, vel = _stepPrevHave ? (p - _stepPrev) / dt : Vector3.zero; vel.y = 0f; _stepPrev = p; _stepPrevHave = true;
            if (vel.magnitude > 12f) { vel = Vector3.zero; _stepper.Reset(); }      // a teleport (re-layout)
            bool engaged = _lastAction == PixFootball.Sim.AgentAction.Engaged || _lastAction == PixFootball.Sim.AgentAction.Shed || _lastAction == PixFootball.Sim.AgentAction.Block;
            // v0.12.0 CONTACT: the tackler's and the carrier's feet in a wrap are pinned too (they used to slide 1.6 to 2 m/s: the pair take's legs are authored for a partner at one distance)
            bool wrap = !EngageStepper.V011 && (_lastAction == PixFootball.Sim.AgentAction.TackleWrapUp || _lastAction == PixFootball.Sim.AgentAction.TackleGang || _lastAction == PixFootball.Sim.AgentAction.Tackled);
            bool getOff = _clock < _getOffUntil && !ContactDriven;      // the first steps out of a stance: the in-place take is played over a body that is already moving
            bool stumble = _lastAction == PixFootball.Sim.AgentAction.Stumble && !ContactDriven;
            // a foot can be set down about every 0.3 s: above the cap the body is running, not being pushed, and the locomotion's own stride (FootPlant) has the legs; a margin keeps the hand-over from flickering
            float cap = EngageStepper.V011 ? ((getOff || stumble) ? 4.5f : 2.6f) : ((getOff || stumble) ? StepCapGetOff : (wrap ? StepCapWrap : StepCap));
            if (_stepper.Weight > 0.5f && !EngageStepper.V011) cap += 0.35f;
            bool active = !DebugNoChoreo && !NoEngageStep && !_engLocoOn && (engaged || wrap || getOff || stumble) && vel.magnitude < cap && !(Ragdoll != null && Ragdoll.Active) && Rig != null && Rig.Valid;
            if (active && getOff && !engaged && !wrap && !stumble && Mm != null && Mm.Layer > 0.5f && MatcherKeepsGetOff) active = false;      // v0.13.0 NATURAL-3 (hook): the matcher already has the legs of a body that runs out of a stance; the stepper set the feet down over its planted foot (up to 7 cm of creep, 2 m/s)
            { var fp = Motion != null ? Motion.Plant : null; if (fp != null && fp.Ok) _stepper.SetRest(fp.RestAnkleL, fp.RestAnkleR, fp.RestToeL, fp.RestToeR); }      // v0.12.0: planted feet are pressed onto the turf
            _stepper.Late(dt, active, vel, Forward, p.y, engaged || wrap);      // (contact = an engaged / blocking / wrapping body; the get-off and stumble windows keep the v0.11 stepping)
        }

        /// <summary>
        /// v0.12.0 CONTACT: an engaged body (a hand fight whose clip plays on the upper body only) that the simulation moves faster than a push gets its legs from the locomotion matcher: a real shuffle / back-pedal / walk with the foot planter,
        /// not a hand-fight clip's legs skating over the turf (the stepper cannot set a foot down more than about every 0.3 s). Hysteresis 1.0 / 1.3 m/s. The stepper (<see cref="EngageStepLate"/>) takes over again below it.
        /// </summary>
        bool ChoreoLocoLegs(PixFootball.Sim.AgentAction action, Vector3 velocity)
        {
            if (EngageStepper.V011 || DebugNoChoreo || NoEngageLoco || Rig == null || !Rig.Valid) { _engLocoOn = false; return false; }
            bool eng = (action == PixFootball.Sim.AgentAction.Engaged || action == PixFootball.Sim.AgentAction.Shed) && Rig.UpperBodyActive;
            float sp = new Vector2(velocity.x, velocity.z).magnitude;
            _engLocoOn = eng && sp > (_engLocoOn ? EngageLocoLow : EngageLocoHigh);
            return _engLocoOn;
        }
        bool _engLocoOn;
        /// <summary>QA: the locomotion matcher has the legs of this engaged body (see <see cref="ChoreoLocoLegs"/>).</summary>
        public bool EngageLocoOn { get { return _engLocoOn; } }
        public static float EngageLocoHigh = ArgF("-pixEngageLoco", 99f), EngageLocoLow = 0.8f;      // off by default: measured, the matcher legs do not reach a body whose upper-body mask plays at full weight (drift 40 cm against 6 cm for the stepper)
        public static bool NoEngageLoco = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoEngageLoco") >= 0;

        /// <summary>v0.12.0 CONTACT: after the hips leash and Final IK (<c>PixIk.Late</c>) the ankles go back to where <see cref="EngageStepLate"/> put them; the hand solve may not move a planted foot. Also repairs the hips leash, which runs after the stepper.</summary>
        void EngageLegsRestore()
        {
            if (_stepper == null || EngageStepper.V011) return;
            _stepper.Reapply();
        }

        ClipPlan ChoreoMask(PixFootball.Sim.AgentAction action, ClipPlan plan)
        {
            if (DebugNoChoreo || !plan.Valid) return plan;
            switch (action)
            {
                case PixFootball.Sim.AgentAction.Engaged: case PixFootball.Sim.AgentAction.Shed:
                    plan.UpperBody = true; plan.MaskSpeed = 0.5f; break;
                case PixFootball.Sim.AgentAction.Handoff:
                    if (plan.UpperBody) plan.MaskSpeed = 0.6f; break;
                case PixFootball.Sim.AgentAction.Catch:
                    if (plan.UpperBody && plan.MaskSpeed <= 0f) plan.MaskSpeed = 0.7f; break;
                case PixFootball.Sim.AgentAction.Jump:
                    if (plan.UpperBody && plan.MaskSpeed <= 0f) plan.MaskSpeed = 0.7f; break;
            }
            return plan;
        }
    }
}
