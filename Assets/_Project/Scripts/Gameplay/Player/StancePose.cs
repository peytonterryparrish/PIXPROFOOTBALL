using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Where a hand goes in a stance.</summary>
    public enum HandMode
    {
        /// <summary>The animation keeps the arm.</summary>
        Free,
        /// <summary>On the turf: x / z from the body frame, height <see cref="StancePose.RY"/> / <see cref="StancePose.LY"/> (the wrist; fingertips loaded).</summary>
        Ground,
        /// <summary>Resting on the same-side thigh / knee (the stance tells of a lineman, a back's hands on his thighs).</summary>
        Thigh,
        /// <summary>Loose in front of the same-side knee (linebackers, corners).</summary>
        Knee,
        /// <summary>At the hip, relaxed.</summary>
        Hip,
        /// <summary>Up at the chest: offsets are (lateral, up from the chest bone, forward).</summary>
        Chest,
        /// <summary>The throwing hand cocked by the ear (like Chest, the elbow up at shoulder height).</summary>
        Cock,
        /// <summary>At the anchor point of the play (the centre's rear for a quarterback under centre, the ball for a snapper).</summary>
        Anchor
    }

    /// <summary>
    /// v0.9.0 QBSTANCE: the authored body shape of one stance, solved over whatever the mocap does (see <see cref="StanceIK"/>). Everything is in the stance frame: x = the player's right, z = forward (the facing), metres for a 1.06 m hips-high body (scaled by his leg length at runtime).
    /// The numbers come from <c>Docs/QA/REFERENCE_Stances.md</c> (images 83-118) and the measurements of <c>Docs/QA/v090/ENGINE_AUDIT.md</c>.
    /// </summary>
    public struct StancePose
    {
        /// <summary>True when the pose is driven at all.</summary>
        public bool Valid;
        /// <summary>Hips height above the turf (m, reference body), and their offset from the player's position (forward / right).</summary>
        public float Hips, HipsZ, HipsX;
        /// <summary>Trunk (hips to chest) lean from vertical in degrees (the minimum: a hand on the turf deepens it until the shoulder can reach), the extra bend of the upper spine over the lower, the neck angle from vertical and the head pitch above the horizon.</summary>
        public float Lean, Bend, Neck, HeadPitch;
        /// <summary>Feet: lateral and forward position of the right and the left ankle, and how far each heel is raised (m).</summary>
        public float RX, RZ, LX, LZ, RHeel, LHeel;
        /// <summary>Toe-out of both feet in degrees (negative = pigeon-toed).</summary>
        public float ToeOut;
        public HandMode RHand, LHand;
        public Vector3 RPos, LPos;
        /// <summary>Wrist height of a hand on the turf / the ball (m).</summary>
        public float RY, LY;
        /// <summary>Body yaw offset of the whole stance (degrees, + = clockwise from above): the tilted wide-9 edge.</summary>
        public float Tilt;
        /// <summary>The head turns toward the ball / the defence by this many degrees (+ = to the player's right).</summary>
        public float HeadYaw;
        /// <summary>Largest trunk lean the reach of a down hand may deepen the stance to.</summary>
        public float MaxLean;
        /// <summary>The shoulders turn this many degrees from the facing (+ = to the player's right) while the hips stay (the closed front shoulder of a throw, the look over the shoulder).</summary>
        public float UpperYaw;

        /// <summary>The mirror image: left and right swap, lateral offsets flip.</summary>
        public StancePose Mirrored()
        {
            var m = this;
            m.HipsX = -HipsX; m.Tilt = -Tilt; m.HeadYaw = -HeadYaw; m.UpperYaw = -UpperYaw;
            m.RX = -LX; m.LX = -RX; m.RZ = LZ; m.LZ = RZ; m.RHeel = LHeel; m.LHeel = RHeel;
            m.RHand = LHand; m.LHand = RHand; m.RY = LY; m.LY = RY;
            m.RPos = new Vector3(-LPos.x, LPos.y, LPos.z); m.LPos = new Vector3(-RPos.x, RPos.y, RPos.z);
            return m;
        }

        /// <summary>Stagger (m) of the forward foot over the back foot.</summary>
        public float Stagger { get { return Mathf.Abs(RZ - LZ); } }
    }

    /// <summary>The table of authored stances (pure data, EditMode tested).</summary>
    public static partial class StancePoses
    {
        const float G = 0.07f;    // wrist height with the fingertips on the turf

        static StancePose Base()
        {
            return new StancePose { Valid = true, Hips = 1.0f, Lean = 10f, Bend = 4f, Neck = 8f, HeadPitch = 0f, RX = 0.17f, LX = -0.17f, ToeOut = 8f, MaxLean = 112f, RY = G, LY = G };
        }

        /// <summary>Pose for a stance; <paramref name="spec"/> carries the mirror / side flags, <paramref name="role"/> picks the position variant. Basis: right hand down, left foot forward (lines); the right foot back for receivers (mirrored when the outside foot is the left).</summary>
        public static StancePose For(in StanceSpec spec, PlayerRole role)
        {
            var p = Raw(spec.Kind, role, spec);
            if (!p.Valid) return p;
            bool mirror;
            switch (spec.Kind)
            {
                case StanceKind.ReceiverSplit: case StanceKind.ReceiverSlot: case StanceKind.Gunner:
                case StanceKind.CornerOff: case StanceKind.CornerPress: case StanceKind.SafetyDeep: case StanceKind.LinebackerTwoPoint: case StanceKind.LinebackerBlitz:
                    // the outside foot is the back foot; a defender faces the other way, so his outside is the other hand
                    mirror = spec.Defense ? !spec.IsLeftOfBall : spec.IsLeftOfBall; break;
                case StanceKind.CenterSnap: mirror = false; break;
                default: mirror = spec.Mirror; break;
            }
            if (mirror) p = p.Mirrored();
            p.Tilt += spec.Tilt;
            return p;
        }

        static StancePose Raw(StanceKind kind, PlayerRole role, in StanceSpec spec)
        {
            var p = Base();
            float tell = spec.Tell;     // -1 = pass set (sits back, light hand), +1 = run block (weight on the hand)
            switch (kind)
            {
                case StanceKind.ThreePoint:
                    if (role == PlayerRole.DL)
                    {
                        // hips above the shoulders, weight forward, hand down in front of the shoulder, the free hand loaded on the thigh
                        p.Hips = 0.86f; p.HipsZ = -0.36f; p.Lean = 90f; p.Bend = 4f; p.Neck = 50f; p.HeadPitch = 12f;
                        p.RX = 0.27f; p.RZ = -0.52f; p.LX = -0.26f; p.LZ = -0.16f; p.ToeOut = 4f; p.RHeel = 0.03f; p.LHeel = 0.02f;
                        p.RHand = HandMode.Ground; p.RPos = new Vector3(0.21f, 0f, 0.20f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.10f, 0.04f);
                        p.MaxLean = 124f; p.HeadYaw = 0f;
                    }
                    else if (role == PlayerRole.RB)
                    {
                        // fullback / goal-line back: a lighter three-point, hips a little higher
                        p.Hips = 0.82f; p.HipsZ = -0.30f; p.Lean = 80f; p.Bend = 4f; p.Neck = 45f; p.HeadPitch = 14f;
                        p.RX = 0.24f; p.RZ = -0.42f; p.LX = -0.22f; p.LZ = -0.12f; p.ToeOut = 4f; p.RHeel = 0.04f; p.LHeel = 0.02f;
                        p.RHand = HandMode.Ground; p.RPos = new Vector3(0.20f, 0f, 0.18f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.10f, 0.04f);
                        p.MaxLean = 112f;
                    }
                    else
                    {
                        // OL / TE: back flat, hips level with or a little above the shoulders, weight on the balls of the feet, one hand down slightly inside the back foot
                        p.Hips = 0.82f; p.HipsZ = -0.34f; p.Lean = 88f; p.Bend = 4f; p.Neck = 52f; p.HeadPitch = 14f;
                        p.RX = 0.25f; p.RZ = -0.48f; p.LX = -0.24f; p.LZ = -0.20f; p.ToeOut = 3f; p.RHeel = 0.04f; p.LHeel = 0.03f;
                        p.RHand = HandMode.Ground; p.RPos = new Vector3(0.20f, 0f, 0.20f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.10f, 0.04f);
                        p.MaxLean = 116f;
                        // stance tells (image 98): weight forward on the hand for a run, sitting back with a light hand for a pass
                        p.HipsZ += -0.05f * -tell; p.Lean += 2.5f * tell; p.RPos.z += 0.03f * tell;
                    }
                    return p;
                case StanceKind.FourPoint:
                    p.Hips = 0.86f; p.HipsZ = -0.34f; p.Lean = 94f; p.Bend = 3f; p.Neck = 48f; p.HeadPitch = 12f;
                    p.RX = 0.26f; p.RZ = -0.38f; p.LX = -0.26f; p.LZ = -0.30f; p.ToeOut = 3f; p.RHeel = 0.03f; p.LHeel = 0.03f;
                    p.RHand = HandMode.Ground; p.RPos = new Vector3(0.28f, 0f, 0.18f); p.LHand = HandMode.Ground; p.LPos = new Vector3(-0.28f, 0f, 0.18f);
                    p.MaxLean = 126f;
                    return p;
                case StanceKind.CenterSnap:
                    // the right hand on the ball (the anchor), the left forearm on the thigh
                    p.Hips = 0.80f; p.HipsZ = -0.36f; p.Lean = 90f; p.Bend = 4f; p.Neck = 52f; p.HeadPitch = 14f;
                    p.RX = 0.25f; p.RZ = -0.40f; p.LX = -0.25f; p.LZ = -0.24f; p.ToeOut = 3f; p.RHeel = 0.04f; p.LHeel = 0.04f;
                    p.RHand = HandMode.Anchor; p.RPos = new Vector3(0.0f, 0f, 0.12f); p.RY = 0.20f; p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.10f, 0.04f);
                    p.MaxLean = 116f;
                    return p;
                case StanceKind.TwoPointLine:
                    if (role == PlayerRole.DL)
                    {
                        // wide-9 / edge: upright but low, hips back, weight on the balls of the feet, hands loose at the thighs ready to strike
                        p.Hips = 0.80f; p.HipsZ = -0.10f; p.Lean = 32f; p.Bend = 6f; p.Neck = 18f; p.HeadPitch = 6f;
                        p.RX = 0.26f; p.RZ = -0.36f; p.LX = -0.26f; p.LZ = -0.06f; p.ToeOut = 8f; p.RHeel = 0.06f; p.LHeel = 0f;
                        p.RHand = HandMode.Knee; p.RPos = new Vector3(0.04f, 0.08f, 0.10f); p.LHand = HandMode.Knee; p.LPos = new Vector3(-0.04f, 0.08f, 0.10f);
                        p.Tilt = 0f;
                    }
                    else
                    {
                        p.Hips = 0.80f; p.HipsZ = -0.08f; p.Lean = 28f; p.Bend = 6f; p.Neck = 16f; p.HeadPitch = 6f;
                        p.RX = 0.22f; p.RZ = -0.30f; p.LX = -0.22f; p.LZ = -0.06f; p.ToeOut = 8f; p.RHeel = 0.04f; p.LHeel = 0f;
                        p.RHand = HandMode.Thigh; p.RPos = new Vector3(0f, 0.08f, 0.10f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.08f, 0.10f);
                    }
                    return p;
                case StanceKind.OlTwoPoint:
                    // image 97 / 101: sitting in a chair, hands up and inside, balanced to kick-slide
                    p.Hips = 0.76f; p.HipsZ = -0.16f; p.Lean = 30f; p.Bend = 6f; p.Neck = 18f; p.HeadPitch = 6f;
                    p.RX = 0.21f; p.RZ = -0.28f; p.LX = -0.21f; p.LZ = -0.08f; p.ToeOut = 6f; p.RHeel = 0.04f; p.LHeel = 0.02f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.15f, -0.04f, 0.30f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.15f, -0.04f, 0.30f);
                    return p;
                case StanceKind.BackTwoPoint:
                    // images 109 / 110: feet shoulder-width, back flat or slightly forward, hands on the thighs above the knees, head up
                    p.Hips = 0.82f; p.HipsZ = -0.04f; p.Lean = 30f; p.Bend = 6f; p.Neck = 14f; p.HeadPitch = 4f;
                    p.RX = 0.19f; p.RZ = -0.14f; p.LX = -0.19f; p.LZ = -0.02f; p.ToeOut = 6f; p.RHeel = 0.02f; p.LHeel = 0f;
                    p.RHand = HandMode.Thigh; p.RPos = new Vector3(0f, 0.12f, 0.10f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.12f, 0.10f);
                    return p;
                case StanceKind.QbUnderCenter:
                    // images 69 / 80: feet shoulder-width and pigeon-toed, about 45 degrees between back, thighs and knees, hands under the centre, thumbs together, head up on a swivel
                    p.Hips = 0.80f; p.HipsZ = -0.04f; p.Lean = 34f; p.Bend = 6f; p.Neck = 18f; p.HeadPitch = 0f;
                    p.RX = 0.19f; p.RZ = -0.08f; p.LX = -0.19f; p.LZ = 0.0f; p.ToeOut = -7f; p.RHeel = 0.01f; p.LHeel = 0f;
                    p.RHand = HandMode.Anchor; p.RPos = new Vector3(0.025f, 0f, 0f); p.RY = 0.80f; p.LHand = HandMode.Anchor; p.LPos = new Vector3(-0.025f, 0f, 0f); p.LY = 0.80f;
                    return p;
                case StanceKind.QbShotgun:
                    // image 77: knees flexed, weight on the balls of the feet, hands ready at the chest, eyes on the defence
                    p.Hips = 0.90f; p.HipsZ = -0.05f; p.Lean = 16f; p.Bend = 5f; p.Neck = 8f; p.HeadPitch = 2f;
                    p.RX = 0.21f; p.RZ = -0.16f; p.LX = -0.21f; p.LZ = -0.02f; p.ToeOut = 8f; p.RHeel = 0.03f; p.LHeel = 0f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.12f, -0.05f, 0.30f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.12f, -0.05f, 0.30f);
                    return p;
                case StanceKind.ReceiverSplit: case StanceKind.Gunner:
                    // images 83-87 / 90: inside foot forward on the line, outside foot back a foot-length or more with the heel up and the leg loaded, hips low, weight on the front foot, head turned in
                    p.Hips = 0.85f; p.HipsZ = -0.04f; p.Lean = 22f; p.Bend = 6f; p.Neck = 14f; p.HeadPitch = 2f;
                    p.RX = 0.15f; p.RZ = -0.36f; p.LX = -0.15f; p.LZ = 0.04f; p.ToeOut = 2f; p.RHeel = 0.07f; p.LHeel = 0f;
                    p.RHand = HandMode.Hip; p.RPos = new Vector3(0.05f, -0.02f, 0f); p.LHand = HandMode.Knee; p.LPos = new Vector3(0f, 0.06f, 0.06f);
                    p.HeadYaw = -38f;    // basis: the right foot is the outside (back) foot, so the head looks to the left, inside toward the ball; mirrored with the stance
                    return p;
                case StanceKind.ReceiverSlot:
                    p.Hips = 0.86f; p.HipsZ = -0.04f; p.Lean = 20f; p.Bend = 6f; p.Neck = 14f; p.HeadPitch = 2f;
                    p.RX = 0.16f; p.RZ = -0.32f; p.LX = -0.16f; p.LZ = 0.02f; p.ToeOut = 2f; p.RHeel = 0.06f; p.LHeel = 0f;
                    p.RHand = HandMode.Hip; p.RPos = new Vector3(0.05f, -0.02f, 0f); p.LHand = HandMode.Knee; p.LPos = new Vector3(0f, 0.06f, 0.06f);
                    p.HeadYaw = -34f;
                    return p;
                case StanceKind.CornerOff:
                    // images 91-95: square to the receiver, in the chair, the outside foot back, weight on the balls of the feet, back flat and leaning forward, hands relaxed in front of the thighs
                    p.Hips = 0.80f; p.HipsZ = -0.05f; p.Lean = 26f; p.Bend = 6f; p.Neck = 14f; p.HeadPitch = 2f;
                    p.RX = 0.19f; p.RZ = -0.30f; p.LX = -0.19f; p.LZ = -0.04f; p.ToeOut = 6f; p.RHeel = 0.05f; p.LHeel = 0f;
                    p.RHand = HandMode.Knee; p.RPos = new Vector3(0.04f, 0.04f, 0.12f); p.LHand = HandMode.Knee; p.LPos = new Vector3(-0.04f, 0.04f, 0.12f);
                    p.HeadYaw = -10f;
                    return p;
                case StanceKind.CornerPress:
                    // images 88 / 89: feet armpit-width and squared up, toes straight, knees bent over the balls of the feet, hands up at the chest ready to jam, head up
                    p.Hips = 0.86f; p.HipsZ = -0.04f; p.Lean = 14f; p.Bend = 5f; p.Neck = 8f; p.HeadPitch = 2f;
                    p.RX = 0.17f; p.RZ = -0.06f; p.LX = -0.17f; p.LZ = -0.02f; p.ToeOut = 0f; p.RHeel = 0.02f; p.LHeel = 0.01f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.16f, 0.03f, 0.30f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.16f, 0.03f, 0.30f);
                    return p;
                case StanceKind.LinebackerTwoPoint:
                    p.Hips = 0.82f; p.HipsZ = -0.06f; p.Lean = 30f; p.Bend = 6f; p.Neck = 14f; p.HeadPitch = 2f;
                    p.RX = 0.22f; p.RZ = -0.24f; p.LX = -0.22f; p.LZ = -0.02f; p.ToeOut = 6f; p.RHeel = 0.04f; p.LHeel = 0f;
                    p.RHand = HandMode.Knee; p.RPos = new Vector3(0.04f, 0.06f, 0.10f); p.LHand = HandMode.Knee; p.LPos = new Vector3(-0.04f, 0.06f, 0.10f);
                    return p;
                case StanceKind.LinebackerBlitz:
                    p.Hips = 0.76f; p.HipsZ = -0.10f; p.Lean = 40f; p.Bend = 6f; p.Neck = 24f; p.HeadPitch = 4f;
                    p.RX = 0.24f; p.RZ = -0.34f; p.LX = -0.24f; p.LZ = -0.02f; p.ToeOut = 6f; p.RHeel = 0.07f; p.LHeel = 0f;
                    p.RHand = HandMode.Knee; p.RPos = new Vector3(0.04f, 0.04f, 0.12f); p.LHand = HandMode.Knee; p.LPos = new Vector3(-0.04f, 0.04f, 0.12f);
                    return p;
                case StanceKind.SafetyDeep:
                    p.Hips = 0.88f; p.HipsZ = -0.04f; p.Lean = 18f; p.Bend = 5f; p.Neck = 10f; p.HeadPitch = 2f;
                    p.RX = 0.19f; p.RZ = -0.22f; p.LX = -0.19f; p.LZ = -0.02f; p.ToeOut = 6f; p.RHeel = 0.04f; p.LHeel = 0f;
                    p.RHand = HandMode.Thigh; p.RPos = new Vector3(0f, 0.06f, 0.12f); p.LHand = HandMode.Thigh; p.LPos = new Vector3(0f, 0.06f, 0.12f);
                    return p;
                case StanceKind.KickoffRunner:
                    // the coverage man on his running start: staggered, weight on the toes, arms loose
                    p.Hips = 0.92f; p.HipsZ = -0.04f; p.Lean = 14f; p.Bend = 4f; p.Neck = 8f; p.HeadPitch = 2f;
                    p.RX = 0.18f; p.RZ = -0.30f; p.LX = -0.18f; p.LZ = 0.02f; p.ToeOut = 4f; p.RHeel = 0.07f; p.LHeel = 0f;
                    p.RHand = HandMode.Hip; p.RPos = new Vector3(0.06f, 0.02f, 0.10f); p.LHand = HandMode.Hip; p.LPos = new Vector3(-0.06f, 0.02f, 0.10f);
                    return p;
                case StanceKind.KickoffReceive:
                    // the receiving front line: staggered, hands up and ready to block
                    p.Hips = 0.88f; p.HipsZ = -0.04f; p.Lean = 14f; p.Bend = 4f; p.Neck = 8f; p.HeadPitch = 2f;
                    p.RX = 0.19f; p.RZ = -0.22f; p.LX = -0.19f; p.LZ = 0.0f; p.ToeOut = 5f; p.RHeel = 0.04f; p.LHeel = 0f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.16f, -0.02f, 0.26f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.16f, -0.02f, 0.26f);
                    return p;
                case StanceKind.Returner:
                    p.Hips = 0.88f; p.HipsZ = -0.04f; p.Lean = 14f; p.Bend = 4f; p.Neck = 8f; p.HeadPitch = 4f;
                    p.RX = 0.20f; p.RZ = -0.10f; p.LX = -0.20f; p.LZ = -0.02f; p.ToeOut = 4f; p.RHeel = 0.03f; p.LHeel = 0.01f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.22f, -0.16f, 0.30f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.22f, -0.16f, 0.30f);
                    return p;
                case StanceKind.Kicker:
                    // relaxed and upright on the mark, eyes on the ball, the right (kicking) foot a little back
                    p.Hips = 1.0f; p.HipsZ = -0.02f; p.Lean = 6f; p.Bend = 3f; p.Neck = 12f; p.HeadPitch = -14f;
                    p.RX = 0.15f; p.RZ = -0.12f; p.LX = -0.15f; p.LZ = 0.05f; p.ToeOut = 8f; p.RHeel = 0.02f; p.LHeel = 0f;
                    p.RHand = HandMode.Hip; p.RPos = new Vector3(0.08f, -0.04f, 0.04f); p.LHand = HandMode.Hip; p.LPos = new Vector3(-0.08f, -0.04f, 0.04f);
                    return p;
                case StanceKind.Punter:
                    // both feet square, hands out in front at the waist for the snap
                    p.Hips = 0.97f; p.HipsZ = -0.02f; p.Lean = 8f; p.Bend = 3f; p.Neck = 8f; p.HeadPitch = -6f;
                    p.RX = 0.14f; p.RZ = -0.03f; p.LX = -0.14f; p.LZ = 0.0f; p.ToeOut = 4f; p.RHeel = 0f; p.LHeel = 0f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.10f, -0.30f, 0.34f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.10f, -0.30f, 0.34f);
                    return p;
                case StanceKind.QbPocket:
                    // images 68 / 75 / 78: base just wider than the shoulders, knees bent and balanced, a slight forward lean, the ball high at the chest in both hands with the point up, elbows in, eyes downfield
                    p.Hips = 0.89f; p.HipsZ = -0.02f; p.Lean = 12f; p.Bend = 5f; p.Neck = 6f; p.HeadPitch = 2f;
                    p.RX = 0.22f; p.RZ = -0.10f; p.LX = -0.22f; p.LZ = 0.02f; p.ToeOut = 8f; p.RHeel = 0.02f; p.LHeel = 0f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.10f, 0.03f, 0.22f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.10f, 0.03f, 0.22f);
                    return p;
                case StanceKind.QbDrop:
                    p.Hips = 0.91f; p.HipsZ = 0f; p.Lean = 14f; p.Bend = 5f; p.Neck = 6f; p.HeadPitch = 2f;
                    p.RX = 0.20f; p.RZ = -0.05f; p.LX = -0.20f; p.LZ = 0.05f; p.ToeOut = 4f; p.RHeel = 0f; p.LHeel = 0f;
                    p.RHand = HandMode.Chest; p.RPos = new Vector3(0.10f, 0.03f, 0.22f); p.LHand = HandMode.Chest; p.LPos = new Vector3(-0.10f, 0.03f, 0.22f);
                    return p;
                case StanceKind.LongSnapper:
                    // wide base, head down, ball between the legs, hips high
                    p.Hips = 0.80f; p.HipsZ = -0.50f; p.Lean = 96f; p.Bend = 4f; p.Neck = 56f; p.HeadPitch = 0f;
                    p.RX = 0.34f; p.RZ = -0.46f; p.LX = -0.34f; p.LZ = -0.40f; p.ToeOut = 10f; p.RHeel = 0.02f; p.LHeel = 0.02f;
                    p.RHand = HandMode.Anchor; p.RPos = new Vector3(0.05f, 0f, 0f); p.RY = 0.26f; p.LHand = HandMode.Anchor; p.LPos = new Vector3(-0.05f, 0f, 0f); p.LY = 0.26f;
                    p.MaxLean = 112f;
                    return p;
            }
            return default(StancePose);
        }
    }
}
