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
        /// <summary>The A actor lies on the ground at the moment (face up: +1 .. face down: -1).</summary>
        public bool Finished { get { return Time >= EndA && Time >= EndB; } }
        public float EndA { get { return Mathf.Min(Pair.lengthA, MaxPlay); } }
        public float EndB { get { return Mathf.Min(Pair.lengthB, MaxPlay); } }

        public static ContactSession Create(ContactTable.Pair p, ContactKind kind, int carrier, int tackler, Vector2 simA, Vector2 simB, float carrierYaw, float yawB, Vector2 simVelA, float lead = 0.18f, float axisWeight = 0.6f)
        {
            var s = new ContactSession { Pair = p, Kind = kind, Carrier = carrier, Tackler = tackler };
            s.Lead = Mathf.Min(lead, p.tContact);
            s.Start = Mathf.Max(0f, p.tContact - s.Lead);
            s.Time = s.Start;
            s.StartYawA = carrierYaw; s.StartYawB = yawB;
            // where the carrier will be when the hips meet: his current position carried forward through the lead-in (the simulation already slowed him at the whistle)
            Vector2 predA = simA + simVelA * (s.Lead * 0.6f);
            Vector2 predB = simB;
            int ic = Mathf.Clamp(Mathf.RoundToInt(p.tContact / p.step), 0, Mathf.Min(p.CountA, p.CountB) - 1);
            Vector2 hA = Hips(p.a, ic), hB = Hips(p.b, ic);
            float yawA = p.a[ic * ContactTable.Stride + 4];
            // AlignPair anchors its second actor (position exact, heading matched): that is the carrier here, the tackler is the "other" one
            s.Align = ContactMath.AlignPair(hB, hA, yawA, predB, predA, carrierYaw, axisWeight);
            return s;
        }

        static Vector2 Hips(float[] a, int i) { int o = i * ContactTable.Stride; return new Vector2(a[o + 2], a[o + 3]); }

        public void Advance(float dt, float speed = 1f)
        {
            Elapsed += dt;
            if (!Held) Time += dt * speed;
        }

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

        /// <summary>Pose of both views for the current clip time. sim* are the simulated bodies this frame (view root space: the sim position drives the view when no take is playing).</summary>
        public void Evaluate(Vector2 simA, float simYawA, Vector2 simB, float simYawB, out ContactActorPose a, out ContactActorPose b)
        {
            float tA = Mathf.Min(Time, Pair.lengthA), tB = Mathf.Min(Time, Pair.lengthB);
            float w = WarpWeight;
            // the carrier's hips as animated vs where the simulation has him: the difference is blended in once the hit is over (forward progress stays authoritative)
            Vector2 animHipsA = Align.Apply(AnimHips(true, tA));
            Vector2 delta = simA - animHipsA;
            float k = Held ? 1f : ContactMath.Authority(Time, Contact, Pair.Length, AuthorityHold, AuthorityRamp);
            Vector2 corr = delta * k;
            Vector2 rootA = Align.Apply(AnimRoot(true, tA)) + corr;
            Vector2 rootB = Align.Apply(AnimRoot(false, tB)) + corr;
            // the take holds the carrier's hips where the sim has them: express the sim position as the root position of that frame (root ~ hips in the in-place import)
            a.Root = Vector2.Lerp(simA, rootA, w);
            b.Root = Vector2.Lerp(simB, rootB, w);
            a.Yaw = Mathf.LerpAngle(StartYawA, Align.Yaw, w);
            b.Yaw = Mathf.LerpAngle(StartYawB, Align.Yaw, w);
            a.Warp = w; b.Warp = w;
        }
    }
}
