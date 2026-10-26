using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 QB: the key poses of the quarterback's choreographed sequence (pure data, same conventions as <see cref="StancePoses"/>): the upright survey, the point, the crouch to the under-centre stance, the secured ball at the belly,
    /// the open (reverse pivot) and the set pocket carriage. Hands are in <see cref="HandMode.Chest"/> offsets (lateral, up from the chest bone, forward) so they can be blended continuously and handed to the anchor by the Reach layer.
    /// </summary>
    public static partial class StancePoses
    {
        /// <summary>Standing tall behind the line, arms relaxed at the belt.</summary>
        public static StancePose QbUpright()
        {
            var p = Base();
            p.Hips = 1.03f; p.HipsZ = 0f; p.Lean = 3f; p.Bend = 3f; p.Neck = 4f; p.HeadPitch = 0f;
            p.RX = 0.19f; p.RZ = -0.03f; p.LX = -0.19f; p.LZ = 0.03f; p.ToeOut = 8f; p.RHeel = 0f; p.LHeel = 0f;
            p.RHand = HandMode.Chest; p.RPos = new Vector3(0.22f, -0.46f, 0.08f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.22f, -0.46f, 0.08f);
            return p;
        }

        /// <summary>The survey behind the line (u = seconds into it, <paramref name="len"/> its length): the head sweeps the defence left then right, the right arm rises and POINTS at the middle of it (the "Mike"), the head holds on the point, the arm comes down.
        /// Returns the pose and the point weight (0..1, how much the head is on the surveyed spot instead of the sweep).</summary>
        public static StancePose QbSurvey(float t, float len, out float pointW)
        {
            var p = QbUpright();
            float k = Mathf.Clamp01(t / Mathf.Max(0.5f, len));
            // sweep: centre -> left 34 -> right 34 -> centre (first 55 percent), then the point (55-88 percent), then the arm comes down
            float sweep = k < 0.55f ? 34f * Mathf.Sin(k / 0.55f * Mathf.PI * 2f) : 0f;
            p.HeadYaw = -sweep; p.Neck = 5f;
            float pk = Ease(Mathf.Clamp01((k - 0.50f) / 0.12f)) * (1f - Ease(Mathf.Clamp01((k - 0.86f) / 0.12f)));
            pointW = pk;
            // the pointing arm: out in front at shoulder height, a little to the right of the head
            Vector3 hang = p.RPos, point = new Vector3(0.17f, 0.10f, 0.64f);
            p.RPos = Vector3.Lerp(hang, point, pk);
            // a little life: weight shifts onto the balls of the feet as he reads
            p.Hips -= 0.01f * Mathf.Sin(t * 2.2f);
            return p;
        }

        /// <summary>The crouch: upright to the under-centre stance (u 0..1): the hips sink, the trunk folds to about 34 degrees, the feet turn in (pigeon-toed) and the hands come up and forward to the centre (the Reach layer finishes them on the anchor).</summary>
        public static StancePose QbCrouch(float u, bool shotgun)
        {
            u = Mathf.Clamp01(u);
            var a = QbUpright();
            var b = For(new StanceSpec { Kind = shotgun ? StanceKind.QbShotgun : StanceKind.QbUnderCenter }, PlayerRole.QB);
            // hands: relaxed at the belt -> in front of the belt, reaching (under centre) / up at the chest (shotgun); the Anchor mode only takes over at u = 1
            if (!shotgun) b = QbUcDeep(b);
            var bh = b;
            if (!shotgun) { bh.RHand = HandMode.Chest; bh.LHand = HandMode.Chest; bh.RPos = new Vector3(0.05f, -0.44f, 0.46f); bh.LPos = new Vector3(-0.05f, -0.44f, 0.46f); }
            float e = Ease(u);
            var p = Lerp(a, bh, e);
            p.RHand = HandMode.Chest; p.LHand = HandMode.Chest;
            if (u >= 0.999f) p = b;
            p.Valid = true;
            return p;
        }

        /// <summary>The ball pulled in to the belly after the snap (u 0..1): both hands on it, elbows in, the knees still bent, the weight over the feet, the head up. This is the "secure" of the drop: nothing moves but the hands until it is done.</summary>
        public static StancePose QbSecure(float u)
        {
            u = Mathf.Clamp01(u);
            var uc = QbUcDeep(For(new StanceSpec { Kind = StanceKind.QbUnderCenter }, PlayerRole.QB));
            var p = uc; p.Hips = Mathf.Lerp(uc.Hips, 0.88f, Ease(u)); p.Lean = Mathf.Lerp(uc.Lean, 20f, Ease(u)); p.HipsZ = Mathf.Lerp(uc.HipsZ, -0.02f, Ease(u));
            p.RHand = HandMode.Chest; p.LHand = HandMode.Chest;
            p.RPos = Vector3.Lerp(new Vector3(0.04f, -0.46f, 0.46f), new Vector3(0.09f, -0.12f, 0.20f), Ease(u)); p.LPos = Vector3.Lerp(new Vector3(-0.04f, -0.46f, 0.46f), new Vector3(-0.09f, -0.12f, 0.20f), Ease(u));
            p.ToeOut = Mathf.Lerp(uc.ToeOut, 6f, Ease(u)); p.HeadPitch = 2f; p.Valid = true;
            return p;
        }

        /// <summary>The pocket carriage with the feet live (the bounce): ball high at the chest in both hands, base just wider than the shoulders, a light alternating heel raise.</summary>
        public static StancePose QbPocketLive(float t) { return QbPocketLive(t, 0f); }

        /// <summary>v0.11.0 QB (note 8): the same stance while he travels (<paramref name="speed"/> m/s): a wider, lower base that leans into the way he goes, the ball still high at the chest in both hands, elbows in; the feet are the footfall planner's slide and shuffle steps (no heel bounce on top of them).</summary>
        public static StancePose QbPocketLive(float t, float speed)
        {
            var p = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            float k = Mathf.Clamp01((speed - 0.5f) / 3.0f);
            if (k > 0.001f)
            {
                p.Hips = Mathf.Lerp(p.Hips, 0.87f, k); p.Lean = Mathf.Lerp(p.Lean, 17f, k); p.Bend = Mathf.Lerp(p.Bend, 6f, k);
                p.RX = Mathf.Lerp(p.RX, 0.25f, k); p.LX = Mathf.Lerp(p.LX, -0.25f, k); p.RZ = Mathf.Lerp(p.RZ, -0.05f, k); p.LZ = Mathf.Lerp(p.LZ, 0.05f, k);
                p.RHeel = 0f; p.LHeel = 0f;
                // quick tiny knee bounce of the stride (the planner owns the feet)
                p.Hips += 0.012f * Mathf.Sin(t * 2f * Mathf.PI * (1.8f + 1.6f * k)) * k;
                return p;
            }
            float s = Mathf.Sin(t * 2f * Mathf.PI * 1.9f);
            p.Hips += 0.020f * Mathf.Abs(s) - 0.010f;
            p.RHeel = 0.03f * Mathf.Max(0f, s); p.LHeel = 0.03f * Mathf.Max(0f, -s);
            return p;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class StancePoses
    {
        /// <summary>Pocket to scramble (u 0..1 over <c>QbSeqTiming.ScrambleStartSeconds</c>): the ball comes up to the throwing ear, the trunk leans into the run and the hips rise out of the pocket crouch; the drive step is the footfall planner's first step.</summary>
        public static StancePose QbScrambleStart(float u)
        {
            u = Mathf.Clamp01(u);
            var a = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            var b = a;
            b.Hips = 0.95f; b.Lean = 24f; b.Bend = 6f; b.ToeOut = 4f; b.RZ = -0.20f; b.LZ = 0.14f;
            b.RHand = HandMode.Cock; b.RPos = new Vector3(0.17f, 0.30f, 0.02f); b.LHand = HandMode.Chest; b.LPos = new Vector3(-0.10f, 0.05f, 0.22f);
            var p = Lerp(a, b, Ease(u)); p.RHand = b.RHand; p.LHand = b.LHand; p.Valid = true;
            return p;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class StancePoses
    {
        /// <summary>After the hand-off (u 0..1 over <c>QbSeqTiming.MeshFollowSeconds</c>): the ball is gone, the arms finish the fake (the throwing arm folds across the belly on the empty carry, the other swings free), the trunk comes up, the eyes are on the field.</summary>
        public static StancePose QbCarryOut(float u)
        {
            u = Mathf.Clamp01(u);
            var a = QbSecure(1f);
            var b = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            b.Hips = 0.92f; b.Lean = 12f;
            b.RHand = HandMode.Chest; b.LHand = HandMode.Chest;
            b.RPos = new Vector3(0.15f, -0.26f, 0.12f); b.LPos = new Vector3(-0.22f, -0.38f, 0.04f);
            var p = Lerp(a, b, Ease(Mathf.Clamp01(u * 1.6f))); p.RHand = HandMode.Chest; p.LHand = HandMode.Chest; p.Valid = true;
            return p;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class StancePoses
    {
        /// <summary>
        /// v0.11.0 QB (playtest note 10): the throw, keyed to the simulated release. <paramref name="u"/>: 0 = the wind-up starts, 1 = the ball leaves the hand, 1..1.5 the follow-through.
        /// Set feet (<paramref name="onRun"/> false): the stance turns SIDE-ON first (hips closed 35 degrees, shoulders closed a further 20, the front arm pointing at the target, the ball by the ear), the front foot steps to the target (the footfall is
        /// <see cref="SimAgentView"/>'s scripted stride), the hips open first (done by u = 0.8) and the shoulders after them (done at the release), the spine near vertical at the release with the back heel up, the throwing hand finishing across the body at the opposite hip.
        /// On the run the turn is small: shoulders to the target, the ball high, the feet are the run's.
        /// </summary>
        public static StancePose QbThrow11(float u, bool onRun)
        {
            u = Mathf.Clamp(u, 0f, 1.5f);
            var ready = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            float side = onRun ? 0.25f : 1f;
            StancePose k0 = ready;
            StancePose k1 = ready; k1.Tilt = 35f * side; k1.UpperYaw = 20f * side; k1.Hips = 0.86f; k1.Lean = 8f; k1.Bend = 4f; k1.HeadPitch = 2f;
            k1.RHand = HandMode.Cock; k1.RPos = new Vector3(0.17f, 0.30f, 0.02f); k1.LHand = HandMode.Chest; k1.LPos = new Vector3(-0.22f, 0.10f, 0.50f);
            StancePose k2 = k1; k2.Tilt = 8f * side; k2.UpperYaw = 30f * side; k2.Lean = 11f; k2.Hips = 0.83f; k2.RPos = new Vector3(0.15f, 0.34f, 0.12f); k2.LPos = new Vector3(-0.20f, 0.04f, 0.40f);
            StancePose k3 = k2; k3.Tilt = 0f; k3.UpperYaw = 0f; k3.Lean = 15f; k3.Hips = 0.84f; k3.HipsZ = 0.05f; k3.RHeel = 0.10f;
            k3.RPos = new Vector3(0.10f, 0.40f, 0.50f); k3.LPos = new Vector3(-0.16f, -0.05f, 0.26f);
            StancePose k4 = k3; k4.Tilt = -4f * side; k4.UpperYaw = -26f * side; k4.Lean = 24f; k4.Hips = 0.88f; k4.HipsZ = 0.09f; k4.RHeel = 0.12f;
            k4.RHand = HandMode.Chest; k4.RPos = new Vector3(-0.13f, -0.42f, 0.10f); k4.LPos = new Vector3(-0.14f, -0.05f, 0.20f);
            StancePose p;
            if (u < 0.45f) p = Lerp(k0, k1, Ease(u / 0.45f));
            else if (u < 0.80f) p = Lerp(k1, k2, Ease((u - 0.45f) / 0.35f));
            else if (u < 1f) p = Lerp(k2, k3, Ease((u - 0.80f) / 0.20f));
            else p = Lerp(k3, k4, Ease((u - 1f) / 0.5f));
            p.Valid = true;
            // the hips open before the shoulders: the hips' own turn (Tilt) leads, the shoulders (Tilt + UpperYaw) follow
            if (!onRun)
            {
                float tHips = Ease(Mathf.Clamp01((u - 0.30f) / 0.50f)), tSh = Ease(Mathf.Clamp01((u - 0.45f) / 0.60f));
                float hipsA = u < 1f ? 35f * (1f - tHips) : Mathf.Lerp(0f, -4f, Ease((u - 1f) / 0.5f));
                float shA = u < 1f ? 55f * (1f - tSh) : Mathf.Lerp(0f, -30f, Ease((u - 1f) / 0.5f));
                if (u < 0.30f) { float a = Ease(u / 0.30f); hipsA = 35f * a; shA = 55f * a; }
                p.Tilt = hipsA; p.UpperYaw = shA - hipsA;
            }
            return p;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class StancePoses
    {
        /// <summary>
        /// v0.11.0 QB (B-061): the under-centre stance with the angles of the coaching card (images 69 / 80: about 45 degrees between back, thighs and knees, the hips dropped). The authored pose rendered at a trunk lean of 24.5 degrees and the hips 0.667 m up: too upright;
        /// the lean goes up (the rendered trunk follows about 0.72 of the authored number) and the hips down. Hands stay on the anchor under the centre.
        /// </summary>
        public static StancePose QbUcDeep(StancePose b)
        {
            if (!b.Valid || PixFootball.Sim.QbV11.Legacy) return b;
            b.Lean = 42f; b.Hips = 0.86f; b.HipsZ = -0.05f; b.Neck = 22f; b.Bend = 8f; b.HeadPitch = -2f;      // v0.13.0 QB-MOCAP: rendered hips 0.60 m / lean 36 degrees failed the stance audit (47 of 145 snapshots under 0.62 m): 0.86 / 42 renders about 0.70 m / 31 degrees, the mocap stance (0.83 to 0.86 m) is the next step up
            return b;
        }
    }
}
