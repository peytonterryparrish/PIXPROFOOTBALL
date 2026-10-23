using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Where one actor of a contact is drawn this frame (view root position on the ground plane, view yaw, and how much of it is animation vs simulation).</summary>
    public struct ContactActorPose
    {
        public Vector2 Root;
        public float Yaw;
        public float Warp;
    }

    /// <summary>
    /// One authored two-actor take played against the simulation: the pair is aligned to the simulated bodies at contact (<see cref="ContactMath.AlignPair"/>), then the views are warped
    /// from the simulation onto the animation root motion over the lead-in, follow it through the hit, and hand back to the simulation (which owns where the carrier ends and the forward
    /// progress) once the take settles. Engine-free math (Vector2 / float only), so it is unit tested.
    /// v0.9.0 CONNECT: the momentum of the carrier is carried through the hit (E-205), the beaten tackler is not dragged along by the carrier's progress (E-207), the yaw hands back to the
    /// simulation (E-214), the pair separation scales with the two bodies (E-224), the warp is pre-rolled over a longer lead (E-225) and the take runs at the pace of the game (E-362).
    /// </summary>
    public sealed class ContactSession
    {
        public ContactTable.Pair Pair;
        public ContactKind Kind;
        public int Carrier, Tackler;
        public Rigid2 Align;
        /// <summary>Clip time at which the take starts playing, and the current clip time.</summary>
        public float Start, Time;
        /// <summary>Clip time at which the contact happens in the take.</summary>
        public float Contact { get { return Pair.tContact; } }
        public bool Held;
        public float StartYawA, StartYawB;
        public float Lead;
        /// <summary>Clip time at which the take freezes while a drag lasts (0 = never).</summary>
        public float HoldAt;
        /// <summary>Seconds after the contact moment during which the pair follows the animation exactly.</summary>
        public float AuthorityHold = 0.3f, AuthorityRamp = 0.9f;
        /// <summary>The take stops being used this long before its end (the final get-up is cropped when the play is long over).</summary>
        public float MaxPlay = 6.5f;
        /// <summary>Seconds since the session started (real time of the take).</summary>
        public float Elapsed;
        /// <summary>The carrier's velocity going into the hit (m/s, ground plane) and the take's own speed at contact: the difference is carried through (E-205).</summary>
        public Vector2 SimVelA;
        /// <summary>The carrier's ground speed going into the hit (m/s). The simulation already carries a tackled runner on for about a metre (x0.55 and a slide): only a share of the surplus over the take's own speed is slid on top, and the correction to the simulated spot starts early for a fast carrier.</summary>
        public float EntrySpeed;
        /// <summary>Time constant (s) of the slide that carries the carrier's momentum through the hit.</summary>
        public float SlideTau = 0.28f;
        /// <summary>Body-size factor on the authored separation of the two actors (1 = the authored layout).</summary>
        public float SepScale = 1f;
        /// <summary>Seconds after the contact moment at which an actor is let go of the take (a contested catch, a jam: the receiver runs on). 0 = never.</summary>
        public float ReleaseAAfter, ReleaseBAfter;
        public bool ReleasedA, ReleasedB;
        /// <summary>Impact (shake, rumble, hit sound) waiting for the contact moment (E-378).</summary>
        public bool ImpactPending;
        /// <summary>The playing clips of the two actors (the session reads the clip clock from them: E-377).</summary>
        public UnityEngine.AnimationClip ClipA, ClipB;
        /// <summary>Struggle clock of a held take (E-222): the take does not freeze, a short window of it plays back and forth.</summary>
        public float StruggleT, HeldFrom;
        /// <summary>0..1 ramp of the held take (a drag): the correction to the simulated spot is blended in over 0.3 s instead of jumping to 1 when the hold starts.</summary>
        public float HeldBlend;
        public void TickHeld(float dt) { HeldBlend = Held ? Mathf.Min(1f, HeldBlend + dt / 0.3f) : 0f; }
        /// <summary>The A actor lies on the ground at the moment (face up: +1 .. face down: -1).</summary>
        public bool Finished { get { return (ReleasedA || Time >= EndA) && (ReleasedB || Time >= EndB); } }
        public float EndA { get { return Mathf.Min(Pair.lengthA, MaxPlay); } }
        public float EndB { get { return Mathf.Min(Pair.lengthB, MaxPlay); } }
        /// <summary>The carrier keeps going in the simulation after the hit (broken tackle, truck, contested catch): his yaw and position are handed back, the other actor is not dragged along.</summary>
        public bool CarrierRunsOn { get { return Kind == ContactKind.Broken || Kind == ContactKind.SackBroken || Kind == ContactKind.Truck || Kind == ContactKind.Contested || Kind == ContactKind.Collision; } }
        public bool PassedContact { get { return Time >= Pair.tContact; } }

        /// <summary>v0.10.0 CHOREO: the simulation owns both bodies and moves them along the take (the choreographer booking); the session only plays the clips and draws the bodies where the simulation has them.</summary>
        public PixFootball.Sim.ChoreoBooking Booking;
        public bool Booked { get { return Booking != null; } }

        /// <summary>A session for a booked take: no alignment of its own (the booking placed the pair), no warp, no correction back to the simulation.</summary>
        public static ContactSession CreateBooked(ContactTable.Pair p, ContactKind kind, int carrier, int tackler, PixFootball.Sim.ChoreoBooking booking, float clipTimeNow)
        {
            var s = new ContactSession { Pair = p, Kind = kind, Carrier = carrier, Tackler = tackler, Booking = booking };
            s.Start = Mathf.Clamp(clipTimeNow, 0f, Mathf.Max(0f, Mathf.Min(p.lengthA, p.lengthB) - 0.05f));
            s.Time = s.Start; s.Lead = Mathf.Max(0f, p.tContact - s.Start);
            return s;
        }

        public static ContactSession Create(ContactTable.Pair p, ContactKind kind, int carrier, int tackler, Vector2 simA, Vector2 simB, float carrierYaw, float yawB, Vector2 simVelA, float lead = 0.36f, float axisWeight = 0.6f)
        {
            var s = new ContactSession { Pair = p, Kind = kind, Carrier = carrier, Tackler = tackler };
            s.Lead = Mathf.Min(lead, p.tContact);
            s.Start = Mathf.Max(0f, p.tContact - s.Lead);
            s.Time = s.Start;
            s.StartYawA = carrierYaw; s.StartYawB = yawB;
            s.SimVelA = simVelA; s.EntrySpeed = simVelA.magnitude;
            // where the carrier will be when the hips meet: his current position carried forward through the whole lead-in (v0.9.0: was 0.6 of it, so he arrived a stride short)
            if (s.EntrySpeed > 5f) s.AuthorityHold = Mathf.Lerp(0.3f, 0.08f, Mathf.Clamp01((s.EntrySpeed - 5f) / 4f));     // a sprinting carrier follows the simulated spot soon after the hit
            Vector2 predA = simA + simVelA * s.Lead;
            Vector2 predB = simB;
            int ic = Mathf.Clamp(Mathf.RoundToInt(p.tContact / p.step), 0, Mathf.Min(p.CountA, p.CountB) - 1);
            Vector2 hA = Hips(p.a, ic), hB = Hips(p.b, ic);
            float yawA = p.a[ic * ContactTable.Stride + 4];
            // AlignPair anchors its second actor (position exact, heading matched): that is the carrier here, the tackler is the "other" one
            s.Align = ContactMath.AlignPair(hB, hA, yawA, predB, predA, carrierYaw, axisWeight);
            return s;
        }

        static Vector2 Hips(float[] a, int i) { int o = i * ContactTable.Stride; return new Vector2(a[o + 2], a[o + 3]); }

        /// <summary>Advances the take clock. speed = the clip playback rate (the pace of the game): the clock and the clip stay one clock.</summary>
        public void Advance(float dt, float speed = 1f)
        {
            Elapsed += dt;
            if (!Held) Time += dt * speed;
        }

        /// <summary>Sets the take clock from the playing clip (E-377: the clip is the single source of truth, the session never drifts from it).</summary>
        public void SyncTo(float clipTime) { if (!Held) Time = Mathf.Max(Time, Mathf.Min(clipTime, Mathf.Max(Pair.lengthA, Pair.lengthB))); }

        /// <summary>Freezes the take at the current time (a drag: the tackler hangs on while the carrier churns on).</summary>
        public void Hold(bool on) { Held = on; }

        public float WarpWeight { get { return ContactMath.WarpIn(Time, Start, Contact); } }

        public Vector2 AnimHips(bool a, float t)
        {
            var arr = a ? Pair.a : Pair.b;
            return ContactMath.SampleXZ(arr, ContactTable.Stride, 2, Pair.step, t);
        }

        public Vector2 AnimRoot(bool a, float t)
        {
            var arr = a ? Pair.a : Pair.b;
            return ContactMath.SampleXZ(arr, ContactTable.Stride, 0, Pair.step, t);
        }

        /// <summary>The momentum carried through the hit at the current time, metres along the incoming velocity (0 before the contact; eased to the surplus of the carrier's speed over the take's own).</summary>
        public Vector2 Slide()
        {
            if (Time <= Contact) return Vector2.zero;
            float sp = SimVelA.magnitude;
            if (sp < 0.3f) return Vector2.zero;
            float surplus = Mathf.Max(0f, EntrySpeed * 0.5f - Pair.speedA);
            if (surplus < 0.2f) return Vector2.zero;
            float u = Time - Contact;
            return SimVelA / sp * Mathf.Min(0.6f, surplus * SlideTau * (1f - Mathf.Exp(-u / SlideTau)));
        }

        /// <summary>Pose of both views for the current clip time. sim* are the simulated bodies this frame (view root space: the sim position drives the view when no take is playing).</summary>
        public void Evaluate(Vector2 simA, float simYawA, Vector2 simB, float simYawB, out ContactActorPose a, out ContactActorPose b)
        {
            if (Booking != null) { a.Root = simA; b.Root = simB; a.Yaw = simYawA; b.Yaw = simYawB; a.Warp = 1f; b.Warp = 1f; return; }      // v0.10.0 CHOREO: the simulation already moves both bodies along the take
            float tA = Mathf.Min(Time, Pair.lengthA), tB = Mathf.Min(Time, Pair.lengthB);
            float w = WarpWeight;
            Vector2 slide = Slide();
            // the carrier's hips as animated (plus the carried momentum) vs where the simulation has him: the difference is blended in once the hit is over (forward progress stays authoritative)
            Vector2 animHipsA = Align.Apply(AnimHips(true, tA)) + slide;
            Vector2 delta = simA - animHipsA;
            float k = Mathf.Max(HeldBlend, ContactMath.Authority(Time, Contact, Pair.Length, AuthorityHold, AuthorityRamp));
            Vector2 corrA = delta * k;
            // E-207: a beaten tackler lies where his take puts him; only a small share of the carrier's progress moves him (he is never dragged along the ground)
            Vector2 corrB = CarrierRunsOn ? Vector2.ClampMagnitude(delta * k * 0.15f, 0.25f) : corrA;
            Vector2 rootA = Align.Apply(AnimRoot(true, tA)) + slide + corrA;
            Vector2 rootB = Align.Apply(AnimRoot(false, tB)) + slide * (CarrierRunsOn ? 0.35f : 1f) + corrB;
            // E-224: the authored separation of the two actors scales with their bodies (the take was authored on two medium bodies)
            if (Mathf.Abs(SepScale - 1f) > 0.01f) rootB = rootA + (rootB - rootA) * Mathf.Lerp(1f, SepScale, w);
            // the take holds the carrier's hips where the sim has them: express the sim position as the root position of that frame (root ~ hips in the in-place import)
            a.Root = Vector2.Lerp(simA, rootA, w);
            b.Root = Vector2.Lerp(simB, rootB, w);
            float yawA = Mathf.LerpAngle(StartYawA, Align.Yaw, w);
            float yawB = Mathf.LerpAngle(StartYawB, Align.Yaw, w);
            // E-214: a carrier who runs on faces the simulation's heading again with the same ramp that gives him back his position
            if (CarrierRunsOn) yawA = Mathf.LerpAngle(yawA, simYawA, k);
            a.Yaw = yawA; b.Yaw = yawB;
            a.Warp = w; b.Warp = w;
        }
    }
}
