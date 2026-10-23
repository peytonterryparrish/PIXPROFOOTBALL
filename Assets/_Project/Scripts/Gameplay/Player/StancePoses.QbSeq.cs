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
            var uc = For(new StanceSpec { Kind = StanceKind.QbUnderCenter }, PlayerRole.QB);
            var p = uc; p.Hips = Mathf.Lerp(uc.Hips, 0.88f, Ease(u)); p.Lean = Mathf.Lerp(uc.Lean, 20f, Ease(u)); p.HipsZ = Mathf.Lerp(uc.HipsZ, -0.02f, Ease(u));
            p.RHand = HandMode.Chest; p.LHand = HandMode.Chest;
            p.RPos = Vector3.Lerp(new Vector3(0.04f, -0.46f, 0.46f), new Vector3(0.09f, -0.12f, 0.20f), Ease(u)); p.LPos = Vector3.Lerp(new Vector3(-0.04f, -0.46f, 0.46f), new Vector3(-0.09f, -0.12f, 0.20f), Ease(u));
            p.ToeOut = Mathf.Lerp(uc.ToeOut, 6f, Ease(u)); p.HeadPitch = 2f; p.Valid = true;
            return p;
        }

        /// <summary>The pocket carriage with the feet live (the bounce): ball high at the chest in both hands, base just wider than the shoulders, a light alternating heel raise.</summary>
        public static StancePose QbPocketLive(float t)
        {
            var p = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
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
